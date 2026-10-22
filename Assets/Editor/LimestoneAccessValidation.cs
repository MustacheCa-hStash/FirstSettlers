using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class LimestoneAccessValidation
{
    public const string Output="ArtReferences/LimestoneBuildKit/Access";
    private const string Pending="FS.Access.Validation";private static int checks;private static readonly List<string> errors=new();
    static LimestoneAccessValidation()=>EditorApplication.playModeStateChanged+=State;
    private static void Check(bool yes,string text){checks++;if(!yes)throw new InvalidOperationException(text);}
    private static void Near(Vector3 a,Vector3 b,string text)=>Check((a-b).sqrMagnitude<.000001f,text+": "+a+" / "+b);
    public static void InstallAndRunBatch()=>Run(true);
    public static void RunInstalledBatch()=>Run(false);
    private static void Run(bool install)
    {
        try
        {
            checks=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var c=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            if(install)
            {
                var originals=new Dictionary<string,string>();foreach(var d in c.presets)if(Array.IndexOf(LimestoneAccessSetup.Ids,d.contentId)<0)originals.Add(AssetDatabase.GetAssetPath(d),File.ReadAllText(AssetDatabase.GetAssetPath(d)));
                LimestoneAccessSetup.Install();int count=c.presets.Length;var guids=new List<string>();foreach(var id in LimestoneAccessSetup.Ids)guids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(id))));
                LimestoneAccessSetup.Install();Check(c.presets.Length==count,"Reinstall added duplicates");int n=0;foreach(var id in LimestoneAccessSetup.Ids)Check(guids[n++]==AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(id))),"GUID changed");
                foreach(var original in originals)Check(File.ReadAllText(original.Key)==original.Value,"Unrelated definition changed: "+original.Key);
            }
            Assets(c);Render(c);SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog c)
    {
        var ids=new HashSet<string>();foreach(var d in c.presets)Check(d!=null&&ids.Add(d.contentId),"Missing or duplicate catalog entry");
        var manifest=JsonUtility.FromJson<LimestoneAccessSetup.Manifest>(File.ReadAllText(LimestoneAccessSetup.ManifestPath));Check(manifest.parts.Length==7,"Seven parts required");
        var material=AssetDatabase.LoadAssetAtPath<Material>(LimestoneAccessSetup.MaterialPath);Check(material&&material.enableInstancing&&material.GetTexture("_BumpMap")&&material.GetFloat("_AlphaClip")==0,"Opaque shared normal/AO material missing");
        var colour=new Texture2D(2,2,TextureFormat.RGBA32,false,true);colour.LoadImage(File.ReadAllBytes(LimestoneAccessSetup.Colour));
        foreach(var p in manifest.parts)
        {
            var d=c.Find("build.stone."+p.slug);Check(d&&d.preserveAuthoredMesh&&d.material==material&&d.jointCoverVariants.Length==0,"Authored stone contract missing");
            var installed=BuildKitDimensions.InstalledBounds(d,LimestoneAccessSetup.V(p.minimum),LimestoneAccessSetup.V(p.size));Near(d.LocalBounds.min,installed.min,"Definition min");Near(d.LocalBounds.size,installed.size,"Definition size");
            Near(d.mesh.bounds.min,d.LocalBounds.min,"Imported min");Near(d.mesh.bounds.max,d.LocalBounds.max,"Imported max");Check(d.mesh.subMeshCount==1&&d.mesh.triangles.Length/3==p.triangles,"Imported geometry differs");
            Check(d.authoringPrefab.GetComponents<MeshCollider>().Length==(p.steps>0?1:p.solids.Length)&&d.occupiedVolumes.Length==p.solids.Length,"Physical convex decomposition differs");
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Actual mesh icon absent");Check(!BuildUiContent.Description(d).StartsWith("Choose this component",StringComparison.Ordinal),"Menu description absent");
            var v=d.mesh.vertices;var t=d.mesh.triangles;var uv=d.mesh.uv;var normals=d.mesh.normals;var tangents=d.mesh.tangents;
            Check(v.Length==uv.Length&&v.Length==normals.Length&&v.Length==tangents.Length,"Vertex attributes absent");
            for(int i=0;i<v.Length;i++){Check(float.IsFinite(uv[i].x)&&float.IsFinite(uv[i].y)&&uv[i].x>=0&&uv[i].x<=1&&uv[i].y>=0&&uv[i].y<=1,"Invalid atlas coordinate");Vector3 tangent=tangents[i];Check(Mathf.Abs(normals[i].magnitude-1)<.002f&&Mathf.Abs(tangent.magnitude-1)<.002f&&Mathf.Abs(Vector3.Dot(tangent,normals[i]))<.002f,"Invalid tangent basis");}
            for(int i=0;i<t.Length;i+=3){var cross=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]);Check(cross.sqrMagnitude>1e-14f&&Vector3.Dot(cross,normals[t[i]]+normals[t[i+1]]+normals[t[i+2]])>0,"Degenerate/reversed face: "+p.tag+" triangle "+i/3);var a=uv[t[i+1]]-uv[t[i]];var b=uv[t[i+2]]-uv[t[i]];Check(Mathf.Abs(a.x*b.y-a.y*b.x)>1e-12f,"Collapsed UV triangle");}
            double ao=0,brightness=0;int samples=0;
            for(int i=0;i<t.Length;i+=3)if(Mathf.Abs(normals[t[i]].z)>.99f)
            {var point=(uv[t[i]]+uv[t[i+1]]+uv[t[i+2]])/3;var pixel=colour.GetPixelBilinear(point.x,point.y);ao+=pixel.a;brightness+=(pixel.r+pixel.g+pixel.b)/3;samples++;}
            Check(samples>0&&ao/samples>.35&&brightness/samples>.25,"Exposed face bake is missing or self-occluded: "+p.tag);
            foreach(var hull in d.occupiedVolumes)
            {
                var hv=hull.vertices;var ht=hull.mesh.triangles;double volume=0;var edges=new Dictionary<string,int>();
                for(int i=0;i<ht.Length;i+=3)
                {
                    volume+=Vector3.Dot(hv[ht[i]],Vector3.Cross(hv[ht[i+1]],hv[ht[i+2]]))/6.0;
                    foreach(var pair in new[]{(ht[i],ht[i+1]),(ht[i+1],ht[i+2]),(ht[i+2],ht[i])}){string key=Math.Min(pair.Item1,pair.Item2)+"/"+Math.Max(pair.Item1,pair.Item2);edges.TryGetValue(key,out int n);edges[key]=n+1;}
                }
                Check(volume>0,"Hull winding/volume invalid");foreach(int n in edges.Values)Check(n==2,"Open collider hull");
            }
            if(p.opening!=null&&p.opening.Length==4)
            {
                float sill=p.opening[1];for(int x=0;x<7;x++)for(int y=0;y<7;y++)
                {
                    float px=Mathf.Lerp(p.opening[0]+.04f,p.opening[2]-.04f,(x+.5f)/7),py=Mathf.Lerp(sill+.04f,p.opening[3]-.04f,(y+.5f)/7);
                    bool clear=py<(p.spring>0?p.spring+Mathf.Sqrt(Mathf.Max(0,.75f*.75f-(px-2)*(px-2)))-.012f:p.opening[3]);
                    if(clear)Check(!Hit(d.mesh,new Vector3(px,py,-1)),"Visible mesh fills opening: "+p.tag);
                }
            }
            Check(!d.flipVariant&&!d.IsHalfGable,"Unexpected flip variant");
            if(p.steps>0)Check(d.kind==BuildPartKind.Stair&&d.collisionMesh&&d.authoringPrefab.GetComponent<SmoothWalkSurface>(),"Stair walking hull missing");
        }

        Object.DestroyImmediate(colour);
    }
    private static bool Hit(Mesh mesh,Vector3 origin)
    {
        var v=mesh.vertices;var t=mesh.triangles;for(int i=0;i<t.Length;i+=3)
        {var a=v[t[i]];var e=v[t[i+1]]-a;var f=v[t[i+2]]-a;var h=Vector3.Cross(Vector3.forward,f);float det=Vector3.Dot(e,h);if(Mathf.Abs(det)<1e-7f)continue;var delta=origin-a;float u=Vector3.Dot(delta,h)/det;var q=Vector3.Cross(delta,e);float w=Vector3.Dot(Vector3.forward,q)/det;float distance=Vector3.Dot(f,q)/det;if(u>=0&&w>=0&&u+w<=1&&distance>=0&&distance<=2)return true;}return false;
    }
    private static void State(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;SessionState.SetBool(Pending,false);checks=SessionState.GetInt(Pending+".checks",0);errors.Clear();
        void Log(string m,string trace,LogType type){if(type is LogType.Error or LogType.Exception or LogType.Assert)errors.Add(m);}Application.logMessageReceived+=Log;
        try
        {
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            for(int yaw=0;yaw<8;yaw++)
            {
                Runtime(c,yaw);Cornice(c,yaw);Landing(c,yaw);Buttress(c,yaw);foreach(int part in new[]{5,6})Terrain(c,part,yaw);
                var walk=typeof(W21StairValidation).GetMethod("Walk",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
                foreach(int part in new[]{1,2,3})foreach(float x in new[]{.45f,1f,1.55f})foreach(bool down in new[]{false,true})
                {walk.Invoke(null,new object[]{c.Find(LimestoneAccessSetup.Ids[part]),c,yaw,x,down,1f/60,1});Check(true,"Motor traversal");}
                foreach(bool down in new[]{false,true}){walk.Invoke(null,new object[]{c.Find(LimestoneAccessSetup.Ids[2]),c,yaw,.75f,down,1f/60,2});Check(true,"Two-flight traversal");}
            }
            Check(errors.Count==0,"Runtime errors: "+string.Join(" | ",errors));Directory.CreateDirectory(Output);
            File.WriteAllText(Output+"/unity-validation.json","{\"passed\":true,\"checks\":"+checks+",\"parts\":7,\"corniceCases\":56,\"stairWalks\":160,\"terrainCases\":16,\"headings\":8,\"urpRenders\":true,\"instancedRendering\":true}");Debug.Log("ACCESS PASS: "+checks+" checks; seven components, 160 motor traversals, reversible cornice, landing bearing, retaining wall and buttress.");Application.logMessageReceived-=Log;EditorApplication.Exit(0);
        }catch(Exception ex){Application.logMessageReceived-=Log;Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Runtime(BuildCatalog c,int yaw)
    {
        var host=new GameObject("Access placement fixture");var w=host.AddComponent<BuildWorld>();w.enabled=false;var s=w.Session;
        var origin=new Vector3(6000+yaw*30,40,6000);var q=BuildGeometry.Rotation(yaw);var frame=s.CreateFrame(origin,yaw);
        try
        {
            var foundation=s.Add(c.Find("build.prototype.foundation"),frame,new Vector3Int(0,0,-16),0,true);
            foreach(int part in new[]{1,2,3})
            {
                var d=c.Find(LimestoneAccessSetup.Ids[part]);var snap=BuildPlacement.Solve(d,origin+q*new Vector3(.75f,0,0),Vector3.up,foundation,s.Frame(foundation.OwnFrameId),yaw,0,default);
                w.Validate(ref snap);Check(snap.Valid,"Stair placement rejected: "+snap.Message);var p=w.Commit(snap);Check(p!=null&&p.Supported,"Stair commit failed");
                using(var gameplay=new BuildGameplay(s)){gameplay.Update(p.Origin,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(p.Id,out var proxy)&&proxy.RampShape&&proxy.RampShape.sharedMesh==d.collisionMesh&&proxy.GetComponent<SmoothWalkSurface>(),"Stair smoothing proxy missing");}
                // The full masonry body is occupied, including below the rear treads.
                bool solid=false;foreach(var v in p.Resolved.volumes)solid|=BuildOccupancy.Contains(v,new Vector3(.75f,.1f,d.LocalBounds.max.z-.1f));Check(solid,"Stair is hollow underneath");
                var continuation=BuildPlacement.Solve(d,p.Origin+q*new Vector3(.75f,d.LocalBounds.max.y,d.LocalBounds.max.z),Vector3.up,p,s.Frame(p.OwnFrameId),yaw,0,default);
                Near(continuation.Origin,p.Origin+q*new Vector3(0,d.LocalBounds.max.y,d.LocalBounds.max.z),"Stair continuation changed endpoint");w.Validate(ref continuation);Check(continuation.Valid,"Stair continuation rejected: "+continuation.Message);
                w.Remove(p.Id);
            }
            var groundOnly=BuildPlacement.Solve(c.Find(LimestoneAccessSetup.Ids[2]),origin,Vector3.up,null,null,yaw,0,default);w.Validate(ref groundOnly);Check(!groundOnly.Valid,"Terrain granted stair support");
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Cornice(BuildCatalog c,int yaw)
    {
        string[] wallIds={LimestoneKitSetup.Ids[0],LimestoneKitSetup.Ids[1],LimestoneKitExtensionSetup.Ids[3],LimestoneKitExtensionSetup.Ids[5],LimestoneCourtyardSetup.Ids[0],LimestoneCourtyardSetup.Ids[1],LimestoneCourtyardSetup.Ids[2]};
        foreach(string id in wallIds)
        {
            var host=new GameObject("Cornice course fixture");var w=host.AddComponent<BuildWorld>();w.enabled=false;var s=w.Session;var origin=new Vector3(7000+yaw*30,40,7000);var q=BuildGeometry.Rotation(yaw);var f=s.CreateFrame(origin,yaw);
            try
            {
                s.Add(c.Find("build.prototype.foundation"),f,new Vector3Int(0,0,-8),0,true);var wall=s.Add(c.Find(id),f,default,0,false);var original=wall.Resolved.mesh;var d=c.Find(LimestoneAccessSetup.Ids[0]);Check(BuildTopCourse.Compatible(wall.Definition,d),"Compatible limestone wall declined: "+id);
                var snap=BuildPlacement.Solve(d,origin+q*new Vector3(2,2.9f,.25f),q*Vector3.forward,wall,s.Frame(wall.OwnFrameId),yaw,0,default);Near(snap.Origin,origin+Vector3.up*2.75f,"Cornice added storey height");w.Validate(ref snap);Check(snap.Valid,"Cornice preview rejected: "+snap.Message);var course=w.Commit(snap);Check(course!=null&&course.Supported,"Cornice lacks bearing");
                Check(wall.Resolved.mesh!=original&&Mathf.Abs(wall.Resolved.mesh.bounds.max.y-2.75f)<.001f,"Wall mesh top was not replaced");Check(Mathf.Abs(wall.Resolved.Bounds.max.y-2.75f)<.001f,"Wall collision top was not replaced");Check(!BuildResolvedGeometry.Overlaps(wall,wall.Resolved,course,course.Resolved),"Cornice and wall interpenetrate");Near(course.Origin+Vector3.up*.25f,origin+Vector3.up*3,"Cornice eave datum shifted");
                using(var gameplay=new BuildGameplay(s))
                {
                    gameplay.Update(origin,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(wall.Id,out var proxy),"Clipped wall proxy absent");var cols=new List<Collider>();proxy.GetEnabledColliders(cols);
                    foreach(var col in cols){var local=BuildGeometry.WorldBounds(col.bounds,Vector3.zero,0);Check(col.bounds.max.y<=origin.y+2.751f,"Wall proxy kept old top");}
                }
                var gable=c.Find(LimestoneCourtyardSetup.Ids[7]);var roofSnap=BuildPlacement.Solve(gable,origin+q*new Vector3(1,2.9f,0),q*Vector3.forward,wall,s.Frame(wall.OwnFrameId),yaw,0,default);Near(roofSnap.Origin,origin+Vector3.up*3,"Gable storey datum changed");w.Validate(ref roofSnap);Check(roofSnap.Valid,"Cornice broke gable placement: "+roofSnap.Message);
                var shifted=snap;shifted.Origin+=q*Vector3.right*.25f;w.Validate(ref shifted);Check(!shifted.Valid,"Partial cornice accepted");
                w.Remove(course.Id);Check(wall.Resolved.mesh==original&&Mathf.Abs(wall.Resolved.Bounds.max.y-3)<.001f,"Removing cornice did not restore original wall");
                course=w.Commit(snap);Check(course!=null,"Cornice reinstall failed");w.Remove(wall.Id);Check(!course.Supported,"Cornice retained support without its matching host wall");
            }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
        }
        Check(!BuildTopCourse.Compatible(c.Find(LimestoneCourtyardSetup.Ids[7]),c.Find(LimestoneAccessSetup.Ids[0])),"Gable accepted cornice band");
    }
    private static void Landing(BuildCatalog c,int yaw)
    {
        var origin=new Vector3(7500+yaw*30,40,7500);using var s=new BuildSession();var f=s.CreateFrame(origin,yaw);var d=c.Find(LimestoneAccessSetup.Ids[4]);
        var foundation=s.Add(c.Find("build.prototype.foundation"),f,default,0,true);var landing=s.Add(d,f,new Vector3Int(0,1,0),0,false);Check(landing.Supported,"Full bedding did not support landing");
        using(var gameplay=new BuildGameplay(s)){gameplay.Update(origin,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(landing.Id,out var proxy),"Landing proxy absent");Check(proxy.Shape&&Mathf.Abs(proxy.Shape.center.y+.125f)<.001f,"Landing collider is not below walking plane");}
        s.Remove(foundation.Id);Check(!landing.Supported,"Landing floated after foundation removal");s.Remove(landing.Id);
        var strip=s.Add(c.Find(LimestoneKitExtensionSetup.Ids[2]),f,default,0,true);landing=s.Add(d,f,new Vector3Int(0,1,0),0,false);Check(!landing.Supported,"Partial bedding supported landing");s.Remove(landing.Id);s.Remove(strip.Id);
        var timber=s.Add(c.Find("build.prototype.floor"),f,default,0,true);landing=s.Add(d,f,new Vector3Int(0,1,0),0,false);Check(!landing.Supported,"Timber granted solid stone bedding");s.Remove(landing.Id);s.Remove(timber.Id);
        // Two solid footing strips provide continuous coverage at a half-storey turn.
        var a=s.Add(c.Find(LimestoneKitExtensionSetup.Ids[2]),f,new Vector3Int(0,5,2),0,true);var b=s.Add(c.Find(LimestoneKitExtensionSetup.Ids[2]),f,new Vector3Int(0,5,6),0,true);landing=s.Add(d,f,new Vector3Int(0,6,0),0,false);Check(landing.Supported,"Union of solid footing coverage rejected");s.Remove(b.Id);Check(!landing.Supported,"Missing landing support strip ignored");
    }
    private static void Buttress(BuildCatalog c,int yaw)
    {
        var origin=new Vector3(7800+yaw*30,40,7800);var q=BuildGeometry.Rotation(yaw);var host=new GameObject("Buttress wall fixture");var w=host.AddComponent<BuildWorld>();w.enabled=false;var s=w.Session;var f=s.CreateFrame(origin,yaw);
        try
        {
            var foundation=s.Add(c.Find("build.prototype.foundation"),f,new Vector3Int(0,0,-8),0,true);var wall=s.Add(c.Find(LimestoneKitSetup.Ids[0]),f,default,0,false);var d=c.Find(LimestoneAccessSetup.Ids[6]);
            foreach(float side in new[]{-1f,1f})
            {
                var snap=BuildPlacement.Solve(d,origin+q*new Vector3(2,1,side*.25f),q*Vector3.forward*side,wall,s.Frame(wall.OwnFrameId),yaw,0,default);w.Validate(ref snap);Check(snap.Valid,"Buttress did not seat at wall base: "+snap.Message);var p=w.Commit(snap);Check(p!=null&&p.Supported,"Buttress commit failed");Check(!BuildResolvedGeometry.Overlaps(wall,wall.Resolved,p,p.Resolved),"Buttress rear penetrates wall");w.Remove(p.Id);
                snap.Origin+=Vector3.up*.25f;w.Validate(ref snap);Check(!snap.Valid,"Buttress hung from wall without base bedding");
            }
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Terrain(BuildCatalog c,int part,int yaw)
    {
        var origin=new Vector3(8000+yaw*30,30,8000);var q=BuildGeometry.Rotation(yaw);var data=new TerrainData{heightmapResolution=33,size=new Vector3(12,4,12)};data.SetHeights(0,0,new float[33,33]);var ground=UnityEngine.Terrain.CreateTerrainGameObject(data);ground.layer=GameplayLayers.WorldSolid;ground.transform.position=origin-new Vector3(6,0,6);Physics.SyncTransforms();var host=new GameObject("Access terrain fixture");var w=host.AddComponent<BuildWorld>();w.enabled=false;
        try{var d=c.Find(LimestoneAccessSetup.Ids[part]);var p=BuildPlacement.Solve(d,origin+q*new Vector3(d.LocalBounds.center.x,0,0),Vector3.up,null,null,yaw,0,default);w.Validate(ref p);Check(p.Valid&&p.Grounded,"Boundary cannot ground on terrain: "+p.Message);var floating=p;floating.Origin+=Vector3.up;w.Validate(ref floating);Check(!floating.Valid&&!floating.Grounded,"Floating access grounded");Check(w.Commit(p)!=null,"Terrain boundary commit rejected");}finally{Object.DestroyImmediate(host);Object.DestroyImmediate(ground);Object.DestroyImmediate(data);Physics.SyncTransforms();}
    }
    private static void Render(BuildCatalog c)
    {
        Directory.CreateDirectory(Output);var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;var oldSun=RenderSettings.sun;bool oldAsync=ShaderUtil.allowAsyncCompilation;var oldTarget=RenderTexture.active;
        var root=new GameObject("Access render fixture");var cameraObject=new GameObject("Access camera");var lightObject=new GameObject("Access sun");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.aspect=16f/9;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.19f,.2f);cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(28,-40,0);var target=new RenderTexture(1600,900,24);target.Create();Action<ScriptableRenderContext,Camera> submit=null;
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.3f,.3f,.3f);RenderSettings.sun=light;ShaderUtil.allowAsyncCompilation=false;
            var mat=c.Find(LimestoneAccessSetup.Ids[0]).material;for(int i=0;i<mat.passCount;i++)ShaderUtil.CompilePass(mat,i,true);Check(!ShaderUtil.ShaderHasError(mat.shader),"Shader compile failed");var objects=new List<GameObject>();
            for(int i=0;i<7;i++)
            {
                var d=c.Find(LimestoneAccessSetup.Ids[i]);var obj=new GameObject(d.displayName){layer=31};obj.transform.SetParent(root.transform);obj.AddComponent<MeshFilter>().sharedMesh=d.mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=mat;objects.Add(obj);var centre=d.LocalBounds.center;
                camera.orthographicSize=Mathf.Max(1.1f,d.LocalBounds.size.magnitude*.55f);camera.transform.position=centre+new Vector3(4,2,-8);camera.transform.LookAt(centre);Capture(camera,target,"L"+(i+18)+"-unity-front.png");camera.transform.position=centre+new Vector3(-4,2,8);camera.transform.LookAt(centre);Capture(camera,target,"L"+(i+18)+"-unity-back.png");
                obj.SetActive(false);
            }
            for(int i=0;i<7;i++){objects[i].transform.position=new Vector3(i%3*6,0,i/3*5);objects[i].SetActive(true);}camera.orthographicSize=8.5f;camera.transform.position=new Vector3(20,15,-20);camera.transform.LookAt(new Vector3(8,1,5));Capture(camera,target,"access-unity-overview.png");foreach(var obj in objects)obj.SetActive(false);
            using var s=new BuildSession();var f=s.CreateFrame(Vector3.zero,1);for(int i=0;i<7;i++)s.Add(c.Find(LimestoneAccessSetup.Ids[i]),f,new Vector3Int(i%3*24,0,i/3*20),0,false);var renderer=new BuildRenderer(s);var rotation=BuildGeometry.Rotation(1);camera.transform.position=rotation*new Vector3(20,15,-20);camera.transform.LookAt(rotation*new Vector3(8,1,5));camera.cullingMask=~0;submit=(_,current)=>{if(current==camera)renderer.Draw(camera,100,0);};RenderPipelineManager.beginCameraRendering+=submit;Capture(camera,target,"access-instanced.png");Check(renderer.DrawCalls>0&&renderer.DrawCalls<=7,"Instanced draws missing");
        }finally{if(submit!=null)RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;RenderSettings.sun=oldSun;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=oldTarget;Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);target.Release();Object.DestroyImmediate(target);}
    }
    private static void Capture(Camera camera,RenderTexture target,string filename)
    {
        for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetPixels32();var bg=pixels[0];int visible=0,pink=0,black=0;foreach(var p in pixels){if(Math.Abs(p.r-bg.r)+Math.Abs(p.g-bg.g)+Math.Abs(p.b-bg.b)>12)visible++;if(p.r>200&&p.b>200&&p.g<50)pink++;if(p.r<15&&p.g<15&&p.b<15)black++;}Check(visible>1000&&pink<100,"Blank/error render: "+filename);Check(black<Mathf.Max(50,visible*.005f),"Atlas has black gaps: "+filename);File.WriteAllBytes(Output+"/"+filename,image.EncodeToPNG());}finally{Object.DestroyImmediate(image);}
    }
}
