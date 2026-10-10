using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

/// <summary>Runs in real Play mode: unreadable FBX data must not be accessed by bearing generation.</summary>
[InitializeOnLoad]
public static class BuildIntersectionRegressionValidation
{
    private const string Pending="FS.Building.IntersectionRegression";
    private static int checks;
    private static readonly List<string> errors=new();
    static BuildIntersectionRegressionValidation()=>EditorApplication.playModeStateChanged+=OnState;
    public static void RunBatch()
    {
        BuildingPrototypeSetup.CreateAssets();SessionState.SetBool(Pending,true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
    }
    private static void OnState(PlayModeStateChange change)
    {
        if(change!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=0;errors.Clear();Application.logMessageReceived+=Log;
        try
        {
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            Check(Application.isPlaying,"The regressions must run in Play mode");
            Check(!c.presets[0].mesh.isReadable,"Imported wall should retain Read/Write disabled");
            for(int yaw=0;yaw<8;yaw++)
            {
                Stack(c,yaw);
                Permissive(c,yaw);
                foreach(bool overlap in new[]{false,true})foreach(bool roofFirst in new[]{false,true})RoofStair(c,yaw,overlap,roofFirst);
            }
            JointMetadata(c);Render(c);WarningHud(c);
            Check(errors.Count==0,"Runtime errors during regression: "+string.Join(" | ",errors));
            Debug.Log("BUILD INTERSECTION PLAY MODE PASS: "+checks+" checks; unreadable imported wall/infill with separate bearing output, eight-yaw stack/floor/remove/preview transitions, roof/stair warnings and commits in both orders, retained physical colliders, hard blockers/support/duplicates, warning HUD and actual stacked-wall pixels.");
            Application.logMessageReceived-=Log;EditorApplication.Exit(0);
        }
        catch(Exception ex){Application.logMessageReceived-=Log;Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Log(string message,string stack,LogType type)
    {if(type is LogType.Error or LogType.Exception or LogType.Assert)errors.Add(message);}
    private static void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
    private static void Invoke(object target,string method)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
    private static BuildWorld World(Vector3 origin)
    {
        var host=new GameObject("Intersection runtime fixture");host.transform.position=origin;
        var world=host.AddComponent<BuildWorld>();world.enabled=false;world.Focus=host.transform;return world;
    }
    private static BuildPreview At(BuildDefinition d,BuildGridFrame f,Vector3Int a,int turn=0)=>new(){Definition=d,Frame=f,Anchor=a,YawStep=turn,Origin=BuildGeometry.WorldPoint(f,a),WorldYaw=BuildGeometry.Turn(f.YawStep+turn)};
    private static void Stack(BuildCatalog c,int yaw)
    {
        foreach(var d in new[]{c.presets[0],c.Find(SplitPlankWallSetup.InfillContentId),c.Find(WattleWallSetup.ContentId),c.presets[3]})
        {
            var w=World(new Vector3(20000+yaw*20,20000,20000));var f=w.Session.CreateFrame(w.transform.position,yaw);
            try
            {
                w.Session.Add(c.presets[2],f,default,0,true);
                var lower=w.Commit(At(d,f,new Vector3Int(1,0,0)));Check(lower!=null,"Lower component failed");
                var p=At(d,f,new Vector3Int(1,12,0));w.Validate(ref p);
                Check(p.Valid && p.VisualMesh==d.mesh && p.Resolved.auxiliaryMesh!=null,"Stack preview lost the original core or separate bearing");
                var upper=w.Commit(p);Check(upper!=null && upper.Resolved.mesh==d.mesh && upper.Resolved.auxiliaryMesh!=null,"Stack commit lost the authored body");
                Check(upper.Resolved.auxiliaryMesh.bounds.max.y<=.001f && upper.Resolved.auxiliaryMesh.bounds.min.y>=-.251f,"Bearing mesh contains wall body geometry");
                var third=w.Commit(At(d,f,new Vector3Int(1,24,0)));Check(third!=null && third.Resolved.mesh==d.mesh,"Third storey lost its body");
                var floorPreview=At(c.presets[1],f,new Vector3Int(0,12,0));w.Validate(ref floorPreview);Check(floorPreview.Valid,"Floor insertion into stack failed: "+floorPreview.Message);
                Check(w.DebugStates[upper.Id].mesh==d.mesh && w.DebugStates[upper.Id].auxiliaryMesh==null,"Floor preview did not suppress just the auxiliary bearing");
                var floor=w.Commit(floorPreview);Check(floor!=null && upper.Resolved.auxiliaryMesh==null && upper.Resolved.mesh==d.mesh,"Floor insertion replaced the wall core");
                w.Remove(floor.Id);Check(upper.Resolved.mesh==d.mesh && upper.Resolved.auxiliaryMesh!=null,"Removing a floor lost the restored bearing/core");
                w.Remove(lower.Id);Check(upper.Resolved.mesh==d.mesh && upper.Resolved.auxiliaryMesh==null && !upper.Supported,"Removing lower wall changed the upper core or retained support");
                var restored=w.Commit(At(d,f,new Vector3Int(1,0,0)));Check(restored!=null && upper.Supported && upper.Resolved.auxiliaryMesh!=null,"Restoring lower wall did not restore bearing/support");
            }finally{Object.DestroyImmediate(w.gameObject);Physics.SyncTransforms();}
        }
    }
    private static void RoofStair(BuildCatalog c,int yaw,bool overlap,bool roofFirst)
    {
        var w=World(new Vector3(23000+yaw*20,23000,23000));var f=w.Session.CreateFrame(w.transform.position,yaw);
        var roof=c.Find(ThatchRoofSetup.ContentId);var stair=c.Find(W21StairSetup.ContentId);
        try
        {
            w.Session.Add(c.presets[2],f,default,0,true);w.Commit(At(c.presets[0],f,new Vector3Int(1,0,0)));
            if(overlap)Check(w.Commit(At(c.presets[1],f,new Vector3Int(0,8,0)))!=null,"Raised stair platform failed");
            var rp=At(roof,f,new Vector3Int(0,12,0));var sp=At(stair,f,new Vector3Int(2,overlap?8:0,6),2);
            var first=w.Commit(roofFirst?rp:sp);Check(first!=null,"First roof/stair placement failed");
            var second=roofFirst?sp:rp;w.Validate(ref second);
            Check(second.Valid && second.Failure==BuildPlacementFailure.None && second.WarningPieceId==first.Id,"Advisory placement rejected or missing warning identity: "+second.Message);
            Check(second.Warning==(overlap?BuildPlacementWarning.RoofStairOverlap:BuildPlacementWarning.RoofStairHeadroom),"Wrong warning category: "+second.Warning);
            var placed=w.Commit(second);Check(placed!=null,"Advisory placement could not commit");
            var stairRecord=roofFirst?placed:first;var roofRecord=roofFirst?first:placed;
            Check(BuildResolvedGeometry.Overlaps(stairRecord,stairRecord.Resolved,roofRecord,roofRecord.Resolved)==overlap,"Fixture does not exercise the requested solid overlap");
            Invoke(w,"LateUpdate");
            Check(w.TryGetCollisionProxy(stairRecord.Id,out var stairProxy) && stairProxy.RampShape.enabled && stairProxy.RampShape.sharedMesh==stair.collisionMesh,"Allowed overlap disabled/changed the physical stair ramp");
            Check(w.TryGetCollisionProxy(roofRecord.Id,out var roofProxy),"Roof collider not streamed");
            var colliders=new List<Collider>();roofProxy.GetEnabledColliders(colliders);int occupied=0;
            foreach(var collider in colliders)if(collider is MeshCollider mesh && mesh.enabled)occupied++;
            Check(occupied==roof.occupiedVolumes.Length,"Allowed overlap removed a physical roof slab");
            var duplicate=rp;w.Validate(ref duplicate);Check(!duplicate.Valid && duplicate.Failure==BuildPlacementFailure.Solid,"Duplicate roof slipped through advisory exception");
            duplicate=sp;w.Validate(ref duplicate);Check(!duplicate.Valid && duplicate.Failure==BuildPlacementFailure.Solid,"Duplicate stairs slipped through advisory exception");
            var wallCross=At(stair,f,new Vector3Int(2,0,4),2);w.Validate(ref wallCross);Check(wallCross.Valid && wallCross.Warning==BuildPlacementWarning.BuildOverlap,"Stair/wall overlap was not advisory");
            var unsupported=At(roof,f,new Vector3Int(0,60,0));w.Validate(ref unsupported);Check(!unsupported.Valid && unsupported.Failure==BuildPlacementFailure.Support,"Roof warning bypassed required support");
            var obstacle=new GameObject("Independent roof blocker"){layer=GameplayLayers.Player};var box=obstacle.AddComponent<BoxCollider>();box.size=Vector3.one*.2f;
            obstacle.transform.position=f.Origin+BuildGeometry.Rotation(yaw)*new Vector3(2,3.85f,.5f);Physics.SyncTransforms();
            w.Remove(roofRecord.Id);var blocked=rp;w.Validate(ref blocked);Check(!blocked.Valid && blocked.Failure==BuildPlacementFailure.External,"Roof/stair exception bypassed independent player obstruction");
            Object.DestroyImmediate(obstacle);Physics.SyncTransforms();w.Remove(stairRecord.Id);
            var clear=rp;w.Validate(ref clear);Check(clear.Valid && clear.Warning==BuildPlacementWarning.None && clear.WarningPieceId==0,"Removed stairs left a stale warning");
        }finally{Object.DestroyImmediate(w.gameObject);Physics.SyncTransforms();}
    }
    private static void Permissive(BuildCatalog c,int yaw)
    {
        var w=World(new Vector3(30000+yaw*20,30000,30000));var f=w.Session.CreateFrame(w.transform.position,yaw);
        var stair=c.Find(W21StairSetup.ContentId);
        try
        {
            w.Session.Add(c.presets[2],f,default,0,true);
            var wall=w.Commit(At(c.presets[0],f,new Vector3Int(1,0,0)));Check(wall!=null,"Permissive base wall failed");
            var first=w.Commit(At(stair,f,new Vector3Int(2,0,6),2));Check(first!=null,"Adjacent stair foot failed");
            var second=w.Commit(At(stair,f,new Vector3Int(10,6,6),2));Check(second!=null,"Adjacent second flight failed");
            var upper=w.Commit(At(c.presets[0],f,new Vector3Int(1,12,0)));Check(upper!=null,"Same-plane upper wall blocked adjacent two-flight stairs");
            Check(BuildResolvedGeometry.Connects(wall,new BuildResolvedState{boxes=new[]{wall.Definition.LocalBounds}},upper,new BuildResolvedState{boxes=new[]{upper.Definition.LocalBounds}}),"Declared stack support still depends on generated bearing output");
            var head=At(c.Find(SplitPlankWallSetup.InfillContentId),f,new Vector3Int(6,12,1));w.Validate(ref head);
            Check(head.Valid && head.Warning==BuildPlacementWarning.StairHeadroom,"Wall headroom still rejects build eligibility: "+head.Message);
            var crossing=At(stair,f,new Vector3Int(2,0,4),2);w.Validate(ref crossing);
            Check(crossing.Valid && crossing.Warning==BuildPlacementWarning.BuildOverlap,"Stair/wall solid overlap still rejects placement");
            var crossed=w.Commit(crossing);Check(crossed!=null && crossed.Supported,"Overlap did not transmit physical support");
            Invoke(w,"LateUpdate");
            Check(w.TryGetCollisionProxy(wall.Id,out var wallProxy) && wallProxy.Shape.enabled && wallProxy.Shape.size==wall.Definition.LocalBounds.size,"Permissive placement carved the wall collider");
            Check(w.TryGetCollisionProxy(crossed.Id,out var stairProxy) && stairProxy.RampShape.enabled && stairProxy.RampShape.sharedMesh==stair.collisionMesh,"Permissive placement changed the walking hull");
            var duplicate=At(c.presets[0],f,new Vector3Int(15,0,1),4);w.Validate(ref duplicate);
            Check(!duplicate.Valid && duplicate.Message.StartsWith("Duplicate"),"Reversed duplicate wall was accepted");
            var diagonal=At(c.presets[1],f,new Vector3Int(0,6,0),1);w.Validate(ref diagonal);
            Check(diagonal.Valid && diagonal.Resolved.invalidJoint && diagonal.Resolved.mesh==c.presets[1].mesh && diagonal.Resolved.boxes.Length==1,"Unavailable diagonal fitting blocked eligibility or generated an incomplete floor");
            Check(w.Commit(diagonal)!=null,"Full-solid fitting fallback did not commit");
            var floorRecord=new BuildPieceRecord{Id=998,Definition=c.presets[1],Origin=first.Origin+Vector3.up*2,WorldYawStep=first.WorldYawStep};
            var solidFloor=new BuildResolvedState{mesh=c.presets[1].mesh,boxes=new[]{c.presets[1].LocalBounds}};
            Check(BuildResolvedGeometry.BlocksStairPassage(first,floorRecord,solidFloor),"Solid floor fallback was ignored by stair headroom diagnostics");
        }finally{Object.DestroyImmediate(w.gameObject);Physics.SyncTransforms();}
    }
    private static void JointMetadata(BuildCatalog c)
    {
        var wall=ScriptableObject.CreateInstance<BuildDefinition>();wall.kind=BuildPartKind.Wall;wall.contentId="fixture.future-full-span";
        wall.sizeUnits=new Vector3Int(16,12,1);wall.boundsOffset=new Vector3(0,0,-.125f);wall.wallEndInsetUnits=0;wall.stackRiseUnits=12;
        wall.jointSockets=new[]{new BuildJointSocket{localPosition=Vector3.zero,outward=Vector3.left},new BuildJointSocket{localPosition=new Vector3(4,0,0),outward=Vector3.right}};
        wall.mesh=BuildTimberMesh.Create("Future wall fixture",new[]{new BuildTimberPart{bounds=wall.LocalBounds,grainAxis=1}});
        try
        {
            Check(Mathf.Abs(wall.LocalBounds.min.z+.125f)<.00001f && Mathf.Abs(wall.WallBaySpan-4)<.00001f,"Future center-line bounds cannot describe a full 4m wall");
            for(int yaw=0;yaw<8;yaw++)
            {
                bool? owner=null;
                foreach(bool reverse in new[]{false,true})
                {
                    using var s=new BuildSession();var f=s.CreateFrame(new Vector3(10,10,10),yaw);BuildPieceRecord a,b;
                    if(reverse){b=s.Add(wall,f,new Vector3Int(16,0,0),2,true);a=s.Add(wall,f,default,0,true);}
                    else{a=s.Add(wall,f,default,0,true);b=s.Add(wall,f,new Vector3Int(16,0,0),2,true);}
                    Check(a.Resolved.joints.Length==1 && b.Resolved.joints.Length==1 && a.Resolved.joints[0].Kind==BuildJointKind.Corner,"Declared corner endpoints did not resolve");
                    Check(a.Resolved.joints[0].PrimaryOwner!=b.Resolved.joints[0].PrimaryOwner,"Corner has two primary owners");
                    if(owner.HasValue)Check(owner.Value==a.Resolved.joints[0].PrimaryOwner,"Corner owner depends on placement order");else owner=a.Resolved.joints[0].PrimaryOwner;
                    Check(a.Resolved.boxes.Length==1 && a.Resolved.auxiliaryMesh==null,"Planning metadata generated unrequested corner geometry/colliders");
                    var stacked=BuildPlacement.Solve(wall,a.Origin+Vector3.up*3,Vector3.up,a,s.Frame(a.OwnFrameId),0,0,default);
                    Check(Mathf.Abs(stacked.Origin.y-a.Origin.y-3)<.00001f,"Future full-height wall still adds a bearing band to its story rise");
                    var deck=BuildPlacement.Solve(c.presets[1],a.Origin+Vector3.up*3,Vector3.up,a,s.Frame(a.OwnFrameId),0,0,default);
                    Check(Mathf.Abs(deck.Origin.y-a.Origin.y-3)<.00001f,"Future floor walking plane is lifted above the 3m wall level");
                    var roof=BuildPlacement.Solve(c.Find(ThatchRoofSetup.ContentId),a.Origin+Vector3.up*3,Vector3.up,a,s.Frame(a.OwnFrameId),0,0,default);
                    Check(Mathf.Abs(roof.Origin.y-a.Origin.y-3)<.00001f,"Future roof bearing is lifted above the 3m wall level");
                }
            }
        }finally{Object.DestroyImmediate(wall.mesh);Object.DestroyImmediate(wall);}
    }
    private static void WarningHud(BuildCatalog c)
    {
        var host=new GameObject("Warning HUD fixture");using var view=new BuildMenuView(c,host.transform,_=>{});
        view.Show("Roof","Ready to place; low stair headroom",true,true);
        var status=host.GetComponentInChildren<UIDocument>().rootVisualElement.Q<Label>("build-status");
        Check(status.ClassListContains("build-warning") && !status.ClassListContains("build-error"),"Advisory HUD styled as a hard rejection");
        view.Show("Roof","Ready to place",true);Check(!status.ClassListContains("build-warning"),"HUD warning persisted after clearance");
        Object.DestroyImmediate(host);
    }
    private static void Render(BuildCatalog c)
    {
        Directory.CreateDirectory(".utmp/intersection-regression");var w=World(Vector3.zero);var f=w.Session.CreateFrame(Vector3.zero,0);
        w.Session.Add(c.presets[2],f,default,0,true);var lower=w.Commit(At(c.presets[0],f,new Vector3Int(1,0,0)));var upper=w.Commit(At(c.presets[0],f,new Vector3Int(1,12,0)));
        var camObj=new GameObject("Stack runtime camera");var cam=camObj.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.orthographicSize=3.4f;cam.aspect=1.5f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.03f,.04f,.06f);
        cam.transform.position=new Vector3(2,2.9f,-8);cam.transform.LookAt(new Vector3(2,2.9f,0));
        var lightObj=new GameObject("Stack runtime light");var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;lightObj.transform.rotation=Quaternion.Euler(35,-35,0);
        var renderer=new BuildRenderer(w.Session);var target=new RenderTexture(1200,800,24);target.Create();var old=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;var active=RenderTexture.active;
        Action<ScriptableRenderContext,Camera> draw=(_,camera)=>{if(camera==cam)renderer.Draw(cam,3000,140);};
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;RenderPipelineManager.beginCameraRendering+=draw;
            void Capture(string name)
            {
                for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;var texture=new Texture2D(1200,800,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,1200,800),0,0);texture.Apply();File.WriteAllBytes(".utmp/intersection-regression/"+name+".png",texture.EncodeToPNG());
                int wood=0,total=0;var pixels=texture.GetPixels32();var lo=cam.WorldToViewportPoint(new Vector3(.65f,3.25f,0));var hi=cam.WorldToViewportPoint(new Vector3(3.35f,5.5f,0));
                for(int y=(int)(lo.y*800);y<(int)(hi.y*800);y++)for(int x=(int)(lo.x*1200);x<(int)(hi.x*1200);x++)
                {var pixel=pixels[y*1200+x];total++;if(pixel.r>pixel.b+8)wood++;}
                Check(wood>total*.7f,$"Upper wall body missing in actual Play-mode render {name}: wood={wood}/{total}");Object.Destroy(texture);
            }
            Capture("stacked-wall");Check(renderer.DrawCalls==3,"Core walls and auxiliary bearing are not separately batched");
            var distantFloor=At(c.presets[1],f,new Vector3Int(32,12,0));w.Validate(ref distantFloor);renderer.SetPreview(w.DebugStates);Capture("unseated-floor-preview");
            Check(renderer.DrawCalls==3,"Distant floor preview changed wall bearing output");
            var floor=At(c.presets[1],f,new Vector3Int(0,12,0));w.Validate(ref floor);renderer.SetPreview(w.DebugStates);Capture("floor-insertion-preview");
            Check(renderer.DrawCalls==2,"Preview cache did not remove the auxiliary batch when only the bearing changed");
            renderer.SetPreview(null);var deck=w.Commit(floor);Check(deck!=null,"Runtime floor insertion failed");Capture("floor-inserted");
            w.Remove(deck.Id);Capture("bearing-restored");
            w.Remove(lower.Id);Capture("lower-wall-removed");
            Check(upper.Resolved.mesh==c.presets[0].mesh && !upper.Resolved.mesh.isReadable,"Rendering changed source mesh readability or identity");
        }finally{RenderPipelineManager.beginCameraRendering-=draw;GraphicsSettings.defaultRenderPipeline=old;QualitySettings.renderPipeline=quality;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(camObj);Object.DestroyImmediate(lightObj);Object.DestroyImmediate(w.gameObject);}
    }
}
