using System.Collections.Generic;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class WaterMeshGenerator
{
    // Identical topology at every normal terrain LOD. Macro tiles keep the greedy
    // flat path below; their boundary is outside the owner's wave distance limit.
    public const float WaveVertexSpacing = 0.6f;
    public const float MaximumWaveDisplacement = 0.3f;

    public static WaterMeshData GenerateWaveMesh(NativeArray<WaterState> states,
        NativeArray<float> heights, int mapSize, float worldScale, float heightMultiplier, float waterY)
    {
        int size = mapSize - 3;
        if (size <= 0 || !states.IsCreated || !heights.IsCreated ||
            states.Length != mapSize * mapSize || heights.Length != states.Length || !(worldScale > 0))
            throw new ArgumentException("Wave water requires matching padded terrain and water maps.");

        // Integer subdivisions make neighboring chunks share exactly the same edge
        // samples. Larger world scales subdivide the source cells instead.
        int subdivisions = math.max(1, (int)math.ceil(worldScale / WaveVertexSpacing));
        int stride = subdivisions > 1 ? 1 : math.max(1, (int)math.floor(WaveVertexSpacing / worldScale));
        int cells = (size * subdivisions + stride - 1) / stride;
        int width = cells + 1;
        var occupied = new List<int>(64);
        for (int z = 0; z < cells; z++) for (int x = 0; x < cells; x++)
        {
            int x0 = x * stride / subdivisions, z0 = z * stride / subdivisions;
            int x1 = math.min((int)math.ceil((x + 1) * stride / (float)subdivisions), size);
            int z1 = math.min((int)math.ceil((z + 1) * stride / (float)subdivisions), size);
            bool wet = false;
            for (int sx = x0; sx <= x1 && !wet; sx++) for (int sz = z0; sz <= z1; sz++)
            {
                WaterState state = states[(sx + 1) * mapSize + sz + 1];
                if (state == WaterState.Shallow || state == WaterState.Deep) { wet = true; break; }
            }
            if (wet) occupied.Add(z * cells + x);
        }
        // Avoid allocating a full grid for a dry chunk, and reserve shared vertices
        // rather than four copies per wet cell in an open lake.
        if (occupied.Count == 0) return new WaterMeshData(0);
        var mesh = new WaterMeshData(math.min(width * width, occupied.Count * 4), occupied.Count * 6);
        var indices = new int[width * width];
        Array.Fill(indices, -1);
        float heightScale = heightMultiplier * worldScale;

        int Vertex(int x, int z)
        {
            int key = z * width + x;
            if (indices[key] >= 0) return indices[key];
            float sx = math.min(x * stride / (float)subdivisions, size);
            float sz = math.min(z * stride / (float)subdivisions, size);
            int ix = (int)sx + 1, iz = (int)sz + 1;
            // Central gradients at source samples are continuous across the padded
            // chunk border. Their interpolation supplies the shore-envelope normal.
            float Depth(int px, int pz) => waterY - heights[px * mapSize + pz] * heightScale;
            Vector3 Sample(int px, int pz) => new Vector3(Depth(px, pz),
                (Depth(px + 1, pz) - Depth(px - 1, pz)) / (2 * worldScale),
                (Depth(px, pz + 1) - Depth(px, pz - 1)) / (2 * worldScale));
            int nx = math.min(ix + 1, size + 1), nz = math.min(iz + 1, size + 1);
            Vector3 d = subdivisions == 1 ? Sample(ix, iz) :
                Vector3.Lerp(Vector3.Lerp(Sample(ix, iz), Sample(nx, iz), sx - (int)sx),
                    Vector3.Lerp(Sample(ix, nz), Sample(nx, nz), sx - (int)sx), sz - (int)sz);
            indices[key] = mesh.AddWaveVertex(new Vector3((sx - size * .5f) * worldScale, waterY,
                (sz - size * .5f) * worldScale), new Vector2(sx / size, sz / size), new Vector4(d.x, d.y, d.z, 1));
            return indices[key];
        }

        foreach (int cell in occupied)
        {
            int x = cell % cells, z = cell / cells;
            int a = Vertex(x, z), b = Vertex(x + 1, z), c = Vertex(x, z + 1), d = Vertex(x + 1, z + 1);
            mesh.AddIndexedTriangle(a, c, b); mesh.AddIndexedTriangle(b, c, d);
        }
        return mesh;
    }

    public static WaterMeshData GenerateWaterMesh(
        NativeArray<WaterState> waterStateMap, int mapSize, int stepIncrement, float worldScale, float waterY)
    {
        int chunkSize = mapSize - 3;
        if (chunkSize <= 0 || !waterStateMap.IsCreated)
            throw new ArgumentException("Water state map must be a square padded chunk.", nameof(waterStateMap));

        int step = math.clamp(stepIncrement, 1, chunkSize);
        int blocksPerAxis = (chunkSize + step - 1) / step;
        using var blocks = new NativeArray<byte>(blocksPerAxis * blocksPerAxis, Allocator.TempJob);
        using var rectangles = new NativeList<int4>(blocksPerAxis * blocksPerAxis, Allocator.TempJob);

        var coverageJob = new WaterCoverageJob
        {
            states = waterStateMap, blocks = blocks, mapSize = mapSize,
            chunkSize = chunkSize, step = step, blocksPerAxis = blocksPerAxis
        };
        JobHandle coverage = coverageJob.Schedule(blocks.Length, 64);
        var mergeJob = new MergeWaterBlocksJob
        {
            blocks = blocks, rectangles = rectangles, blocksPerAxis = blocksPerAxis
        };
        mergeJob.Schedule(coverage).Complete();

        var mesh = new WaterMeshData(rectangles.Length);
        float origin = chunkSize / -2f;
        foreach (int4 rect in rectangles)
        {
            int x0 = rect.x * step;
            int z0 = rect.y * step;
            int x1 = math.min((rect.x + rect.z) * step, chunkSize);
            int z1 = math.min((rect.y + rect.w) * step, chunkSize);
            mesh.AddCell(
                new Vector3((origin + x0) * worldScale, waterY, (origin + z0) * worldScale),
                new Vector3((origin + x1) * worldScale, waterY, (origin + z0) * worldScale),
                new Vector3((origin + x0) * worldScale, waterY, (origin + z1) * worldScale),
                new Vector3((origin + x1) * worldScale, waterY, (origin + z1) * worldScale),
                new Vector2(x0 / (float)chunkSize, z0 / (float)chunkSize),
                new Vector2(x1 / (float)chunkSize, z0 / (float)chunkSize),
                new Vector2(x0 / (float)chunkSize, z1 / (float)chunkSize),
                new Vector2(x1 / (float)chunkSize, z1 / (float)chunkSize));
        }
        return mesh;
    }
    public static WaterMeshData GenerateWaterMesh(
        WaterState[,] waterStateMap, int stepIncrement, float worldScale, float waterY)
    {
        int mapSize = waterStateMap.GetLength(0);
        int chunkSize = mapSize - 3;
        if (chunkSize <= 0 || waterStateMap.GetLength(1) != mapSize)
            throw new ArgumentException("Water state map must be a square padded chunk.", nameof(waterStateMap));

        int step = math.clamp(stepIncrement, 1, chunkSize);
        int blocksPerAxis = (chunkSize + step - 1) / step;
        using var states = CopyStates(waterStateMap, mapSize);
        using var blocks = new NativeArray<byte>(blocksPerAxis * blocksPerAxis, Allocator.TempJob);
        using var rectangles = new NativeList<int4>(blocksPerAxis * blocksPerAxis, Allocator.TempJob);

        var coverageJob = new WaterCoverageJob
        {
            states = states, blocks = blocks, mapSize = mapSize,
            chunkSize = chunkSize, step = step, blocksPerAxis = blocksPerAxis
        };
        JobHandle coverage = coverageJob.Schedule(blocks.Length, 64);
        var mergeJob = new MergeWaterBlocksJob
        {
            blocks = blocks, rectangles = rectangles, blocksPerAxis = blocksPerAxis
        };
        mergeJob.Schedule(coverage).Complete();

        var mesh = new WaterMeshData(rectangles.Length);
        float origin = chunkSize / -2f;
        foreach (int4 rect in rectangles)
        {
            int x0 = rect.x * step;
            int z0 = rect.y * step;
            int x1 = math.min((rect.x + rect.z) * step, chunkSize);
            int z1 = math.min((rect.y + rect.w) * step, chunkSize);
            mesh.AddCell(
                new Vector3((origin + x0) * worldScale, waterY, (origin + z0) * worldScale),
                new Vector3((origin + x1) * worldScale, waterY, (origin + z0) * worldScale),
                new Vector3((origin + x0) * worldScale, waterY, (origin + z1) * worldScale),
                new Vector3((origin + x1) * worldScale, waterY, (origin + z1) * worldScale),
                new Vector2(x0 / (float)chunkSize, z0 / (float)chunkSize),
                new Vector2(x1 / (float)chunkSize, z0 / (float)chunkSize),
                new Vector2(x0 / (float)chunkSize, z1 / (float)chunkSize),
                new Vector2(x1 / (float)chunkSize, z1 / (float)chunkSize));
        }
        return mesh;
    }

    private static NativeArray<WaterState> CopyStates(WaterState[,] source, int size)
    {
        var states = new NativeArray<WaterState>(size * size, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                states[x * size + z] = source[x, z];
        return states;
    }

    [BurstCompile]
    private struct WaterCoverageJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<WaterState> states;
        [WriteOnly] public NativeArray<byte> blocks;
        public int mapSize;
        public int chunkSize;
        public int step;
        public int blocksPerAxis;

        public void Execute(int index)
        {
            int x0 = (index % blocksPerAxis) * step;
            int z0 = (index / blocksPerAxis) * step;
            int x1 = math.min(x0 + step, chunkSize);
            int z1 = math.min(z0 + step, chunkSize);
            // All sample corners participate, preserving the existing under-bank coverage at each LOD.
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    WaterState state = states[(x + 1) * mapSize + z + 1];
                    if (state == WaterState.Shallow || state == WaterState.Deep)
                    {
                        blocks[index] = 1;
                        return;
                    }
                }
            }
            blocks[index] = 0;
        }
    }

    [BurstCompile]
    private struct MergeWaterBlocksJob : IJob
    {
        public NativeArray<byte> blocks;
        public NativeList<int4> rectangles;
        public int blocksPerAxis;

        public void Execute()
        {
            for (int z = 0; z < blocksPerAxis; z++)
            {
                for (int x = 0; x < blocksPerAxis; x++)
                {
                    if (blocks[z * blocksPerAxis + x] == 0)
                        continue;
                    int width = 1;
                    while (x + width < blocksPerAxis && blocks[z * blocksPerAxis + x + width] != 0)
                        width++;
                    int height = 1;
                    while (z + height < blocksPerAxis)
                    {
                        bool fullRow = true;
                        for (int dx = 0; dx < width; dx++)
                        {
                            if (blocks[(z + height) * blocksPerAxis + x + dx] == 0)
                            {
                                fullRow = false;
                                break;
                            }
                        }
                        if (!fullRow)
                            break;
                        height++;
                    }
                    rectangles.Add(new int4(x, z, width, height));
                    for (int dz = 0; dz < height; dz++)
                        for (int dx = 0; dx < width; dx++)
                            blocks[(z + dz) * blocksPerAxis + x + dx] = 0;
                }
            }
        }
    }
}

public class WaterMeshData
{
    private readonly List<Vector3> vertices;
    private readonly List<Vector2> uvs;
    private readonly List<int> triangles;
    private readonly List<Color> colors;
    private readonly List<Vector4> waveData;
    private bool hasWaves;

    private static readonly Color WaterColor = new Color(0.05f, 0.25f, 0.60f, 1f);

    public int VertexCount => vertices.Count;

    public WaterMeshData(int initialCellCount) : this(Mathf.Max(4, initialCellCount * 4), Mathf.Max(6, initialCellCount * 6)) { }

    public WaterMeshData(int initialVertexCapacity, int initialTriangleCapacity)
    {
        vertices = new List<Vector3>(initialVertexCapacity);
        uvs = new List<Vector2>(initialVertexCapacity);
        triangles = new List<int>(initialTriangleCapacity);
        colors = new List<Color>(initialVertexCapacity);
        waveData = new List<Vector4>(initialVertexCapacity);
    }

    public WaterMeshData(Vector3[] vertices, Vector2[] uvs, int[] triangles)
    {
        this.vertices = new List<Vector3>(vertices);
        this.uvs = new List<Vector2>(uvs);
        this.triangles = new List<int>(triangles);
        colors = new List<Color>(vertices.Length);
        waveData = new List<Vector4>(vertices.Length);

        for (int i = 0; i < vertices.Length; i++)
        { colors.Add(WaterColor); waveData.Add(Vector4.zero); }
    }

    public int AddWaveVertex(Vector3 position, Vector2 uv, Vector4 depthGradient)
    {
        int index = vertices.Count;
        vertices.Add(position); uvs.Add(uv); colors.Add(WaterColor); waveData.Add(depthGradient);
        hasWaves = true;
        return index;
    }

    public void AddIndexedTriangle(int a, int b, int c)
    { triangles.Add(a); triangles.Add(b); triangles.Add(c); }

    public void AddCell(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD)
    {
        int baseIndex = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        for (int i = 0; i < 4; i++) waveData.Add(Vector4.zero);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        uvs.Add(uvD);

        colors.Add(WaterColor);
        colors.Add(WaterColor);
        colors.Add(WaterColor);
        colors.Add(WaterColor);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 1);

        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 3);
    }

    public void AddTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC)
    {
        int baseIndex = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);

        for (int i = 0; i < 3; i++) waveData.Add(Vector4.zero);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);

        colors.Add(WaterColor);
        colors.Add(WaterColor);
        colors.Add(WaterColor);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();
        try
        {
            mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.colors = colors.ToArray();
            mesh.SetUVs(1, waveData);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (hasWaves)
            {
                Bounds bounds = mesh.bounds;
                bounds.Expand(Vector3.one * (2 * WaterMeshGenerator.MaximumWaveDisplacement));
                mesh.bounds = bounds;
            }
            return mesh;

        }
        catch { TerrainAssetOwner.Destroy(mesh); throw; }
    }
}
