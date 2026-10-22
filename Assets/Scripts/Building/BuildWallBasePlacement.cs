using UnityEngine;

public static class BuildWallBasePlacement
{
    public static bool Solve(BuildDefinition d,Vector3 hit,Vector3 normal,BuildPieceRecord wall,int turn,Vector3Int nudge,Vector3? viewer,out BuildPreview preview,int? lockedWorldYaw=null)
    {
        preview=default;if(!d.wallBaseAttachment || wall==null || wall.Definition.kind!=BuildPartKind.Wall)return false;
        var q=BuildGeometry.Rotation(wall.WorldYawStep);var inverse=Quaternion.Inverse(q);var point=inverse*(hit-wall.Origin);var n=inverse*normal;var b=wall.Definition.LocalBounds;
        bool positive=Mathf.Abs(n.z)>.5f?n.z>0:!viewer.HasValue||(inverse*(viewer.Value-wall.Origin)).z>=b.center.z;
        int localYaw=lockedWorldYaw.HasValue?BuildGeometry.Turn(lockedWorldYaw.Value-wall.WorldYawStep):BuildGeometry.Turn((positive?0:4)+turn);var bounds=BuildGeometry.WorldBounds(d.LocalBounds,Vector3.zero,localYaw);
        float centre=Mathf.Clamp(BuildGeometry.Tick(point.x)*BuildGeometry.Unit,b.min.x+bounds.extents.x,b.max.x-bounds.extents.x);
        var origin=new Vector3(centre-bounds.center.x,b.min.y-bounds.min.y,positive?b.max.z-bounds.min.z:b.min.z-bounds.max.z)+(Vector3)nudge*BuildGeometry.Unit;
        int yaw=BuildGeometry.Turn(wall.WorldYawStep+localYaw);var world=wall.Origin+q*origin;
        preview=new BuildPreview{Definition=d,Origin=world,WorldYaw=yaw,Frame=new BuildGridFrame{Origin=world,YawStep=(byte)yaw},Hint="Buttress at wall base"};return true;
    }
}
