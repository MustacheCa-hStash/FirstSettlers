using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object=UnityEngine.Object;

public static class StairExtensionValidation
{
    private static Action<bool,string> check;
    private static void C(bool value,string message)=>check(value,"Stair extension: "+message);
    private static void Near(Vector3 a,Vector3 b,string message)=>C((a-b).sqrMagnitude<.000001f,message+" "+a+" != "+b);
    public static void Assets(BuildCatalog c,Action<bool,string> assertion)
    {
        check=assertion;var d=c.Find(StairExtensionSetup.W22Id);var rail=c.Find(StairExtensionSetup.W24Id);
        C(d!=null && rail!=null,"Menu definitions missing");Near(d.LocalBounds.size,new Vector3(1.25f,3,4),"W22 dimensions");Near(d.mesh.bounds.min,Vector3.zero,"W22 origin");Near(d.mesh.bounds.max,new Vector3(1.25f,3,4),"W22 native geometry");
        C(d.collisionMesh.vertexCount==6 && d.collisionMesh.triangles.Length==24 && d.authoringPrefab.GetComponent<SmoothWalkSurface>()!=null,"W22 smooth walking hull missing");
        Near(rail.LocalBounds.size,new Vector3(.25f,2.5f,2),"W24 envelope");C(rail.kind==BuildPartKind.Handrail && rail.occupiedVolumes.Length==3 && rail.authoringPrefab.GetComponent<BoxCollider>()==null,"Rail has solid wall collision");
        foreach(var path in new[]{StairExtensionSetup.W22Model,StairExtensionSetup.W24Model}){var i=(UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(path);C(i.bakeAxisConversion && !i.isReadable,"Import settings");}
    }
    public static void Runtime(BuildCatalog c,Action<bool,string> assertion)
    {
        check=assertion;
        for(int yaw=0;yaw<8;yaw++)foreach(var stair in new[]{c.Find(W21StairSetup.ContentId),c.Find(StairExtensionSetup.W22Id)})Fixture(c,stair,yaw);
        for(int yaw=0;yaw<8;yaw++)foreach(var fixedDefinition in new[]{c.Find(W21StairSetup.ContentId),c.Find(StairExtensionSetup.W22Id)})
            foreach(var selectedDefinition in new[]{c.Find(W21StairSetup.ContentId),c.Find(StairExtensionSetup.W22Id)})ControllerUnderside(c,fixedDefinition,selectedDefinition,yaw);
        Debug.Log("STAIR EXTENSION PASS: all headings; floor/roof underside and downward mixed-flight snaps; left/right segment handrails; real TerrainCollider and marked terrain embedding; buried toe/ground start/fully buried rejection; exposed high-end continuation; external obstacle retention.");
    }
    private static BuildPreview At(BuildDefinition d,BuildGridFrame f,Vector3 local)
    {
        var origin=f.Origin+BuildGeometry.Rotation(f.YawStep)*local;return new BuildPreview{Definition=d,Origin=origin,WorldYaw=f.YawStep,Frame=new BuildGridFrame{Origin=origin,YawStep=f.YawStep}};
    }
    private static void Stream(BuildWorld w)=>typeof(BuildWorld).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,null);
    private static void ControllerUnderside(BuildCatalog c,BuildDefinition fixedDefinition,BuildDefinition selectedDefinition,int yaw)
    {
        var host=new GameObject("Actual stair underside aim fixture");host.transform.position=new Vector3(14000+yaw*20,100,14000);var world=host.AddComponent<BuildWorld>();world.enabled=false;
        var f=world.Session.CreateFrame(host.transform.position,yaw);var q=BuildGeometry.Rotation(yaw);var fields=BindingFlags.Instance|BindingFlags.NonPublic;
        var cameraObject=new GameObject("Underside aim camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.nearClipPlane=.01f;
        var controller=host.AddComponent<BuildingController>();controller.enabled=false;
        try
        {
            world.Session.Add(c.presets[2],f,default,0,true);world.Session.Add(c.presets[0],f,default,0,false);world.Session.Add(c.presets[1],f,new Vector3Int(0,12,0),0,false);
            var fixedFrame=world.Session.CreateFrame(f.Origin+q*new Vector3(1.375f,3,0),yaw);var fixedStair=world.Session.Add(fixedDefinition,fixedFrame,default,0,false);C(fixedStair.Supported,"Controller fixture stair unsupported");
            typeof(BuildingController).GetField("viewCamera",fields).SetValue(controller,camera);typeof(BuildingController).GetMethod("Start",fields).Invoke(controller,null);
            controller.enabled=true;controller.Toggle();controller.Select(selectedDefinition);
            Stream(world);
            foreach(bool inside in new[]{false,true})
            {
                Vector3 local=inside?new Vector3(.625f,.25f,1.25f):new Vector3(-1,.45f,1.25f);Vector3 direction=q*(inside?Vector3.up:Vector3.right);
                camera.transform.position=fixedStair.Origin+q*local;camera.transform.rotation=Quaternion.LookRotation(direction,q*Vector3.forward);Physics.SyncTransforms();
                typeof(BuildingController).GetMethod("LateUpdate",fields).Invoke(controller,null);var preview=controller.Preview;
                C(preview.Definition==selectedDefinition && preview.StairAimTarget==fixedStair.Id,"Actual underside ray lost target: inside="+inside+" yaw="+yaw);
                C(preview.Hint.Contains("downward"),"Actual underside ray suggested upward flight: inside="+inside+" hint="+preview.Hint);
                C(preview.Valid,"Actual underside preview rejected: "+preview.Message);
                Near(BuildStairAttachment.High(new BuildPieceRecord{Definition=preview.Definition,Origin=preview.Origin,WorldYawStep=(byte)preview.WorldYaw}),BuildStairAttachment.Low(fixedStair),"Controller downward socket mismatch");
            }
            camera.transform.position=fixedStair.Origin+q*new Vector3(.625f,fixedDefinition.LocalBounds.size.y+1,1.25f);
            camera.transform.rotation=Quaternion.LookRotation(Vector3.down,q*Vector3.forward);Physics.SyncTransforms();
            typeof(BuildingController).GetMethod("LateUpdate",fields).Invoke(controller,null);
            C(controller.Preview.StairAimTarget==fixedStair.Id && controller.Preview.Hint.Contains("upward") && controller.Preview.Valid,"Top aim no longer continues upward");
            camera.transform.position=fixedStair.Origin+q*new Vector3(.625f,.25f,1.25f);camera.transform.rotation=Quaternion.LookRotation(Vector3.up,q*Vector3.forward);
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.layer=GameplayLayers.WorldSolid;blocker.transform.localScale=Vector3.one*.15f;blocker.transform.position=camera.transform.position+Vector3.up*.25f;
            try{Physics.SyncTransforms();typeof(BuildingController).GetMethod("LateUpdate",fields).Invoke(controller,null);C(controller.Preview.StairAimTarget!=fixedStair.Id,"Underside guide aimed through an obstruction");}
            finally{Object.DestroyImmediate(blocker);Physics.SyncTransforms();}
        }
        finally{Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Fixture(BuildCatalog c,BuildDefinition d,int yaw)
    {
        var host=new GameObject("Stair terrain fixture");host.transform.position=new Vector3(51000+yaw*40,100,51000);var w=host.AddComponent<BuildWorld>();w.enabled=false;w.Focus=host.transform;
        var s=w.Session;var f=s.CreateFrame(host.transform.position,yaw);var q=BuildGeometry.Rotation(yaw);GameObject ground=null;TerrainData terrainData=null;
        try
        {
            s.Add(c.presets[2],f,default,0,true);s.Add(c.presets[0],f,default,0,false);var floor=s.Add(c.presets[1],f,new Vector3Int(0,12,0),0,false);C(floor.Supported,"Fixture floor unsupported");
            var downward=BuildPlacement.Solve(d,f.Origin+q*new Vector3(2,2.75f,.1f),Vector3.down,floor,s.Frame(floor.OwnFrameId),0,0,default);
            Near(BuildStairAttachment.High(new BuildPieceRecord{Definition=d,Origin=downward.Origin,WorldYawStep=(byte)downward.WorldYaw}),f.Origin+q*new Vector3(2,3,0),"Underside floor snap must use high end and walking level");
            var fixedStair=w.Commit(downward);C(fixedStair!=null,"Underside descending placement failed");
            // Both stair lengths connect downward to the fixed toe, including mixed W21/W22 flights.
            var other=d==c.Find(W21StairSetup.ContentId)?c.Find(StairExtensionSetup.W22Id):c.Find(W21StairSetup.ContentId);
            var below=BuildPlacement.Solve(other,fixedStair.Origin+q*new Vector3(.625f,0,.5f),Vector3.down,fixedStair,s.Frame(fixedStair.OwnFrameId),0,0,default);
            var lower=w.Commit(below);C(lower!=null,"Downward mixed-flight continuation failed");Near(BuildStairAttachment.High(lower),BuildStairAttachment.Low(fixedStair),"Downward sockets do not meet");w.Remove(lower.Id);
            // Left/right rails attach outside the full walking width; W22 has two independent segments.
            var rail=c.Find(StairExtensionSetup.W24Id);var rails=new List<BuildPieceRecord>();
            foreach(float segment in d.LocalBounds.size.z>2.01f?new[]{0f,2f}:new[]{0f})foreach(bool left in new[]{true,false})
            {
                var snap=BuildPlacement.Solve(rail,fixedStair.Origin+q*new Vector3(left?0:1.25f,segment*.75f,segment+.5f),q*(left?Vector3.left:Vector3.right),fixedStair,s.Frame(fixedStair.OwnFrameId),0,0,default);
                var r=w.Commit(snap);C(r!=null && r.Supported,"Side handrail rejected");Near(r.Origin,fixedStair.Origin+q*new Vector3(left?-.1875f:1.1875f,segment*.75f,segment),"Rail intrudes into walking width");rails.Add(r);
            }
            Stream(w);
            foreach(var r in rails){C(w.TryGetCollisionProxy(r.Id,out var p),"Rail proxy missing");var colliders=new List<Collider>();p.GetEnabledColliders(colliders);C(colliders.Count==3 && p.Shape==null,"Rail collision filled its envelope");}
            foreach(var r in rails)w.Remove(r.Id);
            w.Remove(fixedStair.Id);
            // Ground is a true TerrainCollider, with a flat top 2m above the fixture's foundation datum.
            terrainData=new TerrainData{heightmapResolution=33,size=new Vector3(32,5,32)};var heights=new float[33,33];for(int x=0;x<33;x++)for(int z=0;z<33;z++)heights[x,z]=1;
            terrainData.SetHeights(0,0,heights);ground=Terrain.CreateTerrainGameObject(terrainData);ground.layer=GameplayLayers.WorldSolid;ground.transform.position=f.Origin+new Vector3(-16,-3,-16);Physics.SyncTransforms();
            C(BuildStairAttachment.Buried(f.Origin+Vector3.up) && !BuildStairAttachment.Buried(f.Origin+Vector3.up*3),"Terrain sampling wrong");
            fixedStair=w.Commit(downward);C(fixedStair!=null,"Attached flight intersecting terrain rejected");C(fixedStair.Supported && !fixedStair.Grounded,"Terrain grounded stair instead of building attachment");
            below=BuildPlacement.Solve(other,fixedStair.Origin+q*new Vector3(.625f,0,.5f),Vector3.down,fixedStair,s.Frame(fixedStair.OwnFrameId),0,0,default);w.Validate(ref below);C(!below.Valid && below.Failure==BuildPlacementFailure.Support,"Buried toe became another attachment");
            var upper=BuildPlacement.Solve(other,fixedStair.Origin+q*new Vector3(.625f,d.LocalBounds.size.y,d.LocalBounds.size.z),Vector3.up,fixedStair,s.Frame(fixedStair.OwnFrameId),0,0,default);
            var extension=w.Commit(upper);C(extension!=null,"Exposed high end of partly buried stair rejected");w.Remove(extension.Id);
            var terrainStart=BuildPlacement.Solve(d,f.Origin+q*new Vector3(2,2,0),Vector3.up,null,null,yaw,0,default);w.Validate(ref terrainStart);C(!terrainStart.Valid && terrainStart.Failure==BuildPlacementFailure.Support,"Direct terrain start accepted");
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.layer=GameplayLayers.WorldSolid;cube.transform.localScale=Vector3.one*.3f;
            try
            {
                w.Remove(fixedStair.Id);cube.transform.position=downward.Origin+q*new Vector3(.625f,.1f,d.LocalBounds.size.z*.5f);Physics.SyncTransforms();
                var blocked=downward;w.Validate(ref blocked);C(!blocked.Valid && blocked.Failure==BuildPlacementFailure.External,"Non-terrain obstruction ignored");
            }finally{Object.DestroyImmediate(cube);Physics.SyncTransforms();}
            Object.DestroyImmediate(ground);Object.DestroyImmediate(terrainData);ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.layer=GameplayLayers.WorldSolid;ground.AddComponent<WorldGroundSurface>();ground.transform.position=f.Origin+Vector3.down*1.5f;ground.transform.localScale=new Vector3(32,7,32);Physics.SyncTransforms();
            fixedStair=w.Commit(downward);C(fixedStair!=null,"Marked streamed terrain embedding rejected");
            // Underside roof aim must be handled by stair logic before generic roof placement.
            var roof=s.Add(c.Find(ThatchRoofSetup.ContentId),f,new Vector3Int(0,12,0),0,false);
            var underRoof=BuildPlacement.Solve(d,roof.Origin+q*new Vector3(2,1,1),q*new Vector3(0,-1,1).normalized,roof,s.Frame(roof.OwnFrameId),0,0,default);
            var roofStair=w.Commit(underRoof);C(roofStair!=null && underRoof.Hint.Contains("under roof"),"Roof underside did not place downward stair");
            Near(BuildStairAttachment.High(roofStair),roof.Origin+q*new Vector3(2,1,1),"Roof underside socket");
            // Raise terrain over the high end: a completely buried flight cannot be added.
            w.Remove(fixedStair.Id);ground.transform.position=f.Origin+Vector3.up;Physics.SyncTransforms();var buried=downward;w.Validate(ref buried);C(!buried.Valid && buried.Failure==BuildPlacementFailure.Support,"Fully buried stair accepted");
            Object.DestroyImmediate(ground);ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.layer=GameplayLayers.WorldSolid;ground.AddComponent<WorldGroundSurface>();ground.transform.position=f.Origin+Vector3.up*40;Physics.SyncTransforms();
            C(BuildStairAttachment.Buried(f.Origin+Vector3.up*3),"Deep marked mesh terrain escaped endpoint test");
        }
        finally{if(ground!=null)Object.DestroyImmediate(ground);if(terrainData!=null)Object.DestroyImmediate(terrainData);Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
}
