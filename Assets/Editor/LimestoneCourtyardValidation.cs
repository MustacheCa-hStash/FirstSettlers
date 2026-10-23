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
public static class LimestoneCourtyardValidation
{
    public const string Output="ArtReferences/LimestoneBuildKit/Courtyard";
    private const string Pending="FS.Courtyard.Validation";private static int checks;private static readonly List<string> errors=new();
    static LimestoneCourtyardValidation()=>EditorApplication.playModeStateChanged+=State;
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
                var originals=new Dictionary<string,string>();foreach(var d in c.presets)if(Array.IndexOf(LimestoneCourtyardSetup.Ids,d.contentId)<0)originals.Add(AssetDatabase.GetAssetPath(d),File.ReadAllText(AssetDatabase.GetAssetPath(d)));
                LimestoneCourtyardSetup.Install();int count=c.presets.Length;var guids=new List<string>();foreach(var id in LimestoneCourtyardSetup.Ids)guids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(id))));
                LimestoneCourtyardSetup.Install();Check(c.presets.Length==count,"Reinstall added duplicates");int n=0;foreach(var id in LimestoneCourtyardSetup.Ids)Check(guids[n++]==AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(id))),"GUID changed");
                foreach(var original in originals)Check(File.ReadAllText(original.Key)==original.Value,"Unrelated definition changed: "+original.Key);
            }
            Assets(c);Render(c);SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog c)
    {
        var ids=new HashSet<string>();foreach(var d in c.presets)Check(d!=null&&ids.Add(d.contentId),"Missing or duplicate catalog entry");
        var manifest=JsonUtility.FromJson<LimestoneCourtyardSetup.Manifest>(File.ReadAllText(LimestoneCourtyardSetup.ManifestPath));Check(manifest.parts.Length==9,"Eight parts plus gable flip required");
        var material=AssetDatabase.LoadAssetAtPath<Material>(LimestoneCourtyardSetup.MaterialPath);Check(material&&material.enableInstancing&&material.GetTexture("_BumpMap")&&material.GetFloat("_AlphaClip")==0,"Opaque shared normal/AO material missing");
        var colour=new Texture2D(2,2,TextureFormat.RGBA32,false,true);colour.LoadImage(File.ReadAllBytes(LimestoneCourtyardSetup.Colour));
        foreach(var p in manifest.parts)
        {
            var d=c.Find("build.stone."+p.slug);Check(d&&d.preserveAuthoredMesh&&d.material==material&&d.jointCoverVariants.Length==0,"Authored stone contract missing");
            var installed=BuildKitDimensions.InstalledBounds(d,LimestoneCourtyardSetup.V(p.minimum),LimestoneCourtyardSetup.V(p.size));Near(d.LocalBounds.min,installed.min,"Definition min");Near(d.LocalBounds.size,installed.size,"Definition size");
            Near(d.mesh.bounds.min,d.LocalBounds.min,"Imported min");Near(d.mesh.bounds.max,d.LocalBounds.max,"Imported max");Check(d.mesh.subMeshCount==1&&d.mesh.triangles.Length/3==p.triangles,"Imported geometry differs");
            Check(d.authoringPrefab.GetComponents<MeshCollider>().Length==p.solids.Length&&d.occupiedVolumes.Length==p.solids.Length,"Physical convex decomposition differs");
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Actual mesh icon absent");Check(!BuildUiContent.Description(d).StartsWith("Choose this component",StringComparison.Ordinal),"Menu description absent");
            var v=d.mesh.vertices;var t=d.mesh.triangles;var uv=d.mesh.uv;var normals=d.mesh.normals;var tangents=d.mesh.tangents;
            Check(v.Length==uv.Length&&v.Length==normals.Length&&v.Length==tangents.Length,"Vertex attributes absent");
            for(int i=0;i<v.Length;i++){Check(float.IsFinite(uv[i].x)&&float.IsFinite(uv[i].y)&&uv[i].x>=0&&uv[i].x<=1&&uv[i].y>=0&&uv[i].y<=1,"Invalid atlas coordinate");Vector3 tangent=tangents[i];Check(Mathf.Abs(normals[i].magnitude-1)<.002f&&Mathf.Abs(tangent.magnitude-1)<.002f&&Mathf.Abs(Vector3.Dot(tangent,normals[i]))<.002f,"Invalid tangent basis");}
            for(int i=0;i<t.Length;i+=3){var cross=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]);Check(cross.sqrMagnitude>1e-14f&&Vector3.Dot(cross,normals[t[i]]+normals[t[i+1]]+normals[t[i+2]])>0,"Degenerate/reversed face");var a=uv[t[i+1]]-uv[t[i]];var b=uv[t[i+2]]-uv[t[i]];Check(Mathf.Abs(a.x*b.y-a.y*b.x)>1e-12f,"Collapsed UV triangle");}
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
            if(d.IsHalfGable)Check(d.flipVariant&&d.flipVariant.flipVariant==d&&d.solidBoxes.Length==0,"Gable triangular flip contract absent");
        }
        Check(Array.Find(c.presets,d=>d.contentId==LimestoneCourtyardSetup.FallingId)==null,"Gable flip is a duplicate menu tile");
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
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");for(int yaw=0;yaw<8;yaw++){for(int part=0;part<8;part++)Runtime(c,part,yaw);Gable(c,yaw);foreach(int part in new[]{3,4,6})Terrain(c,part,yaw);}
            Check(errors.Count==0,"Runtime errors: "+string.Join(" | ",errors));Directory.CreateDirectory(Output);
            File.WriteAllText(Output+"/unity-validation.json","{\"passed\":true,\"checks\":"+checks+",\"parts\":8,\"gableVariants\":2,\"placementCases\":64,\"doorWalks\":16,\"terrainCases\":24,\"headings\":8,\"urpRenders\":true,\"instancedRendering\":true}");Debug.Log("COURTYARD PASS: "+checks+" checks; eight components, arch clearance, physical traversal, terrain boundaries, coping and gable roof fitting.");Application.logMessageReceived-=Log;EditorApplication.Exit(0);
        }catch(Exception ex){Application.logMessageReceived-=Log;Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Runtime(BuildCatalog c,int part,int yaw)
    {
        var origin=new Vector3(6000+yaw*30,40,6000);var q=BuildGeometry.Rotation(yaw);using var s=new BuildSession();var frame=s.CreateFrame(origin,yaw);var foundation=s.Add(c.Find("build.prototype.foundation"),frame,default,0,true);
        var d=c.Find(LimestoneCourtyardSetup.Ids[part]);Vector3Int anchor=default;
        if(part==5){s.Add(c.Find(LimestoneCourtyardSetup.Ids[3]),frame,default,0,false);anchor=new Vector3Int(0,5,0);}
        if(part==7){s.Add(c.Find(LimestoneKitSetup.Ids[0]),frame,default,0,false);anchor=new Vector3Int(0,12,0);}
        var piece=s.Add(d,frame,anchor,0,false);Check(piece.Supported&&piece.Resolved.mesh==d.mesh&&piece.Resolved.auxiliaryMesh==null,"Component missing supported authored mesh: "+d.contentId);
        using var gameplay=new BuildGameplay(s);gameplay.Update(piece.Origin+q*new Vector3(2,1,1),32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(piece.Id,out var proxy),"Physical component absent");var cols=new List<Collider>();proxy.GetEnabledColliders(cols);Check(cols.Count==d.occupiedVolumes.Length,"Physical convex count differs");
        bool Ray(Vector3 p,Vector3 dir){foreach(var col in cols)if(col.Raycast(new Ray(piece.Origin+q*p,q*dir),out _,2))return true;return false;}
        if(part<3)
        {
            var hole=d.wallOpenings[0];Check(!Ray(hole.center+Vector3.back,Vector3.forward)&&!Ray(hole.center+Vector3.forward,Vector3.back),"Physical aperture blocked");Check(Ray(new Vector3(.5f,1,-1),Vector3.forward),"Jamb lost collision");
            if(part!=1){float spring=part==0?1.5f:1.75f;Check(!Ray(new Vector3(2,spring+.65f,-1),Vector3.forward),"Arch crown clearance lost");Check(Ray(new Vector3(2.65f,spring+.60f,-1),Vector3.forward),"Arch shoulder absent");}
            if(part==0)
            {
                var pad=GameObject.CreatePrimitive(PrimitiveType.Cube);pad.layer=GameplayLayers.WorldSolid;pad.transform.position=origin+new Vector3(2,-.1f,0);pad.transform.localScale=new Vector3(12,.2f,12);Physics.SyncTransforms();
                try{Walk(origin,q,false);Walk(origin,q,true);}finally{Object.DestroyImmediate(pad);Physics.SyncTransforms();}
            }
        }
        if(part==3)
        {
            var cap=c.Find(LimestoneCourtyardSetup.Ids[5]);var snap=BuildPlacement.Solve(cap,piece.Origin+q*new Vector3(2,1.25f,0),Vector3.up,piece,s.Frame(piece.OwnFrameId),yaw,0,default);Near(snap.Origin,origin+Vector3.up*1.25f,"Coping seat moved");
            var top=s.Add(cap,frame,new Vector3Int(0,5,0),0,false);Check(top.Supported,"Coping lacks wall bearing");Near(top.Origin+Vector3.up*cap.LocalBounds.max.y,origin+Vector3.up*1.5f,"Coping top differs from pier height");
        }
        s.Remove(foundation.Id);Check(piece.Supported,"Free placement unexpectedly depends on structural support");
    }
    private static void Walk(Vector3 origin,Quaternion q,bool reverse)
    {
        var rig=new GameObject("L10 doorway traversal");var cc=rig.AddComponent<CharacterController>();cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.skinWidth=.08f;cc.stepOffset=.3f;cc.minMoveDistance=0;rig.transform.SetPositionAndRotation(origin+q*new Vector3(2,.03f,reverse?1.5f:-1.5f),q);var motor=rig.AddComponent<CharacterMotor>();Physics.SyncTransforms();
        try{for(int i=0;i<20;i++)motor.Simulate(default,1f/60);for(int i=0;i<52;i++)motor.Simulate(new CharacterMoveCommand(new Vector2(0,reverse?-1:1),false,false,false),1f/60);var p=Quaternion.Inverse(q)*(rig.transform.position-origin);Check(reverse?p.z<-.8f:p.z>.8f,"Character blocked in arch: "+p);Check(Mathf.Abs(p.y)<.15f,"Doorway floor height changed");}finally{Object.DestroyImmediate(rig);Physics.SyncTransforms();}
    }
    private static void Gable(BuildCatalog c,int yaw)
    {
        var host=new GameObject("L17 actual placement");var w=host.AddComponent<BuildWorld>();w.enabled=false;var s=w.Session;var origin=new Vector3(7000+yaw*30,40,7000);var q=BuildGeometry.Rotation(yaw);var f=s.CreateFrame(origin,yaw);
        try
        {
            s.Add(c.Find("build.prototype.foundation"),f,default,0,true);var wall=s.Add(c.Find(LimestoneKitSetup.Ids[0]),f,default,0,false);var d=c.Find(LimestoneCourtyardSetup.Ids[7]);BuildPieceRecord first=null;
            foreach(var g in new[]{d,d.flipVariant}){bool fall=g.gableSlope==GableSlope.Falling;var snap=BuildPlacement.Solve(g,origin+q*new Vector3(fall?3:1,2.9f,0),q*Vector3.forward,wall,s.Frame(wall.OwnFrameId),yaw,0,default);w.Validate(ref snap);Check(snap.Valid,"L17 placement rejected: "+snap.Message);var a=w.Commit(snap);Check(a!=null&&a.Supported,"Gable failed commit");if(first==null)first=a;Check(!Hit(g.mesh,new Vector3(fall?1.5f:.5f,1.5f,-1)),"Gable empty half filled");}
            foreach(var roof in new[]{c.Find(ThatchRoofSetup.ContentId),c.Find(TimberInfillKitSetup.Ids[4])}){var snap=BuildPlacement.Solve(roof,first.Origin+q*new Vector3(1,1,0),q*Vector3.forward,first,s.Frame(first.OwnFrameId),yaw,0,default,first.Origin+q*new Vector3(1,1,2));w.Validate(ref snap);Check(snap.Valid,"Roof rejected on limestone gable: "+snap.Message);var r=w.Commit(snap);Check(r!=null&&r.Supported,"Roof on L17 lacks support");w.Remove(r.Id);}
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Terrain(BuildCatalog c,int part,int yaw)
    {
        var origin=new Vector3(8000+yaw*30,30,8000);var q=BuildGeometry.Rotation(yaw);var data=new TerrainData{heightmapResolution=33,size=new Vector3(12,4,12)};data.SetHeights(0,0,new float[33,33]);var ground=UnityEngine.Terrain.CreateTerrainGameObject(data);ground.layer=GameplayLayers.WorldSolid;ground.transform.position=origin-new Vector3(6,0,6);Physics.SyncTransforms();var host=new GameObject("Courtyard terrain fixture");var w=host.AddComponent<BuildWorld>();w.enabled=false;
        try{var d=c.Find(LimestoneCourtyardSetup.Ids[part]);var p=BuildPlacement.Solve(d,origin+q*new Vector3(d.LocalBounds.center.x,0,0),Vector3.up,null,null,yaw,0,default);w.Validate(ref p);Check(p.Valid&&!p.Grounded,"Boundary cannot ground on terrain: "+p.Message);var floating=p;floating.Origin+=Vector3.up;w.Validate(ref floating);Check(floating.Valid&&!floating.Grounded,"Floating courtyard grounded");Check(w.Commit(p)!=null,"Terrain boundary commit rejected");}finally{Object.DestroyImmediate(host);Object.DestroyImmediate(ground);Object.DestroyImmediate(data);Physics.SyncTransforms();}
    }
    private static void Render(BuildCatalog c)
    {
        Directory.CreateDirectory(Output);var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;var oldSun=RenderSettings.sun;bool oldAsync=ShaderUtil.allowAsyncCompilation;var oldTarget=RenderTexture.active;
        var root=new GameObject("Courtyard render fixture");var cameraObject=new GameObject("Courtyard camera");var lightObject=new GameObject("Courtyard sun");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.aspect=16f/9;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.19f,.2f);cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(28,-40,0);var target=new RenderTexture(1600,900,24);target.Create();Action<ScriptableRenderContext,Camera> submit=null;
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.3f,.3f,.3f);RenderSettings.sun=light;ShaderUtil.allowAsyncCompilation=false;
            var mat=c.Find(LimestoneCourtyardSetup.Ids[0]).material;for(int i=0;i<mat.passCount;i++)ShaderUtil.CompilePass(mat,i,true);Check(!ShaderUtil.ShaderHasError(mat.shader),"Shader compile failed");var objects=new List<GameObject>();
            for(int i=0;i<8;i++)
            {
                var d=c.Find(LimestoneCourtyardSetup.Ids[i]);var obj=new GameObject(d.displayName){layer=31};obj.transform.SetParent(root.transform);obj.AddComponent<MeshFilter>().sharedMesh=d.mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=mat;objects.Add(obj);var centre=d.LocalBounds.center;
                camera.orthographicSize=Mathf.Max(1.1f,d.LocalBounds.size.magnitude*.42f);camera.transform.position=centre+new Vector3(4,2,-8);camera.transform.LookAt(centre);Capture(camera,target,"L"+(i+10)+"-unity-front.png");camera.transform.position=centre+new Vector3(-4,2,8);camera.transform.LookAt(centre);Capture(camera,target,"L"+(i+10)+"-unity-back.png");obj.SetActive(false);
            }
            for(int i=0;i<8;i++){objects[i].transform.position=new Vector3(i%3*6,0,i/3*5);objects[i].SetActive(true);}camera.orthographicSize=7.5f;camera.transform.position=new Vector3(20,15,-20);camera.transform.LookAt(new Vector3(8,1,5));Capture(camera,target,"courtyard-unity-overview.png");foreach(var obj in objects)obj.SetActive(false);
            using var s=new BuildSession();var f=s.CreateFrame(Vector3.zero,1);for(int i=0;i<8;i++)s.Add(c.Find(LimestoneCourtyardSetup.Ids[i]),f,new Vector3Int(i%3*24,0,i/3*20),0,false);var renderer=new BuildRenderer(s);var rotation=BuildGeometry.Rotation(1);camera.transform.position=rotation*new Vector3(20,15,-20);camera.transform.LookAt(rotation*new Vector3(8,1,5));camera.cullingMask=~0;submit=(_,current)=>{if(current==camera)renderer.Draw(camera,100,0);};RenderPipelineManager.beginCameraRendering+=submit;Capture(camera,target,"courtyard-instanced.png");Check(renderer.DrawCalls>0&&renderer.DrawCalls<=8,"Instanced draws missing");
        }finally{if(submit!=null)RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;RenderSettings.sun=oldSun;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=oldTarget;Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);target.Release();Object.DestroyImmediate(target);}
    }
    private static void Capture(Camera camera,RenderTexture target,string filename)
    {
        for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetPixels32();var bg=pixels[0];int visible=0,pink=0;foreach(var p in pixels){if(Math.Abs(p.r-bg.r)+Math.Abs(p.g-bg.g)+Math.Abs(p.b-bg.b)>12)visible++;if(p.r>200&&p.b>200&&p.g<50)pink++;}Check(visible>1000&&pink<100,"Blank/error render: "+filename);File.WriteAllBytes(Output+"/"+filename,image.EncodeToPNG());}finally{Object.DestroyImmediate(image);}
    }
}
