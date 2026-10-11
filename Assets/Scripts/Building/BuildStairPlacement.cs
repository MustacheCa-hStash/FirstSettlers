using UnityEngine;

/// <summary>Stairs connect at exposed toe/high-end sockets; underside aim descends from a real attachment.</summary>
public static class BuildStairPlacement
{
    public static bool Solve(BuildDefinition d,Vector3 hit,Vector3 normal,BuildPieceRecord target,int turn,Vector3Int nudge,Vector3? viewer,out BuildPreview preview)
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
            bool down=n.y<-.5f;Vector3 socket=down?BuildStairAttachment.LowLocal(target.Definition):BuildStairAttachment.HighLocal(target.Definition);
            origin=socket-BuildGeometry.Rotation(yaw)*(down?BuildStairAttachment.HighLocal(d):BuildStairAttachment.LowLocal(d));
            hint=down?"Continue stair downward from toe":"Continue stair upward from high end";
        }
        else if(target.Definition.kind is BuildPartKind.Floor or BuildPartKind.Foundation)
        {
            if(n.y<-.5f || Mathf.Abs(n.y)<.5f)
            {
                // Use the nearest perimeter and walking level, so descending exterior stairs meet the floor without a quarter-metre lip.
                int edge=Edge(b,point,n);yaw=BuildGeometry.Turn(edge+turn);
                Vector3 contact=edge switch{0=>new Vector3(point.x,b.max.y,b.min.z),2=>new Vector3(b.min.x,b.max.y,point.z),4=>new Vector3(point.x,b.max.y,b.max.z),_=>new Vector3(b.max.x,b.max.y,point.z)};
                contact.x=BuildGeometry.Tick(contact.x)*BuildGeometry.Unit;contact.z=BuildGeometry.Tick(contact.z)*BuildGeometry.Unit;
                var top=BuildGeometry.Rotation(yaw)*BuildStairAttachment.HighLocal(d);
                origin=contact-top; // Keep the exact socket; 1.25m width places its centre between origin ticks.
                hint=n.y<-.5f?"Descend from underside edge at floor level":"Stair high end at floor edge";
            }
            else return false; // Existing top-surface placement keeps its footprint fitting.
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
    private static int Edge(Bounds b,Vector3 p,Vector3 n)
    {
        if(Mathf.Abs(n.x)>.5f)return n.x<0?2:6;if(Mathf.Abs(n.z)>.5f)return n.z<0?0:4;
        float best=p.z-b.min.z;int side=0;
        if(p.x-b.min.x<best){best=p.x-b.min.x;side=2;}if(b.max.z-p.z<best){best=b.max.z-p.z;side=4;}if(b.max.x-p.x<best)side=6;return side;
    }
}
