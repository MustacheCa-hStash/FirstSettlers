using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class StoneCompletionKitValidation
{
    private const string Pending="FS.StoneCompletion.Validation";
    private static int checks,walks;
    private static readonly List<string> errors=new();
    static StoneCompletionKitValidation()=>EditorApplication.playModeStateChanged+=State;
    private static void Check(bool yes,string text){checks++;if(!yes)throw new InvalidOperationException(text);}
    private static void Near(Vector3 a,Vector3 b,string text)=>Check((a-b).sqrMagnitude<.000001f,text+": "+a+" / "+b);
    public static void InstallAndRunBatch()=>Run(true);
    public static void RunInstalledBatch()=>Run(false);
    private static void Run(bool install)
    {
        try
        {
            checks=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var c=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            if(install)
            {
                var originals=new Dictionary<string,string>();foreach(var d in c.presets){bool own=false;foreach(var slug in StoneCompletionKitSetup.Slugs)own|=d.contentId==StoneCompletionKitSetup.Id(slug);if(!own)originals.Add(AssetDatabase.GetAssetPath(d),File.ReadAllText(AssetDatabase.GetAssetPath(d)));}
                StoneCompletionKitSetup.Install();int count=c.presets.Length;var guids=new Dictionary<string,string>();foreach(var slug in StoneCompletionKitSetup.Slugs)guids[slug]=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(StoneCompletionKitSetup.Id(slug))));
                StoneCompletionKitSetup.Install();Check(c.presets.Length==count,"Reinstall duplicated menu entries");foreach(var pair in guids)Check(pair.Value==AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(StoneCompletionKitSetup.Id(pair.Key)))),"Reinstall changed GUID");
                foreach(var pair in originals)Check(pair.Value==File.ReadAllText(pair.Key),"Unrelated definition changed: "+pair.Key);
            }
            Assets(c);Render(c);SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog c)
    {
        var ids=new HashSet<string>();foreach(var d in c.presets)Check(d!=null&&ids.Add(d.contentId),"Missing/duplicate catalog ID");
        var source=JsonUtility.FromJson<StoneCompletionKitSetup.Manifest>(File.ReadAllText(StoneCompletionKitSetup.Art+"/stone-completion-models.json"));Check(source.parts.Length==17&&StoneCompletionKitSetup.PresentCount(c)==16,"Expected sixteen pieces plus opposite gable");
        var material=AssetDatabase.LoadAssetAtPath<Material>(StoneCompletionKitSetup.MaterialPath);Check(material!=null&&material.enableInstancing&&material.GetTexture("_BaseMap")&&material.GetTexture("_BumpMap")&&material.GetFloat("_AlphaClip")==0,"Shared opaque AO/normal material missing");
        foreach(var p in source.parts)
        {
            var d=c.Find(StoneCompletionKitSetup.Id(p.slug));Check(d!=null&&d.preserveAuthoredMesh&&d.material==material,"Authored part missing: "+p.tag);
            Near(d.LocalBounds.min,StoneCompletionKitSetup.V(p.minimum),"Logical anchor");Near(d.LocalBounds.size,StoneCompletionKitSetup.V(p.size),"Logical size");
            if(!p.staticLeaf){Near(d.mesh.bounds.min,d.LocalBounds.min,"Mesh min");Near(d.mesh.bounds.max,d.LocalBounds.max,"Mesh max");}
            else{Near(d.mesh.bounds.min,Vector3.zero,"Static leaf base");Check(Mathf.Abs(d.mesh.bounds.size.x-1.2f)<.001f&&Mathf.Abs(d.mesh.bounds.size.z-.08f)<.001f,"Fitted static leaf dimensions");Check(d.authoringPrefab.GetComponents<MonoBehaviour>().Length==0&&d.authoringPrefab.GetComponent<Animator>()==null&&d.authoringPrefab.GetComponent<Rigidbody>()==null,"Static leaf has interactive behaviour");}
            Check(d.mesh.subMeshCount==1&&d.mesh.triangles.Length/3==p.triangles,"Runtime mesh count changed");
            Check(d.jointCoverVariants.Length==0,"Stone gained automatic timber posts");
            Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh==d.mesh&&d.authoringPrefab.GetComponent<MeshRenderer>().sharedMaterial==material,"Prefab references differ");
            Check(d.authoringPrefab.GetComponents<MeshCollider>().Length==(p.steps>0?1:p.solids.Length),"Prefab collision differs");
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Thumbnail missing");Check(!BuildUiContent.Description(d).StartsWith("Choose this component",StringComparison.Ordinal),"Description missing");
            var v=d.mesh.vertices;var t=d.mesh.triangles;var uv=d.mesh.uv;var normals=d.mesh.normals;var tangents=d.mesh.tangents;
            Check(v.Length==uv.Length&&v.Length==normals.Length&&v.Length==tangents.Length,"Missing attributes");
            for(int i=0;i<v.Length;i++){Check(float.IsFinite(uv[i].x)&&float.IsFinite(uv[i].y)&&uv[i].x>=0&&uv[i].x<=1&&uv[i].y>=0&&uv[i].y<=1,"Invalid atlas UV");Vector3 tangent=tangents[i];Check(Mathf.Abs(normals[i].magnitude-1)<.002f&&Mathf.Abs(tangent.magnitude-1)<.003f&&Mathf.Abs(Vector3.Dot(tangent,normals[i]))<.003f,"Invalid tangent basis");}
            for(int i=0;i<t.Length;i+=3){var cross=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]);Check(cross.sqrMagnitude>1e-14f&&Vector3.Dot(cross,normals[t[i]]+normals[t[i+1]]+normals[t[i+2]])>0,"Degenerate/inverted face: "+p.tag);var a=uv[t[i+1]]-uv[t[i]];var b=uv[t[i+2]]-uv[t[i]];Check(Mathf.Abs(a.x*b.y-a.y*b.x)>1e-12f,"Collapsed UV");}
            foreach(var hull in d.occupiedVolumes)
            {
                var hv=hull.vertices;var ht=hull.mesh.triangles;double volume=0;var edges=new Dictionary<string,int>();
                for(int i=0;i<ht.Length;i+=3){volume+=Vector3.Dot(hv[ht[i]],Vector3.Cross(hv[ht[i+1]],hv[ht[i+2]]))/6.0;foreach(var pair in new[]{(ht[i],ht[i+1]),(ht[i+1],ht[i+2]),(ht[i+2],ht[i])}){string key=Math.Min(pair.Item1,pair.Item2)+"/"+Math.Max(pair.Item1,pair.Item2);edges.TryGetValue(key,out int n);edges[key]=n+1;}}
                Check(volume>0,"Hull winding invalid");foreach(int n in edges.Values)Check(n==2,"Open collision hull");
            }
            if(p.opening?.Length==4)for(int x=0;x<7;x++)for(int y=0;y<7;y++)
            {var point=new Vector3(Mathf.Lerp(p.opening[0]+.04f,p.opening[2]-.04f,(x+.5f)/7),Mathf.Lerp(p.opening[1]+.04f,p.opening[3]-.04f,(y+.5f)/7),-1);Check(!MeshHit(d.mesh,new Ray(point,Vector3.forward)),"Mesh blocks fixed opening");}
            if(p.steps>0)Check(d.collisionMesh&&d.authoringPrefab.GetComponent<SmoothWalkSurface>(),"Smooth stone stair collision missing");
        }
        foreach(var id in new[]{ThatchRoofSetup.ContentId,"build.prototype.floor",StairExtensionSetup.W24Id})Check(c.Find(id)!=null,"Missing reused S13/S14/S22 asset");
    }
    private static bool MeshHit(Mesh mesh,Ray ray)
    {
        var v=mesh.vertices;var t=mesh.triangles;
        for(int i=0;i<t.Length;i+=3){var a=v[t[i]];var e=v[t[i+1]]-a;var f=v[t[i+2]]-a;var h=Vector3.Cross(ray.direction,f);float det=Vector3.Dot(e,h);if(Mathf.Abs(det)<1e-8f)continue;var s=ray.origin-a;float u=Vector3.Dot(s,h)/det;if(u<0||u>1)continue;var q=Vector3.Cross(s,e);float w=Vector3.Dot(ray.direction,q)/det;if(w<0||u+w>1)continue;if(Vector3.Dot(f,q)/det>=0)return true;}
        return false;
    }
    private static void State(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;SessionState.SetBool(Pending,false);checks=SessionState.GetInt(Pending+".checks",0);walks=0;errors.Clear();
        void Log(string m,string trace,LogType type){if(type is LogType.Error or LogType.Exception or LogType.Assert)errors.Add(m);}Application.logMessageReceived+=Log;
        try
        {
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            for(int yaw=0;yaw<8;yaw++)
            {
                Runtime(c,yaw);Openings(c,yaw);Plate(c,yaw);Guards(c,yaw);
                var walk=typeof(W21StairValidation).GetMethod("Walk",BindingFlags.NonPublic|BindingFlags.Static);
                foreach(var slug in new[]{"s17-threshold-steps","s18-half-storey-stair","s19-full-storey-stair"})foreach(float x in new[]{.43f,.625f,.82f})foreach(bool down in new[]{false,true}){walk.Invoke(null,new object[]{c.Find(StoneCompletionKitSetup.Id(slug)),c,yaw,x,down,1f/60,1});walks++;Check(true,"Motor traversal");}
                foreach(bool down in new[]{false,true}){walk.Invoke(null,new object[]{c.Find("build.stone.s18-half-storey-stair"),c,yaw,.625f,down,1f/60,2});walks++;Check(true,"Two-flight motor traversal");}
            }
            Check(errors.Count==0,"Runtime errors: "+string.Join(" | ",errors));File.WriteAllText(StoneCompletionKitSetup.Art+"/unity-validation.json",JsonUtility.ToJson(new Proof{passed=true,checks=checks,parts=16,headings=8,motorWalks=walks},true));
            Application.logMessageReceived-=Log;Debug.Log("STONE COMPLETION PASS: "+checks+" checks, 16 pieces, 8 headings and "+walks+" real motor traversals.");EditorApplication.Exit(0);
        }catch(Exception ex){Application.logMessageReceived-=Log;Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    [Serializable]private class Proof{public bool passed;public int checks,parts,headings,motorWalks;}
    private static void Runtime(BuildCatalog c,int yaw)
    {
        var host=new GameObject("Stone completion placement");var w=host.AddComponent<BuildWorld>();w.enabled=false;var s=w.Session;var origin=new Vector3(10000+yaw*50,40,10000);var q=BuildGeometry.Rotation(yaw);
        try
        {
            foreach(var slug in StoneCompletionKitSetup.Slugs)
            {
                var d=c.Find(StoneCompletionKitSetup.Id(slug));var preview=BuildPlacement.Solve(d,origin,Vector3.up,null,null,yaw,0,default);w.Validate(ref preview);Check(preview.Valid,"Free placement rejected "+slug+": "+preview.Message);var piece=w.Commit(preview);Check(piece!=null&&piece.Supported,"Commit failed");
                using(var gameplay=new BuildGameplay(s)){gameplay.Update(piece.Origin,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(piece.Id,out var proxy),"Proxy missing");var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);Check(colliders.Count==(d.kind==BuildPartKind.Stair?1:d.occupiedVolumes.Length),"Runtime collision differs");if(d.kind==BuildPartKind.Stair)Check(proxy.RampShape&&proxy.RampShape.sharedMesh==d.collisionMesh&&proxy.GetComponent<SmoothWalkSurface>(),"Stair ramp missing");}
                Check(w.Remove(piece.Id),"Removal failed");
            }
            var gable=c.Find("build.stone.s15-stone-gable-rising");Check(gable.flipVariant.flipVariant==gable,"Gable flip does not round trip");
            var frame=s.CreateFrame(origin,yaw);var a=s.Add(gable,frame,default,0,false);var b=s.Add(gable.flipVariant,frame,new Vector3Int(8,0,0),0,false);
            var empty=new Vector3(.1f,1.8f,0);foreach(var volume in a.Resolved.volumes)Check(!BuildOccupancy.Contains(volume,empty),"Gable fills empty attic corner");s.Remove(a.Id);s.Remove(b.Id);
            var steps=c.Find("build.stone.s19-full-storey-stair");var stair=s.Add(steps,frame,default,0,false);
            bool filled=false;foreach(var volume in stair.Resolved.volumes)filled|=BuildOccupancy.Contains(volume,new Vector3(.625f,.1f,3.8f));Check(filled,"Stone stair is hollow below rear treads");s.Remove(stair.Id);
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Openings(BuildCatalog c,int yaw)
    {
        var host=new GameObject("Static opening fixture");var w=host.AddComponent<BuildWorld>();w.enabled=false;var s=w.Session;var origin=new Vector3(11000+yaw*50,40,11000);var q=BuildGeometry.Rotation(yaw);var frame=s.CreateFrame(origin,yaw);
        try
        {
            foreach(var pair in new[]{("build.stone.s09-timber-lintel-doorway","build.wood.s10-static-plank-door"),("build.stone.s12-gate-bay","build.wood.s12-static-yard-gate"),("build.stone.s11-open-window-wall","")})
            {
                var wall=s.Add(c.Find(pair.Item1),frame,default,0,false);var aperture=wall.Definition.wallOpenings[0];
                using var gameplay=new BuildGameplay(s);gameplay.Update(origin,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(wall.Id,out var proxy),"Opening proxy missing");var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);
                var ray=new Ray(origin+q*(aperture.center+Vector3.back),q*Vector3.forward);foreach(var col in colliders)Check(!col.Raycast(ray,out _,2),"Collider blocks fixed opening");
                if(pair.Item2.Length>0)
                {
                    var leaf=c.Find(pair.Item2);var preview=BuildPlacement.Solve(leaf,origin+q*new Vector3(.5f,.5f,-.25f),q*Vector3.back,wall,s.Frame(wall.OwnFrameId),yaw,0,default);w.Validate(ref preview);Check(preview.Valid,"Static leaf fit rejected");
                    Near(preview.Origin+q*leaf.mesh.bounds.center,origin+q*aperture.center,"Static leaf not centred in aperture");
                    var reverse=BuildPlacement.Solve(leaf,origin,q*Vector3.back,wall,s.Frame(wall.OwnFrameId),yaw,4,default);Near(reverse.Origin+BuildGeometry.Rotation(reverse.WorldYaw)*leaf.mesh.bounds.center,origin+q*aperture.center,"Reversing static leaf changed aperture centre");
                    var placed=w.Commit(preview);Check(placed!=null,"Static leaf commit failed");
                    gameplay.Update(origin,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(placed.Id,out var leafProxy),"Static leaf proxy missing");colliders.Clear();leafProxy.GetEnabledColliders(colliders);bool blocked=false;foreach(var col in colliders)blocked|=col.Raycast(ray,out _,2);Check(blocked,"Fixed leaf has no blocking collision");w.Remove(placed.Id);
                }
                w.Remove(wall.Id);
            }
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Plate(BuildCatalog c,int yaw)
    {
        var host=new GameObject("Stone timber plate fixture");var w=host.AddComponent<BuildWorld>();w.enabled=false;var s=w.Session;var origin=new Vector3(12000+yaw*50,40,12000);var q=BuildGeometry.Rotation(yaw);var frame=s.CreateFrame(origin,yaw);
        try
        {
            foreach(var id in new[]{"build.stone.s01-rubble-wall","build.stone.s02-coursed-wall","build.stone.s09-timber-lintel-doorway","build.stone.s11-open-window-wall"})
            {
                var wall=s.Add(c.Find(id),frame,default,0,false);var original=wall.Resolved.mesh;var d=c.Find("build.wood.s16-timber-wall-plate");Check(BuildTopCourse.Compatible(wall.Definition,d),"Stone wall declined equal-width plate");
                var preview=BuildPlacement.Solve(d,origin+q*new Vector3(2,3,0),Vector3.up,wall,s.Frame(wall.OwnFrameId),yaw,0,default);Near(preview.Origin,origin+Vector3.up*3,"Plate changed eave datum");w.Validate(ref preview);Check(preview.Valid,"Plate rejected");var plate=w.Commit(preview);Check(plate!=null,"Plate commit failed");
                Check(wall.Resolved.mesh!=original&&Mathf.Abs(wall.Resolved.mesh.bounds.max.y-2.75f)<.001f,"Stone wall top not replaced");Check(!BuildResolvedGeometry.Overlaps(wall,wall.Resolved,plate,plate.Resolved),"Plate and old course overlap");
                var roof=BuildPlacement.Solve(c.Find(ThatchRoofSetup.ContentId),plate.Origin+q*new Vector3(2,0,0),Vector3.up,plate,s.Frame(plate.OwnFrameId),yaw,0,default,origin+q*new Vector3(3,4,2));Near(roof.Origin,origin+Vector3.up*3,"Roof raised by plate");
                w.Remove(plate.Id);Check(wall.Resolved.mesh==original,"Plate removal did not restore wall");w.Remove(wall.Id);
            }
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Guards(BuildCatalog c,int yaw)
    {
        var origin=new Vector3(13000+yaw*50,40,13000);var q=BuildGeometry.Rotation(yaw);using var s=new BuildSession();var f=s.CreateFrame(origin,yaw);var stair=s.Add(c.Find("build.stone.s19-full-storey-stair"),f,default,0,false);var d=c.Find("build.stone.s21-stepped-stone-guard");
        foreach(float side in new[]{-1f,1f})foreach(float z in new[]{1f,3f})
        {
            var p=BuildPlacement.Solve(d,origin+q*new Vector3(side<0?0:1.25f,z*.75f,z),q*Vector3.right*side,stair,s.Frame(stair.OwnFrameId),yaw,0,default);
            var local=Quaternion.Inverse(q)*(p.Origin-origin);Check(Mathf.Abs(local.x-(side<0?-.5f:1.25f))<.001f,"Guard occupies walking strip");Check(Mathf.Abs(local.z-(z>2?2:0))<.001f&&Mathf.Abs(local.y-(z>2?1.5f:0))<.001f,"Guard segment datum");
        }
        foreach(int i in new[]{0,3,7}){var point=new Vector3(.25f,1+(i+1)*.1875f-.01f,(i+.5f)*.25f);bool inside=false;foreach(var volume in d.occupiedVolumes)inside|=BuildOccupancy.Contains(volume,point);Check(inside,"Stepped guard lower than 1 m above tread");}
    }
    private static void Render(BuildCatalog c)
    {
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;var oldMode=RenderSettings.ambientMode;var oldAmbient=RenderSettings.ambientLight;var oldSun=RenderSettings.sun;bool oldAsync=ShaderUtil.allowAsyncCompilation;var oldTarget=RenderTexture.active;
        var root=new GameObject("Stone completion renders");var cameraObject=new GameObject("Stone completion camera");var lightObject=new GameObject("Stone completion sun");var cam=cameraObject.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.nearClipPlane=.01f;cam.farClipPlane=200;cam.aspect=1.6f;cam.cullingMask=1<<31;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.18f,.20f,.21f);cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.shadows=LightShadows.None;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(40,-35,0);var rt=new RenderTexture(1920,1200,24,RenderTextureFormat.ARGB32);rt.Create();Action<ScriptableRenderContext,Camera> submit=null;
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");Check(pipeline!=null,"URP missing");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.4f,.4f,.4f);RenderSettings.sun=light;ShaderUtil.allowAsyncCompilation=false;
            var objects=new List<GameObject>();foreach(var slug in StoneCompletionKitSetup.Slugs)
            {
                var d=c.Find(StoneCompletionKitSetup.Id(slug));for(int i=0;i<d.material.passCount;i++)ShaderUtil.CompilePass(d.material,i,true);Check(!ShaderUtil.ShaderHasError(d.material.shader),"Shader error");var ob=new GameObject(slug){layer=31};ob.transform.SetParent(root.transform);ob.AddComponent<MeshFilter>().sharedMesh=d.mesh;ob.AddComponent<MeshRenderer>().sharedMaterial=d.material;objects.Add(ob);
                cam.orthographicSize=2.7f;cam.transform.position=d.mesh.bounds.center+new Vector3(5,3,-8);cam.transform.LookAt(d.mesh.bounds.center);Capture(cam,rt,slug+"-unity.png");cam.transform.position=d.mesh.bounds.center+new Vector3(-5,2,8);cam.transform.LookAt(d.mesh.bounds.center);Capture(cam,rt,slug+"-unity-back.png");ob.SetActive(false);
            }
            for(int i=0;i<objects.Count;i++){objects[i].transform.position=new Vector3(i%4*6,0,i/4*6);objects[i].SetActive(true);}cam.orthographicSize=13;cam.transform.position=new Vector3(33,30,-30);cam.transform.LookAt(new Vector3(10,1,9));Capture(cam,rt,"stone-completion-unity-overview.png");foreach(var ob in objects)ob.SetActive(false);
            using var session=new BuildSession();var frame=session.CreateFrame(Vector3.zero,1);for(int i=0;i<StoneCompletionKitSetup.Slugs.Length;i++)session.Add(c.Find(StoneCompletionKitSetup.Id(StoneCompletionKitSetup.Slugs[i])),frame,new Vector3Int(i%4*24,0,i/4*24),0,false);var renderer=new BuildRenderer(session);var rotation=BuildGeometry.Rotation(1);cam.transform.position=rotation*new Vector3(33,30,-30);cam.transform.LookAt(rotation*new Vector3(10,1,9));cam.cullingMask=~0;submit=(_,camera)=>{if(camera==cam)renderer.Draw(cam,150,0);};RenderPipelineManager.beginCameraRendering+=submit;Capture(cam,rt,"stone-completion-instanced.png");Check(renderer.DrawCalls>0&&renderer.DrawCalls<=16,"Instanced draws missing");
        }finally{if(submit!=null)RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;RenderSettings.ambientMode=oldMode;RenderSettings.ambientLight=oldAmbient;RenderSettings.sun=oldSun;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=oldTarget;Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);rt.Release();Object.DestroyImmediate(rt);}
    }
    private static void Capture(Camera camera,RenderTexture target,string filename)
    {
        for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetPixels32();var bg=pixels[0];int visible=0,pink=0;foreach(var p in pixels){if(Math.Abs(p.r-bg.r)+Math.Abs(p.g-bg.g)+Math.Abs(p.b-bg.b)>12)visible++;if(p.r>200&&p.b>200&&p.g<50)pink++;}Check(visible>1000&&pink<100,"Blank/error render: "+filename);File.WriteAllBytes(StoneCompletionKitSetup.Art+"/"+filename,image.EncodeToPNG());}finally{Object.DestroyImmediate(image);}
    }
}
