using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using Object = UnityEngine.Object;

// Terrain-only visibility, independent of URP's camera-relative shadow atlas.
// Main thread owns bindings/textures. A single worker owns the shared height cache.
public sealed class TerrainHorizonShadowSystem : IDisposable
{
    public const int DirectionCount = 8;
    private const float FadeSeconds = 1.5f;
    private static readonly ProfilerMarker WorkerMarker = new("FS.Streaming.Worker.TerrainHorizon");
    private static readonly ProfilerMarker UploadMarker = new("FS.Streaming.ApplyTerrainHorizon");
    private static readonly int Map0Id = Shader.PropertyToID("_TerrainHorizon0");
    private static readonly int Map1Id = Shader.PropertyToID("_TerrainHorizon1");
    private static readonly int UvId = Shader.PropertyToID("_TerrainHorizonUV");
    private static readonly int ParamsId = Shader.PropertyToID("_TerrainHorizonParams");
    private static readonly int TintId = Shader.PropertyToID("_TerrainHorizonTint");

    private readonly Dictionary<int3, Tile> tiles = new();
    private readonly List<Tile> pending = new();
    private readonly List<Tile> fading = new();
    private readonly Dictionary<int2, float> heightCache = new();
    private readonly Queue<int2> heightCacheOrder = new();
    private readonly object resultLock = new();
    private readonly TerrainHeightSamplingContext context;
    private readonly int chunkSize, cacheCapacity, inactiveCapacity, steps;
    private readonly float sampleScale, worldScale, heightMultiplier, searchDistance, heightBias;
    private readonly float strength, softness;
    private readonly float tintStrength;
    private readonly Color shadowTint;
    private readonly bool enabled;
    private volatile bool disposed;
    private volatile bool busy;
    private Result completed;
    private int bindingSerial;
    private bool trimRequested;

    private sealed class Tile
    {
        public int3 Key;
        public int Resolution, Halo, LastUse;
        public float[,] Source;
        public readonly HashSet<Material> Materials = new();
        public Texture2D Map0, Map1;
        public float ReadyTime;
    }

    private sealed class Result
    {
        public Tile Tile;
        public Color32[] Map0, Map1;
        public Exception Error;
    }

    public sealed class Binding : IDisposable
    {
        private Action release;
        internal Binding(Action release) { this.release = release; }
        public void Dispose() { var action = release; release = null; action?.Invoke(); }
    }

    public TerrainHorizonShadowSystem(int chunkSize, int seed, float sampleScale, float worldScale,
        float heightMultiplier, TerrainWaterSettings water, float mountainCoverage,
        WorldErosionSettings erosion, TerrainHorizonShadowSettings settings)
    {
        settings ??= new TerrainHorizonShadowSettings();
        enabled = settings.enabled && settings.strength > 0f;
        this.chunkSize = chunkSize;
        this.sampleScale = sampleScale;
        this.worldScale = worldScale;
        this.heightMultiplier = heightMultiplier;
        context = HeightMapGenerator.CreateSamplingContext(seed, water.WaterLevel, mountainCoverage, erosion);
        searchDistance = Mathf.Max(100f, settings.searchDistance) / worldScale;
        heightBias = Mathf.Max(0f, settings.heightBias) / worldScale;
        steps = Mathf.Clamp(settings.searchSteps, 16, 48);
        cacheCapacity = Mathf.Clamp(settings.cachedHeightSamples, 8192, 262144);
        inactiveCapacity = Mathf.Clamp(settings.cachedInactiveTiles, 0, 512);
        strength = Mathf.Clamp01(settings.strength);
        tintStrength = Mathf.Clamp01(settings.tintStrength);
        // Material Color properties are converted to the project's lighting color space by Unity.
        shadowTint = settings.shadowTint;
        softness = Mathf.Clamp(settings.softnessDegrees, 0.5f, 10f) * Mathf.Deg2Rad;
    }

    // Origins and sizes are in unscaled terrain coordinates, matching HeightMapGenerator.
    public Binding Bind(Material material, int2 origin, int size, float[,] heights, int halo)
    {
        if (material == null || !material.HasProperty(ParamsId)) return null;
        material.SetVector(ParamsId, Vector4.zero);
        if (!enabled || disposed || heights == null ||
            (material.HasProperty("_ReceiveShadows") && material.GetFloat("_ReceiveShadows") <= 0f)) return null;

        var key = new int3(origin.x, origin.y, size);
        if (!tiles.TryGetValue(key, out Tile tile))
        {
            tile = new Tile { Key = key, Resolution = GetResolution(size, chunkSize), Source = heights, Halo = halo };
            tiles.Add(key, tile);
            pending.Add(tile);
        }
        tile.LastUse = ++bindingSerial;
        tile.Materials.Add(material);
        if (tile.Map0 != null) Apply(tile, material, Time.unscaledTime);
        return new Binding(() =>
        {
            tile.Materials.Remove(material);
            tile.LastUse = ++bindingSerial;
            trimRequested = true;
            if (material != null)
            {
                material.SetVector(ParamsId, Vector4.zero);
                material.SetTexture(Map0Id, null);
                material.SetTexture(Map1Id, null);
            }
        });
    }

    public static int GetResolution(int size, int chunkSize) =>
        size <= chunkSize ? 5 : size >= chunkSize * 32 ? 33 : 17;

    public void Update(Vector3 viewerPosition, bool allowWork)
    {
        if (disposed || !enabled) return;
        Result result;
        lock (resultLock) { result = completed; completed = null; }
        if (result != null)
        {
            using var uploadScope = UploadMarker.Auto();
            Tile tile = result.Tile;
            tile.Source = null;
            if (tiles.TryGetValue(tile.Key, out Tile current) && ReferenceEquals(current, tile))
            {
                if (result.Error != null)
                {
                    Debug.LogError($"Terrain horizon generation failed: {result.Error}");
                }
                else
                {
                    tile.Map0 = CreateTexture(tile.Resolution, result.Map0);
                    tile.Map1 = CreateTexture(tile.Resolution, result.Map1);
                    tile.ReadyTime = Time.unscaledTime;
                    fading.Add(tile);
                    foreach (Material material in tile.Materials) Apply(tile, material, Time.unscaledTime);
                }
            }
            // Publication precedes becoming idle; never dispatch over an unconsumed result.
            busy = false;
            TrimTiles();
        }

        float now = Time.unscaledTime;
        if (trimRequested) { TrimTiles(); trimRequested = false; }
        for (int i = fading.Count - 1; i >= 0; i--)
        {
            Tile tile = fading[i];
            foreach (Material material in tile.Materials)
                if (material != null) material.SetVector(ParamsId, Parameters(tile, now));
            if (now - tile.ReadyTime >= FadeSeconds) fading.RemoveAt(i);
        }
        if (busy || !allowWork || pending.Count == 0) return;

        // Nearest bound tile wins. No background work for pooled/unbound renderers.
        int best = -1;
        float bestDistance = float.PositiveInfinity;
        float2 viewer = new float2(viewerPosition.x, viewerPosition.z) / worldScale;
        for (int i = 0; i < pending.Count; i++)
        {
            Tile tile = pending[i];
            if (tile.Materials.Count == 0) continue;
            float2 center = new float2(tile.Key.x, tile.Key.y) + tile.Key.z * 0.5f;
            float distance = math.lengthsq(center - viewer);
            if (distance < bestDistance) { best = i; bestDistance = distance; }
        }
        if (best < 0) { TrimTiles(); return; }
        Tile next = pending[best];
        pending.RemoveAt(best);
        float[,] source = next.Source;
        busy = true;
        if (!ThreadPool.QueueUserWorkItem(_ => Generate(next, source)))
        {
            busy = false;
            pending.Add(next);
        }
    }

    private Vector4 Parameters(Tile tile, float now) =>
        new Vector4(strength * Mathf.Clamp01((now - tile.ReadyTime) / FadeSeconds), softness, tintStrength, 0f);

    private void Apply(Tile tile, Material material, float now)
    {
        if (material == null) return;
        material.SetTexture(Map0Id, tile.Map0);
        material.SetTexture(Map1Id, tile.Map1);
        material.SetColor(TintId, shadowTint);
        // Tile corners represent texel centers, rather than texture edges.
        float scale = (tile.Resolution - 1f) / tile.Resolution / (tile.Key.z * worldScale);
        float halfTexel = 0.5f / tile.Resolution;
        material.SetVector(UvId, new Vector4(scale, scale,
            halfTexel - tile.Key.x * worldScale * scale,
            halfTexel - tile.Key.y * worldScale * scale));
        material.SetVector(ParamsId, Parameters(tile, now));
    }

    private static Texture2D CreateTexture(int resolution, Color32[] pixels)
    {
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
        {
            name = "Terrain Horizon (4 directions)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void TrimTiles()
    {
        // Bound tiles remain alive. Only evict the oldest unbound entry.
        int inactive = 0;
        foreach (Tile tile in tiles.Values) if (tile.Materials.Count == 0) inactive++;
        while (inactive > inactiveCapacity)
        {
            Tile oldest = null;
            foreach (Tile tile in tiles.Values)
                if (tile.Materials.Count == 0 && (oldest == null || tile.LastUse < oldest.LastUse)) oldest = tile;
            tiles.Remove(oldest.Key);
            pending.Remove(oldest);
            fading.Remove(oldest);
            DestroyTextures(oldest);
            oldest.Source = null;
            inactive--;
        }
    }

    private void Generate(Tile tile, float[,] source)
    {
        var result = new Result { Tile = tile };
        try
        {
            using var workerScope = WorkerMarker.Auto();
            if (disposed) return;
            ImportExactSamples(tile, source);
            float spacing = tile.Key.z / (tile.Resolution - 1f);
            float firstDistance = Mathf.Max(1f, chunkSize / 4f);
            float range = Mathf.Max(firstDistance * 2f, searchDistance);
            var distances = new float[steps];
            var latticeSteps = new int[steps];
            int baseStep = Mathf.Max(1, chunkSize / 4);
            for (int step = 0; step < steps; step++)
            {
                distances[step] = firstDistance * math.pow(range / firstDistance, step / (float)(steps - 1));
                // ~8 height cells per search radius, capped at 8 chunks (307 units in SmearScene).
                int ratio = Mathf.Max(1, Mathf.CeilToInt(distances[step] / (baseStep * 8f)));
                latticeSteps[step] = baseStep * Mathf.Min(32, Mathf.NextPowerOfTwo(ratio));
            }
            var directions = new float2[DirectionCount];
            for (int d = 0; d < DirectionCount; d++)
            {
                math.sincos(d * math.PI / 4f, out float sin, out float cos);
                directions[d] = new float2(cos, sin);
            }

            // Gather/deduplicate only cache misses; all procedural sampling is a batched Burst job.
            var missingSet = new HashSet<int2>();
            var missing = new List<int2>();
            for (int z = 0; z < tile.Resolution && !disposed; z++)
                for (int x = 0; x < tile.Resolution; x++)
                {
                    float2 receiver = new float2(tile.Key.x + x * spacing, tile.Key.y + z * spacing);
                    for (int d = 0; d < DirectionCount; d++)
                        for (int s = 0; s < steps; s++)
                        {
                            float2 p = receiver + directions[d] * distances[s];
                            int stride = latticeSteps[s];
                            int2 cell = (int2)math.floor(p / stride) * stride;
                            Gather(cell); Gather(cell + new int2(stride, 0));
                            Gather(cell + new int2(0, stride)); Gather(cell + stride);
                        }
                }
            if (disposed) return;
            SampleMissing(missing);

            result.Map0 = new Color32[tile.Resolution * tile.Resolution];
            result.Map1 = new Color32[result.Map0.Length];
            var angles = new byte[DirectionCount];
            for (int z = 0; z < tile.Resolution && !disposed; z++)
                for (int x = 0; x < tile.Resolution; x++)
                {
                    float2 receiver = new float2(tile.Key.x + x * spacing, tile.Key.y + z * spacing);
                    float receiverHeight = SampleSource(source, tile.Halo, x / (float)(tile.Resolution - 1),
                        z / (float)(tile.Resolution - 1)) * heightMultiplier + heightBias;
                    for (int d = 0; d < DirectionCount; d++)
                    {
                        float maxSlope = 0f;
                        for (int s = 0; s < steps; s++)
                        {
                            float2 p = receiver + directions[d] * distances[s];
                            float blocker = ReadHeight(p, latticeSteps[s]) * heightMultiplier;
                            maxSlope = math.max(maxSlope, (blocker - receiverHeight) / distances[s]);
                        }
                        angles[d] = EncodeSlope(maxSlope);
                    }
                    int index = z * tile.Resolution + x;
                    result.Map0[index] = new Color32(angles[0], angles[1], angles[2], angles[3]);
                    result.Map1[index] = new Color32(angles[4], angles[5], angles[6], angles[7]);
                }

            // Evict after reduction so every gathered height remains available during this request.
            while (heightCache.Count > cacheCapacity)
                heightCache.Remove(heightCacheOrder.Dequeue());

            void Gather(int2 p)
            {
                if (!heightCache.ContainsKey(p) && missingSet.Add(p)) missing.Add(p);
            }
        }
        catch (Exception error) { result.Error = error; }
        finally
        {
            lock (resultLock) if (!disposed) completed = result;
            if (disposed) { heightCache.Clear(); heightCacheOrder.Clear(); }
        }
    }

    private void ImportExactSamples(Tile tile, float[,] source)
    {
        int width = source.GetLength(0) - 2 * tile.Halo;
        int height = source.GetLength(1) - 2 * tile.Halo;
        float dx = tile.Key.z / (float)(width - 1);
        float dz = tile.Key.z / (float)(height - 1);
        int baseStep = Mathf.Max(1, chunkSize / 4);
        int stepX = Mathf.Max(1, Mathf.FloorToInt(baseStep / dx));
        int stepZ = Mathf.Max(1, Mathf.FloorToInt(baseStep / dz));
        for (int x = 0; x < width; x += stepX)
            for (int z = 0; z < height; z += stepZ)
            {
                float px = tile.Key.x + x * dx, pz = tile.Key.y + z * dz;
                int ix = Mathf.RoundToInt(px), iz = Mathf.RoundToInt(pz);
                if (ix % baseStep != 0 || iz % baseStep != 0 ||
                    Mathf.Abs(px - ix) > 0.001f || Mathf.Abs(pz - iz) > 0.001f) continue;
                StoreHeight(new int2(ix, iz), source[x + tile.Halo, z + tile.Halo]);
            }
    }

    private void StoreHeight(int2 p, float height)
    {
        if (heightCache.ContainsKey(p)) return;
        heightCache.Add(p, height);
        heightCacheOrder.Enqueue(p);
    }

    private void SampleMissing(List<int2> missing)
    {
        if (missing.Count == 0) return;
        // Run on the existing shadow worker rather than consuming additional job-worker cores.
        using var positions = new NativeArray<int2>(missing.ToArray(), Allocator.Persistent);
        using var heights = new NativeArray<float>(missing.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        new SampleHeightsJob { Positions = positions, Heights = heights, Context = context, Scale = sampleScale }.Run();
        for (int i = 0; i < missing.Count; i++) StoreHeight(missing[i], heights[i]);
    }

    private float ReadHeight(float2 p, int stride)
    {
        int2 cell = (int2)math.floor(p / stride) * stride;
        float2 f = (p - cell) / stride;
        return math.lerp(math.lerp(heightCache[cell], heightCache[cell + new int2(stride, 0)], f.x),
            math.lerp(heightCache[cell + new int2(0, stride)], heightCache[cell + stride], f.x), f.y);
    }

    public static float SampleSource(float[,] source, int halo, float u, float v)
    {
        float x = halo + math.saturate(u) * (source.GetLength(0) - 2 * halo - 1);
        float z = halo + math.saturate(v) * (source.GetLength(1) - 2 * halo - 1);
        int ix = (int)math.floor(x), iz = (int)math.floor(z);
        int nx = math.min(ix + 1, source.GetLength(0) - 1), nz = math.min(iz + 1, source.GetLength(1) - 1);
        return math.lerp(math.lerp(source[ix, iz], source[nx, iz], x - ix),
            math.lerp(source[ix, nz], source[nx, nz], x - ix), z - iz);
    }

    public static byte EncodeSlope(float slope) =>
        (byte)math.clamp(math.round(math.atan(math.max(0f, slope)) * (255f / (math.PI * 0.5f))), 0f, 255f);

    [BurstCompile]
    private struct SampleHeightsJob : IJob
    {
        [ReadOnly] public NativeArray<int2> Positions;
        [WriteOnly] public NativeArray<float> Heights;
        public TerrainHeightSamplingContext Context;
        public float Scale;
        public void Execute()
        {
            for (int i = 0; i < Positions.Length; i++)
                Heights[i] = HeightMapGenerator.SampleTerrainHeightNative(Positions[i].x, Positions[i].y,
                    Scale, Context.RiverSeed, Context.WaterLevel, Context.MountainHorizontalScale, Context.Erosion).Height;
        }
    }

    private static void DestroyTextures(Tile tile)
    {
        DestroyTexture(tile.Map0);
        DestroyTexture(tile.Map1);
        tile.Map0 = tile.Map1 = null;
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (texture == null) return;
        if (Application.isPlaying) Object.Destroy(texture);
        else Object.DestroyImmediate(texture);
    }

    public void Dispose()
    {
        disposed = true;
        lock (resultLock) completed = null;
        foreach (Tile tile in tiles.Values)
        {
            foreach (Material material in tile.Materials)
                if (material != null) material.SetVector(ParamsId, Vector4.zero);
            DestroyTextures(tile);
        }
        tiles.Clear(); pending.Clear(); fading.Clear();
        // Cache belongs to the worker. It observes cancellation between rows and discards its result.
    }
}
