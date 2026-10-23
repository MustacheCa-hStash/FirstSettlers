using UnityEngine;

/// <summary>Top-datum beams seat in a support's upper band and extend from its endpoints.</summary>
public static class BuildBeamPlacement
{
    public static bool Solve(BuildDefinition d,Vector3 hit,Vector3 normal,BuildPieceRecord target,int turn,
        Vector3Int nudge,out BuildPreview preview,int? lockedWorldYaw=null)
    {
        preview=default;
        if(d.kind!=BuildPartKind.Beam || target==null || target.Definition.kind==BuildPartKind.Roof)return false;
        var q=BuildGeometry.Rotation(target.WorldYawStep);
        var point=Quaternion.Inverse(q)*(hit-target.Origin);var n=Quaternion.Inverse(q)*normal;
        var b=target.Definition.LocalBounds;
        int yaw=lockedWorldYaw.HasValue?BuildGeometry.Turn(lockedWorldYaw.Value-target.WorldYawStep):BuildGeometry.Turn(turn);
        var footprint=BuildGeometry.WorldBounds(d.LocalBounds,Vector3.zero,yaw);
        Vector3 origin;string hint;
        if(target.Definition.kind==BuildPartKind.Beam)
        {
            if(Mathf.Abs(n.y)>.6f)
            {
                origin=new Vector3(b.min.x-footprint.min.x,n.y>0?b.max.y-footprint.min.y:b.min.y-footprint.max.y,b.center.z-footprint.center.z);
                hint=n.y>0?"Beam above beam":"Beam underneath beam";
            }
            else if(Mathf.Abs(n.x)>.6f)
            {
                origin=new Vector3(n.x>0?b.max.x-footprint.min.x:b.min.x-footprint.max.x,b.max.y-footprint.max.y,b.center.z-footprint.center.z);
                hint="Continue beam from endpoint";
            }
            else
            {
                origin=new Vector3(BuildGeometry.Tick(point.x)*BuildGeometry.Unit,b.max.y-footprint.max.y,n.z>0?b.max.z-footprint.min.z:b.min.z-footprint.max.z);
                hint="Beam beside beam";
            }
        }
        else if(target.Definition.kind is BuildPartKind.Wall or BuildPartKind.Corner)
        {
            float x=target.Definition.kind==BuildPartKind.Corner?b.center.x:Mathf.Clamp(BuildGeometry.Tick(point.x)*BuildGeometry.Unit,b.min.x,b.max.x);
            float top=n.y<-.6f?b.min.y:BuildPlacement.IsUpperHit(b,point,n)?b.min.y+target.Definition.StackRise:BuildGeometry.Tick(point.y)*BuildGeometry.Unit;
            origin=new Vector3(x,top-footprint.max.y,b.center.z);
            hint=n.y<-.6f?"Beam underneath support":"Beam at support datum";
        }
        else
        {
            origin=new Vector3(BuildGeometry.Tick(point.x-footprint.center.x)*BuildGeometry.Unit,
                n.y<-.6f?b.min.y-footprint.max.y:b.max.y-footprint.min.y,
                BuildGeometry.Tick(point.z-footprint.center.z)*BuildGeometry.Unit);
            hint=n.y<-.6f?"Beam underneath surface":"Beam on surface";
        }
        preview=new BuildPreview{Definition=d,Frame=new BuildGridFrame{Id=target.OwnFrameId,Origin=target.Origin,YawStep=target.WorldYawStep},
            Anchor=BuildGeometry.Ticks(origin)+nudge,YawStep=yaw,Hint=hint};
        preview.Origin=BuildGeometry.WorldPoint(preview.Frame,preview.Anchor);preview.WorldYaw=BuildGeometry.Turn(target.WorldYawStep+yaw);
        return true;
    }
}
