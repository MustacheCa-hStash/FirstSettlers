using UnityEngine;

/// <summary>World-aligned far patch policy; contains no runtimes, rendering or request queues.</summary>
public sealed class TerrainCoveragePolicy
{
    public const int FarTerrainMaxPatchSizeInChunks = 32;
    private readonly bool enableFarTerrain;
    private readonly int farTerrainStartRing, farTerrainMacroTileSize;
    public TerrainCoveragePolicy(TerrainCoverageConfiguration configuration)
    {
        enableFarTerrain = configuration.EnableFarTerrain;
        farTerrainStartRing = Mathf.Max(1, configuration.FarTerrainStartRing);
        farTerrainMacroTileSize = Mathf.Max(1, configuration.FarTerrainMacroTileSize);
    }
    public bool ShouldUseFarTerrain(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!enableFarTerrain)
            return false;

        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        return ring >= farTerrainStartRing;
    }

    public bool TryGetFarTerrainPatch(
        ChunkCoord viewerCoord,
        ChunkCoord targetCoord,
        out FarTerrainPatchKey patch)
    {
        patch = default;
        if (!ShouldUseFarTerrain(viewerCoord, targetCoord))
            return false;

        // Try the largest valid leaf first. A leaf may only begin once its whole
        // footprint is outside the preceding LOD band; otherwise descend to a
        // smaller leaf. This leaves a narrow normal-chunk strip at the seam when
        // a world-aligned leaf would cross it.
        int desiredSize = GetDesiredFarPatchSize(GetChunkRingDistance(viewerCoord, targetCoord));
        for (int size = desiredSize; size >= farTerrainMacroTileSize; size >>= 1)
        {
            ChunkCoord origin = new ChunkCoord(FloorDiv(targetCoord.x, size),
                FloorDiv(targetCoord.z, size));
            int minRing = GetPatchMinimumRingDistance(viewerCoord, origin, size);
            if (minRing >= GetFarPatchStartRing(size))
            {
                patch = new FarTerrainPatchKey(origin, size);
                return true;
            }
        }

        return false;
    }

    private int GetDesiredFarPatchSize(int ring)
    {
        int size = farTerrainMacroTileSize;
        while (size < FarTerrainMaxPatchSizeInChunks && ring >= GetFarPatchStartRing(size << 1))
            size <<= 1;
        return size;
    }

    private int GetFarPatchStartRing(int size)
    {
        int start = farTerrainStartRing;
        int current = farTerrainMacroTileSize;
        while (current < size)
        {
            start += current * 2;
            current <<= 1;
        }
        return start;
    }

    private static int GetPatchMinimumRingDistance(ChunkCoord viewer, ChunkCoord origin, int size)
    {
        int minX = origin.x * size;
        int minZ = origin.z * size;
        int maxX = minX + size - 1;
        int maxZ = minZ + size - 1;
        int closestX = Mathf.Clamp(viewer.x, minX, maxX);
        int closestZ = Mathf.Clamp(viewer.z, minZ, maxZ);
        return Mathf.Max(Mathf.Abs(closestX - viewer.x), Mathf.Abs(closestZ - viewer.z));
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;

        if (remainder != 0 && ((remainder > 0) != (divisor > 0)))
            quotient--;

        return quotient;
    }

    public static int GetChunkRingDistance(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return Mathf.Max(dx, dz);
    }

    public static bool FarTerrainPatchesOverlap(FarTerrainPatchKey a, FarTerrainPatchKey b)
    {
        int aMinX = a.Origin.x * a.SizeInChunks;
        int aMinZ = a.Origin.z * a.SizeInChunks;
        int bMinX = b.Origin.x * b.SizeInChunks;
        int bMinZ = b.Origin.z * b.SizeInChunks;
        return aMinX < bMinX + b.SizeInChunks && bMinX < aMinX + a.SizeInChunks &&
               aMinZ < bMinZ + b.SizeInChunks && bMinZ < aMinZ + a.SizeInChunks;
    }
}
