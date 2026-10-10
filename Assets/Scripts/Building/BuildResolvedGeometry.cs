using UnityEngine;

public static class BuildResolvedGeometry
{
    public static bool IsRoofStairPair(BuildPartKind a,BuildPartKind b)=>
        a==BuildPartKind.Roof && b==BuildPartKind.Stair || a==BuildPartKind.Stair && b==BuildPartKind.Roof;
    public static bool Overlaps(BuildPieceRecord a,BuildResolvedState sa,BuildPieceRecord b,BuildResolvedState sb)
    {
        foreach(var ab in sa.boxes)foreach(var bb in sb.boxes)if(BuildGeometry.Overlaps(ab,a.Origin,a.WorldYawStep,bb,b.Origin,b.WorldYawStep))return true;
        var aq=BuildGeometry.Rotation(a.WorldYawStep);var bq=BuildGeometry.Rotation(b.WorldYawStep);
        foreach(var av in sa.volumes)
        {
            foreach(var bb in sb.boxes)if(BuildOccupancy.SolidOverlap(av,default,a.Origin,aq,null,bb,b.Origin,bq,.005f))return true;
            foreach(var bv in sb.volumes)if(BuildOccupancy.SolidOverlap(av,default,a.Origin,aq,bv,default,b.Origin,bq,.005f))return true;
        }
        foreach(var bv in sb.volumes)foreach(var ab in sa.boxes)if(BuildOccupancy.SolidOverlap(null,ab,a.Origin,aq,bv,default,b.Origin,bq,.005f))return true;
        return false;
    }
    public static bool Connects(BuildPieceRecord a,BuildResolvedState sa,BuildPieceRecord b,BuildResolvedState sb)
    {
        // Accepted interpenetration is real structural contact. Clearance alone never grants support.
        if(Overlaps(a,sa,b,sb))return true;
        if(StackSocket(a,b) || StackSocket(b,a))return true;
        foreach(var joint in sa.joints)if(joint.OtherPieceId==b.Id)return true;
        if(a.Definition.kind==BuildPartKind.Roof && b.Definition.kind==BuildPartKind.Roof || a.Definition.kind==BuildPartKind.Stair && b.Definition.kind==BuildPartKind.Stair)
            if(BuildGeometry.Connects(a.Definition,a.Origin,a.WorldYawStep,b.Definition,b.Origin,b.WorldYawStep))return true;
        foreach(var ab in sa.boxes)foreach(var bb in sb.boxes)if(BuildGeometry.Connects(ab,a.Origin,a.WorldYawStep,bb,b.Origin,b.WorldYawStep))return true;
        foreach(var v in sa.volumes)foreach(var bb in sb.boxes)
            if(BuildGeometry.Connects(v.bounds,a.Origin,a.WorldYawStep,bb,b.Origin,b.WorldYawStep) &&
                BuildOccupancy.SolidOverlap(v,default,a.Origin,BuildGeometry.Rotation(a.WorldYawStep),null,bb,b.Origin,BuildGeometry.Rotation(b.WorldYawStep),-.008f))return true;
        foreach(var v in sb.volumes)foreach(var ab in sa.boxes)
            if(BuildGeometry.Connects(v.bounds,b.Origin,b.WorldYawStep,ab,a.Origin,a.WorldYawStep) &&
                BuildOccupancy.SolidOverlap(v,default,b.Origin,BuildGeometry.Rotation(b.WorldYawStep),null,ab,a.Origin,BuildGeometry.Rotation(a.WorldYawStep),-.008f))return true;
        foreach(var av in sa.volumes)foreach(var bv in sb.volumes)
            if(BuildGeometry.Connects(av.bounds,a.Origin,a.WorldYawStep,bv.bounds,b.Origin,b.WorldYawStep) &&
                BuildOccupancy.SolidOverlap(av,default,a.Origin,BuildGeometry.Rotation(a.WorldYawStep),bv,default,b.Origin,BuildGeometry.Rotation(b.WorldYawStep),-.008f))return true;
        if(a.Definition.kind==BuildPartKind.Roof && b.Definition.kind is BuildPartKind.Wall or BuildPartKind.Corner)
            return BuildRoof.Connects(a.Definition,a.Origin,a.WorldYawStep,b.Definition,b.Origin,b.WorldYawStep);
        if(b.Definition.kind==BuildPartKind.Roof && a.Definition.kind is BuildPartKind.Wall or BuildPartKind.Corner)
            return BuildRoof.Connects(b.Definition,b.Origin,b.WorldYawStep,a.Definition,a.Origin,a.WorldYawStep);
        return false;
    }
    public static bool BlocksStairPassage(BuildPieceRecord stair,BuildPieceRecord solid,BuildResolvedState state)
    {
        if(solid.Id==stair.Id)return false;
        var volume=StairPassage(stair.Definition);
        foreach(var box in state.boxes)if(BuildOccupancy.SolidOverlap(volume,default,stair.Origin,BuildGeometry.Rotation(stair.WorldYawStep),null,box,solid.Origin,BuildGeometry.Rotation(solid.WorldYawStep),.005f))return true;
        foreach(var s in state.volumes)if(BuildOccupancy.SolidOverlap(volume,default,stair.Origin,BuildGeometry.Rotation(stair.WorldYawStep),s,default,solid.Origin,BuildGeometry.Rotation(solid.WorldYawStep),.005f))return true;
        return false;
    }
    private static bool StackSocket(BuildPieceRecord lower,BuildPieceRecord upper)
    {
        if(lower.Definition.IsHalfGable)return false;
        if(lower.Definition.kind is not (BuildPartKind.Wall or BuildPartKind.Corner) || upper.Definition.kind is not (BuildPartKind.Wall or BuildPartKind.Corner))return false;
        Bounds a=lower.Definition.LocalBounds,b=upper.Definition.LocalBounds;float rise=lower.Definition.StackRise;
        if(rise<a.size.y-.012f || rise>a.size.y+BuildResolution.Band+.012f)return false;
        if(Mathf.Abs(lower.Origin.y+a.min.y+rise-upper.Origin.y-b.min.y)>.012f)return false;
        // Declared stacking remains a support connection when optional bearing output is suppressed.
        var envelope=new Bounds(new Vector3(a.center.x,a.min.y+rise*.5f,a.center.z),new Vector3(a.size.x,rise,a.size.z));
        return BuildGeometry.Connects(envelope,lower.Origin,lower.WorldYawStep,b,upper.Origin,upper.WorldYawStep);
    }
    public static BuildConvexVolume StairPassage(BuildDefinition definition)
    {
        Bounds b=definition.LocalBounds;float slope=b.size.y/b.size.z;
        float height=BuildResolution.PassageHeight;
        // Floor-opening margins belong to framing, not to required wall clearance.
        var v=new[]{new Vector3(b.min.x,b.min.y,b.min.z),new Vector3(b.min.x,b.max.y,b.max.z),new Vector3(b.min.x,b.max.y+height,b.max.z),new Vector3(b.min.x,b.min.y+height,b.min.z),
            new Vector3(b.max.x,b.min.y,b.min.z),new Vector3(b.max.x,b.max.y,b.max.z),new Vector3(b.max.x,b.max.y+height,b.max.z),new Vector3(b.max.x,b.min.y+height,b.min.z)};
        return new BuildConvexVolume{vertices=v,faceAxes=new[]{Vector3.right,Vector3.forward,new Vector3(0,1,-slope).normalized},
            edgeAxes=new[]{Vector3.right,Vector3.up,new Vector3(0,slope,1).normalized}};
    }
}
