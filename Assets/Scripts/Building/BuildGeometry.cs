using UnityEngine;

public static class BuildGeometry
{
    public const float Unit = .25f;
    public const float CellSize = 8f;
    public static int Turn(int step) => (step % 8 + 8) % 8;
    public static Quaternion Rotation(int step) => Quaternion.Euler(0, Turn(step) * 45f, 0);
    // Symmetric tie handling avoids a directional bias at negative world coordinates.
    public static int Tick(float value) => (int)System.Math.Round(value / Unit, System.MidpointRounding.AwayFromZero);
    public static Vector3Int Ticks(Vector3 point) => new(Tick(point.x), Tick(point.y), Tick(point.z));
    public static Vector3Int Cell(Vector3 point) => new(Mathf.FloorToInt(point.x / CellSize),
        Mathf.FloorToInt(point.y / CellSize), Mathf.FloorToInt(point.z / CellSize));
    public static Vector3 WorldPoint(BuildGridFrame frame, Vector3Int ticks) => frame.Origin + Rotation(frame.YawStep) * ((Vector3)ticks * Unit);
    public static Vector3 LocalPoint(BuildGridFrame frame, Vector3 world) => Quaternion.Inverse(Rotation(frame.YawStep)) * (world - frame.Origin);
    public static Bounds WorldBounds(Bounds local, Vector3 origin, int yaw)
    {
        var rotation = Rotation(yaw);
        Vector3 x = rotation * Vector3.right * local.extents.x, z = rotation * Vector3.forward * local.extents.z;
        return new Bounds(origin + rotation * local.center,
            new Vector3(Mathf.Abs(x.x) + Mathf.Abs(z.x), local.extents.y, Mathf.Abs(x.z) + Mathf.Abs(z.z)) * 2);
    }
    public static float Distance(Bounds local, Vector3 origin, int yaw, Vector3 point)
    {
        Vector3 p = Quaternion.Inverse(Rotation(yaw)) * (point - origin);
        return Vector3.Distance(p, local.ClosestPoint(p));
    }
    private static float Radius(Bounds bounds, Quaternion rotation, Vector3 axis) =>
        Mathf.Abs(Vector3.Dot(rotation * Vector3.right, axis)) * bounds.extents.x +
        Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, axis)) * bounds.extents.z;
    private static float IntervalOverlap(float a, float ar, float b, float br) => Mathf.Min(a + ar, b + br) - Mathf.Max(a - ar, b - br);
    private static bool HorizontalOverlap(Bounds a, Vector3 ac, Quaternion aq, Bounds b, Vector3 bc, Quaternion bq, float minimum)
    {
        for (int i = 0; i < 4; i++)
        {
            Vector3 axis = (i < 2 ? aq : bq) * (i % 2 == 0 ? Vector3.right : Vector3.forward);
            if (IntervalOverlap(Vector3.Dot(ac, axis), Radius(a, aq, axis), Vector3.Dot(bc, axis), Radius(b, bq, axis)) <= minimum) return false;
        }
        return true;
    }
    public static bool Overlaps(Bounds a, Vector3 ao, int ay, Bounds b, Vector3 bo, int by, float minimum = .005f)
    {
        Quaternion aq = Rotation(ay), bq = Rotation(by);
        Vector3 ac = ao + aq * a.center, bc = bo + bq * b.center;
        return IntervalOverlap(ac.y, a.extents.y, bc.y, b.extents.y) > minimum &&
            HorizontalOverlap(a, ac, aq, b, bc, bq, minimum);
    }
    /// <summary>Face contacts transmit support. Mere edge/corner touching does not.</summary>
    public static bool Connects(Bounds a, Vector3 ao, int ay, Bounds b, Vector3 bo, int by)
    {
        const float tolerance = .012f;
        Quaternion aq = Rotation(ay), bq = Rotation(by);
        Vector3 ac = ao + aq * a.center, bc = bo + bq * b.center;
        if (Mathf.Abs(Mathf.Abs(ac.y - bc.y) - a.extents.y - b.extents.y) <= tolerance &&
            HorizontalOverlap(a, ac, aq, b, bc, bq, .015f)) return true;
        if (IntervalOverlap(ac.y, a.extents.y, bc.y, b.extents.y) <= .015f) return false;
        for (int i = 0; i < 2; i++)
        {
            Vector3 normal = aq * (i == 0 ? Vector3.right : Vector3.forward);
            bool parallel = Mathf.Abs(Vector3.Dot(normal, bq * Vector3.right)) > .999f ||
                Mathf.Abs(Vector3.Dot(normal, bq * Vector3.forward)) > .999f;
            if (!parallel || Mathf.Abs(Mathf.Abs(Vector3.Dot(ac - bc, normal)) - Radius(a, aq, normal) - Radius(b, bq, normal)) > tolerance) continue;
            Vector3 tangent = Vector3.Cross(Vector3.up, normal);
            if (IntervalOverlap(Vector3.Dot(ac, tangent), Radius(a, aq, tangent), Vector3.Dot(bc, tangent), Radius(b, bq, tangent)) > .015f) return true;
        }
        return false;
    }
}
