using UnityEngine;

/// <summary>Stairs connect at toe/high-end sockets even inside terrain; underside aim descends from a real attachment.</summary>
public static class BuildStairPlacement
{
    public static bool Solve(BuildDefinition d,Vector3 hit,Vector3 normal,BuildPieceRecord target,int turn,Vector3Int nudge,Vector3? viewer,out BuildPreview preview,int? lockedWorldYaw=null)
    {
        preview=default;if(target==null || d.kind is not (BuildPartKind.Stair or BuildPartKind.Handrail))return false;
        var q=BuildGeometry.Rotation(target.WorldYawStep);var point=Quaternion.Inverse(q)*(hit-target.Origin);var n=Quaternion.Inverse(q)*normal;
        var b=target.Definition.LocalBounds;Vector3 origin;int yaw=BuildGeometry.Turn(turn);string hint;
        if(d.kind==BuildPartKind.Handrail)
        {
            if(target.Definition.kind!=BuildPartKind.Stair)return false;
            var view=viewer.HasValue?Quaternion.Inverse(q)*(viewer.Value-target.Origin):point;
            bool left=Mathf.Abs(n.x)>.5f?n.x<0:view.x<b.center.x;
            float segment=b.size.z>2.01f && point.z>=2?2:0;
            origin=new Vector3(left?b.min.x-d.mesh.bounds.max.x:b.max.x-d.mesh.bounds.min.x,b.min.y+segment*b.size.y/b.size.z,b.min.z+segment);
            hint=left?"Handrail on left stair edge":"Handrail on right stair edge";
        }
        else if(target.Definition.kind==BuildPartKind.Stair)
        {
            if(Mathf.Abs(n.x)>.5f)
            {
                var shape=BuildGeometry.WorldBounds(d.LocalBounds,Vector3.zero,yaw);
                origin=new Vector3(n.x<0?b.min.x-shape.max.x:b.max.x-shape.min.x,b.min.y-shape.min.y,b.min.z-shape.min.z);
                hint=n.x<0?"Stair alongside left edge":"Stair alongside right edge";
            }
            else
            {
                bool down=n.y<-.5f || n.y<.5f && viewer.HasValue && BelowSlope(target,viewer.Value);
                Vector3 socket=down?BuildStairAttachment.LowLocal(target.Definition):BuildStairAttachment.HighLocal(target.Definition);
                origin=socket-BuildGeometry.Rotation(yaw)*(down?BuildStairAttachment.HighLocal(d):BuildStairAttachment.LowLocal(d));
                hint=down?"Continue stair downward from toe":"Continue stair upward from high end";
            }
        }
        else if(target.Definition.kind is BuildPartKind.Floor or BuildPartKind.Foundation)
        {
            if(n.y<-.5f || Mathf.Abs(n.y)<.5f)
            {
                // Use the nearest perimeter and walking level, so descending exterior stairs meet the floor without a quarter-metre lip.
                int edge=Edge(b,point,n);yaw=lockedWorldYaw.HasValue?BuildGeometry.Turn(lockedWorldYaw.Value-target.WorldYawStep):BuildGeometry.Turn(edge+turn);
                Vector3 contact=edge switch{0=>new Vector3(point.x,b.max.y,b.min.z),2=>new Vector3(b.min.x,b.max.y,point.z),4=>new Vector3(point.x,b.max.y,b.max.z),_=>new Vector3(b.max.x,b.max.y,point.z)};
                contact.x=BuildGeometry.Tick(contact.x)*BuildGeometry.Unit;contact.z=BuildGeometry.Tick(contact.z)*BuildGeometry.Unit;
                var shape=BuildGeometry.WorldBounds(d.LocalBounds,Vector3.zero,yaw);
                if(edge==0 || edge==4)contact.x=Along(point.x,b.min.x,b.max.x,shape.size.x);
                else contact.z=Along(point.z,b.min.z,b.max.z,shape.size.z);
                var top=BuildGeometry.Rotation(yaw)*BuildStairAttachment.HighLocal(d);
                origin=contact-top; // Keep the exact socket; 1.25m width places its centre between origin ticks.
                hint=n.y<-.5f?"Descend from underside edge at floor level":"Stair high end at floor edge";
            }
            else return false; // Existing top-surface placement keeps its footprint fitting.
        }
        else if(target.Definition.kind is BuildPartKind.Wall or BuildPartKind.Corner && Mathf.Abs(n.y)<.5f)
        {
            yaw=lockedWorldYaw.HasValue?BuildGeometry.Turn(lockedWorldYaw.Value-target.WorldYawStep):BuildGeometry.Turn(BuildPlacement.FacingYaw(-n,2)+turn);
            var high=BuildGeometry.Rotation(yaw)*BuildStairAttachment.HighLocal(d);
            origin=new Vector3(BuildGeometry.Tick(point.x)*BuildGeometry.Unit,BuildGeometry.Tick(point.y)*BuildGeometry.Unit,point.z)-high;
            hint="Stair high end at aimed face";
        }
        else if(target.Definition.kind==BuildPartKind.Roof && n.y<-.5f)
        {
            float x=Mathf.Clamp(BuildGeometry.Tick(point.x)*BuildGeometry.Unit,d.LocalBounds.size.x*.5f,BuildRoof.Width-d.LocalBounds.size.x*.5f);
            float z=Mathf.Clamp(BuildGeometry.Tick(point.z)*BuildGeometry.Unit,0,BuildRoof.Run);
            float y=point.y-(n.x*(x-point.x)+n.z*(z-point.z))/n.y;
            origin=new Vector3(x,y,z)-BuildGeometry.Rotation(yaw)*BuildStairAttachment.HighLocal(d);
            hint="Stair high end under roof slope";
        }
        else return false;
        int heading=BuildGeometry.Turn(target.WorldYawStep+yaw);Vector3 world=target.Origin+q*(origin+(Vector3)nudge*BuildGeometry.Unit);
        preview=new BuildPreview{Definition=d,Origin=world,WorldYaw=heading,Frame=new BuildGridFrame{Origin=world,YawStep=(byte)heading},Hint=hint,StairAimTarget=target.Id};return true;
    }
    private static float Along(float point,float min,float max,float width)
    {
        float half=width*.5f;
        if(point-min<.25f)return min+half;
        if(max-point<.25f)return max-half;
        return Mathf.Clamp(BuildGeometry.Tick(point)*BuildGeometry.Unit,min+half,Mathf.Max(min+half,max-half));
    }
    public static bool BelowSlope(BuildPieceRecord stair,Vector3 viewer)
    {
        var local=Quaternion.Inverse(BuildGeometry.Rotation(stair.WorldYawStep))*(viewer-stair.Origin);var b=stair.Definition.LocalBounds;
        float height=b.min.y+Mathf.Clamp(local.z-b.min.z,0,b.size.z)*b.size.y/b.size.z;
        return local.y<height-.03f;
    }
    private static int Edge(Bounds b,Vector3 p,Vector3 n)
    {
        if(Mathf.Abs(n.x)>.5f)return n.x<0?2:6;if(Mathf.Abs(n.z)>.5f)return n.z<0?0:4;
        float best=p.z-b.min.z;int side=0;
        if(p.x-b.min.x<best){best=p.x-b.min.x;side=2;}if(b.max.z-p.z<best){best=b.max.z-p.z;side=4;}if(b.max.x-p.x<best)side=6;return side;
    }
}
