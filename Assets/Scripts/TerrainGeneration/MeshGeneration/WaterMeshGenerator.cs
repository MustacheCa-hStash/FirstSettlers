using System.Collections.Generic;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class WaterMeshGenerator
{
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

    private static readonly Color WaterColor = new Color(0.05f, 0.25f, 0.60f, 1f);

    public int VertexCount => vertices.Count;

    public WaterMeshData(int initialCellCount)
    {
        int initialVertexCapacity = Mathf.Max(4, initialCellCount * 4);
        int initialTriangleCapacity = Mathf.Max(6, initialCellCount * 6);

        vertices = new List<Vector3>(initialVertexCapacity);
        uvs = new List<Vector2>(initialVertexCapacity);
        triangles = new List<int>(initialTriangleCapacity);
        colors = new List<Color>(initialVertexCapacity);
    }

    public WaterMeshData(Vector3[] vertices, Vector2[] uvs, int[] triangles)
    {
        this.vertices = new List<Vector3>(vertices);
        this.uvs = new List<Vector2>(uvs);
        this.triangles = new List<int>(triangles);
        colors = new List<Color>(vertices.Length);

        for (int i = 0; i < vertices.Length; i++)
            colors.Add(WaterColor);
    }

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
        mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.colors = colors.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
