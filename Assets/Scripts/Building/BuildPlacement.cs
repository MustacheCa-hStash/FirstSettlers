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
    public string Message;
}

public static class BuildPlacement
{
    public static BuildPreview Solve(BuildDefinition definition, Vector3 hit, Vector3 normal, BuildPieceRecord target,
        BuildGridFrame targetFrame, int worldHeading, int contextTurn, Vector3Int nudge)
    {
        var preview = new BuildPreview { Definition = definition };
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
            if (definition.kind == BuildPartKind.Foundation && target.Definition.kind != BuildPartKind.Foundation)
            {
                // Aiming at an existing wall/floor can replace its missing footing, preserving its frame.
                origin = new Vector3(bounds.min.x, bounds.min.y - definition.LocalBounds.max.y, bounds.min.z);
                preview.YawStep = BuildGeometry.Turn(contextTurn);
            }
            else if (definition.kind == BuildPartKind.Wall && target.Definition.kind != BuildPartKind.Wall)
            {
                float length = definition.LocalBounds.size.x;
                float alongX = Mathf.Clamp((BuildGeometry.Tick(point.x - length * .5f) * BuildGeometry.Unit), bounds.min.x, Mathf.Max(bounds.min.x, bounds.max.x - length));
                float alongZ = Mathf.Clamp((BuildGeometry.Tick(point.z - length * .5f) * BuildGeometry.Unit), bounds.min.z, Mathf.Max(bounds.min.z, bounds.max.z - length));
                origin = edge switch {
                    0 => new Vector3(alongX, bounds.max.y, bounds.min.z),
                    2 => new Vector3(bounds.min.x, bounds.max.y, alongZ + length),
                    4 => new Vector3(alongX + length, bounds.max.y, bounds.max.z),
                    _ => new Vector3(bounds.max.x, bounds.max.y, alongZ)
                };
                preview.YawStep = BuildGeometry.Turn(edge + contextTurn);
            }
            else if (definition.kind == BuildPartKind.Wall)
            {
                origin = localNormal.y > .6f ? new Vector3(0, bounds.max.y, 0)
                    : new Vector3(point.x < bounds.center.x ? -definition.LocalBounds.size.x : bounds.max.x, 0, 0);
                preview.YawStep = BuildGeometry.Turn(contextTurn);
            }
            else if (definition.kind == BuildPartKind.Floor && target.Definition.kind == BuildPartKind.Foundation && localNormal.y > .6f)
            {
                origin = new Vector3(bounds.min.x, bounds.max.y - definition.LocalBounds.min.y, bounds.min.z);
                preview.YawStep = BuildGeometry.Turn(contextTurn);
            }
            else if (target.Definition.kind == BuildPartKind.Wall)
            {
                origin = new Vector3(point.x - definition.LocalBounds.size.x * .5f, bounds.max.y - definition.LocalBounds.min.y,
                    point.z - definition.LocalBounds.size.z * .5f);
                preview.YawStep = BuildGeometry.Turn(contextTurn);
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
