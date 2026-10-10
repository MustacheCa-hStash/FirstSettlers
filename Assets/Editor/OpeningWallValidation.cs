using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Real Play-mode aperture collision, movement and half-gable assembly regressions.</summary>
public static class OpeningWallValidation
{
    private static Action<bool,string> check;
    private static void C(bool condition,string message)=>check(condition,"Opening kit: "+message);
    private static BuildPreview At(BuildDefinition d,BuildGridFrame f,Vector3 local,int turn=0)
    {
        var origin=f.Origin+BuildGeometry.Rotation(f.YawStep)*local;int yaw=BuildGeometry.Turn(f.YawStep+turn);
        return new BuildPreview{Definition=d,Origin=origin,WorldYaw=yaw,Frame=new BuildGridFrame{Origin=origin,YawStep=(byte)yaw}};
    }
    private static void Near(Vector3 a,Vector3 b,string message)=>C((a-b).sqrMagnitude<.000001f,message+" "+a+" != "+b);
    public static void Assets(BuildCatalog c,Action<bool,string> assertion)
    {
        check=assertion;var door=c.Find(OpeningWallSetup.DoorId);var window=c.Find(OpeningWallSetup.WindowId);var rise=c.Find(OpeningWallSetup.GableId);var fall=c.Find(OpeningWallSetup.FallingId);
        C(door!=null && window!=null && rise!=null && fall!=null,"Definitions missing");
        C(rise.flipVariant==fall && fall.flipVariant==rise && Array.IndexOf(c.presets,fall)<0,"Gable flip/menu references wrong");
        foreach(var d in new[]{door,window})
        {
            C(d.sizeUnits==new Vector3Int(16,12,1) && d.stackRiseUnits==12 && d.wallEndInsetUnits==0,"Full-span opening wall profile");
            Near(d.mesh.bounds.min,new Vector3(0,0,-.125f),"Opening mesh origin");Near(d.mesh.bounds.max,new Vector3(4,3,.125f),"Opening mesh dimensions");
            C(d.wallOpenings.Length==1 && d.jointCoverVariants.Length==3,"Opening/corner references missing");
            C(d.authoringPrefab.GetComponentsInChildren<BoxCollider>().Length==d.solidBoxes.Length,"Prefab fills aperture with extra collider");
            foreach(var box in d.solidBoxes)
            {
                Vector3 overlap=Vector3.Min(box.max,d.wallOpenings[0].max)-Vector3.Max(box.min,d.wallOpenings[0].min);
                C(overlap.x<.001f || overlap.y<.001f || overlap.z<.001f,"Authored box fills aperture");
            }
        }
        Near(door.wallOpenings[0].size,new Vector3(1.25f,2.25f,.25f),"Door clear size");Near(window.wallOpenings[0].size,new Vector3(1,.75f,.25f),"Window clear size");
        C(door.solidBoxes.Length==3 && window.solidBoxes.Length==4,"Opening decomposition");
        foreach(var d in new[]{rise,fall})
        {
            Near(d.mesh.bounds.min,new Vector3(0,0,-.125f),"Gable origin");Near(d.mesh.bounds.max,new Vector3(2,2,.125f),"Gable dimensions");
            C(d.occupiedVolumes.Length==1 && d.occupiedVolumes[0].vertices.Length==6 && d.occupiedVolumes[0].mesh.triangles.Length==24,"Gable needs six-vertex triangular prism");
            C(d.authoringPrefab.GetComponentsInChildren<BoxCollider>().Length==0,"Gable rectangular collision present");
            C(d.authoringPrefab.GetComponent<MeshCollider>().convex && d.jointCoverVariants.Length==0,"Gable collision/framing wrong");
        }
    }
    public static void Runtime(BuildCatalog c,Action<bool,string> assertion)
    {
        check=assertion;
        for(int yaw=0;yaw<8;yaw++)
        {
            foreach(var d in new[]{c.Find(OpeningWallSetup.DoorId),c.Find(OpeningWallSetup.WindowId)})Aperture(c,d,yaw);
            Gables(c,yaw);
            FloorFitting(c,yaw);
        }
        Debug.Log("OPENING WALLS PASS: W09/W11 aperture physics and all headings; 16 real-motor doorway crossings; opening-aware covers; 3m stacks; mirrored triangular gables, roof-first/wall-first fitting and bidirectional roof support.");
    }
    private static int Covers(BuildSession s){int count=0;foreach(var p in s.Pieces.Values)count+=p.Resolved.attachments.Length;return count;}
    private static void Stream(BuildWorld w)=>typeof(BuildWorld).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,null);
    private static void Aperture(BuildCatalog c,BuildDefinition d,int yaw)
    {
        var host=new GameObject("Aperture collider fixture");host.transform.position=new Vector3(42000+yaw*30,42000,42000);var w=host.AddComponent<BuildWorld>();w.enabled=false;w.Focus=host.transform;
        var s=w.Session;var f=s.CreateFrame(host.transform.position,yaw);var q=BuildGeometry.Rotation(yaw);
        try
        {
            var foundation=s.Add(c.presets[2],f,default,0,true);s.Add(c.presets[2],f,new Vector3Int(0,0,-16),0,true);
            var starter=BuildPlacement.Solve(d,f.Origin+q*new Vector3(2,0,0),Vector3.up,foundation,s.Frame(foundation.OwnFrameId),0,0,default);
            var wall=w.Commit(starter);C(wall!=null,"Opening wall does not build on foundation");Near(wall.Origin,f.Origin,"Opening wall boundary shift");
            Stream(w);C(w.TryGetCollisionProxy(wall.Id,out var proxy),"Opening wall proxy missing");
            var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);C(colliders.Count==d.solidBoxes.Length,"Runtime solid count differs from authored sections");
            bool Ray(Vector3 local)=>Physics.Raycast(wall.Origin+q*(local+Vector3.back),q*Vector3.forward,2,1<<GameplayLayers.WorldSolid,QueryTriggerInteraction.Ignore);
            C(!Ray(d.wallOpenings[0].center),"Aperture still blocks ray");C(Ray(new Vector3(.5f,1,0)),"Solid side lost collision");C(Ray(new Vector3(2,2.7f,0)),"Header lost collision");
            C(!Physics.Raycast(wall.Origin+q*(d.wallOpenings[0].center+Vector3.forward),q*Vector3.back,2,1<<GameplayLayers.WorldSolid),"Aperture blocks reverse ray");
            if(d==c.Find(OpeningWallSetup.WindowId))C(Ray(new Vector3(2,.75f,0)),"Sill/bottom wall lost collision");
            else foreach(bool reverse in new[]{false,true})DoorWalk(wall,q,reverse);
            var obstruction=GameObject.CreatePrimitive(PrimitiveType.Cube);obstruction.layer=GameplayLayers.WorldSolid;obstruction.transform.localScale=Vector3.one*.2f;
            try
            {
                obstruction.transform.position=f.Origin+q*(d.wallOpenings[0].center+Vector3.forward*2);Physics.SyncTransforms();
                var surrounding=At(d,f,new Vector3(0,0,2));w.Validate(ref surrounding);C(surrounding.Valid,"External object in aperture falsely blocked wall");
                obstruction.transform.position=f.Origin+q*new Vector3(.5f,1,2);Physics.SyncTransforms();w.Validate(ref surrounding);
                C(!surrounding.Valid && surrounding.Failure==BuildPlacementFailure.External,"External object in solid side did not block wall");
            }finally{Object.DestroyImmediate(obstruction);Physics.SyncTransforms();}
            // A perpendicular partition through the opening is allowed, but adds no automatic post in it.
            var partition=w.Commit(At(c.presets[0],f,new Vector3(2,0,0),2));C(partition!=null && Covers(s)==0,"Junction post filled aperture");
            w.Remove(partition.Id);C(Covers(s)==0,"Removing aperture intersection left a cover");
            partition=w.Commit(At(c.presets[0],f,new Vector3(4,0,0),2));C(partition!=null && Covers(s)==1,"Normal end corner lost its shared cover");w.Remove(partition.Id);
            var upper=BuildPlacement.Solve(d,wall.Origin+q*new Vector3(2,2.9f,0),q*Vector3.forward,wall,s.Frame(wall.OwnFrameId),0,0,default);
            var second=w.Commit(upper);C(second!=null,"Opening wall stack rejected");Near(second.Origin,f.Origin+Vector3.up*3,"Opening wall wrong stack height");
            C(second.Resolved.boxes.Length==d.solidBoxes.Length && second.Resolved.mesh==d.mesh,"Stack closed aperture or replaced body");
            var floor=At(c.presets[1],f,new Vector3(0,3,0));C(w.Commit(floor)!=null,"Opening wall upper floor rejected");
            var roof=BuildPlacement.Solve(c.Find(ThatchRoofSetup.ContentId),second.Origin+q*new Vector3(2,3,0),Vector3.up,second,s.Frame(second.OwnFrameId),0,0,default,second.Origin+q*new Vector3(2,4,2));
            C(w.Commit(roof)!=null,"Roof on opening wall rejected");Near(roof.Origin,f.Origin+Vector3.up*6,"Roof datum changed on opening wall");
            var duplicate=starter;w.Validate(ref duplicate);C(!duplicate.Valid && duplicate.Failure==BuildPlacementFailure.Solid,"Duplicate opening wall accepted");
        }
        finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void FloorFitting(BuildCatalog c,int yaw)
    {
        foreach(var id in new[]{OpeningWallSetup.DoorId,OpeningWallSetup.WindowId})
        {
            using var s=new BuildSession();var f=s.CreateFrame(new Vector3(47000,47000,47000),yaw);
            s.Add(c.Find(id),f,default,0,true);var floor=s.Add(c.presets[1],f,new Vector3Int(0,7,-8),0,false);
            bool Contains(Vector3 p){foreach(var box in floor.Resolved.boxes)if(box.Contains(p))return true;return false;}
            C(Contains(new Vector3(2,-.125f,2)),"Floor unnecessarily cut through clear aperture");C(!Contains(new Vector3(.5f,-.125f,2)),"Floor ignored solid side");
        }
        using(var s=new BuildSession())
        {
            var f=s.CreateFrame(new Vector3(47000,47000,47000),yaw);s.Add(c.Find(OpeningWallSetup.GableId),f,default,0,true);
            var floor=s.Add(c.presets[1],f,new Vector3Int(0,6,-8),0,false);bool left=false,right=false;
            foreach(var box in floor.Resolved.boxes){left|=box.Contains(new Vector3(.5f,-.125f,2));right|=box.Contains(new Vector3(1.75f,-.125f,2));}
            C(left && !right,"Floor used rectangular gable envelope instead of occupied slope");
        }
    }
    private static void DoorWalk(BuildPieceRecord wall,Quaternion q,bool reverse)
    {
        var rig=new GameObject("W09 traversal");var cc=rig.AddComponent<CharacterController>();cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.skinWidth=.08f;cc.stepOffset=.3f;cc.minMoveDistance=0;
        rig.transform.SetPositionAndRotation(wall.Origin+q*new Vector3(2,.08f,reverse?1.5f:-1.5f),q);
        var motor=rig.AddComponent<CharacterMotor>();typeof(CharacterMotor).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(motor,null);Physics.SyncTransforms();
        try
        {
            for(int i=0;i<20;i++)motor.Simulate(default,1f/60);
            for(int i=0;i<52;i++)motor.Simulate(new CharacterMoveCommand(new Vector2(0,reverse?-1:1),false,false,false),1f/60);
            var local=Quaternion.Inverse(q)*(rig.transform.position-wall.Origin);
            C(reverse?local.z<-.8f:local.z>.8f,"Character got stuck in doorway: "+local);C(Mathf.Abs(local.y)<.15f,"Door threshold changed walking height");
        }finally{Object.DestroyImmediate(rig);Physics.SyncTransforms();}
    }
    private static void Gables(BuildCatalog c,int yaw)
    {
        var host=new GameObject("Half-gable fixture");host.transform.position=new Vector3(44000+yaw*30,44000,44000);var w=host.AddComponent<BuildWorld>();w.enabled=false;w.Focus=host.transform;
        var s=w.Session;var f=s.CreateFrame(host.transform.position,yaw);var q=BuildGeometry.Rotation(yaw);var rise=c.Find(OpeningWallSetup.GableId);var fall=rise.flipVariant;var roof=c.Find(ThatchRoofSetup.ContentId);
        try
        {
            s.Add(c.presets[2],f,default,0,true);var wall=s.Add(c.Find(OpeningWallSetup.DoorId),f,default,0,false);
            var left=BuildPlacement.Solve(rise,wall.Origin+q*new Vector3(1,2.9f,0),q*Vector3.forward,wall,s.Frame(wall.OwnFrameId),0,0,default);
            var a=w.Commit(left);C(a!=null,"Rising half on wall top rejected");Near(a.Origin,wall.Origin+Vector3.up*3,"Left half datum");
            var right=BuildPlacement.Solve(fall,wall.Origin+q*new Vector3(3,2.9f,0),q*Vector3.forward,wall,s.Frame(wall.OwnFrameId),0,0,default);
            var b=w.Commit(right);C(b!=null,"Falling half on wall top rejected");Near(b.Origin,wall.Origin+q*new Vector3(2,3,0),"Right half datum");C(Covers(s)==0,"Gable generated full-height post");
            Near(a.Origin+q*BuildWallShape.Peak(rise),b.Origin+q*BuildWallShape.Peak(fall),"Halves missed common ridge");
            Stream(w);
            foreach(var p in new[]{a,b})
            {
                C(w.TryGetCollisionProxy(p.Id,out var proxy),"Gable proxy missing");var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);C(colliders.Count==1 && colliders[0] is MeshCollider && proxy.Shape==null,"Gable uses bounding box physics");
                Vector3 hole=new(p==a?.5f:1.5f,1.5f,0),solid=new(p==a?1.5f:.5f,.5f,0);
                C(!Physics.Raycast(p.Origin+q*(hole+Vector3.back),q*Vector3.forward,2,1<<GameplayLayers.WorldSolid),"Empty half of gable blocks ray");
                C(Physics.Raycast(p.Origin+q*(solid+Vector3.back),q*Vector3.forward,2,1<<GameplayLayers.WorldSolid),"Triangle interior has no collision");
            }
            var roofA=BuildPlacement.Solve(roof,a.Origin+q*new Vector3(1,1,0),q*Vector3.forward,a,s.Frame(a.OwnFrameId),0,0,default,a.Origin+q*new Vector3(1,1,2));
            var r=w.Commit(roofA);C(r!=null,"Roof from gable rejected");Near(r.Origin,a.Origin+q*new Vector3(0,0,4),"Roof did not use gable eave");
            var roofB=BuildPlacement.Solve(roof,b.Origin+q*new Vector3(1,1,0),q*Vector3.forward,b,s.Frame(b.OwnFrameId),0,0,default,b.Origin+q*new Vector3(1,1,2));
            var rr=w.Commit(roofB);C(rr!=null,"Opposite roof from gable rejected");C(r.Supported && rr.Supported,"Roof lost support through gable");
            C(a.Resolved.attachments.Length==1 && b.Resolved.attachments.Length==1,"Aligned roofs did not seal their visual gable seams");
            C(a.Resolved.volumes.Length==1 && a.Resolved.boxes.Length==0,"Visual seam changed gable collision");
            var frame=s.Frame(r.OwnFrameId);var rq=BuildGeometry.Rotation(r.WorldYawStep);
            var end=BuildPlacement.Solve(rise,r.Origin+rq*new Vector3(0,1,1),rq*Vector3.left,r,frame,0,0,default);
            Near(end.Origin,r.Origin,"Roof-end gable translated by thickness");C(end.WorldYaw==BuildGeometry.Turn(r.WorldYawStep+6),"Roof-end gable heading");
            var endPiece=w.Commit(end);C(endPiece!=null,"Roof-first end gable rejected");
            var mate=BuildPlacement.Solve(fall,r.Origin+rq*new Vector3(0,1,1),rq*Vector3.left,r,frame,0,0,default);Near(mate.Origin,r.Origin+rq*new Vector3(0,0,2),"Opposite half ridge datum");C(w.Commit(mate)!=null,"Roof-first opposite half rejected");
            // A mirrored piece at the same box origin is a different solid, not a false duplicate.
            C(!BuildPlacementPolicy.IsDuplicate(a,new BuildPieceRecord{Definition=fall,Origin=a.Origin,WorldYawStep=a.WorldYawStep}),"Mirrored triangle falsely treated as duplicate");
            w.Remove(r.Id);C(a.Resolved.attachments.Length==0,"Roof removal left orphaned seam trim");
            s.Remove(wall.Id);C(!a.Supported && !rr.Supported,"Unsupported gable/roof retained false support");
        }
        finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
}
