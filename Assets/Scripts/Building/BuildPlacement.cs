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
            Vector3 origin;
            int edge = ClosestEdge(bounds, point);
            bool upper = IsUpperHit(bounds, point, localNormal);
            if (definition.kind == BuildPartKind.Foundation && target.Definition.kind != BuildPartKind.Foundation)
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
                            bounds.max.y - definition.LocalBounds.min.y,
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
                    origin = upper ? new Vector3(bounds.min.x, bounds.max.y, bounds.min.z) : side switch {
                        0 => new Vector3(bounds.min.x, bounds.min.y, bounds.min.z - size.z),
                        2 => new Vector3(bounds.min.x - size.x, bounds.min.y, bounds.min.z),
                        4 => new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
                        _ => new Vector3(bounds.max.x, bounds.min.y, bounds.min.z)
                    };
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
            else if (definition.kind == BuildPartKind.Wall && target.Definition.kind == BuildPartKind.Corner)
            {
                int side = FacingSide(localNormal, edge);
                origin = upper ? new Vector3(bounds.center.x - definition.LocalBounds.center.x,
                    bounds.max.y - definition.LocalBounds.min.y, bounds.min.z - definition.LocalBounds.min.z) : side switch {
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
                float length = definition.LocalBounds.size.x;
                float alongX = BayStart(point.x, bounds.min.x, bounds.max.x, definition);
                float alongZ = BayStart(point.z, bounds.min.z, bounds.max.z, definition);
                origin = edge switch {
                    0 => new Vector3(alongX, bounds.max.y, bounds.min.z),
                    2 => new Vector3(bounds.min.x, bounds.max.y, alongZ + length),
                    4 => new Vector3(alongX + length, bounds.max.y, bounds.max.z),
                    _ => new Vector3(bounds.max.x, bounds.max.y, alongZ)
                };
                preview.YawStep = BuildGeometry.Turn(edge + contextTurn);
                preview.Hint = "Wall along edge";
            }
            else if (definition.kind == BuildPartKind.Wall)
            {
                origin = upper ? new Vector3(bounds.min.x, bounds.max.y - definition.LocalBounds.min.y, bounds.min.z)
                    : new Vector3(point.x < bounds.center.x ? bounds.min.x - definition.LocalBounds.max.x :
                        bounds.max.x - definition.LocalBounds.min.x, bounds.min.y, bounds.min.z);
                preview.YawStep = BuildGeometry.Turn(contextTurn);
                preview.Hint = upper ? "Stack above" : "Extend wall";
            }
            else if (definition.kind == BuildPartKind.Floor && target.Definition.kind == BuildPartKind.Foundation && localNormal.y > .6f)
            {
                origin = new Vector3(bounds.min.x, bounds.max.y - definition.LocalBounds.min.y, bounds.min.z);
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
                        seated ? bounds.max.y - footprint.min.y : BuildGeometry.Tick(point.y) * BuildGeometry.Unit - footprint.max.y,
                        towardsPositiveZ
                            ? (seated ? bounds.min.z : bounds.max.z) - footprint.min.z
                            : (seated ? bounds.max.z : bounds.min.z) - footprint.max.z);
                    preview.Hint = seated ? "Ceiling toward you" : "Side platform";
                }
                else
                {
                    origin = new Vector3(bounds.center.x - footprint.center.x, bounds.max.y - footprint.min.y,
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

/// <summary>A real wall hit seeds a bounded extension of its viewed face; no world objects are created.</summary>
public struct BuildWallAimGuide
{
    private BuildGridFrame frame;
    private Bounds bounds;
    private float faceZ;
    private Vector3 faceNormal;
    public void Capture(BuildPieceRecord wall, BuildGridFrame wallFrame, Vector3 hitNormal, Vector3 viewer)
    {
        frame = wallFrame; bounds = wall.Definition.LocalBounds;
        Vector3 normal = Quaternion.Inverse(BuildGeometry.Rotation(frame.YawStep)) * hitNormal;
        bool positive = Mathf.Abs(normal.z) > .5f ? normal.z > 0 : BuildGeometry.LocalPoint(frame, viewer).z >= bounds.center.z;
        faceZ = positive ? bounds.max.z : bounds.min.z;
        faceNormal = BuildGeometry.Rotation(frame.YawStep) * (positive ? Vector3.forward : Vector3.back);
    }
    public bool TryContinue(Ray ray, float reach, float aboveTop, float sideMargin, out Vector3 point, out Vector3 normal)
    {
        point = normal = default;
        if (frame == null) return false;
        Vector3 origin = BuildGeometry.LocalPoint(frame, ray.origin);
        Vector3 direction = Quaternion.Inverse(BuildGeometry.Rotation(frame.YawStep)) * ray.direction;
        // Reject near-parallel and back-facing rays instead of extrapolating unstable/distant intersections.
        if (Mathf.Abs(direction.z) < .01f || Vector3.Dot(ray.direction, faceNormal) >= 0) return false;
        float distance = (faceZ - origin.z) / direction.z;
        if (distance <= 0 || distance > reach) return false;
        Vector3 local = origin + direction * distance;
        if (local.x < bounds.min.x - sideMargin || local.x > bounds.max.x + sideMargin ||
            local.y <= bounds.max.y || local.y > bounds.max.y + aboveTop) return false;
        point = ray.GetPoint(distance); normal = faceNormal;
        return true;
    }
}
