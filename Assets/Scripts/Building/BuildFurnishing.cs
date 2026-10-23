using System.Collections.Generic;
using UnityEngine;

/// <summary>Surface snapping for freely placed furnishings.</summary>
public static class BuildFurnishing
{
    public static BuildPreview Solve(BuildDefinition d,Vector3 hit,Vector3 normal,BuildPieceRecord target,
        BuildGridFrame frame,int heading,int turn,Vector3Int nudge,int? lockedWorldYaw=null)
    {
        var p=new BuildPreview{Definition=d,Hint="Furniture on ground"};
        if(target==null)
        {
            Vector3 origin=(Vector3)BuildGeometry.Ticks(hit)*BuildGeometry.Unit;
            origin.y=Mathf.Floor(hit.y/BuildGeometry.Unit)*BuildGeometry.Unit-d.LocalBounds.min.y;
            p.Frame=new BuildGridFrame{Origin=origin,YawStep=(byte)BuildGeometry.Turn(heading)};
            p.Anchor=nudge;p.InvalidAttachment=d.furnitureWallMounted;
        }
        else
        {
            p.Frame=frame;p.YawStep=BuildGeometry.Turn(turn);
            Vector3 point=BuildGeometry.LocalPoint(frame,hit);
            var bounds=target.Definition.LocalBounds;
            Vector3 localNormal=Quaternion.Inverse(BuildGeometry.Rotation(frame.YawStep))*normal;
            if(d.furnitureWallMounted)
            {
                p.Hint=d.flamePrefab!=null?"Torch on wall face":d.contentId=="build.wood.l35-braced-door-canopy"?"Canopy on wall face":"Shelf on wall face";
                p.InvalidAttachment=target.Definition.kind is not (BuildPartKind.Wall or BuildPartKind.Corner) || Mathf.Abs(localNormal.y)>.25f;
                p.YawStep=lockedWorldYaw.HasValue?BuildGeometry.Turn(lockedWorldYaw.Value-frame.YawStep):BuildGeometry.Turn(BuildPlacement.FacingYaw(localNormal,2)+turn);
                // Preserve the actual wall face, including 0.125 m timber half-thickness.
                // Only tangent/elevation use quarter-metre ticks. A dedicated persistent frame
                // represents that face without moving or changing the structural wall frame.
                Vector3 face=(Vector3)BuildGeometry.Ticks(point)*BuildGeometry.Unit;
                if(Mathf.Abs(localNormal.x)>Mathf.Abs(localNormal.z))face.x=point.x;else face.z=point.z;
                p.Frame=new BuildGridFrame{Origin=frame.Origin+BuildGeometry.Rotation(frame.YawStep)*face,YawStep=frame.YawStep};
                p.Anchor=nudge;
            }
            else
            {
                p.Hint="Furniture on floor";
                p.InvalidAttachment=target.Definition.kind is not (BuildPartKind.Floor or BuildPartKind.Foundation) || localNormal.y<.6f;
                point.y=bounds.max.y-d.LocalBounds.min.y;
                p.Anchor=BuildGeometry.Ticks(point)+nudge;
            }
            if(d.kind!=BuildPartKind.Furniture || target.Definition.kind==BuildPartKind.Furniture)p.InvalidAttachment=true;
        }
        p.Origin=BuildGeometry.WorldPoint(p.Frame,p.Anchor);
        p.WorldYaw=BuildGeometry.Turn(p.Frame.YawStep+p.YawStep);
        return p;
    }

}
