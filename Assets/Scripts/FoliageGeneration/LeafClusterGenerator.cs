using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public struct LeafClusterInstance
{
    public Vector3 position;
    public Quaternion rotation;
    public float scale;
    public Vector4 tint;
    public uint rank;
}

// Small managed slices: no full-map copies, job waits, or per-instance GameObjects.
public sealed class LeafClusterGeneration
{
    public readonly List<LeafClusterInstance> Instances = new List<LeafClusterInstance>();
    public bool Complete { get; private set; }
    public int VisitedCells { get; private set; }
    private readonly ChunkRecord record;
    private readonly float[,] heights, slopes, rivers;
    private readonly BiomeType[,] biomes;
    private readonly SurfaceType[,] surfaces;
    private readonly WorldFeaturePlan plan;
    private readonly float4[,] floor;
    private readonly LeafClusterSettings settings;
    private readonly int signature, seed, size, minX, minZ, countX, countZ;
    private readonly float scale, heightMultiplier, spacing;
    private readonly float footprint;
    private readonly Dictionary<Vector2Int, List<Blocker>> blockers = new Dictionary<Vector2Int, List<Blocker>>();
    private const float BlockerCellSize = 4f;
    private struct Blocker { public Vector2 position; public float radius; }

    public LeafClusterGeneration(ChunkRecord record, LeafClusterSettings settings,
        int seed, int chunkSize, float worldScale, float heightMultiplier, float meshRadius = 0.32f)
    {
        this.record = record; this.settings = settings; this.seed = seed;
        signature = settings.PlacementSignature;
        size = chunkSize; scale = Mathf.Max(0.001f, worldScale); this.heightMultiplier = heightMultiplier;
        heights = record.HeightMap; slopes = record.SlopeMap; rivers = record.RiverMaskMap;
        biomes = record.BiomeMap; surfaces = record.SurfaceTypeMap; plan = record.WorldFeaturePlan;
        floor = plan?.ForestStructure.FloorEcologyMap;
        spacing = Mathf.Max(0.75f, settings.cellSize);
        footprint = meshRadius * Mathf.Max(0.1f, Mathf.Max(settings.scaleRange.x, settings.scaleRange.y));
        minX = Mathf.FloorToInt(record.ChunkCoord.x * size * scale / spacing);
        minZ = Mathf.FloorToInt(record.ChunkCoord.z * size * scale / spacing);
        countX = Mathf.CeilToInt((record.ChunkCoord.x + 1) * size * scale / spacing) - minX;
        countZ = Mathf.CeilToInt((record.ChunkCoord.z + 1) * size * scale / spacing) - minZ;
        Complete = heights == null || slopes == null || biomes == null || surfaces == null ||
            floor == null || countX <= 0 || countZ <= 0;
        if (!Complete && plan != null)
            foreach (WorldFeaturePlacement feature in plan.Placements)
            {
                // Litter can approach trunks; bushes and boulders need their whole footprint clear.
                float radius = feature.featureType == WorldFeatureType.Tree
                    ? Mathf.Max(0.25f, feature.exclusionRadius * scale * 0.18f)
                    : feature.exclusionRadius * scale;
                if (feature.featureType == WorldFeatureType.None || radius <= 0) continue;
                radius += footprint;
                var blocker = new Blocker { position = new Vector2(feature.sampleX, feature.sampleZ) * scale, radius = radius };
                int x0 = Mathf.FloorToInt((blocker.position.x - radius) / BlockerCellSize);
                int z0 = Mathf.FloorToInt((blocker.position.y - radius) / BlockerCellSize);
                int x1 = Mathf.FloorToInt((blocker.position.x + radius) / BlockerCellSize);
                int z1 = Mathf.FloorToInt((blocker.position.y + radius) / BlockerCellSize);
                for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!blockers.TryGetValue(key, out var bucket)) blockers.Add(key, bucket = new List<Blocker>());
                    bucket.Add(blocker);
                }
            }
    }

    public bool Matches(ChunkRecord current, LeafClusterSettings options) => ReferenceEquals(current, record) &&
        signature == options.PlacementSignature && ReferenceEquals(heights, current.HeightMap) &&
        ReferenceEquals(slopes, current.SlopeMap) && ReferenceEquals(biomes, current.BiomeMap) &&
        ReferenceEquals(surfaces, current.SurfaceTypeMap) && ReferenceEquals(rivers, current.RiverMaskMap) &&
        ReferenceEquals(plan, current.WorldFeaturePlan) && ReferenceEquals(floor, current.WorldFeaturePlan?.ForestStructure.FloorEcologyMap);

    public void Step(int budget)
    {
        for (int i = 0; i < Mathf.Max(1, budget) && !Complete; i++)
        {
            int cell = VisitedCells++;
            int cx = minX + cell % countX, cz = minZ + cell / countX;
            uint rank = math.hash(new int4(seed, settings.seedOffset, cx, cz));
            Vector2 world = new Vector2(cx + 0.18f + Unit(rank, 17) * 0.64f,
                cz + 0.18f + Unit(rank, 31) * 0.64f) * spacing;
            Vector2 sample = world / scale - new Vector2(record.ChunkCoord.x, record.ChunkCoord.z) * size;
            // Half-open ownership gives neighboring chunks identical candidates with no duplicates.
            if (sample.x >= 0 && sample.y >= 0 && sample.x < size && sample.y < size)
                TryAdd(sample, world, rank);
            Complete = VisitedCells >= countX * countZ;
        }
    }

    private void TryAdd(Vector2 sample, Vector2 world, uint rank)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(sample.x) + 1, 0, biomes.GetLength(0) - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(sample.y) + 1, 0, biomes.GetLength(1) - 1);
        if (biomes[x, z] != BiomeType.Forest || surfaces[x, z] != SurfaceType.Grass) return;
        float slope = Sample(slopes, sample);
        if (slope >= Mathf.Max(0.01f, settings.maxSlope)) return;
        Vector3 normal = TerrainNormal(sample);
        if (Vector3.Angle(normal, Vector3.up) >= Mathf.Max(0.01f, settings.maxSlope)) return;
        float river = rivers == null ? 0 : Sample(rivers, sample);
        float4 ecology = SampleFloor(sample);
        float colony = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0, 1,
            (noise.snoise(new float2(world.x, world.y) * 0.12f + seed * 0.0013f) + 1) * 0.5f));
        float keep = Mathf.Clamp01(settings.density) * colony * (1 - ecology.x * 0.75f) *
            (1 - ecology.y * 0.5f) * ForestFloorPolicy.LeafRetention(ecology.z) *
            (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(settings.maxSlope * 0.4f, settings.maxSlope, slope))) *
            (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.15f, 0.5f, river)));
        if (Unit(rank, 59) >= keep || Blocked(sample * scale)) return;
        float y = SampleTerrain(heights, sample) * heightMultiplier * scale;
        float s = Mathf.Lerp(Mathf.Max(0.1f, Mathf.Min(settings.scaleRange.x, settings.scaleRange.y)),
            Mathf.Max(0.1f, Mathf.Max(settings.scaleRange.x, settings.scaleRange.y)), Unit(rank, 83));
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Unit(rank, 101) * 360, 0);
        float tone = Mathf.Lerp(0.77f, 0.98f, Unit(rank, 127));
        Instances.Add(new LeafClusterInstance {
            position = new Vector3(world.x, y, world.y) + normal * 0.006f,
            rotation = rotation, scale = s, rank = rank,
            tint = new Vector4(tone, tone * Mathf.Lerp(0.95f, 1.02f, Unit(rank, 151)), tone * 0.94f, 1)
        });
    }

    private bool Blocked(Vector2 point)
    {
        if (!blockers.TryGetValue(new Vector2Int(Mathf.FloorToInt(point.x / BlockerCellSize),
            Mathf.FloorToInt(point.y / BlockerCellSize)), out var bucket)) return false;
        foreach (Blocker blocker in bucket)
            if ((point - blocker.position).sqrMagnitude < blocker.radius * blocker.radius) return true;
        return false;
    }

    public static float Unit(uint rank, uint salt) => GrassStreamingPolicy.UnitRank(math.hash(new uint2(rank, salt)));
    public static float Sample(float[,] map, Vector2 point)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(point.x) + 1, 0, map.GetLength(0) - 2);
        int z = Mathf.Clamp(Mathf.FloorToInt(point.y) + 1, 0, map.GetLength(1) - 2);
        float tx = point.x - Mathf.Floor(point.x), tz = point.y - Mathf.Floor(point.y);
        return Mathf.Lerp(Mathf.Lerp(map[x, z], map[x + 1, z], tx),
            Mathf.Lerp(map[x, z + 1], map[x + 1, z + 1], tx), tz);
    }
    public static float SampleTerrain(float[,] map, Vector2 point)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(point.x) + 1, 0, map.GetLength(0) - 2);
        int z = Mathf.Clamp(Mathf.FloorToInt(point.y) + 1, 0, map.GetLength(1) - 2);
        float tx = point.x - Mathf.Floor(point.x), tz = point.y - Mathf.Floor(point.y);
        // MeshGenerator splits each cell from (0,0) to (1,1).
        return tz >= tx ? map[x, z] + (map[x + 1, z + 1] - map[x, z + 1]) * tx + (map[x, z + 1] - map[x, z]) * tz
            : map[x, z] + (map[x + 1, z] - map[x, z]) * tx + (map[x + 1, z + 1] - map[x + 1, z]) * tz;
    }
    private Vector3 TerrainNormal(Vector2 point)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(point.x) + 1, 0, heights.GetLength(0) - 2);
        int z = Mathf.Clamp(Mathf.FloorToInt(point.y) + 1, 0, heights.GetLength(1) - 2);
        bool upper = point.y - Mathf.Floor(point.y) >= point.x - Mathf.Floor(point.x);
        float dx = upper ? heights[x + 1, z + 1] - heights[x, z + 1] : heights[x + 1, z] - heights[x, z];
        float dz = upper ? heights[x, z + 1] - heights[x, z] : heights[x + 1, z + 1] - heights[x + 1, z];
        return new Vector3(-dx * heightMultiplier, 1, -dz * heightMultiplier).normalized;
    }
    private float4 SampleFloor(Vector2 point)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(point.x) + 1, 0, floor.GetLength(0) - 2);
        int z = Mathf.Clamp(Mathf.FloorToInt(point.y) + 1, 0, floor.GetLength(1) - 2);
        float tx = point.x - Mathf.Floor(point.x), tz = point.y - Mathf.Floor(point.y);
        return math.lerp(math.lerp(floor[x, z], floor[x + 1, z], tx), math.lerp(floor[x, z + 1], floor[x + 1, z + 1], tx), tz);
    }
}
