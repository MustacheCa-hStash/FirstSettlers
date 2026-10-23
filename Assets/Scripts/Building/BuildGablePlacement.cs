using System.Collections.Generic;
using UnityEngine;

/// <summary>Aim chooses the half bay and its physical slope; explicit flips retain that bay.</summary>
public static class BuildGablePlacement
{
    /// <summary>Acquire an empty triangular bay without needing a previous collider hit to seed a sky guide.</summary>
    public static bool TryAim(BuildSession session,BuildDefinition d,Ray ray,float maximumDistance,List<BuildPieceRecord> candidates,
        out BuildPieceRecord target,out Vector3 hit,out Vector3 normal)
    {
        target=null;hit=normal=default;if(!d.IsHalfGable)return false;
        var end=ray.GetPoint(maximumDistance);var search=new Bounds(ray.origin,Vector3.zero);search.Encapsulate(end);
        search.Expand(Mathf.Max(d.LocalBounds.size.x,d.LocalBounds.size.y)*2+.1f);session.Query(search,candidates);
        float best=maximumDistance-.01f;
        foreach(var piece in candidates)
        {
            if(piece.Definition.kind!=BuildPartKind.Wall)continue;
            var q=BuildGeometry.Rotation(piece.WorldYawStep);var inverse=Quaternion.Inverse(q);
            var from=inverse*(ray.origin-piece.Origin);var direction=inverse*ray.direction;var b=piece.Definition.LocalBounds;
            if(Mathf.Abs(direction.z)<.01f)continue;
            bool front=from.z>=b.center.z;float distance=((front?b.max.z:b.min.z)-from.z)/direction.z;
            if(distance<=0||distance>=best)continue;var point=from+direction*distance;
            if(piece.Definition.IsHalfGable)
            {
                // Only its immediate left/right bay is virtual; the existing triangle keeps collider priority.
                float width=d.LocalBounds.size.x;
                if((point.x>=b.min.x-.01f&&point.x<=b.max.x+.01f)||point.x<b.min.x-width||point.x>b.max.x+width)continue;
            }
            else if(point.x<b.min.x||point.x>b.max.x||point.y<=b.min.y+piece.Definition.StackRise+.01f)continue;
            var worldHit=ray.GetPoint(distance);var face=q*(front?Vector3.forward:Vector3.back);
            if(!Solve(d,worldHit,face,piece,0,default,ray.origin,out var preview))continue;
            var local=Quaternion.Inverse(BuildGeometry.Rotation(preview.WorldYaw))*(worldHit-preview.Origin);var shape=preview.Definition.LocalBounds;
            float t=(local.x-shape.min.x)/shape.size.x;
            float ceiling=shape.min.y+shape.size.y*(preview.Definition.gableSlope==GableSlope.Rising?t:1-t);
            if(t<0||t>1||local.y<shape.min.y||local.y>ceiling+.025f)continue;
            best=distance;target=piece;hit=worldHit;normal=face;
        }
        return target!=null;
    }
    public static bool Solve(BuildDefinition d,Vector3 hit,Vector3 normal,BuildPieceRecord target,int turn,Vector3Int nudge,Vector3? viewer,out BuildPreview preview,int? lockedWorldYaw=null,bool automaticSlope=true)
    {
        preview=default;if(target==null)return false;
        var q=BuildGeometry.Rotation(target.WorldYawStep);var point=Quaternion.Inverse(q)*(hit-target.Origin);
        var n=Quaternion.Inverse(q)*normal;var b=target.Definition.LocalBounds;
        var view=viewer.HasValue?Quaternion.Inverse(q)*(viewer.Value-target.Origin):point+n;
        Vector3 origin;int yaw=BuildGeometry.Turn(turn);string hint;
        if(d.IsHalfGable && target.Definition.kind==BuildPartKind.Roof)
        {
            float end=point.x<BuildRoof.Width*.5f?0:BuildRoof.Width;
            yaw=lockedWorldYaw.HasValue?BuildGeometry.Turn(lockedWorldYaw.Value-target.WorldYawStep):BuildGeometry.Turn(6+turn);
            var along=BuildGeometry.Rotation(yaw)*Vector3.right;
            // Reversing the mesh heading also reverses its local rising/falling label.
            d=Slope(d,along.z>=0?GableSlope.Rising:GableSlope.Falling,automaticSlope);
            origin=Centre(d,new Vector3(end,0,BuildRoof.Run*.5f),yaw);
            hint=automaticSlope?"Gable fits aimed roof half · R overrides slope":"Manual gable slope at roof end";
        }
        else if(d.kind==BuildPartKind.Roof && target.Definition.IsHalfGable)
        {
            bool rising=target.Definition.gableSlope==GableSlope.Rising;
            yaw=lockedWorldYaw.HasValue?BuildGeometry.Turn(lockedWorldYaw.Value-target.WorldYawStep):BuildGeometry.Turn((rising?2:6)+turn);
            origin=new Vector3(rising?0:b.max.x,0,rising?(view.z>=0?BuildRoof.Width:0):(view.z>=0?0:-BuildRoof.Width));
            hint="Roof slope on gable eave datum";
        }
        else if(d.IsHalfGable && target.Definition.kind==BuildPartKind.Wall && !target.Definition.IsHalfGable && BuildPlacement.IsUpperHit(b,point,n))
        {
            float width=d.LocalBounds.size.x;
            float start=Mathf.Clamp(b.min.x+Mathf.Floor((point.x-b.min.x)/width)*width,b.min.x,Mathf.Max(b.min.x,b.max.x-width));
            float centre=start+width*.5f;bool left=Mathf.Abs(centre-b.center.x)>.001f?centre<b.center.x:point.x<b.center.x;
            yaw=lockedWorldYaw.HasValue?BuildGeometry.Turn(lockedWorldYaw.Value-target.WorldYawStep):yaw;
            var along=BuildGeometry.Rotation(yaw)*Vector3.right;
            d=Slope(d,(left?1:-1)*along.x>=0?GableSlope.Rising:GableSlope.Falling,automaticSlope);
            origin=Centre(d,new Vector3(centre,b.min.y+target.Definition.StackRise,b.center.z),yaw);
            hint=automaticSlope?(left?"Left gable half · R overrides slope":"Right gable half · R overrides slope"):"Manual gable slope on wall top";
        }
        else if(d.IsHalfGable && target.Definition.IsHalfGable)
        {
            bool endFace=Mathf.Abs(n.y)<.3f&&Mathf.Abs(n.x)>.6f;
            bool left=endFace?n.x<0:point.x<b.center.x;
            yaw=lockedWorldYaw.HasValue?BuildGeometry.Turn(lockedWorldYaw.Value-target.WorldYawStep):yaw;
            var along=BuildGeometry.Rotation(yaw)*Vector3.right;
            // Opposite slopes share equal-height endpoints, at either a peak or a valley.
            float desired=target.Definition.gableSlope==GableSlope.Rising?-1:1;
            d=Slope(d,desired*along.x>=0?GableSlope.Rising:GableSlope.Falling,automaticSlope);
            origin=Centre(d,new Vector3(left?b.min.x-d.LocalBounds.size.x*.5f:b.max.x+d.LocalBounds.size.x*.5f,b.min.y,b.center.z),yaw);
            hint=automaticSlope?"Match neighbouring gable edge · R overrides slope":"Manual gable slope beside gable";
        }
        else return false;
        int heading=BuildGeometry.Turn(target.WorldYawStep+yaw);Vector3 world=target.Origin+q*(origin+(Vector3)nudge*BuildGeometry.Unit);
        preview=new BuildPreview{Definition=d,Origin=world,WorldYaw=heading,Frame=new BuildGridFrame{Origin=world,YawStep=(byte)heading},Hint=hint};return true;
    }
    private static BuildDefinition Slope(BuildDefinition d,GableSlope slope,bool automatic)
        =>automatic&&d.gableSlope!=slope&&d.flipVariant!=null&&d.flipVariant.gableSlope==slope?d.flipVariant:d;
    private static Vector3 Centre(BuildDefinition d,Vector3 centre,int yaw)
    {
        var b=d.LocalBounds;return centre-BuildGeometry.Rotation(yaw)*new Vector3(b.center.x,b.min.y,b.center.z);
    }
}
