using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Keeps outgoing terrain/water attached until all desired replacements have meshes.</summary>
public sealed class TerrainHandoffCoordinator
{
    private readonly TerrainCoveragePolicy policy;
    private readonly Dictionary<ChunkCoord, ChunkRuntime> loadedChunks;
    private readonly Dictionary<FarTerrainPatchKey, FarTerrainTileRuntime> loadedFarTerrainTiles;
    private readonly Action<ChunkCoord, ChunkRuntime> releaseNormal;
    private readonly Action<FarTerrainTileRuntime> releaseFar;
    public TerrainHandoffCoordinator(TerrainCoveragePolicy policy, Dictionary<ChunkCoord, ChunkRuntime> normal,
        Dictionary<FarTerrainPatchKey, FarTerrainTileRuntime> far, Action<ChunkCoord, ChunkRuntime> releaseNormal,
        Action<FarTerrainTileRuntime> releaseFar)
    { this.policy = policy; loadedChunks = normal; loadedFarTerrainTiles = far; this.releaseNormal = releaseNormal; this.releaseFar = releaseFar; }
    public void Reconcile(HashSet<ChunkCoord> desired)
    {
        outgoingNormalChunks.Clear();
        foreach (var entry in loadedChunks) if (!desired.Contains(entry.Key)) outgoingNormalChunks.Add(entry.Key);
    }
    private readonly List<FarTerrainPatchKey> completedTerrainHandoffs = new();
    private readonly List<ChunkCoord> outgoingNormalChunks = new();
    private readonly Dictionary<FarTerrainPatchKey, bool> normalReplacementReadiness = new();
    private readonly HashSet<ChunkCoord> terrainHandoffHiddenCoords = new();
    private static readonly ProfilerMarker TerrainHandoffsMarker = new ProfilerMarker("FS.Streaming.TerrainHandoffs");
    private static readonly ProfilerMarker NormalHandoffsMarker = new ProfilerMarker("FS.Streaming.TerrainHandoffs.NormalCleanup");
    private static readonly ProfilerMarker FarHandoffsMarker = new ProfilerMarker("FS.Streaming.TerrainHandoffs.FarReadinessAndCleanup");
    private static readonly ProfilerMarker ReleaseNormalHandoffMarker = new ProfilerMarker("FS.Streaming.TerrainHandoffs.ReleaseNormalRuntime");
    private static readonly ProfilerMarker ReleaseFarHandoffMarker = new ProfilerMarker("FS.Streaming.TerrainHandoffs.ReleaseFarRuntime");
    private static readonly ProfilerMarker HandoffVisibilityMarker = new ProfilerMarker("FS.Streaming.TerrainHandoffs.ApplyVisibility");

    public void Update(ChunkCoord viewer, HashSet<ChunkCoord> activeLastUpdate, HashSet<FarTerrainPatchKey> activeFarTilesLastUpdate)
    {
        using (TerrainHandoffsMarker.Auto())
        {
        // Resolve the final hidden set before changing renderers, so waiting handoffs
        // do not reveal and re-hide the same terrain and water every frame.
        terrainHandoffHiddenCoords.Clear();
        // Generated data alone is insufficient: budgeted queues must attach it first.
        using (NormalHandoffsMarker.Auto())
        {
        normalReplacementReadiness.Clear();
        int retainedCount = 0;
        for (int i = 0; i < outgoingNormalChunks.Count; i++)
        {
            ChunkCoord coord = outgoingNormalChunks[i];
            if (!loadedChunks.TryGetValue(coord, out var runtime))
                continue;
            if (!policy.TryGetFarTerrainPatch(viewer, coord, out FarTerrainPatchKey patch))
            {
                using (ReleaseNormalHandoffMarker.Auto()) releaseNormal(coord, runtime);
                loadedChunks.Remove(coord);
                continue;
            }
            if (!normalReplacementReadiness.TryGetValue(patch, out bool ready))
            {
                ready = !activeFarTilesLastUpdate.Contains(patch) ||
                    (loadedFarTerrainTiles.TryGetValue(patch, out var replacement) && replacement.HasTerrainMesh);
                normalReplacementReadiness.Add(patch, ready);
            }
            if (!ready)
            {
                outgoingNormalChunks[retainedCount++] = coord;
                continue;
            }
            using (ReleaseNormalHandoffMarker.Auto()) releaseNormal(coord, runtime);
            loadedChunks.Remove(coord);
        }
        if (retainedCount < outgoingNormalChunks.Count)
            outgoingNormalChunks.RemoveRange(retainedCount, outgoingNormalChunks.Count - retainedCount);
        }

        using (FarHandoffsMarker.Auto())
        {
        completedTerrainHandoffs.Clear();
        foreach (var entry in loadedFarTerrainTiles)
        {
            if (activeFarTilesLastUpdate.Contains(entry.Key))
                continue;

            // A moving viewer can replace one far leaf with several finer leaves
            // (or vice versa). Retain the outgoing mesh until every overlapping
            // incoming leaf has attached its mesh, just as normal/far handoffs
            // retain their outgoing representation.
            bool farReplacementReady = true;
            foreach (FarTerrainPatchKey candidate in activeFarTilesLastUpdate)
            {
                if (!TerrainCoveragePolicy.FarTerrainPatchesOverlap(entry.Key, candidate))
                    continue;

                if (!loadedFarTerrainTiles.TryGetValue(candidate, out var replacement) || !replacement.HasTerrainMesh)
                {
                    farReplacementReady = false;
                    break;
                }
            }
            if (!farReplacementReady)
                continue;

            bool ready = true;
            int patchSize = entry.Key.SizeInChunks;
            int originX = entry.Key.Origin.x * patchSize;
            int originZ = entry.Key.Origin.z * patchSize;
            for (int x = 0; x < patchSize && ready; x++)
                for (int z = 0; z < patchSize; z++)
                {
                    ChunkCoord coord = new ChunkCoord(originX + x, originZ + z);
                    if (activeLastUpdate.Contains(coord) &&
                        (!loadedChunks.TryGetValue(coord, out var replacement) || !replacement.HasTerrainMesh))
                    {
                        ready = false;
                        break;
                    }
                }
            if (!ready)
            {
                if (entry.Value.HasTerrainMesh)
                    for (int x = 0; x < patchSize; x++)
                        for (int z = 0; z < patchSize; z++)
                            terrainHandoffHiddenCoords.Add(new ChunkCoord(originX + x, originZ + z));
                continue;
            }
            using (ReleaseFarHandoffMarker.Auto()) releaseFar(entry.Value);
            completedTerrainHandoffs.Add(entry.Key);
        }
        foreach (FarTerrainPatchKey patch in completedTerrainHandoffs)
            loadedFarTerrainTiles.Remove(patch);
        }
        using (HandoffVisibilityMarker.Auto())
        {
            foreach (var entry in loadedChunks)
                entry.Value.SetTerrainHandoffHidden(terrainHandoffHiddenCoords.Contains(entry.Key));
        }
        }
    }
}
