using UnityEngine;

public struct BuildPreview
{
    public BuildDefinition Definition;
    public BuildGridFrame Frame;
    public Vector3Int Anchor;
    public int YawStep;
    public Vector3 Origin;
    public int WorldYaw;
    public bool Grounded, Valid;
    public bool TopAttachment;
    public bool FitStairContinuation;
    public Vector3Int StairFitNudge;
    public bool RoofContinuesFromBelow;
    public BuildResolvedState Resolved;
    public BuildPlacementFailure Failure;
    public ulong BlockingPieceId;
    public Collider BlockingCollider;
    public BuildPlacementWarning Warning;
    public ulong WarningPieceId;
    public string WarningMessage;
    public Mesh VisualMesh => Resolved!=null ? Resolved.mesh : RoofContinuesFromBelow && Definition.roofContinuationMesh!=null ? Definition.roofContinuationMesh : Definition.mesh;
    public string Message;
    public string Hint;
}

public static class BuildPlacement
{
    public static BuildPreview Solve(BuildDefinition definition, Vector3 hit, Vector3 normal, BuildPieceRecord target,
        BuildGridFrame targetFrame, int worldHeading, int contextTurn, Vector3Int nudge, Vector3? viewerPosition = null,
        bool preferTopAttachment = false)
    {
        var preview = new BuildPreview { Definition = definition, Hint = "Ground grid" };
        if (target == null)
        {
            // Seed origins, including height, are on the world lattice. Terrain footing may embed by < one unit.
            Vector3 centreOffset = BuildGeometry.Rotation(worldHeading) * new Vector3(definition.LocalBounds.center.x, 0, definition.LocalBounds.center.z);
            Vector3 point = hit - centreOffset;
            point.y = definition.kind == BuildPartKind.Foundation
                ? Mathf.Floor(hit.y / BuildGeometry.Unit) * BuildGeometry.Unit - definition.LocalBounds.min.y
                : Mathf.Ceil(hit.y / BuildGeometry.Unit) * BuildGeometry.Unit - definition.LocalBounds.min.y;
            Vector3 origin = (Vector3)BuildGeometry.Ticks(point) * BuildGeometry.Unit;
            preview.Frame = new BuildGridFrame { Origin = origin, YawStep = (byte)BuildGeometry.Turn(worldHeading) };
            preview.Anchor = nudge;
        }
        else
        {
            preview.Frame = targetFrame;
            Bounds bounds = target.Definition.LocalBounds;
            Vector3 point = BuildGeometry.LocalPoint(targetFrame, hit);
            Vector3 localNormal = Quaternion.Inverse(BuildGeometry.Rotation(target.WorldYawStep)) * normal;
            if(BuildGablePlacement.Solve(definition,hit,normal,target,contextTurn,nudge,viewerPosition,out var gablePreview))return gablePreview;
            if(BuildSocketPlacement.Solve(definition,hit,normal,target,contextTurn,nudge,viewerPosition,preferTopAttachment,out var socketPreview))return socketPreview;
            Vector3 origin;
            int edge = ClosestEdge(bounds, point);
            bool upper = IsUpperHit(bounds, point, localNormal);
            if (definition.kind == BuildPartKind.Roof || target.Definition.kind == BuildPartKind.Roof)
            {
                origin = BuildRoof.Snap(definition,target,point,localNormal,contextTurn,viewerPosition,out int roofYaw,out string roofHint);
                preview.YawStep = roofYaw; preview.Hint = roofHint;
            }
            else if (definition.kind == BuildPartKind.Stair)
            {
                preview.YawStep = BuildGeometry.Turn(contextTurn);
                Bounds footprint = BuildGeometry.WorldBounds(definition.LocalBounds, Vector3.zero, preview.YawStep);
                if (localNormal.y < -.6f)
                {
                    origin = new Vector3(SurfaceAnchor(point.x,bounds.min.x,bounds.max.x,footprint.min.x,footprint.max.x),
                        bounds.min.y-footprint.max.y,SurfaceAnchor(point.z,bounds.min.z,bounds.max.z,footprint.min.z,footprint.max.z));
                    preview.Hint = "Attach underneath";
                }
                else if (target.Definition.kind == BuildPartKind.Stair)
                {
                    origin = new Vector3(bounds.min.x - footprint.min.x, bounds.max.y - footprint.min.y, bounds.max.z - footprint.min.z);
                    preview.Hint = "Continue stair flight";
                }
                else if ((target.Definition.kind is BuildPartKind.Floor or BuildPartKind.Foundation) && localNormal.y > .6f)
                {
                    origin = new Vector3(SurfaceAnchor(point.x,bounds.min.x,bounds.max.x,footprint.min.x,footprint.max.x),
                        bounds.max.y-footprint.min.y, SurfaceAnchor(point.z,bounds.min.z,bounds.max.z,footprint.min.z,footprint.max.z));
                    preview.Hint = "Stair foot on surface";
                    preview.FitStairContinuation = true; preview.StairFitNudge = nudge;
                }
                else if ((target.Definition.kind is BuildPartKind.Floor or BuildPartKind.Foundation) && Mathf.Abs(localNormal.y) < .6f)
                {
                    int side = FacingSide(localNormal,edge);
                    preview.YawStep = BuildGeometry.Turn(side + contextTurn);
                    Vector3 contact = side switch {
                        0 => new Vector3(point.x,bounds.max.y,bounds.min.z),
                        2 => new Vector3(bounds.min.x,bounds.max.y,point.z),
                        4 => new Vector3(point.x,bounds.max.y,bounds.max.z),
                        _ => new Vector3(bounds.max.x,bounds.max.y,point.z)
                    };
                    Vector3 top = new(definition.LocalBounds.center.x,definition.LocalBounds.max.y,definition.LocalBounds.max.z);
                    origin = contact - BuildGeometry.Rotation(preview.YawStep)*top;
                    preview.Hint = "Stair high end at floor edge";
                }
                else
                {
                    origin = new Vector3(point.x-footprint.center.x, point.y-footprint.min.y, point.z-footprint.center.z);
                    preview.Hint = "Stair grid";
                }
            }
            else if (definition.kind == BuildPartKind.Floor && target.Definition.kind == BuildPartKind.Stair && localNormal.y >= -.6f)
            {
                preview.YawStep = BuildGeometry.Turn(contextTurn);
                Bounds footprint = BuildGeometry.WorldBounds(definition.LocalBounds, Vector3.zero, preview.YawStep);
                origin = new Vector3(bounds.min.x-footprint.min.x,bounds.max.y-footprint.max.y,bounds.max.z-footprint.min.z);
                preview.Hint = "Landing beyond stair";
            }
            else if (localNormal.y < -.6f && !(definition.kind == BuildPartKind.Foundation && target.Definition.kind != BuildPartKind.Foundation))
            {
                // The aimed face decides elevation before any upper-half/perimeter heuristics.
                preview.YawStep = BuildGeometry.Turn((definition.IsWallInfill ? ViewerFacingYaw(bounds, point, targetFrame, viewerPosition) : 0) + contextTurn);
                Bounds footprint = BuildGeometry.WorldBounds(definition.LocalBounds, Vector3.zero, preview.YawStep);
                if (definition.kind == BuildPartKind.Wall && target.Definition.kind is BuildPartKind.Floor or BuildPartKind.Foundation)
                {
                    if (definition.IsWallInfill || definition.IsSurfaceWall)
                        origin = SurfaceWallOrigin(bounds, point, footprint);
                    else
                    {
                        origin = WallEdgeOrigin(definition, bounds, point, edge);
                        preview.YawStep = BuildGeometry.Turn(edge + contextTurn);
                        footprint = BuildGeometry.WorldBounds(definition.LocalBounds, Vector3.zero, preview.YawStep);
                    }
                }
                else if (definition.kind == BuildPartKind.Corner)
                    origin = new Vector3(SurfaceAnchor(point.x, bounds.min.x, bounds.max.x, footprint.min.x, footprint.max.x), 0,
                        SurfaceAnchor(point.z, bounds.min.z, bounds.max.z, footprint.min.z, footprint.max.z));
                else if (target.Definition.kind == BuildPartKind.Wall && definition.kind == BuildPartKind.Floor)
                {
                    bool positive = ViewerSide(bounds, point, localNormal, targetFrame, viewerPosition);
                    origin = new Vector3(bounds.center.x - footprint.center.x, 0,
                        positive ? bounds.min.z - footprint.min.z : bounds.max.z - footprint.max.z);
                }
                else if (target.Definition.kind == BuildPartKind.Corner)
                    origin = new Vector3(bounds.center.x - footprint.center.x, 0, bounds.center.z - footprint.center.z);
                else
                    origin = new Vector3(bounds.min.x - footprint.min.x, 0, bounds.min.z - footprint.min.z);
                origin.y = bounds.min.y - footprint.max.y;
                preview.Hint = "Attach underneath";
            }
            else if (definition.kind == BuildPartKind.Foundation && target.Definition.kind != BuildPartKind.Foundation)
            {
                // Aiming at an existing wall/floor can replace its missing footing, preserving its frame.
                origin = new Vector3(bounds.min.x - (target.Definition.kind == BuildPartKind.Wall ? target.Definition.WallEndInset : 0),
                    bounds.min.y - definition.LocalBounds.max.y, bounds.min.z);
                preview.YawStep = BuildGeometry.Turn(contextTurn);
            }
            else if (definition.kind == BuildPartKind.Corner)
            {
                if (target.Definition.kind == BuildPartKind.Wall)
                {
                    if (upper)
                    {
                        origin = new Vector3(SurfaceAnchor(point.x, bounds.min.x, bounds.max.x, definition.LocalBounds.min.x, definition.LocalBounds.max.x),
                            bounds.min.y + target.Definition.StackRise - definition.LocalBounds.min.y,
                            SurfaceAnchor(point.z, bounds.min.z, bounds.max.z, definition.LocalBounds.min.z, definition.LocalBounds.max.z));
                        preview.Hint = "Pillar above";
                    }
                    else if (point.x <= bounds.min.x + .15f || point.x >= bounds.max.x - .15f)
                    {
                        origin = new Vector3(point.x < bounds.center.x ? bounds.min.x - definition.LocalBounds.size.x : bounds.max.x,
                            bounds.min.y, bounds.min.z);
                        preview.Hint = "Fill wall end";
                    }
                    else
                    {
                        bool towardsPositiveZ = ViewerSide(bounds, point, localNormal, targetFrame, viewerPosition);
                        origin = new Vector3(BuildGeometry.Tick(point.x - definition.LocalBounds.center.x) * BuildGeometry.Unit,
                            bounds.min.y - definition.LocalBounds.min.y,
                            towardsPositiveZ ? bounds.max.z - definition.LocalBounds.min.z : bounds.min.z - definition.LocalBounds.max.z);
                        preview.Hint = "Pillar beside wall";
                    }
                }
                else if (target.Definition.kind == BuildPartKind.Corner)
                {
                    Vector3 size = definition.LocalBounds.size;
                    int side = FacingSide(localNormal, edge);
                    origin = upper ? new Vector3(bounds.min.x, bounds.min.y + target.Definition.StackRise, bounds.min.z) : side switch {
                        0 => new Vector3(bounds.min.x, bounds.min.y, bounds.min.z - size.z),
                        2 => new Vector3(bounds.min.x - size.x, bounds.min.y, bounds.min.z),
                        4 => new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
                        _ => new Vector3(bounds.max.x, bounds.min.y, bounds.min.z)
                    };
                    preview.Hint = upper ? "Stack above" : "Adjacent pillar";
                }
                else
                {
                    // Ordinary grid placement across the entire surface, including its flush corner cells.
                    origin = new Vector3(SurfaceAnchor(point.x, bounds.min.x, bounds.max.x, definition.LocalBounds.min.x, definition.LocalBounds.max.x),
                        bounds.max.y - definition.LocalBounds.min.y,
                        SurfaceAnchor(point.z, bounds.min.z, bounds.max.z, definition.LocalBounds.min.z, definition.LocalBounds.max.z));
                    preview.Hint = "Pillar on surface";
                }
                preview.YawStep = BuildGeometry.Turn(contextTurn);
            }
            else if ((definition.IsWallInfill && target.Definition.kind != BuildPartKind.Wall) ||
                (definition.IsSurfaceWall && target.Definition.kind is BuildPartKind.Floor or BuildPartKind.Foundation))
            {
                // Fillers use the surface grid rather than the full panel's perimeter/corner sockets.
                preview.YawStep = BuildGeometry.Turn((definition.IsWallInfill ? ViewerFacingYaw(bounds, point, targetFrame, viewerPosition) : 0) + contextTurn);
                Bounds footprint = BuildGeometry.WorldBounds(definition.LocalBounds, Vector3.zero, preview.YawStep);
                if (target.Definition.kind == BuildPartKind.Corner)
                {
                    bool stack = localNormal.y > .6f || point.y > bounds.max.y;
                    if (stack)
                        origin = new Vector3(bounds.center.x - footprint.center.x, bounds.max.y - footprint.min.y,
                            bounds.center.z - footprint.center.z);
                    else if (footprint.size.x >= footprint.size.z)
                        origin = new Vector3(point.x < bounds.center.x ? bounds.min.x - footprint.max.x : bounds.max.x - footprint.min.x,
                            bounds.min.y - footprint.min.y, bounds.center.z - footprint.center.z);
                    else
                        origin = new Vector3(bounds.center.x - footprint.center.x, bounds.min.y - footprint.min.y,
                            point.z < bounds.center.z ? bounds.min.z - footprint.max.z : bounds.max.z - footprint.min.z);
                    preview.Hint = stack ? "Infill above pillar" : "Infill beside pillar";
                }
                else
                {
                    origin = SurfaceWallOrigin(bounds, point, footprint);
                    preview.Hint = definition.IsWallInfill ? "Infill on surface" : "Wall on surface";
                }
            }
            else if (definition.kind == BuildPartKind.Wall && target.Definition.kind == BuildPartKind.Corner)
            {
                int side = FacingSide(localNormal, edge);
                origin = upper ? new Vector3(bounds.center.x - definition.LocalBounds.center.x,
                    bounds.min.y + target.Definition.StackRise - definition.LocalBounds.min.y, bounds.min.z - definition.LocalBounds.min.z) : side switch {
                    0 => new Vector3(bounds.min.x, bounds.min.y, bounds.min.z),
                    2 => new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
                    4 => new Vector3(bounds.max.x, bounds.min.y, bounds.max.z),
                    _ => new Vector3(bounds.max.x, bounds.min.y, bounds.min.z)
                };
                preview.YawStep = BuildGeometry.Turn((upper ? 0 : side + 2) + contextTurn);
                preview.Hint = upper ? "Stack above" : "Extend from pillar";
            }
            else if (definition.kind == BuildPartKind.Wall && target.Definition.kind != BuildPartKind.Wall)
            {
                origin = WallEdgeOrigin(definition, bounds, point, edge);
                preview.YawStep = BuildGeometry.Turn(edge + contextTurn);
                preview.Hint = "Wall along edge";
            }
            else if (definition.kind == BuildPartKind.Wall)
            {
                bool stack = definition.IsWallInfill ? localNormal.y > .6f || point.y > bounds.max.y : upper;
                origin = stack ? new Vector3(bounds.min.x, bounds.min.y + target.Definition.StackRise - definition.LocalBounds.min.y, bounds.min.z)
                    : new Vector3(point.x < bounds.center.x ? bounds.min.x - definition.LocalBounds.max.x :
                        bounds.max.x - definition.LocalBounds.min.x, bounds.min.y, bounds.min.z);
                preview.YawStep = BuildGeometry.Turn(contextTurn);
                preview.Hint = stack ? "Stack above" : definition.IsWallInfill ? "Fill wall end" : "Extend wall";
            }
            else if (definition.kind == BuildPartKind.Floor && target.Definition.kind == BuildPartKind.Foundation && localNormal.y > .6f)
            {
                origin = new Vector3(bounds.min.x, bounds.max.y - definition.LocalBounds.max.y, bounds.min.z);
                preview.YawStep = BuildGeometry.Turn(contextTurn);
            }
            else if (target.Definition.kind == BuildPartKind.Wall || target.Definition.kind == BuildPartKind.Corner)
            {
                preview.YawStep = BuildGeometry.Turn(contextTurn);
                Bounds footprint = BuildGeometry.WorldBounds(definition.LocalBounds, Vector3.zero, preview.YawStep);
                if (target.Definition.kind == BuildPartKind.Wall)
                {
                    bool towardsPositiveZ = ViewerSide(bounds, point, localNormal, targetFrame, viewerPosition);
                    // Face aiming retains every height tick; only the actual top/its sky extension seats a slab.
                    bool seated = localNormal.y > .6f || point.y > bounds.max.y + (preferTopAttachment ? -.03f : .03f);
                    preview.TopAttachment = seated;
                    // The wall's centre selects the bay; the aimed face selects the side of that bay.
                    // At its top, the slab covers the full wall thickness before extending toward the viewer.
                    origin = new Vector3(bounds.center.x - footprint.center.x,
                        seated ? bounds.min.y + target.Definition.StackRise - footprint.max.y : BuildGeometry.Tick(point.y) * BuildGeometry.Unit - footprint.max.y,
                        towardsPositiveZ
                            ? (seated ? bounds.min.z : bounds.max.z) - footprint.min.z
                            : (seated ? bounds.max.z : bounds.min.z) - footprint.max.z);
                    preview.Hint = seated ? "Ceiling toward you" : "Side platform";
                }
                else
                {
                    if(definition.kind==BuildPartKind.Floor && viewerPosition.HasValue)
                    {
                        var view=BuildGeometry.LocalPoint(targetFrame,viewerPosition.Value);
                        origin=new Vector3(view.x>=bounds.center.x?bounds.min.x-footprint.min.x:bounds.max.x-footprint.max.x,
                            bounds.min.y+target.Definition.StackRise-footprint.max.y,view.z>=bounds.center.z?bounds.min.z-footprint.min.z:bounds.max.z-footprint.max.z);
                    }
                    else origin = new Vector3(bounds.center.x - footprint.center.x, bounds.min.y + target.Definition.StackRise - footprint.max.y,
                        bounds.center.z - footprint.center.z);
                    preview.Hint = "Platform above pillar";
                }
            }
            else
            {
                // Floors and foundations extend horizontally, preserving the target walking elevation.
                Vector3 size = definition.LocalBounds.size;
                origin = edge switch {
                    0 => new Vector3(bounds.min.x, bounds.max.y, bounds.min.z - size.z),
                    2 => new Vector3(bounds.min.x - size.x, bounds.max.y, bounds.min.z),
                    4 => new Vector3(bounds.min.x, bounds.max.y, bounds.max.z),
                    _ => new Vector3(bounds.max.x, bounds.max.y, bounds.min.z)
                };
                preview.YawStep = BuildGeometry.Turn(contextTurn);
            }
            preview.Anchor = BuildGeometry.Ticks(origin) + nudge;
        }
        preview.Origin = BuildGeometry.WorldPoint(preview.Frame, preview.Anchor);
        preview.WorldYaw = BuildGeometry.Turn(preview.Frame.YawStep + preview.YawStep);
        return preview;
    }
    private static bool IsUpperHit(Bounds bounds, Vector3 point, Vector3 normal) => normal.y > .6f || point.y >= bounds.center.y;
    private static Vector3 SurfaceWallOrigin(Bounds surface, Vector3 point, Bounds footprint)
    {
        // Long-axis overhang permits seam/interior construction; the thin axis stays on the supporting face.
        bool alongX = footprint.size.x >= footprint.size.z;
        return new Vector3(alongX ? BuildGeometry.Tick(point.x - footprint.center.x) * BuildGeometry.Unit :
                SurfaceAnchor(point.x, surface.min.x, surface.max.x, footprint.min.x, footprint.max.x),
            surface.max.y - footprint.min.y,
            alongX ? SurfaceAnchor(point.z, surface.min.z, surface.max.z, footprint.min.z, footprint.max.z) :
                BuildGeometry.Tick(point.z - footprint.center.z) * BuildGeometry.Unit);
    }
    private static Vector3 WallEdgeOrigin(BuildDefinition definition, Bounds bounds, Vector3 point, int edge)
    {
        float length = definition.LocalBounds.size.x;
        float alongX = BayStart(point.x, bounds.min.x, bounds.max.x, definition);
        float alongZ = BayStart(point.z, bounds.min.z, bounds.max.z, definition);
        return edge switch {
            0 => new Vector3(alongX, bounds.max.y, bounds.min.z),
            2 => new Vector3(bounds.min.x, bounds.max.y, alongZ + length),
            4 => new Vector3(alongX + length, bounds.max.y, bounds.max.z),
            _ => new Vector3(bounds.max.x, bounds.max.y, alongZ)
        };
    }
    public static int FacingYaw(Vector3 towardsViewer, int stepSize = 1) =>
        BuildGeometry.Turn(Mathf.RoundToInt(Mathf.Atan2(towardsViewer.x, towardsViewer.z) * Mathf.Rad2Deg / (45 * stepSize)) * stepSize);
    private static int ViewerFacingYaw(Bounds bounds, Vector3 point, BuildGridFrame frame, Vector3? viewer)
    {
        if (!viewer.HasValue) return 0;
        Vector3 direction = BuildGeometry.LocalPoint(frame, viewer.Value) - point;
        if (new Vector2(direction.x, direction.z).sqrMagnitude < .0001f)
            direction = BuildGeometry.LocalPoint(frame, viewer.Value) - bounds.center;
        // Defaults follow the target's two wall axes. Manual turns still offer every 45-degree step.
        return FacingYaw(direction, 2);
    }
    private static float SurfaceAnchor(float point, float surfaceMin, float surfaceMax, float partMin, float partMax)
    {
        float anchor = BuildGeometry.Tick(point - (partMin + partMax) * .5f) * BuildGeometry.Unit;
        return Mathf.Clamp(anchor, surfaceMin - partMin, Mathf.Max(surfaceMin - partMin, surfaceMax - partMax));
    }
    private static bool ViewerSide(Bounds bounds, Vector3 point, Vector3 normal, BuildGridFrame frame, Vector3? viewer)
    {
        if (Mathf.Abs(normal.z) > .5f) return normal.z > 0;
        Vector3 localViewer = viewer.HasValue ? BuildGeometry.LocalPoint(frame, viewer.Value) : point;
        return localViewer.z >= bounds.center.z;
    }
    private static float BayStart(float point, float min, float max, BuildDefinition wall)
    {
        float span = wall.WallBaySpan;
        int last = Mathf.Max(0, Mathf.FloorToInt((max - min + .001f) / span) - 1);
        int bay = Mathf.Clamp(Mathf.FloorToInt((point - min) / span), 0, last);
        return min + wall.WallEndInset + bay * span;
    }
    private static int FacingSide(Vector3 normal, int nearest)
    {
        if (Mathf.Abs(normal.y) > .6f) return nearest;
        return Mathf.Abs(normal.x) > Mathf.Abs(normal.z) ? (normal.x > 0 ? 6 : 2) : (normal.z > 0 ? 4 : 0);
    }
    private static int ClosestEdge(Bounds bounds, Vector3 p)
    {
        float best = Mathf.Abs(p.z - bounds.min.z); int edge = 0;
        void Consider(float distance, int candidate) { if (distance < best) { best = distance; edge = candidate; } }
        Consider(Mathf.Abs(p.x - bounds.min.x), 2);
        Consider(Mathf.Abs(p.z - bounds.max.z), 4);
        Consider(Mathf.Abs(p.x - bounds.max.x), 6);
        return edge;
    }
}

/// <summary>A real wall/pillar hit seeds a bounded extension of its viewed face; no world objects are created.</summary>
public struct BuildSurfaceAimGuide
{
    private BuildGridFrame frame;
    private Bounds bounds;
    private float faceCoordinate;
    private bool alongX;
    private bool pillar;
    private Vector3 faceNormal;
    public void Capture(BuildPieceRecord piece, BuildGridFrame pieceFrame, Vector3 hitNormal, Vector3 viewer)
    {
        frame = pieceFrame; bounds = piece.Definition.LocalBounds;
        pillar = piece.Definition.kind == BuildPartKind.Corner;
        Vector3 normal = Quaternion.Inverse(BuildGeometry.Rotation(frame.YawStep)) * hitNormal;
        Vector3 towardsViewer = BuildGeometry.LocalPoint(frame, viewer) - bounds.center;
        alongX = piece.Definition.kind == BuildPartKind.Corner &&
            (Mathf.Abs(normal.y) > .6f ? Mathf.Abs(towardsViewer.x) > Mathf.Abs(towardsViewer.z) : Mathf.Abs(normal.x) > Mathf.Abs(normal.z));
        float component = alongX ? normal.x : normal.z;
        bool positive = Mathf.Abs(component) > .5f ? component > 0 : (alongX ? towardsViewer.x : towardsViewer.z) >= 0;
        faceCoordinate = alongX ? (positive ? bounds.max.x : bounds.min.x) : (positive ? bounds.max.z : bounds.min.z);
        faceNormal = BuildGeometry.Rotation(frame.YawStep) * ((alongX ? Vector3.right : Vector3.forward) * (positive ? 1 : -1));
    }
    public bool TryContinue(Ray ray, float reach, float aboveTop, float sideMargin, out Vector3 point, out Vector3 normal)
    {
        point = normal = default;
        if (frame == null) return false;
        Vector3 origin = BuildGeometry.LocalPoint(frame, ray.origin);
        Vector3 direction = Quaternion.Inverse(BuildGeometry.Rotation(frame.YawStep)) * ray.direction;
        if (pillar)
        {
            // A volume above a narrow post remains selectable when an adjacent wall masks one face plane.
            var volume = new Bounds(new Vector3(bounds.center.x, bounds.max.y + aboveTop * .5f, bounds.center.z),
                new Vector3(bounds.size.x + sideMargin * 2, aboveTop, bounds.size.z + sideMargin * 2));
            if (!volume.IntersectRay(new Ray(origin, direction), out float entry) || entry <= 0 || entry > reach) return false;
            point = ray.GetPoint(entry); normal = faceNormal; return true;
        }
        // Reject near-parallel and back-facing rays instead of extrapolating unstable/distant intersections.
        float normalDirection = alongX ? direction.x : direction.z;
        if (Mathf.Abs(normalDirection) < .01f || Vector3.Dot(ray.direction, faceNormal) >= 0) return false;
        float distance = (faceCoordinate - (alongX ? origin.x : origin.z)) / normalDirection;
        if (distance <= 0 || distance > reach) return false;
        Vector3 local = origin + direction * distance;
        float tangent = alongX ? local.z : local.x;
        if (tangent < (alongX ? bounds.min.z : bounds.min.x) - sideMargin ||
            tangent > (alongX ? bounds.max.z : bounds.max.x) + sideMargin ||
            local.y <= bounds.max.y || local.y > bounds.max.y + aboveTop) return false;
        point = ray.GetPoint(distance); normal = faceNormal;
        return true;
    }
}
