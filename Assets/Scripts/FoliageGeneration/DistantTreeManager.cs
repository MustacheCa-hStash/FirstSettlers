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
        public int[] GpuSlots;
        public Bounds Bounds;
        public float LoadFade;
        public int LastUsed;
        public readonly float[] CrownArea = new float[16];
        public readonly int[] CellTrees = new int[16];
        public float[] Crowding, EdgeExposure, Density;
        public bool CrowdingDirty = true;
    }
    private sealed class Batch
    {
        public Mesh Mesh, SourceMesh;
        public Material Material, SourceMaterial;
        public readonly Matrix4x4[] Matrices = new Matrix4x4[1023];
        public readonly Vector4[] Tints = new Vector4[1023], Fades = new Vector4[1023];
        public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        public int Count;
        public readonly List<OrderedInstance> Pending = new List<OrderedInstance>();
        public int[] Order = Array.Empty<int>();
        public readonly int[] BandOffsets = new int[64];
        public float MinDepth, MaxDepth;
        public bool SupportsIndirect;
        public DistantTreeGpuBatch Gpu;
    }
    private struct OrderedInstance
    {
        public Matrix4x4 Matrix;
        public Vector4 Tint, Fade;
        public float Depth;
    }
    private readonly TreeSettings settings;
    private readonly WorldErosionSettings erosion;
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
    private readonly Vector4[] gpuPlanes = new Vector4[6];
    private ComputeShader activeCompute;
    private bool gpuEnabled;
    private ChunkCoord lastViewer;
    private int lastRadius = -1;
    private bool disposed;
    public RenderGeometryStats RenderStats { get; private set; }

    public DistantTreeManager(TreeSettings settings, int seed, int chunkSize, float sampleScale,
        int octaves, float persistence, float lacunarity, float worldScale, float heightMultiplier,
        float waterLevel, float mountainScale, WorldFeatureGenerationSettings placementSettings, WorldErosionSettings erosion = default)
    {
        this.erosion = erosion.Sanitized();
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
        if (material.shader.name == "Custom/SpruceBillboardVariationSimpleLitCutout")
            material.EnableKeyword("SPRUCE_FAR_SIMPLE");
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
        var batch = new Batch
        {
            Mesh = mesh,
            SourceMesh = filter.sharedMesh,
            Material = material,
            SourceMaterial = renderer.sharedMaterial,
            SupportsIndirect = material.GetTag("DistantTreeIndirect", false, "False") == "True"
        };
        // Procedural variants are selected by Unity for indirect draws only.
        material.DisableKeyword("DISTANT_TREE_INDIRECT");
        material.DisableKeyword("PROCEDURAL_INSTANCING_ON");
        batches.Add(variant, batch);
        uniqueBatches.Add(batch);
    }

    public void Update(ChunkManager manager, Vector3 viewer, Camera camera, int terrainViewDistance)
    {
        if (disposed) return;
        RenderStats = default;
        ConfigureGpu();
        foreach (var batch in uniqueBatches) batch.Gpu?.BeginFrame();
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

        if (camera != null)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            for (int p = 0; p < 6; p++)
                gpuPlanes[p] = new Vector4(planes[p].normal.x, planes[p].normal.y, planes[p].normal.z, planes[p].distance);
        }
        float duration = Mathf.Max(0.05f, settings.distantTreeTransitionSeconds);
        float fadeStep = Time.unscaledDeltaTime / duration;
        float maxDistance = rangeChunks * chunkWorldSize;
        bool orderByDepth = settings.distantTreeDepthOrdering && camera != null;
        Vector3 cameraPosition = camera != null ? camera.transform.position : viewer;
        Vector3 cameraForward = camera != null ? camera.transform.forward : Vector3.forward;
        float outerWidth = Mathf.Clamp(settings.distantTreeFadeWidthChunks * chunkWorldSize, 1f, maxDistance);
        float protectionChunks = Mathf.Max(0, settings.gameObjectTreeChunkRingRadius) + 1f;
        float thinStart = Mathf.Max(protectionChunks, settings.distantTreeThinningStartChunks) * chunkWorldSize;
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
                            waterLevel, mountainScale, tintSeed, placementSettings, erosion)));
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
                ReleaseGpuSlots(manifest);
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
            if (settings.distantTreeDensityAware && manifest.CrowdingDirty) RefreshCrowding(coord, manifest);
            for (int i = 0; i < manifest.Trees.Length; i++)
            {
                var tree = manifest.Trees[i];
                if (!batches.TryGetValue(tree.variant, out var batch)) continue;
                var matrix = manifest.Matrices[i];
                if (gpuEnabled && batch.Gpu != null)
                {
                    var ecology = new Vector4(manifest.Priority[i], manifest.ProtectionOrder[i],
                        manifest.Crowding[i], manifest.EdgeExposure[i]);
                    if (manifest.GpuSlots[i] < 0)
                        manifest.GpuSlots[i] = batch.Gpu.Register(matrix, (Color)tree.leafTint, ecology, manifest.Density[i]);
                    batch.Gpu.Submit(manifest.GpuSlots[i], manifest.Height[i], manifest.LoadFade, billboardTransition, ecology);
                    continue;
                }
                float dx = matrix.m03 - viewer.x, dz = matrix.m23 - viewer.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                float outer = Mathf.Clamp01((maxDistance - distance) / outerWidth);
                if (outer <= 0f) continue;
                float outerDensity = Mathf.Clamp01(settings.distantTreeDensity);
                if (settings.distantTreeDensityAware)
                {
                    float crowded = Mathf.InverseLerp(settings.distantTreeCrowdingThreshold,
                        settings.distantTreeCrowdingThreshold * 2f + 0.001f, manifest.Crowding[i]);
                    float reduction = crowded * (1f - manifest.EdgeExposure[i] * settings.distantTreeEdgeProtection);
                    outerDensity = Mathf.Lerp(1f, outerDensity, reduction);
                }
                float density = Mathf.Lerp(1f, outerDensity,
                    Mathf.InverseLerp(thinStart, Mathf.Max(thinStart + 1f, maxDistance - outerWidth), distance));
                // Neighbor manifests can arrive later: approach new density without a visibility jump.
                manifest.Density[i] = distance <= thinStart ? 1f : Mathf.MoveTowards(manifest.Density[i], density, fadeStep);
                density = manifest.Density[i];
                // Smooth removal around a stable priority; never move surviving trees.
                float thinning = manifest.ProtectionOrder[i] < Mathf.Max(0, settings.distantTreeProtectedCount)
                    ? 1f : Mathf.Clamp01((density - manifest.Priority[i]) / 0.08f);
                float coverage = Mathf.Min(manifest.LoadFade, Mathf.Min(outer, thinning));
                if (coverage <= 0f) continue;
                matrix.m13 = manifest.Height[i];
                var instance = new OrderedInstance
                {
                    Matrix = matrix,
                    Tint = (Color)tree.leafTint,
                    Fade = new Vector4(coverage, billboardTransition, 0f, 0f)
                };
                if (orderByDepth)
                {
                    instance.Depth = Vector3.Dot(new Vector3(matrix.m03, matrix.m13, matrix.m23) - cameraPosition, cameraForward);
                    if (batch.Pending.Count == 0) batch.MinDepth = batch.MaxDepth = instance.Depth;
                    else
                    {
                        batch.MinDepth = Mathf.Min(batch.MinDepth, instance.Depth);
                        batch.MaxDepth = Mathf.Max(batch.MaxDepth, instance.Depth);
                    }
                    batch.Pending.Add(instance);
                }
                else Append(batch, instance, camera);
            }
        }
        foreach (var batch in uniqueBatches)
        {
            if (orderByDepth) DrawOrdered(batch, camera);
            if (gpuEnabled && batch.Gpu != null)
            {
                batch.Gpu.Draw(settings, camera, viewer, gpuPlanes, maxDistance, outerWidth, thinStart, fadeStep);
                var stats = RenderStats;
                stats.AddMeshInstances(batch.Mesh, batch.Gpu.SubmittedCount);
                RenderStats = stats;
            }
            Flush(batch, camera);
        }
        Evict();
    }

    private Manifest Build(ChunkCoord coord, TreeInstanceData[] trees)
    {
        InvalidateCrowding(coord);
        int count = trees.Length;
        var m = new Manifest { Trees = trees, Matrices = new Matrix4x4[count], Priority = new float[count],
            Height = new float[count], ProtectionOrder = new int[count], GpuSlots = new int[count], LastUsed = Time.frameCount };
        Array.Fill(m.GpuSlots, -1);
        Vector3 origin = new Vector3((coord.x + 0.5f) * chunkWorldSize, 0f, (coord.z + 0.5f) * chunkWorldSize);
        m.Bounds = new Bounds(origin, Vector3.zero);
        m.Crowding = new float[count]; m.EdgeExposure = new float[count]; m.Density = new float[count];
        bool hasBounds = false;
        for (int i = 0; i < count; i++)
        {
            var t = trees[i];
            Vector3 position = origin + t.localPosition;
            m.Matrices[i] = Matrix4x4.TRS(position, t.localRotation, t.localScale);
            m.Height[i] = position.y;
            m.Priority[i] = StablePriority(coord, t);
            m.Density[i] = 1f;
            int cellX = Mathf.Clamp(Mathf.FloorToInt((t.localPosition.x / chunkWorldSize + 0.5f) * 4f), 0, 3);
            int cellZ = Mathf.Clamp(Mathf.FloorToInt((t.localPosition.z / chunkWorldSize + 0.5f) * 4f), 0, 3);
            int cell = cellX + cellZ * 4;
            m.CellTrees[cell]++;
            if (batches.TryGetValue(t.variant, out var batch))
            {
                var b = batch.Mesh.bounds;
                float horizontal = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x), Mathf.Abs(b.min.z), Mathf.Abs(b.max.z)) *
                    Mathf.Max(Mathf.Abs(t.localScale.x), Mathf.Abs(t.localScale.z));
                m.CrownArea[cell] += Mathf.PI * horizontal * horizontal;
                Vector4 envelope = DistantTreeGpuBatch.CalculateBounds(b, m.Matrices[i]);
                Vector3 minimum = position + new Vector3(-envelope.x, envelope.y, -envelope.x);
                Vector3 maximum = position + new Vector3(envelope.x, envelope.z, envelope.x);
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

    private void InvalidateCrowding(ChunkCoord coord)
    {
        for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
                if (cache.TryGetValue(new ChunkCoord(coord.x + x, coord.z + z), out var neighbor))
                    neighbor.CrowdingDirty = true;
    }

    // Four cells per chunk, with a 3x3 cell neighborhood crossing chunk boundaries.
    // Missing neighbors count as open space: streaming never invents a dense stand.
    private void RefreshCrowding(ChunkCoord coord, Manifest manifest)
    {
        float cellArea = chunkWorldSize * chunkWorldSize / 16f;
        for (int i = 0; i < manifest.Trees.Length; i++)
        {
            Vector3 p = manifest.Trees[i].localPosition;
            int cx = Mathf.Clamp(Mathf.FloorToInt((p.x / chunkWorldSize + 0.5f) * 4f), 0, 3);
            int cz = Mathf.Clamp(Mathf.FloorToInt((p.z / chunkWorldSize + 0.5f) * 4f), 0, 3);
            float area = 0f;
            int trees = 0, open = 0;
            for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    int sx = cx + x, sz = cz + z;
                    int ox = sx < 0 ? -1 : sx >= 4 ? 1 : 0;
                    int oz = sz < 0 ? -1 : sz >= 4 ? 1 : 0;
                    Manifest neighbor = manifest;
                    if (ox != 0 || oz != 0)
                        cache.TryGetValue(new ChunkCoord(coord.x + ox, coord.z + oz), out neighbor);
                    int cell = (sx + 4) % 4 + ((sz + 4) % 4) * 4;
                    int n = neighbor != null ? neighbor.CellTrees[cell] : 0;
                    trees += n;
                    area += neighbor != null ? neighbor.CrownArea[cell] : 0f;
                    if ((x != 0 || z != 0) && n == 0) open++;
                }
            manifest.Crowding[i] = trees <= 3 ? 0f : area / (9f * cellArea);
            manifest.EdgeExposure[i] = open / 8f;
        }
        manifest.CrowdingDirty = false;
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

    // Stable counting sort: linear work, reusable scratch, and no per-band draw calls.
    private void DrawOrdered(Batch batch, Camera camera)
    {
        int count = batch.Pending.Count;
        if (count == 0) return;
        int bands = Mathf.Clamp(settings.distantTreeDepthBands, 4, 64);
        Array.Clear(batch.BandOffsets, 0, bands);
        if (batch.Order.Length < count)
            Array.Resize(ref batch.Order, Mathf.NextPowerOfTwo(count));
        float scale = (bands - 1) / Mathf.Max(0.001f, batch.MaxDepth - batch.MinDepth);
        for (int i = 0; i < count; i++)
            batch.BandOffsets[GetDepthBand(batch.Pending[i].Depth, batch.MinDepth, scale, bands)]++;
        int offset = 0;
        for (int band = 0; band < bands; band++)
        {
            int size = batch.BandOffsets[band];
            batch.BandOffsets[band] = offset;
            offset += size;
        }
        for (int i = 0; i < count; i++)
        {
            int band = GetDepthBand(batch.Pending[i].Depth, batch.MinDepth, scale, bands);
            batch.Order[batch.BandOffsets[band]++] = i;
        }
        for (int i = 0; i < count; i++) Append(batch, batch.Pending[batch.Order[i]], camera);
        batch.Pending.Clear();
    }

    private static int GetDepthBand(float depth, float minimum, float scale, int bands)
        => Mathf.Clamp((int)((depth - minimum) * scale), 0, bands - 1);

    private void Append(Batch batch, OrderedInstance instance, Camera camera)
    {
        int index = batch.Count++;
        batch.Matrices[index] = instance.Matrix;
        batch.Tints[index] = instance.Tint;
        batch.Fades[index] = instance.Fade;
        if (batch.Count == 1023) Flush(batch, camera);
    }

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

    private void ConfigureGpu()
    {
        bool requested = settings.distantTreeGpuCompaction;
        var compute = settings.distantTreeCompactShader;
        if (activeCompute == compute && gpuEnabled == requested) return;
        bool enabled = requested && DistantTreeGpuBatch.IsSupported(compute);
        if (activeCompute == compute && gpuEnabled == enabled) return;
        foreach (var batch in uniqueBatches)
        {
            batch.Gpu?.Dispose();
            batch.Gpu = enabled && batch.SupportsIndirect ? new DistantTreeGpuBatch(compute, batch.Mesh, batch.Material) : null;
        }
        foreach (var manifest in cache.Values) Array.Fill(manifest.GpuSlots, -1);
        activeCompute = compute;
        gpuEnabled = enabled;
    }

    private void ReleaseGpuSlots(Manifest manifest)
    {
        for (int i = 0; i < manifest.GpuSlots.Length; i++)
        {
            if (batches.TryGetValue(manifest.Trees[i].variant, out var batch)) batch.Gpu?.Release(manifest.GpuSlots[i]);
            manifest.GpuSlots[i] = -1;
        }
    }

    private void Evict()
    {
        int excess = cache.Count - Mathf.Max(64, settings.distantTreeCacheChunks);
        if (excess <= 0) return;
        evictions.Clear();
        foreach (var pair in cache) if (pair.Value.LastUsed < Time.frameCount - 1) evictions.Add(pair.Key);
        evictions.Sort((a, b) => cache[a].LastUsed.CompareTo(cache[b].LastUsed));
        for (int i = 0; i < Mathf.Min(excess, evictions.Count); i++)
        {
            InvalidateCrowding(evictions[i]);
            ReleaseGpuSlots(cache[evictions[i]]);
            cache.Remove(evictions[i]);
        }
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
            batch.Gpu?.Dispose();
        }
        batches.Clear(); uniqueBatches.Clear();
    }
}
