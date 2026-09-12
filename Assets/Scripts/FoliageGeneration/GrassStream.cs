using System;
using System.Collections.Generic;
using UnityEngine;

// Persistent state is the backlog. Scheduling lists are rebuilt from unmet versions, so bounded
// work cannot lose a request. GPU publication and job completion have independent progress budgets.
public sealed class GrassStream : IDisposable
{
#if UNITY_EDITOR
    public static double RefreshMs, CompleteMs, PublishMs, ScheduleMs, DrawMs;
    public static int PendingCount, ActiveCount;
#endif
    public sealed class State
    {
        public int DesiredVersion = 1, DataVersion, DisplayVersion;
        public float WaitingSince, Blend;
        public bool Wanted;
        public bool NeedsData => DataVersion != DesiredVersion;
        public bool NeedsUpload => DataVersion == DesiredVersion && DisplayVersion != DataVersion;
    }
    private sealed class Tile
    {
        public State State = new State();
        public int X, Z, Index, JobVersion;
        public float Distance, TargetBlend;
        public FoliageGenerator.GrassSubChunkGenerationJob Job;
        public List<FoliageInstanceData> Candidates;
        public Entry Owner;
    }
    private sealed class Entry : IDisposable
    {
        public ChunkRecord Record;
        public ChunkRuntime Runtime;
        public object Height, Surface, Ground, Biome;
        public ChunkFoliageData Data;
        public List<FoliageInstanceData>[,] Storage;
        public Matrix4x4 Transform;
        public int Version, GrassRevision;
        public bool Prepared;
        public Tile[] Tiles;
        public ResidentGrassRenderer Renderer;
        public void Dispose() => Renderer.Dispose();
    }
    private readonly Dictionary<ChunkCoord, Entry> entries = new Dictionary<ChunkCoord, Entry>();
    private readonly List<Tile> retiredJobs = new List<Tile>();
    private readonly List<Tile> requests = new List<Tile>();
    private readonly List<ChunkCoord> remove = new List<ChunkCoord>();
    private readonly HashSet<ChunkCoord> touched = new HashSet<ChunkCoord>();
    private readonly GrassSettings settings;
    private readonly CloverSettings clover;
    private readonly TreeSettings trees;
    private readonly int seed, chunkSize;
    private readonly float worldScale, heightMultiplier;
    private readonly Action<ChunkRecord> prepare;
    private readonly Func<bool> useClover;
    private int generationHash, version = 1;
    private int activeJobs;
    private float priorityNow, priorityRadius;
    private readonly Comparison<Tile> compareRequests;
    private double nextRefresh;
    private Vector3 lastViewer;
    private int lastActiveCount = -1;
    public GrassStream(GrassSettings settings, CloverSettings clover, TreeSettings trees, int seed,
        int chunkSize, float worldScale, float heightMultiplier, Action<ChunkRecord> prepare, Func<bool> useClover)
    {
        this.settings = settings; this.clover = clover; this.trees = trees; this.seed = seed;
        this.chunkSize = chunkSize; this.worldScale = worldScale; this.heightMultiplier = heightMultiplier;
        this.prepare = prepare; this.useClover = useClover;
        compareRequests = CompareRequests;
        generationHash = GenerationHash();
    }
    private int GenerationHash()
    {
        unchecked
        {
            int hash = settings.cellsPerAxis;
            hash = hash * 31 + settings.subChunksPerChunk;
            hash = hash * 31 + settings.cellJitter.GetHashCode();
            hash = hash * 31 + settings.uniformScaleRange.GetHashCode();
            hash = hash * 31 + settings.randomizeYaw.GetHashCode();
            hash = hash * 31 + settings.seedOffset;
            if (trees != null)
            {
                hash = hash * 31 + trees.grassExclusionRadius.GetHashCode();
                hash = hash * 31 + trees.bushGrassExclusionRadius.GetHashCode();
                hash = hash * 31 + trees.rockGrassExclusionRadius.GetHashCode();
            }
            if (clover != null) hash = hash * 31 + clover.grassDensityInsidePatch.GetHashCode();
            return hash * 31 + useClover().GetHashCode();
        }
    }
    public void Update(ChunkManager manager, List<ChunkCoord> activeCoords, Vector3 viewer, Camera camera,
        Vector4[] planes, Mesh nearMesh, Material nearMaterial, Mesh farMesh, Material farMaterial)
    {
        long phaseStart = TerrainGenerationProfiler.GetTimestamp();
        int hash = GenerationHash();
        bool settingsChanged = hash != generationHash;
        if (settingsChanged) { generationHash = hash; version++; }
        float now = Time.unscaledTime;
        int axis = Mathf.Max(1, settings.subChunksPerChunk);
        float chunkWorld = Mathf.Max(0.001f, chunkSize * worldScale), subSize = chunkWorld / axis;
        float nearRadius = Mathf.Max(0, settings.activeRingRadius) * chunkWorld;
        float width = Mathf.Max(subSize, settings.transitionWidthChunks * chunkWorld);
        float outer = Mathf.Max(nearRadius + width, Mathf.Max(0, settings.billboardRingRadius) * chunkWorld);
        float prefetch = Mathf.Max(subSize, settings.nearGrassPrecomputeChunkPadding * chunkWorld);
        Vector2 player = new Vector2(viewer.x, viewer.z);
        // Movement is reconciled immediately; stationary terrain arrivals/invalidation are checked
        // at 10 Hz instead of traversing every chunk/subchunk hundreds of times per second.
        bool refresh = settingsChanged || activeCoords.Count != lastActiveCount ||
            (viewer - lastViewer).sqrMagnitude > 0.000001f || Time.realtimeSinceStartupAsDouble >= nextRefresh;
        if (refresh)
        {
            nextRefresh = Time.realtimeSinceStartupAsDouble + 0.1; lastViewer = viewer; lastActiveCount = activeCoords.Count;
            touched.Clear();
            foreach (var coord in activeCoords)
            {
                var record = manager.GetChunkRecord(coord); var runtime = manager.GetChunkRuntime(record);
                if (record == null || runtime == null || runtime.RootTransform == null || record.HeightMap == null || record.SurfaceTypeMap == null || record.BiomeMap == null) continue;
                Vector3 origin = runtime.RootTransform.position;
                float distance = GrassStreamingPolicy.DistanceToSquare(player, new Vector2(origin.x, origin.z), chunkWorld * 0.5f);
                if (distance > outer + prefetch + width) continue;
                touched.Add(coord);
                if (entries.TryGetValue(coord, out var entry) &&
                    (!ReferenceEquals(entry.Record, record) || !ReferenceEquals(entry.Runtime, runtime) ||
                     !ReferenceEquals(entry.Height, record.HeightMap) || !ReferenceEquals(entry.Surface, record.SurfaceTypeMap) ||
                     !ReferenceEquals(entry.Ground, record.GroundCoverMap) || !ReferenceEquals(entry.Biome, record.BiomeMap) ||
                     entry.Tiles.Length != axis * axis || !ReferenceEquals(entry.Data, record.FoliageData) ||
                     !ReferenceEquals(entry.Storage, record.FoliageData?.nearGrassInstancesBySubChunk)))
                { Retire(entry); entries.Remove(coord); entry = null; }
                if (entry == null)
                {
                    if (record.FoliageData == null) record.FoliageData = new ChunkFoliageData();
                    // This streamer exclusively owns grass storage; other foliage retains its own fields.
                    var data = record.FoliageData;
                    bool reuse = data.streamingGrassCacheValid && data.streamingGrassSignature == generationHash &&
                        data.subChunksPerChunk == axis && data.nearGrassInstancesBySubChunk != null &&
                        ReferenceEquals(data.streamingGrassHeight, record.HeightMap) && ReferenceEquals(data.streamingGrassSurface, record.SurfaceTypeMap) &&
                        ReferenceEquals(data.streamingGrassGround, record.GroundCoverMap) && ReferenceEquals(data.streamingGrassBiome, record.BiomeMap);
                    if (!reuse) data.InitializeNearGrass(axis);
                    data.streamingGrassCacheValid = true; data.streamingGrassSignature = generationHash;
                    data.streamingGrassHeight = record.HeightMap; data.streamingGrassSurface = record.SurfaceTypeMap;
                    data.streamingGrassGround = record.GroundCoverMap; data.streamingGrassBiome = record.BiomeMap;
                    entry = new Entry { Record = record, Runtime = runtime, Height = record.HeightMap,
                        Surface = record.SurfaceTypeMap, Ground = record.GroundCoverMap, Biome = record.BiomeMap,
                        Data = record.FoliageData, Storage = record.FoliageData.nearGrassInstancesBySubChunk,
                        Version = version, GrassRevision = data.nearGrassRevision, Transform = runtime.RootTransform.localToWorldMatrix,
                        Tiles = new Tile[axis * axis], Renderer = new ResidentGrassRenderer(axis * axis, Mathf.Max(1, settings.cellsPerAxis), axis) };
                    for (int x = 0; x < axis; x++) for (int z = 0; z < axis; z++)
                        entry.Tiles[x * axis + z] = new Tile { X = x, Z = z, Index = x * axis + z, Owner = entry,
                            State = new State { WaitingSince = now, Blend = 1, DataVersion = data.IsNearGrassSubChunkGenerated(x, z) ? 1 : 0 },
                            Candidates = data.nearGrassInstancesBySubChunk[x, z] };
                    entries.Add(coord, entry);
                }
                if (entry.Version != version)
                {
                    // Keep the displayed arena until each replacement subchunk is published.
                    entry.Version = version; entry.Prepared = false;
                    entry.Data.streamingGrassSignature = generationHash; entry.Data.ClearNearGrass();
                    entry.GrassRevision = entry.Data.nearGrassRevision;
                    foreach (var tile in entry.Tiles) { tile.State.DesiredVersion++; tile.State.WaitingSince = now; }
                }
                if (entry.GrassRevision != entry.Data.nearGrassRevision)
                {
                    entry.GrassRevision = entry.Data.nearGrassRevision; entry.Prepared = false;
                    foreach (var tile in entry.Tiles) { tile.State.DesiredVersion++; tile.State.WaitingSince = now; }
                }
                if (entry.Transform != runtime.RootTransform.localToWorldMatrix)
                {
                    entry.Transform = runtime.RootTransform.localToWorldMatrix;
                    foreach (var tile in entry.Tiles) tile.State.DisplayVersion = 0;
                }
                foreach (var tile in entry.Tiles)
                {
                    if (tile.Job == null && tile.State.DataVersion == tile.State.DesiredVersion &&
                        !entry.Data.IsNearGrassSubChunkGenerated(tile.X, tile.Z))
                    {
                        tile.State.DesiredVersion++; tile.State.WaitingSince = now; entry.Prepared = false;
                    }
                    Vector3 center = entry.Transform.MultiplyPoint3x4(new Vector3((tile.X + 0.5f) * subSize - chunkWorld * 0.5f, 0, (tile.Z + 0.5f) * subSize - chunkWorld * 0.5f));
                    tile.Distance = Vector2.Distance(player, new Vector2(center.x, center.z));
                    tile.State.Wanted = GrassStreamingPolicy.DistanceToSquare(player, new Vector2(center.x, center.z), subSize * 0.5f) <= outer + prefetch;
                    tile.TargetBlend = GrassStreamingPolicy.TargetBlend(tile.Distance, nearRadius, width);
                }
            }
            remove.Clear();
            foreach (var pair in entries) if (!touched.Contains(pair.Key)) remove.Add(pair.Key);
            foreach (var coord in remove) { Retire(entries[coord]); entries.Remove(coord); }
        }
        foreach (var entry in entries.Values) foreach (var tile in entry.Tiles)
        {
            tile.State.Blend = GrassStreamingPolicy.AdvanceBlend(tile.State.Blend, tile.TargetBlend, Time.unscaledDeltaTime,
                settings.representationTransitionSeconds, settings.representationHysteresis);
            entry.Renderer.SetBlend(tile.Index, tile.State.Blend);
        }
#if UNITY_EDITOR
        RefreshMs = TerrainGenerationProfiler.GetElapsedMilliseconds(phaseStart);
#endif
        phaseStart = TerrainGenerationProfiler.GetTimestamp();
        CompleteJobs();
#if UNITY_EDITOR
        CompleteMs = TerrainGenerationProfiler.GetElapsedMilliseconds(phaseStart);
#endif
        phaseStart = TerrainGenerationProfiler.GetTimestamp();
        Publish(nearMesh, farMesh);
#if UNITY_EDITOR
        PublishMs = TerrainGenerationProfiler.GetElapsedMilliseconds(phaseStart);
#endif
        phaseStart = TerrainGenerationProfiler.GetTimestamp();
        Schedule(now, outer);
#if UNITY_EDITOR
        ScheduleMs = TerrainGenerationProfiler.GetElapsedMilliseconds(phaseStart);
        ResidentGrassRenderer.GpuChunks = ResidentGrassRenderer.FallbackChunks = 0;
        PendingCount = requests.Count; ActiveCount = activeJobs;
#endif
        phaseStart = TerrainGenerationProfiler.GetTimestamp();
        foreach (var entry in entries.Values)
            if (entry.Runtime.IsVisible && entry.Runtime.HasTerrainMesh && entry.Runtime.IsFoliageRenderVisible)
                entry.Renderer.Draw(settings, nearMesh, nearMaterial, farMesh, farMaterial, camera, planes, viewer, subSize, outer, width);
#if UNITY_EDITOR
        DrawMs = TerrainGenerationProfiler.GetElapsedMilliseconds(phaseStart);
#endif
    }
    private void CompleteJobs()
    {
        long start = TerrainGenerationProfiler.GetTimestamp(); int completed = 0;
        for (int i = retiredJobs.Count - 1; i >= 0; i--)
            if (retiredJobs[i].Job.IsCompleted) { retiredJobs[i].Job.Dispose(); retiredJobs.RemoveAt(i); activeJobs--; }
        foreach (var entry in entries.Values) foreach (var tile in entry.Tiles)
        {
            if (tile.Job == null || !tile.Job.IsCompleted) continue;
            if (tile.JobVersion == tile.State.DesiredVersion)
            {
                tile.Job.CompleteAndApply();
                tile.Candidates = entry.Data.nearGrassInstancesBySubChunk[tile.X, tile.Z];
                tile.State.DataVersion = tile.JobVersion;
            }
            else tile.Job.Dispose();
            tile.Job = null; activeJobs--; completed++;
            if (completed >= Mathf.Max(1, settings.maxSubChunkGenerationsPerFrame) ||
                TerrainGenerationProfiler.GetElapsedMilliseconds(start) >= Mathf.Max(0.05f, settings.grassCompletionBudgetMs)) return;
        }
    }
    private void Publish(Mesh near, Mesh far)
    {
        requests.Clear();
        foreach (var entry in entries.Values) foreach (var tile in entry.Tiles)
            if (tile.State.NeedsUpload) requests.Add(tile);
        SortRequests(Time.unscaledTime, Mathf.Max(1, settings.billboardRingRadius * chunkSize * worldScale));
        long start = TerrainGenerationProfiler.GetTimestamp(); int count = 0;
        foreach (var tile in requests)
        {
            tile.Owner.Renderer.Upload(tile.Index, tile.Candidates, tile.Owner.Transform, near, far);
            tile.State.DisplayVersion = tile.State.DataVersion;
            if (++count >= Mathf.Max(1, settings.maxGrassUploadsPerFrame) ||
                TerrainGenerationProfiler.GetElapsedMilliseconds(start) >= Mathf.Max(0.05f, settings.grassUploadBudgetMs)) break;
        }
    }
    private void Schedule(float now, float radius)
    {
        requests.Clear();
        foreach (var entry in entries.Values) foreach (var tile in entry.Tiles)
            if (tile.State.Wanted && tile.State.NeedsData && tile.Job == null) requests.Add(tile);
        SortRequests(now, radius);
        long start = TerrainGenerationProfiler.GetTimestamp(); int scheduled = 0;
        foreach (var tile in requests)
        {
            if (activeJobs >= Mathf.Max(1, settings.maxConcurrentGrassJobs)) break;
            var entry = tile.Owner;
            if (!entry.Prepared)
            {
                prepare(entry.Record);
                entry.Prepared = !useClover() || entry.Data.cloverGenerated;
                if (!entry.Prepared)
                {
                    if (TerrainGenerationProfiler.GetElapsedMilliseconds(start) >= Mathf.Max(0.05f, settings.subChunkGenerationBudgetMsPerFrame)) break;
                    continue;
                }
            }
            tile.JobVersion = tile.State.DesiredVersion;
            if (FoliageGenerator.TryScheduleGrassForSubChunk(entry.Record, settings, clover, trees, seed,
                chunkSize, worldScale, heightMultiplier, tile.X, tile.Z, useClover(), out tile.Job)) activeJobs++;
            else
            {
                tile.Candidates = entry.Data.nearGrassInstancesBySubChunk[tile.X, tile.Z];
                tile.State.DataVersion = tile.JobVersion;
            }
            if (++scheduled >= Mathf.Max(1, settings.maxSubChunkGenerationsPerFrame) ||
                TerrainGenerationProfiler.GetElapsedMilliseconds(start) >= Mathf.Max(0.05f, settings.subChunkGenerationBudgetMsPerFrame)) break;
        }
    }
    private void SortRequests(float now, float radius)
    {
        priorityNow = now; priorityRadius = radius;
        requests.Sort(compareRequests);
    }
    private int CompareRequests(Tile a, Tile b) =>
        GrassStreamingPolicy.Priority(a.Distance, priorityNow - a.State.WaitingSince, a.Distance <= priorityRadius, priorityRadius)
        .CompareTo(GrassStreamingPolicy.Priority(b.Distance, priorityNow - b.State.WaitingSince, b.Distance <= priorityRadius, priorityRadius));

    private void Retire(Entry entry)
    {
        foreach (var tile in entry.Tiles) if (tile.Job != null) retiredJobs.Add(tile);
        entry.Dispose();
    }
#if UNITY_EDITOR
    public bool IsSettledForValidation()
    {
        if (activeJobs != 0) return false;
        foreach (var entry in entries.Values) foreach (var tile in entry.Tiles)
            if (tile.State.Wanted && (tile.State.NeedsData || tile.State.NeedsUpload)) return false;
        return true;
    }
#endif
    public void Dispose()
    {
        foreach (var entry in entries.Values) { foreach (var tile in entry.Tiles) tile.Job?.Dispose(); entry.Dispose(); }
        foreach (var tile in retiredJobs) tile.Job.Dispose();
        entries.Clear(); retiredJobs.Clear(); activeJobs = 0;
    }
}
