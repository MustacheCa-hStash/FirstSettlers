using System.Collections.Generic;
using UnityEngine;

/// <summary>Surface placement and one-way furnishing support; never enters the structural contact graph.</summary>
public static class BuildFurnishing
{
    public static BuildPreview Solve(BuildDefinition d,Vector3 hit,Vector3 normal,BuildPieceRecord target,
        BuildGridFrame frame,int heading,int turn,Vector3Int nudge)
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
                p.YawStep=BuildGeometry.Turn(BuildPlacement.FacingYaw(localNormal,2)+turn);
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

    public static bool Grounded(BuildPreview p)
    {
        var points=p.Definition.furnitureSupportPoints;
        if(p.Definition.furnitureWallMounted || points==null || points.Length==0)return false;
        var q=BuildGeometry.Rotation(p.WorldYaw);
        foreach(var foot in points)
        {
            Vector3 bottom=p.Origin+q*foot;
            if(!Physics.Raycast(bottom+Vector3.up*.3f,Vector3.down,out var hit,.65f,GameplayLayers.SolidSurfaceMask,QueryTriggerInteraction.Ignore) ||
                !BuildWorld.IsGround(hit.collider) || hit.point.y-bottom.y<-.04f || hit.point.y-bottom.y>.251f || hit.normal.y<.5f)return false;
        }
        return true;
    }

    public static bool Supported(BuildPieceRecord p,IReadOnlyList<BuildPieceRecord> pieces,
        Dictionary<ulong,BuildResolvedState> states,HashSet<ulong> supported)
    {
        var points=p.Definition.furnitureSupportPoints;
        if(points==null || points.Length==0)return false;
        var rotation=BuildGeometry.Rotation(p.WorldYawStep);
        foreach(var point in points)
        {
            Vector3 world=p.Origin+rotation*point;bool found=false;
            foreach(var other in pieces)
            {
                if(other.Id==p.Id || !supported.Contains(other.Id))continue;
                var kind=other.Definition.kind;
                if(p.Definition.furnitureWallMounted ? kind is not (BuildPartKind.Wall or BuildPartKind.Corner) :
                    kind is not (BuildPartKind.Floor or BuildPartKind.Foundation))continue;
                var inverse=Quaternion.Inverse(BuildGeometry.Rotation(other.WorldYawStep));
                Vector3 local=inverse*(world-other.Origin);
                foreach(var box in states[other.Id].boxes)
                {
                    if(p.Definition.furnitureWallMounted)
                    {
                        Vector3 inward=inverse*(rotation*Vector3.back);
                        // Contact must be at a real rear surface, not buried through masonry.
                        Vector3 inside=local+inward*.008f,outside=local-inward*.008f;
                        if(box.Contains(inside) && !box.Contains(outside)){found=true;break;}
                    }
                    else if(Mathf.Abs(local.y-box.max.y)<.012f && local.x>=box.min.x-.002f && local.x<=box.max.x+.002f &&
                        local.z>=box.min.z-.002f && local.z<=box.max.z+.002f){found=true;break;}
                }
                if(!found && p.Definition.furnitureWallMounted)
                {
                    Vector3 inward=inverse*(rotation*Vector3.back);
                    foreach(var volume in states[other.Id].volumes)
                        if(BuildOccupancy.Contains(volume,local+inward*.008f) && !BuildOccupancy.Contains(volume,local-inward*.008f)){found=true;break;}
                }
                if(found)break;
            }
            if(!found)return false;
        }
        return true;
    }
}
