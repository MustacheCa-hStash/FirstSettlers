using UnityEngine;

/// <summary>Half-gables follow eave/ridge datums and two-metre half bays, not rectangular wall tops.</summary>
public static class BuildGablePlacement
{
    public static bool Solve(BuildDefinition d,Vector3 hit,Vector3 normal,BuildPieceRecord target,int turn,Vector3Int nudge,Vector3? viewer,out BuildPreview preview)
    {
        preview=default;if(target==null)return false;
        var q=BuildGeometry.Rotation(target.WorldYawStep);var point=Quaternion.Inverse(q)*(hit-target.Origin);
        var n=Quaternion.Inverse(q)*normal;var b=target.Definition.LocalBounds;
        var view=viewer.HasValue?Quaternion.Inverse(q)*(viewer.Value-target.Origin):point+n;
        Vector3 origin;int yaw=BuildGeometry.Turn(turn);string hint;
        if(d.IsHalfGable && target.Definition.kind==BuildPartKind.Roof)
        {
            float end=point.x<BuildRoof.Width*.5f?0:BuildRoof.Width;
            yaw=BuildGeometry.Turn(6+turn);
            origin=new Vector3(end,0,d.gableSlope==GableSlope.Rising?0:BuildRoof.Run);
            hint=d.gableSlope==GableSlope.Rising?"Gable half from eave to ridge":"Gable half from ridge to opposite eave";
        }
        else if(d.kind==BuildPartKind.Roof && target.Definition.IsHalfGable)
        {
            bool rising=target.Definition.gableSlope==GableSlope.Rising;
            yaw=BuildGeometry.Turn((rising?2:6)+turn);
            origin=new Vector3(rising?0:b.max.x,0,rising?(view.z>=0?BuildRoof.Width:0):(view.z>=0?0:-BuildRoof.Width));
            hint="Roof slope on gable eave datum";
        }
        else if(d.IsHalfGable && target.Definition.kind==BuildPartKind.Wall && !target.Definition.IsHalfGable && (n.y>.6f || point.y>=b.center.y))
        {
            float width=d.LocalBounds.size.x;
            origin=new Vector3(Mathf.Clamp(Mathf.Floor(point.x/width)*width,b.min.x,Mathf.Max(b.min.x,b.max.x-width)),b.min.y+target.Definition.StackRise,0);
            hint="Gable half on wall top · R flips slope";
        }
        else if(d.IsHalfGable && target.Definition.IsHalfGable)
        {
            bool left=point.x<b.center.x;origin=new Vector3(left?-d.LocalBounds.size.x:b.max.x,b.min.y,0);
            hint="Continue gable at base level · R flips slope";
        }
        else return false;
        int heading=BuildGeometry.Turn(target.WorldYawStep+yaw);Vector3 world=target.Origin+q*(origin+(Vector3)nudge*BuildGeometry.Unit);
        preview=new BuildPreview{Definition=d,Origin=world,WorldYaw=heading,Frame=new BuildGridFrame{Origin=world,YawStep=(byte)heading},Hint=hint};return true;
    }
}
