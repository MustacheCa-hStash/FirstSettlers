using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

// Independent of macro tile ownership: one compact manifest per original logical chunk.
public sealed class DistantTreeManager : IDisposable
{
    private sealed class Manifest
    {
        public TreeInstanceData[] Trees;
        public List<TreeInstanceData> NearSource;
        public Matrix4x4[] Matrices;
        public float[] Priority, Height;
        public int[] ProtectionOrder;
        public Bounds Bounds;
        public float LoadFade;
        public int LastUsed;
    }
    private sealed class Batch
    {
        public Mesh Mesh, SourceMesh;
        public Material Material, SourceMaterial;
        public readonly Matrix4x4[] Matrices = new Matrix4x4[1023];
        public readonly Vector4[] Tints = new Vector4[1023], Fades = new Vector4[1023];
        public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        public int Count;
    }
    private readonly TreeSettings settings;
    private readonly int seed, chunkSize, octaves, tintSeed;
    private readonly float sampleScale, persistence, lacunarity, worldScale, heightMultiplier, waterLevel, mountainScale, chunkWorldSize;
    private readonly WorldFeatureGenerationSettings placementSettings;
    private readonly Dictionary<ChunkCoord, Manifest> cache = new Dictionary<ChunkCoord, Manifest>();
    private readonly Dictionary<ChunkCoord, Task<TreeInstanceData[]>> jobs = new Dictionary<ChunkCoord, Task<TreeInstanceData[]>>();
    private readonly Dictionary<WorldFeatureVariant, Batch> batches = new Dictionary<WorldFeatureVariant, Batch>();
    private readonly List<Batch> uniqueBatches = new List<Batch>();
    private readonly List<ChunkCoord> wanted = new List<ChunkCoord>(), completed = new List<ChunkCoord>();
    private readonly List<ChunkCoord> evictions = new List<ChunkCoord>();
    private readonly Plane[] planes = new Plane[6];
    private ChunkCoord lastViewer;
    private int lastRadius = -1;
    private bool disposed;
    public RenderGeometryStats RenderStats { get; private set; }

    public DistantTreeManager(TreeSettings settings, int seed, int chunkSize, float sampleScale,
        int octaves, float persistence, float lacunarity, float worldScale, float heightMultiplier,
        float waterLevel, float mountainScale, WorldFeatureGenerationSettings placementSettings)
    {
        this.settings = settings; this.seed = seed; this.chunkSize = chunkSize; this.sampleScale = sampleScale;
        this.octaves = octaves; this.persistence = persistence; this.lacunarity = lacunarity;
        this.worldScale = worldScale; this.heightMultiplier = heightMultiplier; this.waterLevel = waterLevel;
        this.mountainScale = mountainScale; this.placementSettings = placementSettings;
        tintSeed = settings.seedOffset;
        chunkWorldSize = chunkSize * worldScale;
        Add(WorldFeatureVariant.MapleTree, settings.mapleTreeBillboardPrefab);
        Add(WorldFeatureVariant.SugarMapleTree, settings.sugarMapleTreeBillboardPrefab);
        Add(WorldFeatureVariant.BirchAspenTree, settings.birchAspenTreeBillboardPrefab);
        Add(WorldFeatureVariant.BeechTree, settings.beechTreeBillboardPrefab);
        Add(WorldFeatureVariant.SpruceTree, settings.spruceTreeBillboardPrefab);
        Add(WorldFeatureVariant.WhitePineTree, settings.whitePineTreeBillboardPrefab);
        Add(WorldFeatureVariant.OakTree, settings.oakTreeBillboardPrefab);
        Add(WorldFeatureVariant.GrasslandMapleTree, settings.grasslandMapleTreeBillboardPrefab, true);
        Add(WorldFeatureVariant.GrasslandBirchAspenTree, settings.grasslandBirchAspenTreeBillboardPrefab, true);
        Add(WorldFeatureVariant.GrasslandWhitePineTree, settings.grasslandWhitePineTreeBillboardPrefab, true);
        Add(WorldFeatureVariant.GrasslandOakTree, settings.grasslandOakTreeBillboardPrefab, true);
        Add(WorldFeatureVariant.GrasslandWillowTree, settings.grasslandWillowTreeBillboardPrefab, true);
    }

    private void Add(WorldFeatureVariant variant, GameObject prefab, bool grassland = false)
    {
        if (prefab == null) prefab = grassland && settings.grasslandTreeBillboardFallbackPrefab != null
            ? settings.grasslandTreeBillboardFallbackPrefab : settings.treeBillboardPrefab;
        if (prefab == null) return;
        var filter = prefab.GetComponentInChildren<MeshFilter>();
        var renderer = prefab.GetComponentInChildren<MeshRenderer>();
        if (filter == null || renderer == null || filter.sharedMesh == null || renderer.sharedMaterial == null) return;
        foreach (var existing in uniqueBatches)
        {
            if (existing.SourceMesh != filter.sharedMesh || existing.SourceMaterial != renderer.sharedMaterial) continue;
            batches.Add(variant, existing);
            return;
        }
        var material = new Material(renderer.sharedMaterial) { enableInstancing = true };
        material.name += " (Distant Trees)";
        material.SetShaderPassEnabled("ShadowCaster", false);
        if (material.HasProperty("_WindStrength")) material.SetFloat("_WindStrength", 0f);
        if (material.HasProperty("_WindFlutterStrength")) material.SetFloat("_WindFlutterStrength", 0f);
        // Unity also culls the instanced draw using mesh bounds before the vertex shader
        // turns its cards. Expand an owned copy so a thin source card cannot pop at screen edges.
        var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
        mesh.name += " (Distant Tree Bounds)";
        var bounds = mesh.bounds;
        float radius = Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x), Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z));
        mesh.bounds = new Bounds(new Vector3(0f, bounds.center.y, 0f), new Vector3(radius * 2f, bounds.size.y, radius * 2f));
        var batch = new Batch { Mesh = mesh, SourceMesh = filter.sharedMesh, Material = material, SourceMaterial = renderer.sharedMaterial };
        batches.Add(variant, batch);
        uniqueBatches.Add(batch);
    }

    public void Update(ChunkManager manager, Vector3 viewer, Camera camera, int terrainViewDistance)
    {
        if (disposed) return;
        RenderStats = default;
        float handoffChunks = Mathf.Max(0, settings.gameObjectTreeChunkRingRadius) + 2f;
        float rangeChunks = Mathf.Max(handoffChunks + Mathf.Max(0.1f, settings.distantTreeFadeWidthChunks), settings.distantTreeDistanceChunks);
        int radius = Mathf.Min(Mathf.Max(1, terrainViewDistance), Mathf.CeilToInt(rangeChunks) + 1);
        var center = new ChunkCoord(Mathf.FloorToInt(viewer.x / chunkWorldSize), Mathf.FloorToInt(viewer.z / chunkWorldSize));
        if (radius != lastRadius || !center.Equals(lastViewer))
        {
            wanted.Clear();
            for (int x = -radius; x <= radius; x++)
                for (int z = -radius; z <= radius; z++)
                    if (x * x + z * z <= (radius + 1) * (radius + 1))
                        wanted.Add(new ChunkCoord(center.x + x, center.z + z));
            wanted.Sort((a, b) => DistanceSquared(a, center).CompareTo(DistanceSquared(b, center)));
            lastRadius = radius; lastViewer = center;
        }

        completed.Clear();
        foreach (var pair in jobs)
        {
            if (!pair.Value.IsCompleted) continue;
            completed.Add(pair.Key);
            if (completed.Count >= Mathf.Clamp(settings.distantTreeResultsPerFrame, 1, 8)) break;
        }
        foreach (var coord in completed)
        {
            var task = jobs[coord]; jobs.Remove(coord);
            if (task.IsFaulted)
            {
                Debug.LogError("Distant tree placement failed: " + task.Exception.GetBaseException());
                // Cache an empty failure result to avoid retrying a broken request every frame.
                cache[coord] = Build(coord, Array.Empty<TreeInstanceData>());
            }
            else if (!cache.ContainsKey(coord) && !task.IsCanceled && DistanceSquared(coord, center) <= (radius + 2) * (radius + 2))
                cache[coord] = Build(coord, task.Result);
        }

        if (camera != null) GeometryUtility.CalculateFrustumPlanes(camera, planes);
        float duration = Mathf.Max(0.05f, settings.distantTreeTransitionSeconds);
        float fadeStep = Time.unscaledDeltaTime / duration;
        float maxDistance = rangeChunks * chunkWorldSize;
        float outerWidth = Mathf.Clamp(settings.distantTreeFadeWidthChunks * chunkWorldSize, 1f, maxDistance);
        float thinStart = Mathf.Max(handoffChunks, settings.distantTreeThinningStartChunks) * chunkWorldSize;
        foreach (var coord in wanted)
        {
            if (!manager.TryGetDistantTreeSurface(coord, out var runtime, out var heights, out var origin, out float surfaceSize)) continue;
            if (!cache.TryGetValue(coord, out var manifest))
            {
                // Reuse the authoritative near list whenever it already exists.
                var nearData = runtime?.ChunkRecord.FoliageData;
                if (nearData != null && nearData.treeCubesGenerated)
                {
                    manifest = Build(coord, nearData.treeCubeInstances.ToArray());
                    manifest.NearSource = nearData.treeCubeInstances;
                    cache.Add(coord, manifest);
                }
                else
                {
                    if (!jobs.ContainsKey(coord) && jobs.Count < Mathf.Clamp(settings.distantTreeWorkerCount, 1, 4))
                    {
                        ChunkCoord requested = coord;
                        jobs.Add(coord, Task.Run(() => DistantTreePlacement.Generate(requested, chunkSize, seed,
                            sampleScale, octaves, persistence, lacunarity, worldScale, heightMultiplier,
                            waterLevel, mountainScale, tintSeed, placementSettings)));
                    }
                    // Keep existing GameObjects fully visible until we can draw their replacement.
                    runtime?.FoliageRuntime?.SetDistantTreeNearFade(0f);
                    continue;
                }
            }
            var source = runtime?.ChunkRecord.FoliageData;
            if (source != null && source.treeCubesGenerated && manifest.NearSource != source.treeCubeInstances)
            {
                float previousFade = manifest.LoadFade;
                manifest = Build(coord, source.treeCubeInstances.ToArray());
                manifest.NearSource = source.treeCubeInstances;
                manifest.LoadFade = previousFade;
                cache[coord] = manifest;
            }
            manifest.LastUsed = Time.frameCount;
            manifest.LoadFade = Mathf.MoveTowards(manifest.LoadFade, 1f, fadeStep);
            var foliage = runtime?.FoliageRuntime;
            if (foliage != null && foliage.TreeGameObjectCount > 0) manifest.LoadFade = 1f;
            bool nearReady = foliage != null && foliage.HasCurrentTreeRepresentation(FoliageRepresentationMode.GameObjectWithCollision);
            float billboardTransition = 1f;
            if (foliage != null && foliage.TreeGameObjectCount > 0)
            {
                billboardTransition = Mathf.MoveTowards(foliage.DistantTreeNearFade, nearReady ? 0f : 1f, fadeStep);
                foliage.SetDistantTreeNearFade(billboardTransition);
                if (!nearReady && billboardTransition >= 1f) foliage.ReleaseTreeGameObjectsToPool();
            }
            if (billboardTransition <= 0f) continue;

            // Bounds include billboard rotation and tree heights. Coarse ground conformity can
            // shift the bases; expand vertically by the largest shift before frustum culling.
            var bounds = manifest.Bounds;
            float largestShift = 0f;
            for (int i = 0; i < manifest.Trees.Length; i++)
            {
                var matrix = manifest.Matrices[i];
                float trueY = manifest.Trees[i].localPosition.y;
                float targetY = heights == null ? trueY : Mathf.Lerp(trueY,
                    SampleSurface(heights, origin, surfaceSize, matrix.m03, matrix.m23) * heightMultiplier * worldScale,
                    Mathf.Clamp01(settings.distantTreeTerrainConform));
                // Start on displayed terrain before the initial load fade exposes the card.
                manifest.Height[i] = manifest.LoadFade <= fadeStep || settings.distantTreeHeightBlendSpeed <= 0f ? targetY : Mathf.MoveTowards(manifest.Height[i], targetY,
                    Mathf.Max(0f, settings.distantTreeHeightBlendSpeed) * Time.unscaledDeltaTime);
                largestShift = Mathf.Max(largestShift, Mathf.Abs(manifest.Height[i] - trueY));
            }
            bounds.Expand(new Vector3(0f, largestShift * 2f, 0f));
            if (camera != null && !GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
            for (int i = 0; i < manifest.Trees.Length; i++)
            {
                var tree = manifest.Trees[i];
                if (!batches.TryGetValue(tree.variant, out var batch)) continue;
                var matrix = manifest.Matrices[i];
                float dx = matrix.m03 - viewer.x, dz = matrix.m23 - viewer.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                float outer = Mathf.Clamp01((maxDistance - distance) / outerWidth);
                if (outer <= 0f) continue;
                float density = Mathf.Lerp(1f, Mathf.Clamp01(settings.distantTreeDensity),
                    Mathf.InverseLerp(thinStart, Mathf.Max(thinStart + 1f, maxDistance - outerWidth), distance));
                // Smooth removal around a stable priority; never move surviving trees.
                float thinning = manifest.ProtectionOrder[i] < Mathf.Max(0, settings.distantTreeProtectedCount)
                    ? 1f : Mathf.Clamp01((density - manifest.Priority[i]) / 0.08f);
                float coverage = Mathf.Min(manifest.LoadFade, Mathf.Min(outer, thinning));
                if (coverage <= 0f) continue;
                matrix.m13 = manifest.Height[i];
                int index = batch.Count++;
                batch.Matrices[index] = matrix;
                batch.Tints[index] = (Color)tree.leafTint;
                batch.Fades[index] = new Vector4(coverage, billboardTransition, 0f, 0f);
                if (batch.Count == 1023) Flush(batch, camera);
            }
        }
        foreach (var batch in uniqueBatches) Flush(batch, camera);
        Evict();
    }

    private Manifest Build(ChunkCoord coord, TreeInstanceData[] trees)
    {
        int count = trees.Length;
        var m = new Manifest { Trees = trees, Matrices = new Matrix4x4[count], Priority = new float[count],
            Height = new float[count], ProtectionOrder = new int[count], LastUsed = Time.frameCount };
        Vector3 origin = new Vector3((coord.x + 0.5f) * chunkWorldSize, 0f, (coord.z + 0.5f) * chunkWorldSize);
        m.Bounds = new Bounds(origin, Vector3.zero);
        bool hasBounds = false;
        for (int i = 0; i < count; i++)
        {
            var t = trees[i];
            Vector3 position = origin + t.localPosition;
            m.Matrices[i] = Matrix4x4.TRS(position, t.localRotation, t.localScale);
            m.Height[i] = position.y;
            m.Priority[i] = StablePriority(coord, t);
            if (batches.TryGetValue(t.variant, out var batch))
            {
                var b = batch.Mesh.bounds;
                float horizontal = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x), Mathf.Abs(b.min.z), Mathf.Abs(b.max.z)) *
                    Mathf.Max(Mathf.Abs(t.localScale.x), Mathf.Abs(t.localScale.z));
                Vector3 minimum = position + new Vector3(-horizontal, b.min.y * t.localScale.y, -horizontal);
                Vector3 maximum = position + new Vector3(horizontal, b.max.y * t.localScale.y, horizontal);
                if (!hasBounds) { m.Bounds = new Bounds((minimum + maximum) * 0.5f, maximum - minimum); hasBounds = true; }
                else { m.Bounds.Encapsulate(minimum); m.Bounds.Encapsulate(maximum); }
            }
        }
        // Farthest-point order protects spatial representatives instead of one corner of a stand.
        var chosen = new bool[count];
        for (int rank = 0; rank < count; rank++)
        {
            int best = -1; float score = -1f;
            for (int i = 0; i < count; i++)
            {
                if (chosen[i]) continue;
                float nearest = rank == 0 ? 1f - m.Priority[i] : float.MaxValue;
                for (int j = 0; j < count; j++)
                    if (chosen[j]) nearest = Mathf.Min(nearest, (trees[i].localPosition - trees[j].localPosition).sqrMagnitude);
                if (nearest > score) { best = i; score = nearest; }
            }
            chosen[best] = true; m.ProtectionOrder[best] = rank;
        }
        return m;
    }

    public static float StablePriority(ChunkCoord coord, TreeInstanceData tree)
    {
        unchecked
        {
            uint h = (uint)coord.x * 73856093u ^ (uint)coord.z * 19349663u;
            h ^= (uint)Mathf.RoundToInt(tree.localPosition.x * 1024f) * 83492791u;
            h ^= (uint)Mathf.RoundToInt(tree.localPosition.z * 1024f) * 2654435761u ^ (uint)tree.variant;
            h ^= h >> 16; h *= 2246822519u; h ^= h >> 13;
            return (h & 0x00ffffffu) / 16777216f * 0.92f;
        }
    }

    public static float SampleSurface(float[,] heights, Vector2 origin, float size, float x, float z)
    {
        int cells = heights.GetLength(0) - 1;
        float gx = Mathf.Clamp01((x - origin.x) / size) * cells, gz = Mathf.Clamp01((z - origin.y) / size) * cells;
        int ix = Mathf.Min(Mathf.FloorToInt(gx), cells - 1), iz = Mathf.Min(Mathf.FloorToInt(gz), cells - 1);
        float tx = gx - ix, tz = gz - iz;
        float a = heights[ix, iz], c = heights[ix + 1, iz + 1];
        return tz >= tx ? a + (heights[ix, iz + 1] - a) * (tz - tx) + (c - a) * tx
            : a + (heights[ix + 1, iz] - a) * (tx - tz) + (c - a) * tz;
    }

    private static int DistanceSquared(ChunkCoord a, ChunkCoord b)
    { int x = a.x - b.x, z = a.z - b.z; return x * x + z * z; }

    private void Flush(Batch batch, Camera camera)
    {
        if (batch.Count == 0) return;
        var stats = RenderStats;
        stats.AddMeshInstances(batch.Mesh, batch.Count);
        RenderStats = stats;
        batch.Properties.SetFloat("_DistantTreeEnabled", 1f);
        batch.Properties.SetFloat("_DistantTreeBillboard", 1f);
        batch.Properties.SetVectorArray("_TreeLeafTint", batch.Tints);
        batch.Properties.SetVectorArray("_DistantTreeFade", batch.Fades);
        Graphics.DrawMeshInstanced(batch.Mesh, 0, batch.Material, batch.Matrices, batch.Count,
            batch.Properties, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
        batch.Count = 0;
    }

    private void Evict()
    {
        int excess = cache.Count - Mathf.Max(64, settings.distantTreeCacheChunks);
        if (excess <= 0) return;
        evictions.Clear();
        foreach (var pair in cache) if (pair.Value.LastUsed < Time.frameCount - 1) evictions.Add(pair.Key);
        evictions.Sort((a, b) => cache[a].LastUsed.CompareTo(cache[b].LastUsed));
        for (int i = 0; i < Mathf.Min(excess, evictions.Count); i++) cache.Remove(evictions[i]);
    }

    public void Dispose()
    {
        disposed = true;
        // Running workers own their scratch memory and finish without touching Unity objects.
        foreach (var job in jobs.Values)
            job.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        jobs.Clear(); cache.Clear();
        foreach (var batch in uniqueBatches)
        {
            UnityEngine.Object.Destroy(batch.Material);
            UnityEngine.Object.Destroy(batch.Mesh);
        }
        batches.Clear(); uniqueBatches.Clear();
    }
}
