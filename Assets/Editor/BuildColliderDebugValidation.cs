using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class BuildColliderDebugValidation
{
    private static int checks;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
    private static void Invoke(object obj,string method)=>obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            BuildingPrototypeSetup.CreateAssets();
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            foreach(int yaw in new[]{0,1,2,3,4,5,6,7}){Diagnostics(c,yaw);StarterFits(c,yaw);}
            foreach(int yaw in new[]{0,1,3,7})foreach(bool down in new[]{false,true})WallWalk(c,yaw,down);
            Render(c);
            Debug.Log("BUILD COLLIDER DEBUG PASS: "+checks+" checks; blocker identity, exact physical wires, restored fitted pose, headroom vs solids, freeze/live/remove, eight rotations, external solids, eight wall-adjacent real-motor walks and URP xray render.");
            EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static BuildWorld World(string name)
    {
        var w=new GameObject(name).AddComponent<BuildWorld>();if(w.Session==null)Invoke(w,"Awake");return w;
    }
    private static BuildPreview At(BuildDefinition d,BuildGridFrame f,Vector3Int a,int yaw=0)=>new(){Definition=d,Frame=f,Anchor=a,YawStep=yaw,Origin=BuildGeometry.WorldPoint(f,a),WorldYaw=BuildGeometry.Turn(f.YawStep+yaw)};
    private static void StarterFits(BuildCatalog c,int yaw)
    {
        var w=World("Starter fit diagnostic fixture");var f=w.Session.CreateFrame(new Vector3(28000+yaw*20,28000,28000),yaw);var q=BuildGeometry.Rotation(yaw);
        var stair=c.Find(W21StairSetup.ContentId);
        try
        {
            var floor=w.Session.Add(c.presets[2],f,default,0,true);
            w.Session.Add(c.presets[0],f,new Vector3Int(15,0,16),4,false);
            w.Session.Add(c.presets[1],f,new Vector3Int(0,12,0),0,false);
            BuildPreview Solve(float z)=>BuildPlacement.Solve(stair,f.Origin+q*new Vector3(.625f,0,z),Vector3.up,floor,w.Session.Frame(floor.OwnFrameId),0,0,default);
            var p=Solve(1);w.Validate(ref p);
            var local=Quaternion.Inverse(q)*(p.Origin-f.Origin);
            Check(p.Valid && Mathf.Abs(local.z)<.005f,"Starter moved away from its selected snap: "+p.Message);
            Check(w.DebugPreview.Origin==p.Origin && w.DebugPreview.Resolved==p.Resolved && w.DebugStates[ulong.MaxValue]==p.Resolved,"Debug pose does not match the fitted starter.");
            w.Session.Add(c.presets[0],f,new Vector3Int(1,0,0),0,false);
            var tight=Solve(1.25f);var before=tight.Origin;w.Validate(ref tight);
            Check(tight.Valid && (tight.Origin-before).sqrMagnitude<.000001f,"An impossible automatic fit moved the preview into its near wall.");
            Check(w.DebugPreview.Origin==tight.Origin && w.DebugPreview.Valid && w.DebugPreview.Resolved==tight.Resolved && w.DebugStates[ulong.MaxValue]==tight.Resolved,"Rejected speculative correction left diagnostics on a different invalid pose.");
        }finally{Object.DestroyImmediate(w.gameObject);Physics.SyncTransforms();}
    }
    private static void Diagnostics(BuildCatalog c,int yaw)
    {
        var w=World("Collider diagnostics fixture");var f=w.Session.CreateFrame(new Vector3(27000+yaw*20,27000,27000),yaw);
        var stair=c.Find(W21StairSetup.ContentId);var q=BuildGeometry.Rotation(yaw);
        var outside=new GameObject("External blocker"){layer=GameplayLayers.WorldSolid};outside.transform.position=f.Origin+q*new Vector3(20.625f,.6f,1);outside.transform.rotation=q;outside.AddComponent<BoxCollider>().size=Vector3.one*.5f;
        try
        {
            Check(w.ColliderDebug.Mode==BuildColliderDebugMode.Off,"Diagnostics should default off.");
            w.Session.Add(c.presets[2],f,new Vector3Int(0,0,-8),0,true);
            var wall=w.Session.Add(c.presets[0],f,new Vector3Int(1,0,1),6,false);
            var p=At(stair,f,new Vector3Int(1,0,0));w.Validate(ref p);
            Check(p.Valid,"A stair touching the side wall is still rejected: "+p.Message);
            var first=w.Commit(p);Check(first!=null,"Wall-adjacent stair commit failed");
            var duplicate=p;w.Validate(ref duplicate);
            Check(!duplicate.Valid && duplicate.Failure==BuildPlacementFailure.Solid && duplicate.BlockingPieceId==first.Id,"Duplicate did not identify its exact solid blocker.");
            Check(duplicate.Message.Contains("#"+first.Id),"Blocker ID missing from placement feedback.");
            w.ColliderDebug.Mode=BuildColliderDebugMode.SolidsAndClearance;w.ColliderDebug.Rebuild();
            Check(w.ColliderDebug.WireMesh.vertexCount>0,"Logical shapes are invisible before proxy activation");
            var headWall=w.Session.Add(c.presets[0],f,new Vector3Int(2,8,4),0,true);
            w.Remove(first.Id);var head=p;w.Validate(ref head);
            Check(head.Valid && head.Warning==BuildPlacementWarning.StairHeadroom && head.WarningPieceId==headWall.Id,"Headroom collision was hidden or identified as physical intersection: "+head.Message);
            w.Remove(headWall.Id);w.Validate(ref p);Check(p.Valid,"Removing a blocker did not clear diagnostics");
            w.ColliderDebug.Rebuild();w.ColliderDebug.ToggleFreeze();var frozen=w.ColliderDebug.WireMesh.vertices;
            w.ClearPreview();Invoke(w.ColliderDebug,"LateUpdate");
            Check(w.ColliderDebug.Frozen && w.ColliderDebug.WireMesh.vertexCount==frozen.Length,"Freeze lost the failed pose when aiming away");
            w.ColliderDebug.ToggleFreeze();Invoke(w.ColliderDebug,"LateUpdate");
            Check(!w.ColliderDebug.Frozen && w.DebugPreview.Definition==null,"Resuming retained stale placement feedback");
            Physics.SyncTransforms();var ext=At(stair,f,new Vector3Int(80,0,0));w.Session.Add(c.presets[2],f,new Vector3Int(80,0,-8),0,true);w.Validate(ref ext);
            Check(ext.Failure==BuildPlacementFailure.External && ext.BlockingCollider==outside.GetComponent<BoxCollider>(),"External blocker identity missing: "+ext.Message);
            w.ColliderDebug.Rebuild();var wiresBefore=w.ColliderDebug.WireMesh.bounds;
            outside.transform.position+=Vector3.up*8;Physics.SyncTransforms();Invoke(w.ColliderDebug,"LateUpdate");
            Check(w.ColliderDebug.WireMesh.bounds.max.y>wiresBefore.max.y+4,"Live external collider wires did not follow a moving blocker");
            var passage=BuildResolvedGeometry.StairPassage(stair);
            Check(Mathf.Abs(passage.vertices[0].x-stair.LocalBounds.min.x)<.00001f && Mathf.Abs(passage.vertices[4].x-stair.LocalBounds.max.x)<.00001f,"Wall headroom still contains floor framing side margins.");
            // Geometry capture uses the bound proxy, including its authored ramp, not a bounding box.
            var actual=w.Session.Add(stair,f,new Vector3Int(32,0,0),0,true);
            var focus=new GameObject("Debug focus");focus.transform.position=f.Origin;w.Focus=focus.transform;Invoke(w,"LateUpdate");
            Check(w.TryGetCollisionProxy(actual.Id,out var proxy),"Collision proxy was not activated");
            var list=new System.Collections.Generic.List<Collider>();proxy.GetEnabledColliders(list);
            Check(list.Count==1 && list[0] is MeshCollider,"Stair physical diagnostic is not its smooth ramp");
            var lines=new BuildDebugLines();lines.Collider(list[0],BuildColliderDebug.PhysicalColor);var mesh=new Mesh();lines.Upload(mesh);
            var expected=actual.Origin+q*stair.collisionMesh.vertices[1];bool found=false;
            foreach(var point in mesh.vertices)if((point-expected).sqrMagnitude<.000001f)found=true;
            Check(found,"Wire mesh does not use the actual transformed ramp vertex");
            Object.DestroyImmediate(mesh);Object.DestroyImmediate(focus);
        }
        finally{Object.DestroyImmediate(outside);Object.DestroyImmediate(w.gameObject);Physics.SyncTransforms();}
    }
    private static void WallWalk(BuildCatalog c,int yaw,bool down)
    {
        var w=World("Wall-adjacent motor fixture");var origin=new Vector3(400+yaw*20,40,400);var f=w.Session.CreateFrame(origin,yaw);var q=BuildGeometry.Rotation(yaw);
        var rig=new GameObject("Wall-adjacent motor");
        try
        {
            w.Session.Add(c.presets[2],f,new Vector3Int(-8,0,-8),0,true);
            var wall=w.Session.Add(c.presets[0],f,new Vector3Int(1,0,1),6,false);
            var stair=c.Find(W21StairSetup.ContentId);var p=At(stair,f,new Vector3Int(1,0,0));
            Check(w.Commit(p)!=null,"Wall-adjacent foot rejected");
            Check(w.Commit(At(stair,f,new Vector3Int(1,6,8)))!=null,"Wall-adjacent continuation rejected");
            Check(w.Commit(At(c.presets[1],f,new Vector3Int(0,12,16)))!=null,"Landing rejected");
            using var gameplay=new BuildGameplay(w.Session);gameplay.Update(origin,32,40,16);Physics.SyncTransforms();
            var cc=rig.AddComponent<CharacterController>();cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.slopeLimit=50;cc.stepOffset=.3f;cc.skinWidth=.08f;
            // Near the wall side, rather than the centre of the stair.
            rig.transform.SetPositionAndRotation(origin+q*new Vector3(.73f,down?3.08f:.08f,down?4.8f:-.8f),q);
            var motor=rig.AddComponent<CharacterMotor>();Invoke(motor,"Awake");Physics.SyncTransforms();
            for(int i=0;i<30;i++)motor.Simulate(new CharacterMoveCommand(Vector2.zero,false,false,false),1f/60);
            bool finished=false;float previous=down?4.8f:-.8f;int stalled=0;
            for(int i=0;i<420;i++)
            {
                motor.Simulate(new CharacterMoveCommand(new Vector2(0,down?-1:1),false,false,false),1f/60);
                var pos=Quaternion.Inverse(q)*(rig.transform.position-origin);
                if(i>12 && Mathf.Abs(pos.z-previous)<.001f)stalled++;else stalled=0;
                Check(stalled<25,$"Wall-adjacent motor stuck yaw={yaw}, down={down}, {pos}");
                if(!down && pos.z>4.7f || down && pos.z<-.65f){Check(Mathf.Abs(pos.y-(down?0:3))<.12f,"Wall-adjacent landing height wrong");finished=true;break;}
                previous=pos.z;
            }
            Check(finished,"Wall-adjacent traversal timed out");
        }finally{Object.DestroyImmediate(rig);Object.DestroyImmediate(w.gameObject);Physics.SyncTransforms();}
    }
    private static void Render(BuildCatalog c)
    {
        Directory.CreateDirectory(".utmp/collider-debug");var w=World("Diagnostic render");var f=w.Session.CreateFrame(Vector3.zero,0);
        w.Session.Add(c.presets[2],f,default,0,true);w.Session.Add(c.presets[0],f,new Vector3Int(1,0,1),6,false);
        var stair=c.Find(W21StairSetup.ContentId);w.Session.Add(stair,f,new Vector3Int(3,0,0),0,false);
        var p=At(stair,f,new Vector3Int(3,0,0));w.Validate(ref p);Check(p.Failure==BuildPlacementFailure.Solid,"Render needs a real duplicate rejection");
        var camObj=new GameObject("Collider debug camera");var camera=camObj.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.aspect=1.5f;camera.orthographicSize=4;camera.backgroundColor=new Color(.06f,.08f,.11f);camera.clearFlags=CameraClearFlags.SolidColor;
        camera.transform.position=new Vector3(9,7,-8);camera.transform.LookAt(new Vector3(1.8f,1.5f,1.5f));w.Camera=camera;w.Focus=camObj.transform;
        var lightObj=new GameObject("Diagnostic light");var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;lightObj.transform.rotation=Quaternion.Euler(40,-35,0);
        var target=new RenderTexture(1200,800,24);target.Create();var old=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;var active=RenderTexture.active;
        Action<ScriptableRenderContext,Camera> submit=(_,cam)=>{if(cam==camera){Invoke(w,"LateUpdate");Invoke(w.ColliderDebug,"LateUpdate");}};
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;RenderPipelineManager.beginCameraRendering+=submit;
            foreach(var mode in new[]{BuildColliderDebugMode.Off,BuildColliderDebugMode.Solids,BuildColliderDebugMode.SolidsAndClearance})
            {
                w.ColliderDebug.Mode=mode;
                for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;var pixels=new Texture2D(1200,800,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1200,800),0,0);pixels.Apply();
                File.WriteAllBytes(".utmp/collider-debug/"+mode+".png",pixels.EncodeToPNG());
                int red=0,blue=0,purple=0;foreach(var color in pixels.GetPixels32())
                {if(color.r>200 && color.g<150 && color.b<170)red++;if(color.b>220 && color.r<140 && color.g>170)blue++;if(color.r>180 && color.b>200 && color.g<180)purple++;}
                if(mode!=BuildColliderDebugMode.Off)Check(red>100 && blue>100,$"Physical / invalid preview debug colors missing in URP render: red={red}, blue={blue}");
                if(mode==BuildColliderDebugMode.SolidsAndClearance)Check(purple>100,"Headroom debug not rendered");
                Object.DestroyImmediate(pixels);
            }
            var allowed=At(stair,f,new Vector3Int(5,0,0));w.Validate(ref allowed);
            Check(allowed.Valid && allowed.Warning==BuildPlacementWarning.BuildOverlap,"Overlapping diagnostic preview is still rejected");
            w.ColliderDebug.Mode=BuildColliderDebugMode.SolidsAndClearance;
            for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;var acceptedPixels=new Texture2D(1200,800,TextureFormat.RGBA32,false);acceptedPixels.ReadPixels(new Rect(0,0,1200,800),0,0);acceptedPixels.Apply();
            File.WriteAllBytes(".utmp/collider-debug/AcceptedOverlap.png",acceptedPixels.EncodeToPNG());
            int amber=0;foreach(var color in acceptedPixels.GetPixels32())if(color.r>220 && color.g>190 && color.b<150)amber++;
            Check(amber>100,"Accepted-overlap wires did not render in amber");Object.DestroyImmediate(acceptedPixels);
        }finally{RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=old;QualitySettings.renderPipeline=quality;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(camObj);Object.DestroyImmediate(lightObj);Object.DestroyImmediate(w.gameObject);}
    }
}
