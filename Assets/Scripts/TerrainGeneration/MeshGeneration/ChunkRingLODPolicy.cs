using UnityEngine;

public static class ChunkRingLODPolicy
{
    public static int GetLOD(ChunkCoord viewer, ChunkCoord target)
    {
        int dx = Mathf.Abs(viewer.x - target.x);
        int dz = Mathf.Abs(viewer.z - target.z);
        int ring = Mathf.Max(dx, dz);

        //0123 to 2334
        if (ring <= 1) return 2;
        if (ring <= 3) return 3;
        if (ring <= 5) return 3;
        if (ring <= 7) return 4;
        return 4;
    }
}
