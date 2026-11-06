using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

/// <summary>Maintained regression for clamped-density vertical contour runs and steep entrances.</summary>
public static class TerrainSteepExcavationValidation
{
    private static readonly TerrainDensityProfile P=TerrainDensityProfile.Default;
    private static readonly StringBuilder Report=new StringBuilder();
    private static int checks;
    private static string output;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    public static void RunWorldBatch()
    {
        try
        {
            TerrainSculptingWorldValidation.Run();TerrainSculptingWorldGraphicsValidation.Run();
            Debug.Log("STEEP EXCAVATION WORLD PASS: vegetation, building grounding, sky lighting, URP excavation materials and roof shadows.");EditorApplication.Exit(0);
        }
        catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private sealed class Plane:ITerrainHeightDensityGenerator,ITerrainDensityBounds
    {
        internal double Slope,Angle;internal double2 Origin;
        public double SampleHeight(double x,double z)=>1.47+Slope*((x-Origin.x)*Math.Sin(Angle)+(z-Origin.y)*Math.Cos(Angle)+.037);
        public double SampleDensity(double3 p)=>p.y-SampleHeight(p.x,p.z);
        public bool TryGetDensityBounds(double3 min,double3 max,out double low,out double high)
        {
            double a=SampleHeight(min.x,min.z),b=SampleHeight(max.x,min.z),c=SampleHeight(min.x,max.z),d=SampleHeight(max.x,max.z);
            low=min.y-Math.Max(Math.Max(a,b),Math.Max(c,d));high=max.y-Math.Min(Math.Min(a,b),Math.Min(c,d));return true;
        }
    }
    public static void RunBatch()
    {
        try
        {
            checks=0;Report.Clear();
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-landformOutput");output=args[i+1];Directory.CreateDirectory(output);
            TerrainSculptingValidation.Run();
            TerrainSculptingMeshValidation.Run();
            Geometry();Planner();Entrances();Preview();
            TerrainSculptingIntegrationValidation.Run();
            TerrainSculptingPersistenceValidation.Run();
            TerrainSculptingInteractionValidation.Run();
            TerrainSculptingPerformanceValidation.Run();
            TerrainSculptingWorldValidation.Run();
            TerrainSculptingWorldGraphicsValidation.Run();
            TerrainEdgeValidation.ValidatePrecision(Report);
            Report.AppendLine($"STEEP EXCAVATION PASS: {checks} new checks, plus existing foundation, mesh/motor, transactions, persistence, interaction, performance, world-consumer and URP graphics suites.");
            File.WriteAllText(Path.Combine(output,"validation.txt"),Report.ToString());Debug.Log(Report.ToString());EditorApplication.Exit(0);
        }
        catch(Exception ex){if(output!=null)File.WriteAllText(Path.Combine(output,"validation.txt"),Report+"FAILED: "+ex);Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Geometry()
    {
        foreach(double slope in new[]{.4,7.0,10.0,12.0})foreach(double angle in new[]{0.0,Math.PI/2,.71,3.8})
        {
            var field=new Plane{Slope=slope,Angle=angle,Origin=new double2(-32*P.Spacing,16*P.Spacing)};
            using var world=new TerrainDensityWorld(P,field);
            var f=new TerrainSculptFootprint(new int2(-48,0),new int2(-16,32));
            foreach(int cut in new[]{0,1,2})
            {
                if(cut>0)
                {
                    var centre=new double3(field.Origin.x,1.5,field.Origin.y);
                    var brush=cut==1?TerrainDensityBrush.Sphere(centre,1.1):TerrainDensityBrush.Box(centre+new double3(.5,0,.5),new double3(.9,1.1,.9));
                    using var edit=world.StageExcavation(brush);Check(world.TryCommit(edit),"Steep fixture cut did not commit.");
                }
                using var snapshot=world.CaptureSnapshot();
                var patch=TerrainSculptPatchBuilder.BuildPatch(snapshot,f,-400,400,field.SampleHeight);
                Check(patch.TriangleCount>0,"Missing steep surface.");
                if(cut==0)foreach(var data in patch.SurfaceData)Check(data.x==0,"Untouched steep reconstruction was classified as excavation.");
                else { bool exposed=false;foreach(var data in patch.SurfaceData)exposed|=data.x>.1;Check(exposed,"Actual removal did not receive excavated stone."); }
                TerrainSculptingMeshValidation.Topology(new[]{patch},f);Winding(patch);
                if(cut==2)
                {
                    var repeat=TerrainSculptPatchBuilder.BuildPatch(snapshot,f,-400,400,field.SampleHeight);
                    Check(repeat.Positions.Length==patch.Positions.Length&&repeat.Triangles.Length==patch.Triangles.Length,"Repeated steep extraction changed counts.");
                    for(int j=0;j<patch.Positions.Length;j++)Check(math.all(patch.Positions[j]==repeat.Positions[j]),"Repeated steep extraction changed a position.");
                    for(int j=0;j<patch.Triangles.Length;j++)Check(patch.Triangles[j]==repeat.Triangles[j],"Repeated steep extraction changed winding/order.");
                }
            }
            Report.AppendLine($"Slope {slope:F1}, angle {angle:F2}: untouched, sphere and repeated box cuts; connected oriented collars, negative coordinates and repeatability passed.");
            Debug.Log($"STEEP GEOMETRY: slope {slope:F1}, angle {angle:F2} passed.");
        }
    }
    private static void Winding(TerrainSculptMeshData mesh)
    {
        var vertices=new Dictionary<double3,int>();var remap=new int[mesh.Positions.Length];
        for(int i=0;i<remap.Length;i++)
        {var p=mesh.Positions[i];if(!vertices.TryGetValue(p,out int v)){v=vertices.Count;vertices.Add(p,v);}remap[i]=v;}
        var edges=new Dictionary<(int,int),int>();
        for(int i=0;i<mesh.Triangles.Length;i+=3)for(int j=0;j<3;j++)
        {
            int a=remap[mesh.Triangles[i+j]],b=remap[mesh.Triangles[i+(j+1)%3]];var key=(Math.Min(a,b),Math.Max(a,b));
            int sign=a<b?1:-1;if(edges.TryGetValue(key,out int old))Check(old==-sign,"Joined triangles disagree about edge orientation.");else edges.Add(key,sign);
        }
    }
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    private static void Planner()
    {
        var field=new Plane{Slope=0};using var world=new TerrainDensityWorld(P,field);
        using var edit=world.StageExcavation(TerrainDensityBrush.Sphere(new double3(2.3,1.47,0),1.3));
        using var snapshot=world.CaptureMeshingSnapshot(edit);
        TerrainSculptGroup Group(int x0,int x1)
        {
            var g=new TerrainSculptGroup();
            typeof(TerrainSculptGroup).GetProperty("Footprint").SetValue(g,new TerrainSculptFootprint(new int2(x0,-8),new int2(x1,8)));
            typeof(TerrainSculptGroup).GetProperty("Minimum").SetValue(g,new double3(-2,-2,-2));
            typeof(TerrainSculptGroup).GetProperty("Maximum").SetValue(g,new double3(5,5,2));return g;
        }
        var groups=new List<TerrainSculptGroup>{Group(-8,8),Group(20,36)};
        var type=typeof(TerrainSculptRuntime).Assembly.GetType("TerrainSculptPatchPlanner");
        type.GetMethod("PrepareGroups",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{snapshot,groups,(Func<double,double,double>)field.SampleHeight,TerrainSourceSurfaceLayout.Current(P),new TerrainSculptBuildBudget(32L*1024*1024),null});
        Check(groups.Count==1&&groups[0].Footprint.Min.x==-24&&groups[0].Footprint.Max.x==36,"Guard replan did not merge newly overlapping groups.");
        Check(world.Revision==0&&world.ModifiedBrickCount==0,"Guard replan committed the pending edit.");
        var f=groups[0].Footprint;var patch=TerrainSculptPatchBuilder.BuildPatch(snapshot,f,-16,32,field.SampleHeight);TerrainSculptingMeshValidation.Topology(new[]{patch},f);
        Report.AppendLine("Pending snapshot guard expansion and group re-merge passed without committing or restaging the edit.");
    }
    private static void Preview()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;var sun=RenderSettings.sun;
        var objects=new List<Object>();
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=null;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.4f,.48f);
            var source=AssetDatabase.LoadAssetAtPath<Material>(DesertSurfaceSetup.MaterialPath);Check(source!=null,"Terrain preview material missing.");
            var appearance=AssetDatabase.LoadAssetAtPath<TerrainSculptSurfaceSettings>("Assets/Resources/TerrainSculpting/ExcavatedStone.asset");
            var disabledSky=ScriptableObject.CreateInstance<TerrainSkyAccessSettings>();disabledSky.enabled=false;objects.Add(disabledSky);
            var lo=new GameObject("Steep preview sunlight");objects.Add(lo);var light=lo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.shadows=LightShadows.Soft;light.shadowBias=.02f;light.shadowNormalBias=.1f;light.transform.rotation=Quaternion.Euler(48,-125,0);RenderSettings.sun=light;
            var co=new GameObject("Steep preview camera");objects.Add(co);var camera=co.AddComponent<Camera>();camera.nearClipPlane=.05f;camera.farClipPlane=300;camera.fieldOfView=65;camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.43f,.6f,.74f);co.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            using var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>1+Math.Clamp(9*z,0,32),terrainShader:source.shader,appearance:appearance,receiveShadows:true,skyAccess:disabledSky);
            f.Material.CopyPropertiesFromMaterial(source);f.Material.SetVector("_TerrainHorizonParams",Vector4.zero);
            var rock=new Texture2D(1,1,TextureFormat.RGBA32,false,true);rock.SetPixel(0,0,new Color(0,0,0,1));rock.Apply();objects.Add(rock);
            var empty=new Texture2D(1,1,TextureFormat.RGBA32,false,true);empty.SetPixel(0,0,Color.clear);empty.Apply();objects.Add(empty);
            f.Material.SetTexture("_ControlMap0",rock);f.Material.SetTexture("_ControlMap1",empty);f.Material.SetTexture("_ControlMap2",empty);
            foreach(int x in new[]{-1,0})foreach(int z in new[]{-1,0})f.Find(new ChunkCoord(x,z));
            f.Viewer.transform.position=new Vector3(12,2.3f,-1);f.Ready();
            camera.transform.position=new Vector3(7,7,-12);camera.transform.LookAt(new Vector3(12,9,1));Capture(camera,"SteepWall_Before.png");
            foreach(double z in new[]{.35,1.3,2.2,3.1}) { Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,2.3,z),1.55)),"Preview stroke rejected.");f.Ready(); }
            Capture(camera,"SteepWall_Entrance.png");
            camera.transform.position=new Vector3(11.4f,2.4f,-2.3f);camera.transform.LookAt(new Vector3(12,2.3f,3));Capture(camera,"SteepWall_Close.png");
            Check(!ShaderUtil.ShaderHasError(source.shader),"Production terrain shader has compilation errors.");
            Report.AppendLine("Native URP rock-wall overview, entrance and close views captured using the production terrain/excavation shader and published meshes.");
        }
        finally
        {
            foreach(var o in objects)Object.DestroyImmediate(o);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;
            RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;RenderSettings.sun=sun;
        }
    }
    private static void Capture(Camera camera,string name)
    {
        var rt=RenderTexture.GetTemporary(1440,900,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var previous=RenderTexture.active;var image=new Texture2D(1440,900,TextureFormat.RGBA32,false);
        try
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();
            foreach(var p in image.GetPixels32())Check(!(p.r>180&&p.b>180&&p.g<50),"Shader error pixels in excavation preview.");
            int ground=0;foreach(var p in image.GetPixels32())if(Math.Abs(p.r-110)+Math.Abs(p.g-153)+Math.Abs(p.b-189)>30)ground++;
            Check(ground>100000,"Excavation preview contains no terrain.");
            File.WriteAllBytes(Path.Combine(output,name),image.EncodeToPNG());
        }
        finally {camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(image);}
    }
    private static void Entrances()
    {
        foreach(double seam in new[]{0.0,128*P.Spacing,-128*P.Spacing})
        {
            double H(double x,double z)=>1+Math.Clamp(9*(z-seam),0,32);
            using var fixture=new TerrainSculptingIntegrationValidation.Fixture(H);
            fixture.Viewer.transform.position=new Vector3(12,2.5f,(float)seam-1);
            fixture.Ready();
            foreach(double advance in new[]{.35,1.3,2.2,3.1})
            {
                Check(fixture.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,2.3,seam+advance),1.55)),"Steep entrance rejected admission.");
                fixture.Ready();Check(fixture.Runtime.Status=="Ready","Steep entrance failed: "+fixture.Runtime.Status);
            }
            Physics.SyncTransforms();
            var live=Field<System.Collections.IDictionary>(fixture.Runtime,"live");
            foreach(System.Collections.DictionaryEntry pair in live)
            {
                var surface=pair.Value;
                // Compare actual published Unity render/collider geometry, not a separate height estimate.
                var type=surface.GetType();Mesh render=null,collision=null;
                foreach(var member in type.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public))
                    if(member.FieldType==typeof(Mesh)) { var mesh=(Mesh)member.GetValue(surface);if(mesh?.uv2.Length>0)render=mesh;else if(mesh!=null)collision=mesh; }
                if(render!=null&&collision!=null)
                {
                    Check(render.vertexCount==collision.vertexCount,"Near collision and rendering have different vertex counts.");
                    var a=render.vertices;var b=collision.vertices;for(int i=0;i<a.Length;i++)Check(a[i]==b[i],"Near render/collision position mismatch.");
                    var ta=render.triangles;var tb=collision.triangles;Check(ta.Length==tb.Length,"Near collision has different topology.");for(int i=0;i<ta.Length;i++)Check(ta[i]==tb[i],"Near collision has different triangle order.");
                }
            }
            var player=new GameObject("Steep entrance motor");
            try
            {
                player.transform.position=new Vector3(12,1.08f,(float)seam-1.5f);
                var controller=player.AddComponent<CharacterController>();controller.height=1.7f;controller.radius=.28f;controller.center=new Vector3(0,.85f,0);controller.stepOffset=.3f;controller.skinWidth=.025f;
                Physics.SyncTransforms();
                for(int i=0;i<180;i++)controller.Move(new Vector3(0,-.04f,.025f));
                Check(player.transform.position.z>seam+2.5,"Actual capsule cannot traverse the steep-wall entrance.");
                Check(player.transform.position.y>.6f,"Capsule fell through the entrance floor.");
            }
            finally {Object.DestroyImmediate(player);}
            Check(fixture.Runtime.CommittedEdits==4,"Repeated steep excavation did not publish exactly once per stroke.");
            Report.AppendLine($"Steep entrance at signed source boundary z={seam:F2}: four cuts, actual PhysX capsule traversal and published render/collision equality passed. Peak live {fixture.Runtime.DerivedLiveBytes/1048576d:F2} MiB; last worker {fixture.Runtime.LastWorkerMs:F1} ms.");
        }
    }
}
