using UnityEngine;

/// <summary>Fits a fixed decorative leaf inside an authored aperture; adds no door interaction.</summary>
public static class BuildStaticLeafPlacement
{
    public static bool Solve(BuildDefinition d,BuildPieceRecord target,Vector3Int nudge,int turn,out BuildPreview preview,int? lockedWorldYaw=null)
    {
        preview=default;
        if(d.contentId is not ("build.wood.s10-static-plank-door" or "build.wood.s12-static-yard-gate") || target?.Definition.wallOpenings?.Length!=1)return false;
        var aperture=target.Definition.wallOpenings[0];var leaf=d.mesh.bounds;
        if(Mathf.Abs(aperture.size.x-leaf.size.x-.05f)>.002f || aperture.size.y-leaf.size.y<.04f || aperture.size.y-leaf.size.y>.11f)return false;
        int yaw=lockedWorldYaw??BuildGeometry.Turn(target.WorldYawStep+turn);
        var origin=target.Origin+BuildGeometry.Rotation(target.WorldYawStep)*(aperture.center+(Vector3)nudge*BuildGeometry.Unit)-BuildGeometry.Rotation(yaw)*leaf.center;
        preview=new BuildPreview{Definition=d,Origin=origin,WorldYaw=yaw,Frame=new BuildGridFrame{Origin=origin,YawStep=(byte)yaw},Hint="Static leaf in opening"};
        return true;
    }
}
