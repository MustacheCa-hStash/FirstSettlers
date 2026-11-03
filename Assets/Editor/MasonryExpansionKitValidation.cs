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
public static class MasonryExpansionKitValidation
{
    private const string Pending="FS.MasonryExpansion.Validation";
    private static int checks,walks;
    static MasonryExpansionKitValidation()=>EditorApplication.playModeStateChanged+=State;
    private static void Check(bool yes,string message){checks++;if(!yes)throw new InvalidOperationException(message);}
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.000002f,message+": "+a+" / "+b);
    public static void InstallAndRunBatch()
    {
        try
        {
            checks=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var c=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);var originals=new Dictionary<string,string>();
            var owned=new HashSet<string>();foreach(var p in MasonryExpansionKitSetup.Source().parts)owned.Add(MasonryExpansionKitSetup.Id(p.slug));
            foreach(var d in c.presets)if(!owned.Contains(d.contentId))originals[AssetDatabase.GetAssetPath(d)]=File.ReadAllText(AssetDatabase.GetAssetPath(d));
            MasonryExpansionKitSetup.Install();int count=c.presets.Length;var guids=new Dictionary<string,string>();
            foreach(var p in MasonryExpansionKitSetup.Source().parts){var d=c.Find(MasonryExpansionKitSetup.Id(p.slug));guids[p.slug]=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(d));}
            MasonryExpansionKitSetup.Install();Check(c.presets.Length==count,"Repeat install duplicated catalog entries");
            foreach(var p in MasonryExpansionKitSetup.Source().parts)Check(guids[p.slug]==AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Find(MasonryExpansionKitSetup.Id(p.slug)))),"Repeat install changed GUID");
            foreach(var pair in originals)Check(File.ReadAllText(pair.Key)==pair.Value,"Unrelated definition changed: "+pair.Key);
            Assets(c);Render(c);SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog c)
    {
        var source=MasonryExpansionKitSetup.Source();Check(source.parts.Length==11,"Expected eleven new parts");var ids=new HashSet<string>();
        foreach(var d in c.presets)Check(d!=null&&ids.Add(d.contentId),"Null/duplicate catalog entry");
        foreach(var p in source.parts)
        {
            var d=c.Find(MasonryExpansionKitSetup.Id(p.slug));Check(d!=null&&d.mesh&&d.material&&d.material.enableInstancing,"Missing render definition: "+p.slug);
            Near(d.LocalBounds.size,new Vector3(p.size[0],p.size[1],p.size[2]),"Logical dimensions");
            Check(d.mesh.triangles.Length/3==p.triangles&&d.mesh.subMeshCount==1,"Unexpected imported triangles/submeshes");
            Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh==d.mesh&&d.authoringPrefab.GetComponent<MeshRenderer>().sharedMaterial==d.material,"Prefab reference mismatch");
            Check(d.authoringPrefab.GetComponent<Animator>()==null&&d.authoringPrefab.GetComponent<Rigidbody>()==null,"Moving behaviour added to static part");
            Check(d.occupiedVolumes.Length==p.solids.Length&&d.jointCoverVariants.Length==0,"Wrong collision or automatic timber cover");
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Missing thumbnail");
            Check(!BuildUiContent.Description(d).StartsWith("Choose this component",StringComparison.Ordinal),"Missing menu description");
            var v=d.mesh.vertices;var n=d.mesh.normals;var uv=d.mesh.uv;var tangents=d.mesh.tangents;var t=d.mesh.triangles;
            Check(v.Length==n.Length&&v.Length==uv.Length&&v.Length==tangents.Length,"Missing vertex attributes");
            for(int i=0;i<v.Length;i++){Check(float.IsFinite(uv[i].x)&&float.IsFinite(uv[i].y)&&uv[i].x>=0&&uv[i].x<=1&&uv[i].y>=0&&uv[i].y<=1,"UV outside shared atlas");Vector3 tangent=tangents[i];Check(Mathf.Abs(n[i].magnitude-1)<.002f&&Mathf.Abs(tangent.magnitude-1)<.003f&&Mathf.Abs(Vector3.Dot(n[i],tangent))<.003f,"Invalid tangent basis");}
            for(int i=0;i<t.Length;i+=3){var cross=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]);Check(cross.sqrMagnitude>1e-13f&&Vector3.Dot(cross,n[t[i]]+n[t[i+1]]+n[t[i+2]])>0,"Inverted/degenerate visible triangle");var a=uv[t[i+1]]-uv[t[i]];var b=uv[t[i+2]]-uv[t[i]];Check(Mathf.Abs(a.x*b.y-a.y*b.x)>1e-12f,"Collapsed UV triangle");}
            Near(d.mesh.bounds.min,d.LocalBounds.min,"Visual minimum");Near(d.mesh.bounds.max,d.LocalBounds.max,"Visual maximum");
            if(p.slug.EndsWith("-slab-roof",StringComparison.Ordinal))
            {
                Check(d.kind==BuildPartKind.Floor&&BuildUiContent.Category(d)==6&&d.displayName.Contains("flat roof"),"Flat deck menu/placement mismatch");
                Near(d.LocalBounds.size,new Vector3(4,.25f,4),"Flat roof dimensions");Check(d.occupiedVolumes.Length==1,"Flat roof collision must be a single solid deck");
            }
            if(p.slug.EndsWith("open-arch",StringComparison.Ordinal))
            {
                Check(p.arch.Length==5&&Mathf.Abs(p.arch[1]*2-3.5f)<.001f&&Mathf.Abs(p.arch[2]-2.25f)<.001f&&Mathf.Abs(p.arch[4]-.5f)<.001f,"Broad arch clearances");
                var wall=c.Find(p.style=="stone"?"build.stone.s02-coursed-wall":"build.stone.l01-coursed-limestone");
                Check(Mathf.Abs(d.mesh.bounds.max.y-wall.mesh.bounds.max.y)<.001f&&Mathf.Abs(d.LocalBounds.size.y-wall.LocalBounds.size.y)<.001f,"Broad arch exceeds ordinary wall height");
            }
            foreach(var hull in d.occupiedVolumes)
            {
                double volume=0;var edges=new Dictionary<string,int>();var hv=hull.vertices;var ht=hull.mesh.triangles;
                for(int i=0;i<ht.Length;i+=3){volume+=Vector3.Dot(hv[ht[i]],Vector3.Cross(hv[ht[i+1]],hv[ht[i+2]]))/6.0;foreach(var pair in new[]{(ht[i],ht[i+1]),(ht[i+1],ht[i+2]),(ht[i+2],ht[i])}){string key=Math.Min(pair.Item1,pair.Item2)+"/"+Math.Max(pair.Item1,pair.Item2);edges.TryGetValue(key,out int value);edges[key]=value+1;}}
                Check(volume>0,"Inverted occupied hull");foreach(int value in edges.Values)Check(value==2,"Open occupied hull");
            }
        }
        Check(c.Find("build.stone.s04-flagstone-floor")!=null&&c.Find("build.stone.l05-limestone-paving")!=null,"Existing full floors lost");
    }
    private static void State(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=SessionState.GetInt(Pending+".checks",0);walks=0;
        try
        {
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");for(int yaw=0;yaw<8;yaw++)Runtime(c,yaw);
            File.WriteAllText(MasonryExpansionKitSetup.Art+"/unity-validation.json","{\"passed\":true,\"checks\":"+checks+",\"parts\":11,\"headings\":8,\"motorWalks\":"+walks+",\"urpRenders\":true}");
            Debug.Log("MASONRY EXPANSION PASS: "+checks+" checks; eleven pieces, eight headings, flat roof decks, spacious arch apertures, square infill, half floors and "+walks+" motor traversals.");EditorApplication.Exit(0);
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Runtime(BuildCatalog c,int yaw)
    {
        var host=new GameObject("Masonry expansion placement");var world=host.AddComponent<BuildWorld>();world.enabled=false;var s=world.Session;var q=BuildGeometry.Rotation(yaw);
        var origin=new Vector3(1000+yaw*40,50,1000);var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.layer=GameplayLayers.WorldSolid;floor.transform.SetPositionAndRotation(origin+q*new Vector3(2,-.125f,0),q);floor.transform.localScale=new Vector3(6,.25f,6);
        try
        {
            foreach(var p in MasonryExpansionKitSetup.Source().parts)
            {
                var d=c.Find(MasonryExpansionKitSetup.Id(p.slug));floor.SetActive(false);Physics.SyncTransforms();
                var aim=origin+q*new Vector3(d.LocalBounds.center.x,d.LocalBounds.min.y,d.LocalBounds.center.z);
                var preview=BuildPlacement.Solve(d,aim,Vector3.up,null,null,yaw,0,default);world.Validate(ref preview);Check(preview.Valid,"Free placement failed: "+p.slug);var piece=world.Commit(preview);Check(piece!=null,"Commit failed");Near(piece.Origin,origin,"Free placement origin");
                using(var gameplay=new BuildGameplay(s))
                {
                    gameplay.Update(origin,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(piece.Id,out var proxy),"Pooled proxy missing");
                    bool Hit(Vector3 point,Vector3 direction,out RaycastHit hit,float length=4)=>Physics.Raycast(origin+q*point,q*direction,out hit,length,1<<GameplayLayers.WorldSolid,QueryTriggerInteraction.Ignore);
                    if(p.arch?.Length>=4)
                    {
                        float cx=p.arch[0],r=p.arch[1],spring=p.arch[2],sill=p.arch[3];
                        float rise=p.arch.Length>4?p.arch[4]:r,radius=(r*r+rise*rise)/(2*rise),cy=spring+rise-radius;
                        for(int ix=1;ix<12;ix++)
                        {
                            float x=cx-r+2*r*ix/12,head=cy+Mathf.Sqrt(Mathf.Max(0,radius*radius-(x-cx)*(x-cx)));
                            foreach(float ratio in new[]{.1f,.5f,.9f})Check(!Hit(new Vector3(x,Mathf.Lerp(sill+.04f,head-.04f,ratio),-2),Vector3.forward,out _),"Collider fills arch opening: "+p.slug);
                            if(p.arch.Length>4)Check(Hit(new Vector3(x,head+.05f,-2),Vector3.forward,out _),"Missing solid wall above broad arch: "+p.slug);
                        }
                        Check(Hit(new Vector3(p.arch.Length>4?.125f:.3f,.75f,-2),Vector3.forward,out _),"Arch jamb lost collision");
                        Check(Hit(new Vector3(.125f,2.8f,-2),Vector3.forward,out _),"Full arched wall lost upper corner");
                        if(p.arch.Length>4)for(int ix=0;ix<=16;ix++)
                        {
                            float x=.05f+3.9f*ix/16;
                            Check(Hit(new Vector3(x,4,0),Vector3.down,out var cap),"Broad arch wall has an open top");
                            Near(cap.point,origin+q*new Vector3(x,3,0),"Broad arch wall top is not level at ordinary wall height");
                        }
                    }
                    if(p.slug.EndsWith("-slab-roof",StringComparison.Ordinal))
                    {
                        var side=BuildPlacement.Solve(d,origin+q*new Vector3(4,-.125f,2),q*Vector3.right,piece,s.Frame(piece.OwnFrameId),yaw,0,default);Near(side.Origin,origin+q*new Vector3(4,0,0),"Flat roof sideways datum");
                        foreach(float z in new[]{.05f,2f,3.95f}){Check(Hit(new Vector3(2,1,z),Vector3.down,out var roofHit),"Flat roof lacks walkable coverage");Near(roofHit.point,origin+q*new Vector3(2,0,z),"Flat roof top varies in height");}
                    }
                    if(d.kind==BuildPartKind.Floor)
                    {Check(Hit(new Vector3(2,1,1),Vector3.down,out var top),"Floor collision missing");Near(top.point,origin+q*new Vector3(2,0,1),"Floor walking datum");}
                    if(p.slug=="braced-timber-frame")Check(!Hit(new Vector3(2,1.5f,-2),Vector3.forward,out _),"Braced frame filled its central passage");
                    if(p.slug is "stone-open-arch" or "limestone-open-arch" or "stone-arched-doorway" or "braced-timber-frame")
                    {
                        floor.SetActive(true);Physics.SyncTransforms();
                        foreach(float x in p.slug.EndsWith("open-arch",StringComparison.Ordinal)?new[]{.85f,2,3.15f}:new[]{2f})foreach(bool reverse in new[]{false,true})Walk(piece,q,reverse,x);floor.SetActive(false);
                    }
                }
                Check(world.Remove(piece.Id),"Removal failed");
            }
            foreach(string style in new[]{"stone","limestone"})
            {
                var wall=s.Add(c.Find(style=="stone"?"build.stone.s02-coursed-wall":"build.stone.l01-coursed-limestone"),s.CreateFrame(origin,yaw),default,0,false);
                var infill=c.Find(MasonryExpansionKitSetup.Id(style+"-square-infill"));
                foreach(float x in new[]{1f,3f}){var p=BuildPlacement.Solve(infill,origin+q*new Vector3(x,3,0),Vector3.up,wall,s.Frame(wall.OwnFrameId),yaw,0,default);Near(p.Origin,origin+q*new Vector3(x<2?0:2,3,0),"Square infill half-bay datum");}
                var deck=c.Find(MasonryExpansionKitSetup.Id(style+"-slab-roof"));
                foreach(float side in new[]{-1f,1f})
                {
                    var roof=BuildPlacement.Solve(deck,origin+q*new Vector3(2,3,0),Vector3.up,wall,s.Frame(wall.OwnFrameId),yaw,0,default,origin+q*new Vector3(2,4,side*2));
                    Near(roof.Origin,origin+q*new Vector3(0,3,side>0?0:-4),"Flat roof wall-top datum toward viewer");Check(roof.TopAttachment&&roof.WorldYaw==yaw,"Flat roof storey attachment/yaw");
                }
                s.Remove(wall.Id);
            }
        }finally{Object.DestroyImmediate(floor);Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Walk(BuildPieceRecord piece,Quaternion q,bool reverse,float x)
    {
        var rig=new GameObject("Masonry opening traversal");var cc=rig.AddComponent<CharacterController>();cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.skinWidth=.08f;cc.stepOffset=.3f;cc.minMoveDistance=0;
        rig.transform.SetPositionAndRotation(piece.Origin+q*new Vector3(x,.08f,reverse?1.5f:-1.5f),q);
        var motor=rig.AddComponent<CharacterMotor>();typeof(CharacterMotor).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,null);Physics.SyncTransforms();
        try
        {
            for(int i=0;i<20;i++)motor.Simulate(default,1f/60);
            for(int i=0;i<52;i++)motor.Simulate(new CharacterMoveCommand(new Vector2(0,reverse?-1:1),false,false,false),1f/60);
            var local=Quaternion.Inverse(q)*(rig.transform.position-piece.Origin);
            Check(reverse?local.z<-.8f:local.z>.8f,"Character stuck in "+piece.Definition.displayName+": "+local);Check(Mathf.Abs(local.y)<.15f,"Opening changed walking height");walks++;
        }finally{Object.DestroyImmediate(rig);Physics.SyncTransforms();}
    }
    private static void Render(BuildCatalog c)
    {
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;var oldSun=RenderSettings.sun;bool oldAsync=ShaderUtil.allowAsyncCompilation;var previous=RenderTexture.active;
        var root=new GameObject("Masonry expansion render");var cameraObj=new GameObject("Masonry camera");var lightObj=new GameObject("Masonry sun");var cam=cameraObj.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.aspect=1.6f;cam.nearClipPlane=.01f;cam.farClipPlane=200;cam.cullingMask=1<<31;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.18f,.20f,.21f);cameraObj.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.shadows=LightShadows.None;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(40,-35,0);
        var target=new RenderTexture(1920,1200,24,RenderTextureFormat.ARGB32);target.Create();Action<ScriptableRenderContext,Camera> submit=null;
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");Check(pipeline!=null,"URP pipeline missing");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.4f,.4f,.4f);RenderSettings.sun=light;ShaderUtil.allowAsyncCompilation=false;
            var objects=new List<GameObject>();foreach(var p in MasonryExpansionKitSetup.Source().parts)
            {
                var d=c.Find(MasonryExpansionKitSetup.Id(p.slug));for(int pass=0;pass<d.material.passCount;pass++)ShaderUtil.CompilePass(d.material,pass,true);Check(!ShaderUtil.ShaderHasError(d.material.shader),"Shader compilation error");
                var ob=new GameObject(p.slug){layer=31};ob.transform.SetParent(root.transform);ob.AddComponent<MeshFilter>().sharedMesh=d.mesh;ob.AddComponent<MeshRenderer>().sharedMaterial=d.material;objects.Add(ob);
                cam.orthographicSize=2.4f;cam.transform.position=d.mesh.bounds.center+new Vector3(5,3,-8);cam.transform.LookAt(d.mesh.bounds.center);Capture(cam,target,p.slug+"-unity.png");ob.SetActive(false);
            }
            for(int i=0;i<objects.Count;i++){objects[i].transform.position=new Vector3(i%4*6,0,i/4*6);objects[i].SetActive(true);}cam.orthographicSize=12.5f;cam.transform.position=new Vector3(31,29,-31);cam.transform.LookAt(new Vector3(10,1,7));Capture(cam,target,"masonry-expansion-unity.png");foreach(var ob in objects)ob.SetActive(false);
            using var session=new BuildSession();var frame=session.CreateFrame(Vector3.zero,0);int index=0;foreach(var p in MasonryExpansionKitSetup.Source().parts){session.Add(c.Find(MasonryExpansionKitSetup.Id(p.slug)),frame,new Vector3Int(index%4*24,0,index/4*24),0,false);index++;}
            var renderer=new BuildRenderer(session);cam.cullingMask=~0;submit=(_,camera)=>{if(camera==cam)renderer.Draw(cam,150,0);};RenderPipelineManager.beginCameraRendering+=submit;Capture(cam,target,"masonry-expansion-instanced.png");Check(renderer.DrawCalls>0&&renderer.DrawCalls<=11,"Instanced drawing failed");
        }finally{if(submit!=null)RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;RenderSettings.sun=oldSun;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=previous;Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObj);Object.DestroyImmediate(lightObj);target.Release();Object.DestroyImmediate(target);}
    }
    private static void Capture(Camera camera,RenderTexture target,string filename)
    {
        for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetPixels32();var bg=pixels[0];int visible=0,pink=0;foreach(var p in pixels){if(Math.Abs(p.r-bg.r)+Math.Abs(p.g-bg.g)+Math.Abs(p.b-bg.b)>12)visible++;if(p.r>200&&p.b>200&&p.g<50)pink++;}Check(visible>1000&&pink<100,"Blank/error render: "+filename);File.WriteAllBytes(MasonryExpansionKitSetup.Art+"/"+filename,image.EncodeToPNG());}finally{Object.DestroyImmediate(image);}
    }
}
