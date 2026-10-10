using UnityEngine;

/// <summary>Structural sockets ignore thatch overhangs. All dimensions are in metres.</summary>
public static class BuildRoof
{
    public const float Width=4, Rise=2, Run=2;
    public static bool Connects(BuildDefinition roof,Vector3 origin,int yaw,BuildDefinition other,Vector3 oo,int oy)
    {
        Quaternion q=BuildGeometry.Rotation(yaw);
        Vector3 delta=Quaternion.Inverse(q)*(oo-origin);
        int relative=BuildGeometry.Turn(oy-yaw);
        if(other.kind==BuildPartKind.Roof)
        {
            bool repeat=relative==0 && Mathf.Abs(Mathf.Abs(delta.x)-Width)<.012f && Mathf.Abs(delta.y)<.012f && Mathf.Abs(delta.z)<.012f;
            bool ridge=relative==4 && Mathf.Abs(delta.x-Width)<.012f && Mathf.Abs(delta.y)<.012f && Mathf.Abs(delta.z-2*Run)<.012f;
            bool slope=relative==0 && Mathf.Abs(delta.x)<.012f &&
                ((Mathf.Abs(delta.y-Rise)<.012f && Mathf.Abs(delta.z-Run)<.012f) ||
                 (Mathf.Abs(delta.y+Rise)<.012f && Mathf.Abs(delta.z+Run)<.012f));
            return repeat || ridge || slope;
        }
        Bounds b=BuildGeometry.WorldBounds(other.LocalBounds,delta,relative);
        // The eave bearing seats on either a wall or a post; no face of the
        // empty nominal attic box can transmit support.
        if(other.kind is BuildPartKind.Wall or BuildPartKind.Corner)
        {
            if(Mathf.Abs(b.max.y)<.012f && b.max.x>.015f && b.min.x<Width-.015f && b.max.z>.015f && b.min.z<.25f-.015f) return true;
            // Gable infill and interior beam attachment require real contact
            // with an occupied slab, with zero positive penetration allowed.
            if(!BuildOccupancy.Overlaps(roof,origin,yaw,other,oo,oy))
                foreach(var volume in roof.occupiedVolumes)
                {
                    if(BuildOccupancy.Custom(other))
                    {
                        foreach(var solid in other.occupiedVolumes)
                            if(BuildOccupancy.SolidOverlap(volume,default,origin,q,solid,default,oo,BuildGeometry.Rotation(oy),-.008f)) return true;
                    }
                    else if(BuildOccupancy.SolidOverlap(volume,default,origin,q,null,other.LocalBounds,oo,BuildGeometry.Rotation(oy),-.008f)) return true;
                }
        }
        return false;
    }
    public static Vector3 Snap(BuildDefinition definition,BuildPieceRecord target,Vector3 point,Vector3 normal,
        int turn,Vector3? viewer,out int yaw,out string hint)
    {
        Bounds b=target.Definition.LocalBounds;
        yaw=BuildGeometry.Turn(turn);
        if(definition.kind==BuildPartKind.Roof)
        {
            if(target.Definition.kind==BuildPartKind.Roof)
            {
                // Inherit the aimed roof's heading. Only an actual side/end
                // selects ridge-width repetition; the broad face continues uphill.
                if(Mathf.Abs(normal.x)>.65f || point.x<=.25f || point.x>=Width-.25f)
                {
                    hint="Continue roof sideways";
                    return new Vector3(point.x<Width*.5f?-Width:Width,0,0);
                }
                bool lower=point.z<.35f;
                hint=lower?"Continue roof downhill":"Continue roof uphill";
                return new Vector3(0,lower?-Rise:Rise,lower?-Run:Run);
            }
            bool reverse=false;
            if(viewer.HasValue)
                reverse=BuildGeometry.LocalPoint(new BuildGridFrame{Origin=target.Origin,YawStep=target.WorldYawStep},viewer.Value).z<b.center.z;
            yaw=BuildGeometry.Turn((reverse?4:0)+turn);
            float viewerX=viewer.HasValue?BuildGeometry.LocalPoint(new BuildGridFrame{Origin=target.Origin,YawStep=target.WorldYawStep},viewer.Value).x:b.max.x;
            bool centred=target.Definition.jointSockets?.Length>0 || target.Definition.kind==BuildPartKind.Corner && b.min.x<0 && b.min.z<0;
            float start=target.Definition.kind==BuildPartKind.Wall? b.min.x-target.Definition.WallEndInset:
                centred?(viewerX>=b.center.x?0:-Width):
                (viewerX>=b.center.x ? b.min.x : b.max.x-Width);
            hint="Roof eave on wall/post top";
            float seat=target.Definition.kind is BuildPartKind.Wall or BuildPartKind.Corner ? b.min.y+target.Definition.StackRise : b.max.y;
            return reverse?new Vector3(start+Width,seat,centred?0:b.max.z):new Vector3(start,seat,centred?0:b.min.z);
        }
        yaw=BuildGeometry.Turn(turn);
        if(definition.kind==BuildPartKind.Floor)
        { hint="Floor at roof bearing level";return new Vector3(0,-definition.LocalBounds.max.y,0); }
        if(definition.roofAttachment==RoofAttachmentMode.Gable)
        {
            // A four-metre triangular infill spans both roof halves. Its base
            // belongs on the eave reference, not below a nominal attic box.
            bool left=point.x<Width*.5f;
            yaw=BuildGeometry.Turn(6+turn);
            hint="Gable infill at roof end";
            return new Vector3(left?definition.LocalBounds.size.z:Width,0,definition.WallEndInset);
        }
        var footprint=BuildGeometry.WorldBounds(definition.LocalBounds,Vector3.zero,yaw);
        // Attach existing infill/posts or beam-like parts under the real slope,
        // preserving quarter-metre ticks; tall parts are rejected if they cross it.
        float z=Mathf.Clamp(BuildGeometry.Tick(point.z)*BuildGeometry.Unit,0,Run);
        float x=BuildGeometry.Tick(point.x)*BuildGeometry.Unit;
        hint="Under roof slope · adjust height/offset for clearance";
        float lowerZ=z-footprint.center.z+footprint.min.z;
        return new Vector3(x-footprint.center.x,lowerZ-footprint.max.y,z-footprint.center.z);
    }
    public static bool IsBelow(BuildDefinition a,Vector3 ao,int ay,BuildDefinition b,Vector3 bo,int by)
    {
        if(a.kind!=BuildPartKind.Roof || b.kind!=BuildPartKind.Roof || BuildGeometry.Turn(ay)!=BuildGeometry.Turn(by))return false;
        Vector3 d=Quaternion.Inverse(BuildGeometry.Rotation(ay))*(bo-ao);
        return Mathf.Abs(d.x)<.012f && Mathf.Abs(d.y+Rise)<.012f && Mathf.Abs(d.z+Run)<.012f;
    }
    public static Mesh VisualMesh(BuildPieceRecord piece,BuildSession session)
    {
        if(piece.Resolved!=null)return piece.Resolved.mesh;
        if(piece.Definition.roofContinuationMesh!=null)
            foreach(ulong id in piece.Connections)
                if(session.TryGet(id,out var other) && IsBelow(piece.Definition,piece.Origin,piece.WorldYawStep,other.Definition,other.Origin,other.WorldYawStep))
                    return piece.Definition.roofContinuationMesh;
        return piece.Definition.mesh;
    }
}
