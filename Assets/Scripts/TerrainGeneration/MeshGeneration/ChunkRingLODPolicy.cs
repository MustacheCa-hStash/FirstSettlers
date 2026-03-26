using UnityEngine;

public static class ChunkRingLODPolicy
{
    public static int GetLOD(ChunkCoord viewer, ChunkCoord target)
    {
        int dx = Mathf.Abs(viewer.x - target.x);
        int dz = Mathf.Abs(viewer.z - target.z);
        int ring = Mathf.Max(dx, dz);

        //return between 3 and 5 (5 is upper bound, 2^5)
        return 3;
        //0123 to 2334
        if (ring <= 1) return 3;
        if (ring <= 3) return 3;
        if (ring <= 5) return 3;
        if (ring <= 7) return 3;
        return 3;
    }
}
