using UnityEngine;

/// <summary>Structural stair contacts are independent of terrain overlap and visibility.
/// Terrain never grants a stair its own grounding anchor.</summary>
public static class BuildStairAttachment
{
    public static Vector3 LowLocal(BuildDefinition d)=>new(d.LocalBounds.center.x,d.LocalBounds.min.y,d.LocalBounds.min.z);
    public static Vector3 HighLocal(BuildDefinition d)=>new(d.LocalBounds.center.x,d.LocalBounds.max.y,d.LocalBounds.max.z);
    public static Vector3 Low(BuildPieceRecord p)=>p.Origin+BuildGeometry.Rotation(p.WorldYawStep)*LowLocal(p.Definition);
    public static Vector3 High(BuildPieceRecord p)=>p.Origin+BuildGeometry.Rotation(p.WorldYawStep)*HighLocal(p.Definition);
    private static bool Near(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<.000225f;
    public static bool Connects(BuildPieceRecord stair,BuildPieceRecord other,BuildResolvedState state)
    {
        if(other.Definition.kind==BuildPartKind.Handrail)return RailConnects(other,stair);
        var low=Low(stair);var high=High(stair);
        if(other.Definition.kind==BuildPartKind.Stair)
            return BuildGeometry.Turn(stair.WorldYawStep-other.WorldYawStep)==0 &&
                (Near(low,High(other)) || Near(high,Low(other)));
        return EndpointContact(low,other,state) || EndpointContact(high,other,state);
    }
    public static bool EndpointContact(Vector3 endpoint,BuildPieceRecord other,BuildResolvedState state)
    {
        Vector3 local=Quaternion.Inverse(BuildGeometry.Rotation(other.WorldYawStep))*(endpoint-other.Origin);
        if(other.Definition.kind is BuildPartKind.Floor or BuildPartKind.Foundation)
        {
            var b=other.Definition.LocalBounds;bool inside=local.x>=b.min.x-.015f && local.x<=b.max.x+.015f && local.z>=b.min.z-.015f && local.z<=b.max.z+.015f;
            bool edge=Mathf.Abs(local.x-b.min.x)<.015f || Mathf.Abs(local.x-b.max.x)<.015f || Mathf.Abs(local.z-b.min.z)<.015f || Mathf.Abs(local.z-b.max.z)<.015f;
            // The declared landing socket survives optional fitting/headroom margins at a tile edge.
            if(inside && edge && Mathf.Abs(local.y-b.max.y)<.015f)return true;
        }
        foreach(var box in state.boxes)
        {
            var expanded=box;expanded.Expand(.03f);if(!expanded.Contains(local))continue;
            if(Mathf.Abs(local.y-box.max.y)<.015f || Mathf.Abs(local.y-box.min.y)<.015f || Mathf.Abs(local.x-box.min.x)<.015f || Mathf.Abs(local.x-box.max.x)<.015f || Mathf.Abs(local.z-box.min.z)<.015f || Mathf.Abs(local.z-box.max.z)<.015f)return true;
        }
        var probe=new Bounds(endpoint,Vector3.one*.002f);
        foreach(var volume in state.volumes)
            if(BuildOccupancy.SolidOverlap(null,probe,Vector3.zero,Quaternion.identity,volume,default,other.Origin,BuildGeometry.Rotation(other.WorldYawStep),-.012f))return true;
        return false;
    }
    public static bool RailConnects(BuildPieceRecord rail,BuildPieceRecord stair)
    {
        if(stair.Definition.kind!=BuildPartKind.Stair || BuildGeometry.Turn(rail.WorldYawStep-stair.WorldYawStep)!=0)return false;
        var delta=Quaternion.Inverse(BuildGeometry.Rotation(stair.WorldYawStep))*(rail.Origin-stair.Origin);var b=stair.Definition.LocalBounds;
        bool side=Mathf.Abs(delta.x-(b.min.x-rail.Definition.mesh.bounds.max.x))<.015f || Mathf.Abs(delta.x-(b.max.x-rail.Definition.mesh.bounds.min.x))<.015f;
        return side && delta.z>=b.min.z-.015f && delta.z+rail.Definition.LocalBounds.size.z<=b.max.z+.015f && Mathf.Abs(delta.y-(b.min.y+(delta.z-b.min.z)*b.size.y/b.size.z))<.015f;
    }
}
