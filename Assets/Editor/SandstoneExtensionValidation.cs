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
public static class SandstoneExtensionValidation
{
    private const string Pending="FS.SandstoneExtension.Validation";
    private static int checks,walks,pockets;
    static SandstoneExtensionValidation()=>EditorApplication.playModeStateChanged+=State;
    private static void Check(bool yes,string message){checks++;if(!yes)throw new InvalidOperationException(message);}
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.000004f,message+": "+a+" / "+b);
    private static BuildDefinition D(BuildCatalog c,string tag)
    {foreach(var p in SandstoneExtensionSetup.Source().parts)if(p.tag==tag)return c.Find(SandstoneExtensionSetup.Id(p));throw new InvalidOperationException(tag);}
    public static void InstallAndRunBatch()
    {
        try
        {
            checks=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var c=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            var originals=new Dictionary<string,string>();var owned=new HashSet<string>();
            foreach(var p in SandstoneExtensionSetup.Source().parts)owned.Add(SandstoneExtensionSetup.Id(p));
            foreach(var d in c.presets)if(!owned.Contains(d.contentId))originals[AssetDatabase.GetAssetPath(d)]=File.ReadAllText(AssetDatabase.GetAssetPath(d));
            SandstoneExtensionSetup.Install();int count=c.presets.Length;var guids=new Dictionary<string,string>();
            foreach(var p in SandstoneExtensionSetup.Source().parts)guids[p.slug]=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(SandstoneExtensionSetup.Id(p))));
            SandstoneExtensionSetup.Install();Check(c.presets.Length==count,"Duplicate menu choices on repeat install");
            foreach(var p in SandstoneExtensionSetup.Source().parts)Check(guids[p.slug]==AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(SandstoneExtensionSetup.Id(p)))),"GUID changed");
            foreach(var p in originals)Check(File.ReadAllText(p.Key)==p.Value,"Existing definition changed: "+p.Key);
            Assets(c);Render(c);SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog c)
    {
        var source=SandstoneExtensionSetup.Source();Check(source.parts.Length==14&&source.trianglesSelectable==468,"Geometry budget changed");
        var ids=new HashSet<string>();foreach(var d in c.presets)Check(d&&ids.Add(d.contentId),"Duplicate/null definition");
        Check(SandstoneExtensionSetup.PresentCount(c)==14,"Expected eleven designs and three length/turn choices");
        Material shared=null;
        foreach(var p in source.parts)
        {
            var d=c.Find(SandstoneExtensionSetup.Id(p));Check(d&&d.mesh&&d.material&&d.authoringPrefab,"Missing asset: "+p.tag);
            if(shared==null)shared=d.material;
            Check(d.material==shared&&shared.enableInstancing&&shared.shader.name=="Custom/BakedStoneWallMatte","Shared matte sandstone material missing");
            Near(d.LocalBounds.size,new Vector3(p.size[0],p.size[1],p.size[2]),"Logical size");
            Near(d.mesh.bounds.min,d.LocalBounds.min,"Visual minimum");Near(d.mesh.bounds.max,d.LocalBounds.max,"Visual maximum");
            Check(d.mesh.triangles.Length/3==p.triangles&&d.mesh.subMeshCount==1,"Triangle/submesh budget: "+p.tag);
            Check(d.preserveAuthoredMesh&&d.jointCoverVariants.Length==0,"Automatic timber geometry introduced");
            Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh==d.mesh&&d.authoringPrefab.GetComponent<MeshRenderer>().sharedMaterial==shared,"Prefab render references");
            int colliders=p.steps>0?1:p.solids.Length;
            Check(d.authoringPrefab.GetComponents<Collider>().Length==colliders,"Prefab collision count");
            Check(d.solidBoxes.Length+d.occupiedVolumes.Length==p.solids.Length,"Occupancy decomposition");
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d)),"Missing icon");
            Check(!BuildUiContent.Description(d).StartsWith("Choose this component",StringComparison.Ordinal),"Missing menu description");
            var v=d.mesh.vertices;var n=d.mesh.normals;var uv=d.mesh.uv;var ts=d.mesh.tangents;var t=d.mesh.triangles;
            Check(v.Length==n.Length&&v.Length==uv.Length&&v.Length==ts.Length,"Missing vertex attributes");
            for(int i=0;i<v.Length;i++)
            {
                Check(float.IsFinite(uv[i].x)&&float.IsFinite(uv[i].y)&&uv[i].x>=0&&uv[i].x<=1&&uv[i].y>=0&&uv[i].y<=1,"Invalid UV");
                Vector3 tangent=ts[i];Check(Mathf.Abs(n[i].magnitude-1)<.003f&&Mathf.Abs(tangent.magnitude-1)<.003f&&Mathf.Abs(Vector3.Dot(n[i],tangent))<.003f,"Tangent basis");
            }
            var levels=new HashSet<int>();
            for(int i=0;i<t.Length;i+=3)
            {
                var cross=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]);
                Check(cross.sqrMagnitude>1e-13f&&Vector3.Dot(cross,n[t[i]]+n[t[i+1]]+n[t[i+2]])>0,"Inverted/degenerate triangle");
                var a=uv[t[i+1]]-uv[t[i]];var b=uv[t[i+2]]-uv[t[i]];Check(Mathf.Abs(a.x*b.y-a.y*b.x)>1e-12f,"Collapsed UV triangle");
                if(cross.normalized.y>.99f)levels.Add(Mathf.RoundToInt(v[t[i]].y*10000));
            }
            if(p.steps>0)
            {
                Check(levels.Count==p.steps,"Visible tread count");Check(d.kind==BuildPartKind.Stair&&d.collisionMesh&&d.authoringPrefab.GetComponent<SmoothWalkSurface>(),"Smooth stair collision missing");
            }
            foreach(var volume in d.occupiedVolumes)
            {
                var vs=volume.vertices;var hs=volume.mesh.triangles;float signed=0;
                for(int i=0;i<hs.Length;i+=3)
                {
                    signed+=Vector3.Dot(vs[hs[i]],Vector3.Cross(vs[hs[i+1]],vs[hs[i+2]]))/6;
                    var normal=Vector3.Cross(vs[hs[i+1]]-vs[hs[i]],vs[hs[i+2]]-vs[hs[i]]).normalized;
                    foreach(var point in vs)Check(Vector3.Dot(normal,point-vs[hs[i]])<.0001f,"Non-convex collision hull");
                }
                Check(signed>0,"Inverted collision hull");
            }
        }
        foreach(string path in new[]{SandstoneExtensionSetup.Colour,SandstoneExtensionSetup.Normal})
        {
            var ti=AssetImporter.GetAtPath(path) as TextureImporter;bool normal=path==SandstoneExtensionSetup.Normal;
            Check(ti&&ti.mipmapEnabled&&ti.wrapMode==TextureWrapMode.Clamp&&ti.anisoLevel==4&&ti.sRGBTexture!=normal,"Texture sampling/colour space");
            Check(ti.GetPlatformTextureSettings("Standalone").format==(normal?TextureImporterFormat.BC5:TextureImporterFormat.BC7),"Texture compression");
        }
    }
    private static void State(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=SessionState.GetInt(Pending+".checks",0);walks=0;pockets=0;
        try
        {
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            var walk=typeof(W21StairValidation).GetMethod("Walk",BindingFlags.NonPublic|BindingFlags.Static);
            for(int yaw=0;yaw<8;yaw++)
            {
                Runtime(c,yaw);
                foreach(bool back in new[]{false,true})foreach(bool shelfFirst in new[]{false,true})Pockets(c,yaw,back,shelfFirst);
                foreach(string tag in new[]{"SS19","SS20"})foreach(float x in new[]{.45f,.75f,1.05f})foreach(bool down in new[]{false,true})
                {walk.Invoke(null,new object[]{D(c,tag),c,yaw,x,down,1f/60,1});walks++;Check(true,"Stair traversal");}
                foreach(bool down in new[]{false,true}){walk.Invoke(null,new object[]{D(c,"SS20"),c,yaw,.75f,down,1f/60,2});walks++;Check(true,"Two-flight traversal");}
            }
            File.WriteAllText(SandstoneExtensionSetup.Art+"/unity-validation.json","{\"passed\":true,\"checks\":"+checks+",\"designs\":11,\"parts\":14,\"triangles\":468,\"headings\":8,\"stairWalks\":"+walks+",\"pocketCases\":"+pockets+",\"urpRenders\":true}");
            Debug.Log("SANDSTONE EXTENSION PASS: "+checks+" checks; fourteen choices, 468 triangles, "+walks+" stair traversals and "+pockets+" reversible pocket cases.");EditorApplication.Exit(0);
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Runtime(BuildCatalog c,int yaw)
    {
        var host=new GameObject("Sandstone court fixture");var world=host.AddComponent<BuildWorld>();world.enabled=false;
        var q=BuildGeometry.Rotation(yaw);var origin=new Vector3(2000+yaw*40,50,2000);
        try
        {
            foreach(var p in SandstoneExtensionSetup.Source().parts)
            {
                var d=c.Find(SandstoneExtensionSetup.Id(p));
                var preview=BuildPlacement.Solve(d,origin+q*new Vector3(d.LocalBounds.center.x,d.LocalBounds.min.y,d.LocalBounds.center.z),Vector3.up,null,null,yaw,0,default);
                world.Validate(ref preview);Check(preview.Valid,"Free placement rejected: "+p.tag);
                var piece=world.Commit(preview);Check(piece!=null&&piece.Resolved.mesh==d.mesh,"Authored mesh lost: "+p.tag);var at=piece.Origin;
                using(var gameplay=new BuildGameplay(world.Session))
                {
                    gameplay.Update(at,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(piece.Id,out var proxy),"Pooled collision absent");
                    bool Hit(Vector3 a,Vector3 direction,out RaycastHit hit)=>Physics.Raycast(at+q*a,q*direction,out hit,5,1<<GameplayLayers.WorldSolid,QueryTriggerInteraction.Ignore);
                    bool Contains(Vector3 point){foreach(var b in piece.Resolved.boxes)if(b.Contains(point))return true;foreach(var v in piece.Resolved.volumes)if(BuildOccupancy.Contains(v,point))return true;return false;}
                    if(p.tag=="SS17")
                    {Check(!Hit(new Vector3(1,.2f,-1),Vector3.forward,out _),"Bench space filled");Check(Hit(new Vector3(1,2,.25f),Vector3.down,out var h),"Bench seat absent");Near(h.point,at+q*new Vector3(1,.5f,.25f),"Bench seat height");}
                    else if(p.tag=="SS18")
                    {
                        Check(!Contains(new Vector3(1,.375f,.375f)),"Trough basin filled");
                        Check(Hit(new Vector3(1,2,.375f),Vector3.down,out var h),"Trough bottom absent");Near(h.point,at+q*new Vector3(1,.25f,.375f),"Trough basin depth");
                        Check(Hit(new Vector3(1,.375f,.375f),Vector3.right,out h),"Trough side absent");Near(h.point,at+q*new Vector3(1.875f,.375f,.375f),"Trough rim thickness");
                    }
                    else if(p.tag=="SS22")
                    {Check(!Hit(new Vector3(-1,.2f,.25f),Vector3.right,out _),"Drain inlet/outlet blocked");Check(Hit(new Vector3(1,1,.25f),Vector3.down,out var h),"Drain bottom absent");Near(h.point,at+q*new Vector3(1,.125f,.25f),"Drain channel depth");}
                    else if(p.tag=="SS24")
                    {Check(!Hit(new Vector3(.75f,2,.75f),Vector3.down,out _),"Hearth centre filled");Check(Hit(new Vector3(.125f,2,.75f),Vector3.down,out _),"Hearth curb missing");}
                    else if(p.tag=="SS21")
                    {Check(Hit(new Vector3(2,.75f,2),Vector3.back,out var h),"Retaining face absent");Near(h.point,at+q*new Vector3(2,.75f,.25f),"Retaining batter");}
                    else if(p.tag is "SS15" or "SS15S")
                    {foreach(float x in new[]{.125f,.5f}){Check(Hit(new Vector3(x,1,0),Vector3.down,out var h),"Coping cap absent");Near(h.point,at+q*new Vector3(x,x<.25f?.25f:.1875f,0),"Coping height rhythm");}}
                    else if(p.tag=="SS15C")
                    {Check(!Hit(new Vector3(.25f,1,.5f),Vector3.down,out _),"Coping turn inside corner filled");Check(Hit(new Vector3(.75f,1,.5f),Vector3.down,out _),"Coping return missing");}
                    else if(p.tag=="SS23")Check(!Contains(new Vector3(.5f,.125f,.1f)),"Corbel shelf space filled");
                    if(p.steps>0)
                    {
                        Check(proxy.RampShape&&proxy.RampShape.sharedMesh==d.collisionMesh&&proxy.GetComponent<SmoothWalkSurface>(),"Stair proxy lacks smooth walking hull");
                        Check(Contains(new Vector3(.75f,.1f,d.LocalBounds.max.z-.1f)),"Masonry flight hollow underneath");
                        var end=BuildPlacement.Solve(d,at+q*new Vector3(.75f,p.size[1],p.size[2]),Vector3.up,piece,world.Session.Frame(piece.OwnFrameId),yaw,0,default);
                        world.Validate(ref end);Check(end.Valid,"Stair continuation rejected");Near(end.Origin,at+q*new Vector3(0,p.size[1],p.size[2]),"Stair continuation datum");
                    }
                }
                Check(world.Remove(piece.Id),"Piece removal failed");
            }
            foreach(var pair in new[]{("SS13","SS15"),("SS13S","SS15S")})
            {
                var wall=world.Session.Add(D(c,pair.Item1),world.Session.CreateFrame(origin,yaw),default,0,false);
                var cap=BuildPlacement.Solve(D(c,pair.Item2),origin+q*new Vector3(wall.Definition.LocalBounds.center.x,1.25f,0),Vector3.up,wall,world.Session.Frame(wall.OwnFrameId),yaw,0,default);
                world.Validate(ref cap);Check(cap.Valid,"Court coping snap rejected");Near(cap.Origin,origin+Vector3.up*1.25f,"Court coping top datum");world.Remove(wall.Id);
            }
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Pockets(BuildCatalog c,int yaw,bool back,bool shelfFirst)
    {
        var host=new GameObject("Corbel pocket fixture");var world=host.AddComponent<BuildWorld>();world.enabled=false;var s=world.Session;
        var q=BuildGeometry.Rotation(yaw);var origin=new Vector3(3000+yaw*30,50,3000);var wd=c.Find("build.stone.ss01-bedded-sandstone");var sd=D(c,"SS23");
        var wallFrame=s.CreateFrame(origin,yaw);var shelfOrigin=origin+q*new Vector3(1.5f,1,back?-.25f:.25f);int sy=BuildGeometry.Turn(yaw+(back?4:0));
        var shelfFrame=s.CreateFrame(shelfOrigin,sy);BuildPieceRecord wall=null,shelf=null;
        try
        {
            if(shelfFirst)shelf=s.Add(sd,shelfFrame,default,0,false);
            wall=s.Add(wd,wallFrame,default,0,false);
            if(!shelfFirst)
            {
                var preview=BuildPlacement.Solve(sd,shelfOrigin,q*(back?Vector3.back:Vector3.forward),wall,wallFrame,yaw,0,default);
                world.Validate(ref preview);Check(preview.Valid,"Shelf mount preview rejected");Near(preview.Origin,shelfOrigin,"Shelf mount face shifted");
                Check(wall.Resolved.mesh==wd.mesh,"Preview permanently cut the host wall");shelf=world.Commit(preview);Check(shelf!=null,"Shelf mount commit failed");
            }
            Check(wall.Resolved.mesh!=wd.mesh,"Embedded tails did not create wall pockets");
            var cuts=BuildCorbelPocket.Cuts(wall,new[]{shelf},wall.Resolved.mesh.bounds);Check(cuts.Count==2,"Expected two tail pockets");
            foreach(var cut in cuts)foreach(var box in wall.Resolved.boxes)Check(!box.Contains(cut.center),"Host collision still fills tail pocket");
            var before=wall.Resolved.mesh;var duplicate=s.Add(sd,shelfFrame,default,0,false);
            Check(wall.Resolved.mesh==before,"Duplicate tails caused a different cached mesh");s.Remove(shelf.Id);
            Check(wall.Resolved.mesh==before,"Removing one overlapping shelf restored an occupied pocket");
            using(var gameplay=new BuildGameplay(s))
            {
                gameplay.Update(origin,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(wall.Id,out _),"Fitted wall proxy absent");
                Check(gameplay.TryGetProxy(duplicate.Id,out var shelfProxy),"Shelf proxy absent");var colliders=new List<Collider>();shelfProxy.GetEnabledColliders(colliders);foreach(var collider in colliders)collider.enabled=false;Physics.SyncTransforms();
                foreach(var cut in cuts)
                {
                    var from=new Vector3(cut.center.x,cut.center.y,back?-2:2);var direction=back?Vector3.forward:Vector3.back;
                    Check(Physics.Raycast(origin+q*from,q*direction,out var hit,5,1<<GameplayLayers.WorldSolid),"Pocket backing collision missing");
                    Near(hit.point,origin+q*new Vector3(cut.center.x,cut.center.y,0),"Embedded tail pocket collision depth");
                }
            }
            s.Remove(duplicate.Id);Check(wall.Resolved.mesh==wd.mesh&&wall.Resolved.boxes.Length==1,"Host wall did not restore after shelf removal");
            var niche=c.Find("build.stone.ss09-sandstone-niche");var thin=s.Add(niche,s.CreateFrame(origin+q*new Vector3(6,0,0),yaw),default,0,false);
            var incompatible=s.Add(sd,s.CreateFrame(thin.Origin+q*new Vector3(1.5f,1,.25f),yaw),default,0,false);
            Check(thin.Resolved.mesh==niche.mesh,"Corbel fitting cut an opening or thin niche backing");pockets++;
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Render(BuildCatalog c)
    {
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;var oldSun=RenderSettings.sun;bool oldAsync=ShaderUtil.allowAsyncCompilation;var previous=RenderTexture.active;
        var root=new GameObject("Sandstone extension render");var cameraObj=new GameObject("Court camera");var lightObj=new GameObject("Court sun");
        var cam=cameraObj.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.aspect=1.6f;cam.nearClipPlane=.01f;cam.farClipPlane=200;cam.cullingMask=1<<31;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.18f,.20f,.21f);cameraObj.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.shadows=LightShadows.None;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(40,-35,0);
        var target=new RenderTexture(1920,1200,24,RenderTextureFormat.ARGB32);target.Create();Action<ScriptableRenderContext,Camera> submit=null;
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");Check(pipeline,"URP missing");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.4f,.4f,.4f);RenderSettings.sun=light;ShaderUtil.allowAsyncCompilation=false;
            var material=D(c,"SS13").material;for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);Check(!ShaderUtil.ShaderHasError(material.shader),"Shader compile failed");
            var objects=new List<GameObject>();
            foreach(var p in SandstoneExtensionSetup.Source().parts)
            {
                var d=c.Find(SandstoneExtensionSetup.Id(p));var ob=new GameObject(p.tag){layer=31};ob.transform.SetParent(root.transform);ob.AddComponent<MeshFilter>().sharedMesh=d.mesh;ob.AddComponent<MeshRenderer>().sharedMaterial=material;objects.Add(ob);
                cam.orthographicSize=Mathf.Max(.65f,d.LocalBounds.size.magnitude*.43f);cam.transform.position=d.mesh.bounds.center+new Vector3(4,3,-8);cam.transform.LookAt(d.mesh.bounds.center);Capture(cam,target,p.tag+"-unity-front.png");
                if(p.tag is "SS18" or "SS22" or "SS23" or "SS24"){cam.transform.position=d.mesh.bounds.center+new Vector3(-4,3,8);cam.transform.LookAt(d.mesh.bounds.center);Capture(cam,target,p.tag+"-unity-back.png");}ob.SetActive(false);
            }
            for(int i=0;i<objects.Count;i++){objects[i].transform.position=new Vector3(i%4*6,0,i/4*6);objects[i].SetActive(true);}cam.orthographicSize=13;cam.transform.position=new Vector3(32,33,-32);cam.transform.LookAt(new Vector3(10,1,9));Capture(cam,target,"extension-unity-overview.png");foreach(var ob in objects)ob.SetActive(false);
            using var session=new BuildSession();var frame=session.CreateFrame(Vector3.zero,0);int index=0;
            foreach(var p in SandstoneExtensionSetup.Source().parts){session.Add(c.Find(SandstoneExtensionSetup.Id(p)),frame,new Vector3Int(index%4*24,0,index/4*24),0,false);index++;}
            var renderer=new BuildRenderer(session);cam.cullingMask=~0;submit=(_,camera)=>{if(camera==cam)renderer.Draw(cam,150,0);};RenderPipelineManager.beginCameraRendering+=submit;Capture(cam,target,"extension-instanced.png");Check(renderer.DrawCalls>0&&renderer.DrawCalls<=14,"Instanced rendering failed");
            foreach(var p in new List<BuildPieceRecord>(session.Pieces.Values))session.Remove(p.Id);
            var wall=session.Add(c.Find("build.stone.ss01-bedded-sandstone"),frame,default,0,false);
            session.Add(D(c,"SS23"),session.CreateFrame(new Vector3(1.5f,1,.25f),0),default,0,false);
            Check(wall.Resolved.mesh!=wall.Definition.mesh,"Mounted shelf render did not fit wall");
            cam.orthographicSize=2;cam.transform.position=new Vector3(5,3,8);cam.transform.LookAt(new Vector3(2,1.3f,0));Capture(cam,target,"SS23-mounted-unity.png");
        }finally{if(submit!=null)RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;RenderSettings.sun=oldSun;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=previous;Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObj);Object.DestroyImmediate(lightObj);target.Release();Object.DestroyImmediate(target);}
    }
    private static void Capture(Camera camera,RenderTexture target,string filename)
    {
        for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
        RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try
        {
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetPixels32();var bg=pixels[0];int visible=0,pink=0;
            foreach(var p in pixels){if(Math.Abs(p.r-bg.r)+Math.Abs(p.g-bg.g)+Math.Abs(p.b-bg.b)>12)visible++;if(p.r>200&&p.b>200&&p.g<50)pink++;}Check(visible>1000&&pink<100,"Blank/error render: "+filename);File.WriteAllBytes(SandstoneExtensionSetup.Art+"/"+filename,image.EncodeToPNG());
        }finally{Object.DestroyImmediate(image);}
    }
}
