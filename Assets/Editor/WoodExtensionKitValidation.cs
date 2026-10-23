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
public static class WoodExtensionKitValidation
{
    private const string Pending="FS.WoodExtension.Validation";
    private static int checks;
    static WoodExtensionKitValidation()=>EditorApplication.playModeStateChanged+=State;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.00001f,message+": "+a+" / "+b);
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
                var originals=new Dictionary<string,string>();
                foreach(var d in c.presets)if(Array.IndexOf(WoodExtensionKitSetup.Slugs,d.name)<0)originals[AssetDatabase.GetAssetPath(d)]=File.ReadAllText(AssetDatabase.GetAssetPath(d));
                WoodExtensionKitSetup.Install();int count=c.presets.Length;
                var guids=new Dictionary<string,string>();foreach(var slug in WoodExtensionKitSetup.Slugs)guids[slug]=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(WoodExtensionKitSetup.Id(slug))));
                WoodExtensionKitSetup.Install();Check(count==c.presets.Length,"Reinstall duplicated entries");
                foreach(var pair in guids)Check(pair.Value==AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(WoodExtensionKitSetup.Id(pair.Key)))),"Definition GUID changed");
                foreach(var pair in originals)Check(pair.Value==File.ReadAllText(pair.Key),"Unrelated definition changed: "+pair.Key);
            }
            var ids=new HashSet<string>();foreach(var d in c.presets)Check(d!=null&&ids.Add(d.contentId),"Catalog contains duplicate/missing entries");
            var parts=JsonUtility.FromJson<WoodExtensionKitSetup.Manifest>(File.ReadAllText(WoodExtensionKitSetup.Art+"/wood-extension-models.json")).parts;
            foreach(var p in parts)
            {
                if(p.tag=="Cover")continue;var d=c.Find(WoodExtensionKitSetup.Id(p.slug));Check(d!=null,"Missing "+p.tag);
                Near(d.LocalBounds.min,WoodExtensionKitSetup.V(p.minimum),"Anchor "+p.tag);Near(d.LocalBounds.size,WoodExtensionKitSetup.V(p.size),"Size "+p.tag);
                Near(d.mesh.bounds.min,d.LocalBounds.min,"Mesh min "+p.tag);Near(d.mesh.bounds.max,d.LocalBounds.max,"Mesh max "+p.tag);
                Check(d.mesh.subMeshCount==1&&d.mesh.triangles.Length/3==p.triangles,"Imported mesh differs "+p.tag);
                Check(d.material.enableInstancing&&d.preserveAuthoredMesh,"Instanced authored mesh missing");
                Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh==d.mesh,"Prefab mesh differs");
                Check(d.authoringPrefab.GetComponents<BoxCollider>().Length==(p.tag=="W05"?10:1),"Prefab solids differ");
                Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Missing thumbnail");
                Check(!BuildUiContent.Description(d).StartsWith("Choose this component",StringComparison.Ordinal),"Missing description");
                Check(BuildUiContent.Category(d)==(p.tag=="W05"?1:p.tag=="W06"?4:2),"Wrong category");
                var uv=d.mesh.uv;var vertices=d.mesh.vertices;var triangles=d.mesh.triangles;var normals=d.mesh.normals;
                Check(uv.Length==vertices.Length&&normals.Length==vertices.Length,"Missing mesh attributes");
                foreach(var t in uv)Check(float.IsFinite(t.x)&&float.IsFinite(t.y)&&t.x>=0&&t.x<=1&&t.y>=0&&t.y<=1,"UV escaped shared atlas");
                for(int i=0;i<triangles.Length;i+=3)
                {
                    int a=triangles[i],b=triangles[i+1],e=triangles[i+2];
                    var cross=Vector3.Cross(vertices[b]-vertices[a],vertices[e]-vertices[a]);
                    Check(cross.sqrMagnitude>1e-14f,"Degenerate triangle "+p.tag);
                    Check(Vector3.Dot(cross,normals[a]+normals[b]+normals[e])>0,"Inverted triangle "+p.tag);
                    var u=uv[b]-uv[a];var v=uv[e]-uv[a];Check(Mathf.Abs(u.x*v.y-u.y*v.x)>1e-12f,"Collapsed UV "+p.tag);
                }
            }
            Render(c);SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void State(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=SessionState.GetInt(Pending+".checks",0);
        try
        {
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            for(int yaw=0;yaw<8;yaw++)Runtime(c,yaw);
            File.WriteAllText(WoodExtensionKitSetup.Art+"/unity-validation.json",JsonUtility.ToJson(new Proof{passed=true,checks=checks,parts=5,headings=8},true));
            Debug.Log("WOOD EXTENSION PASS: "+checks+" checks, five parts, eight headings, terrain/support/colliders/beam/roof lifecycle.");EditorApplication.Exit(0);
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    [Serializable] private class Proof {public bool passed;public int checks,parts,headings;}
    private static void Runtime(BuildCatalog c,int yaw)
    {
        var origin=new Vector3(6000+yaw*50,30,6000);var q=BuildGeometry.Rotation(yaw);
        var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.name="Wood extension test ground";ground.layer=GameplayLayers.WorldSolid;
        ground.transform.position=origin-Vector3.up*.5f;ground.transform.localScale=new Vector3(24,1,24);ground.AddComponent<WorldGroundSurface>();
        var host=new GameObject("Wood extension world");var world=host.AddComponent<BuildWorld>();world.enabled=false;
        var s=world.Session;Physics.SyncTransforms();
        try
        {
            var footing=c.Find(WoodExtensionKitSetup.Id(WoodExtensionKitSetup.Slugs[0]));var beam=c.Find(WoodExtensionKitSetup.Id(WoodExtensionKitSetup.Slugs[1]));
            var low=c.Find(WoodExtensionKitSetup.Id(WoodExtensionKitSetup.Slugs[2]));var narrow=c.Find(WoodExtensionKitSetup.Id(WoodExtensionKitSetup.Slugs[3]));var fence=c.Find(WoodExtensionKitSetup.Id(WoodExtensionKitSetup.Slugs[4]));
            foreach(var d in new[]{low,fence})
            {
                var p=BuildPlacement.Solve(d,origin,Vector3.up,null,null,yaw,0,default);world.Validate(ref p);
                Check(p.Valid&&!p.Grounded,"Boundary cannot start on terrain: "+d.name+" / "+p.Message);
                var piece=world.Commit(p);Check(piece!=null&&piece.Supported,"Boundary commit failed");
                var next=BuildPlacement.Solve(d,piece.Origin+q*new Vector3(4,.5f,0),q*Vector3.right,piece,s.Frame(piece.OwnFrameId),yaw,0,default);
                Near(next.Origin,piece.Origin+q*Vector3.right*4,"Boundary endpoint");
                var stacked=BuildPlacement.Solve(d,piece.Origin+q*new Vector3(2,1.25f,0),Vector3.up,piece,s.Frame(piece.OwnFrameId),yaw,0,default);
                Near(stacked.Origin,piece.Origin+Vector3.up*1.25f,"Low wall stack rise");s.Remove(piece.Id);
            }
            var invalid=BuildPlacement.Solve(beam,origin,Vector3.up,null,null,yaw,0,default);world.Validate(ref invalid);Check(invalid.Valid,"Free beam placement rejected");
            invalid=BuildPlacement.Solve(narrow,origin,Vector3.up,null,null,yaw,0,default);world.Validate(ref invalid);Check(invalid.Valid,"Free narrow wall placement rejected");
            var preview=BuildPlacement.Solve(footing,origin,Vector3.up,null,null,yaw,0,default);world.Validate(ref preview);Check(preview.Valid&&!preview.Grounded,"Sleeper footing ground placement: "+preview.Message);
            var basePiece=world.Commit(preview);var frame=s.Frame(basePiece.OwnFrameId);
            var paving=s.Add(c.Find("build.stone.s04-flagstone-floor"),frame,new Vector3Int(0,1,0),0,false);
            Check(paving.Supported,"Open sleepers falsely supply full paving bearing");s.Remove(paving.Id);
            var deck=s.Add(c.Find("build.prototype.floor"),frame,default,0,false);
            Check(deck.Supported,"Timber deck unsupported on sleepers");Check(basePiece.Resolved.mesh!=footing.mesh,"Sleeper top did not fit beneath floor");
            Check(!BuildResolvedGeometry.Overlaps(basePiece,basePiece.Resolved,deck,deck.Resolved),"Sleeper and deck retain coincident occupied layers");
            foreach(var a in basePiece.Resolved.boxes)foreach(var b in deck.Resolved.boxes)
                Check(!(Mathf.Abs(a.max.y-b.max.y)<.001f&&BuildResolution.PlanOverlap(a,b)),"Sleeper top is coplanar with floor top");
            s.Remove(deck.Id);Check(basePiece.Resolved.mesh==footing.mesh&&basePiece.Resolved.boxes.Length==10,"Sleeper does not restore after floor removal");
            var wall=s.Add(narrow,frame,default,0,false);Check(wall.Supported,"Sleeper does not support narrow wall");
            var beamSnap=BuildPlacement.Solve(beam,wall.Origin+Vector3.up*3,Vector3.up,wall,s.Frame(wall.OwnFrameId),yaw,0,default);world.Validate(ref beamSnap);
            Near(beamSnap.Origin,wall.Origin+Vector3.up*3,"Beam top datum");Check(beamSnap.Valid,"Beam cannot attach to wall: "+beamSnap.Message);
            var timber=world.Commit(beamSnap);Check(timber!=null&&timber.Supported,"Beam unsupported");
            var extension=BuildPlacement.Solve(beam,timber.Origin+q*new Vector3(4,-.125f,0),q*Vector3.right,timber,s.Frame(timber.OwnFrameId),yaw,0,default);world.Validate(ref extension);
            Near(extension.Origin,timber.Origin+q*Vector3.right*4,"Beam endpoint extension");Check(extension.Valid,"Beam continuation rejected");
            var extra=world.Commit(extension);Check(extra!=null&&extra.Supported,"Beam extension lost support");
            var roof=c.Find(ThatchRoofSetup.ContentId);
            var roofSnap=BuildPlacement.Solve(roof,timber.Origin+q*new Vector3(2,0,0),Vector3.up,timber,s.Frame(timber.OwnFrameId),yaw,0,default,timber.Origin+q*new Vector3(3,1,2));
            Near(roofSnap.Origin,timber.Origin,"Roof on beam datum");world.Validate(ref roofSnap);Check(roofSnap.Valid,"Roof cannot seat on beam: "+roofSnap.Message);
            var covering=world.Commit(roofSnap);Check(covering!=null&&covering.Supported,"Roof lost beam bearing");
            using(var gameplay=new BuildGameplay(s))
            {
                gameplay.Update(basePiece.Origin,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(basePiece.Id,out var proxy),"Sleeper proxy missing");
                var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);Check(colliders.Count==10,"Sleeper gaps filled by runtime collider");
                var gap=new Ray(basePiece.Origin+q*new Vector3(.5f,1,.5f),Vector3.down);foreach(var col in colliders)Check(!col.Raycast(gap,out _,2),"Sleeper gap is blocked");
            }
            var floor=s.Add(c.Find("build.prototype.floor"),frame,new Vector3Int(0,12,0),0,false);
            var underside=BuildPlacement.Solve(beam,floor.Origin+q*new Vector3(2,-.25f,2),Vector3.down,floor,s.Frame(floor.OwnFrameId),yaw,0,default);
            Check(Mathf.Abs(underside.Origin.y-(floor.Origin.y-.25f))<.001f,"Beam underside seating");s.Remove(floor.Id);
            s.Remove(basePiece.Id);Check(wall.Supported&&timber.Supported&&extra.Supported&&covering.Supported,"Free placement unexpectedly depends on structural support");
            using var mixed=new BuildSession();var mf=mixed.CreateFrame(origin,yaw);
            var shortWall=mixed.Add(low,mf,default,0,true);var tallWall=mixed.Add(narrow,mf,default,2,false);
            bool fullCover=false;foreach(var attachment in shortWall.Resolved.attachments)fullCover|=attachment.mesh.bounds.size.y>2.9f;foreach(var attachment in tallWall.Resolved.attachments)fullCover|=attachment.mesh.bounds.size.y>2.9f;
            Check(fullCover,"Mixed-height junction selected a short cover");
            using var reverse=new BuildSession();var rf=reverse.CreateFrame(origin,yaw);var tallFirst=reverse.Add(narrow,rf,default,2,false);var shortSecond=reverse.Add(low,rf,default,0,true);
            Check(tallWall.Resolved.attachments.Length==tallFirst.Resolved.attachments.Length&&shortWall.Resolved.attachments.Length==shortSecond.Resolved.attachments.Length,"Mixed-height cover ownership depends on insertion order");
        }finally{Object.DestroyImmediate(host);Object.DestroyImmediate(ground);Physics.SyncTransforms();}
    }
    private static void Render(BuildCatalog c)
    {
        Directory.CreateDirectory(WoodExtensionKitSetup.Art);
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
        var oldMode=RenderSettings.ambientMode;var oldAmbient=RenderSettings.ambientLight;var oldSun=RenderSettings.sun;var oldTarget=RenderTexture.active;bool oldAsync=ShaderUtil.allowAsyncCompilation;
        var root=new GameObject("Wood extension render");var cameraObject=new GameObject("Wood extension camera");var lightObject=new GameObject("Wood extension sun");
        var cam=cameraObject.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.nearClipPlane=.01f;cam.farClipPlane=100;cam.aspect=16f/9;cam.cullingMask=1<<31;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.17f,.19f,.20f);
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(35,-35,0);
        var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);rt.Create();
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");Check(pipeline!=null,"URP missing");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.35f,.35f);RenderSettings.sun=light;ShaderUtil.allowAsyncCompilation=false;
            var objects=new List<GameObject>();
            foreach(var slug in WoodExtensionKitSetup.Slugs)
            {
                var d=c.Find(WoodExtensionKitSetup.Id(slug));for(int i=0;i<d.material.passCount;i++)ShaderUtil.CompilePass(d.material,i,true);Check(!ShaderUtil.ShaderHasError(d.material.shader),"Shader error");
                var ob=new GameObject(slug){layer=31};ob.transform.SetParent(root.transform);ob.AddComponent<MeshFilter>().sharedMesh=d.mesh;ob.AddComponent<MeshRenderer>().sharedMaterial=d.material;objects.Add(ob);
                cam.orthographicSize=2.6f;cam.transform.position=d.mesh.bounds.center+new Vector3(5,3,-7);cam.transform.LookAt(d.mesh.bounds.center);Capture(cam,rt,slug+"-unity.png");
                cam.transform.position=d.mesh.bounds.center+new Vector3(-5,2,7);cam.transform.LookAt(d.mesh.bounds.center);Capture(cam,rt,slug+"-unity-back.png");ob.SetActive(false);
            }
            for(int i=0;i<objects.Count;i++){objects[i].transform.position=new Vector3(i%3*5.5f,0,i/3*5);objects[i].SetActive(true);}
            cam.orthographicSize=6.4f;cam.transform.position=new Vector3(19,15,-19);cam.transform.LookAt(new Vector3(7,1,2.5f));Capture(cam,rt,"wood-extension-unity-overview.png");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;RenderSettings.ambientMode=oldMode;RenderSettings.ambientLight=oldAmbient;RenderSettings.sun=oldSun;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=oldTarget;
            Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);rt.Release();Object.DestroyImmediate(rt);
        }
    }
    private static void Capture(Camera camera,RenderTexture target,string name)
    {
        for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try
        {
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetPixels32();var background=pixels[0];int visible=0,pink=0;
            foreach(var p in pixels){if(p.r>200&&p.b>200&&p.g<50)pink++;if(Math.Abs(p.r-background.r)+Math.Abs(p.g-background.g)+Math.Abs(p.b-background.b)>12)visible++;}
            Check(visible>1000&&pink<100,"Blank/error render: "+name);File.WriteAllBytes(WoodExtensionKitSetup.Art+"/"+name,image.EncodeToPNG());
        }finally{Object.DestroyImmediate(image);}
    }
}
