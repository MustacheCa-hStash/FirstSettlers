using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class MeshGenerator
{
    public static MeshData GenerateTerrainMesh(
        ChunkCoord chunkCoord,
        float[,] heightMap,
        BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap,
        WaterState[,] waterStateMap,
        float heightMultiplier,
        int stepIncrement,
        float worldScale,
        float[,] riverMaskMap)
    {
        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;
        int safeStepIncrement = Mathf.Max(1, stepIncrement);
        int estimatedVertexCapacity = EstimateTerrainVertexCapacity(chunkSize, safeStepIncrement);
        List<int2> vertexCoordinates = new List<int2>(estimatedVertexCapacity);
        List<int> triangles = new List<int>(estimatedVertexCapacity * 6);
        int[] vertexIndicesByGridCoordinate = new int[(chunkSize + 1) * (chunkSize + 1)];
        for (int i = 0; i < vertexIndicesByGridCoordinate.Length; i++)
            vertexIndicesByGridCoordinate[i] = -1;

        int gridVerticesPerLine = chunkSize + 1;

        int GetVertexIndex(int x, int z)
        {
            int key = z * gridVerticesPerLine + x;
            int existingIndex = vertexIndicesByGridCoordinate[key];
            if (existingIndex >= 0)
                return existingIndex;

            int newIndex = vertexCoordinates.Count;
            vertexCoordinates.Add(new int2(x, z));
            vertexIndicesByGridCoordinate[key] = newIndex;
            return newIndex;
        }

        void AddTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        int strip = safeStepIncrement;
        int interiorMin = strip;
        int interiorMax = chunkSize - strip;

        for (int z = interiorMin; z < interiorMax; z += safeStepIncrement)
        {
            for (int x = interiorMin; x < interiorMax; x += safeStepIncrement)
            {
                int a = GetVertexIndex(x, z);
                int b = GetVertexIndex(x, z + safeStepIncrement);
                int c = GetVertexIndex(x + safeStepIncrement, z + safeStepIncrement);
                int d = GetVertexIndex(x + safeStepIncrement, z);

                AddTriangle(a, b, c);
                AddTriangle(a, c, d);
            }
        }

        for (int x0 = 0; x0 < chunkSize; x0 += safeStepIncrement)
        {
            int x1 = Mathf.Min(x0 + safeStepIncrement, chunkSize);

            int anchor = GetVertexIndex(x0, 0);

            int prev = GetVertexIndex(x0 + 1, 0);
            for (int x = x0 + 2; x <= x1; x++)
            {
                int next = GetVertexIndex(x, 0);
                AddTriangle(anchor, next, prev);
                prev = next;
            }

            int innerRight = GetVertexIndex(x1, strip);
            int innerLeft = GetVertexIndex(x0, strip);

            AddTriangle(anchor, innerRight, prev);
            AddTriangle(anchor, innerLeft, innerRight);
        }

        for (int x0 = 0; x0 < chunkSize; x0 += safeStepIncrement)
        {
            int x1 = Mathf.Min(x0 + safeStepIncrement, chunkSize);

            int anchor = GetVertexIndex(x0, chunkSize - strip);

            int prev = GetVertexIndex(x0, chunkSize);
            for (int x = x0 + 1; x <= x1; x++)
            {
                int next = GetVertexIndex(x, chunkSize);
                AddTriangle(anchor, prev, next);
                prev = next;
            }

            int innerRight = GetVertexIndex(x1, chunkSize - strip);
            AddTriangle(anchor, prev, innerRight);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += safeStepIncrement)
        {
            int z1 = Mathf.Min(z0 + safeStepIncrement, chunkSize - strip);

            int anchor = GetVertexIndex(0, z0);

            int prev = GetVertexIndex(0, z0 + 1);
            for (int z = z0 + 2; z <= z1; z++)
            {
                int next = GetVertexIndex(0, z);
                AddTriangle(anchor, prev, next);
                prev = next;
            }

            int innerBottom = GetVertexIndex(strip, z1);
            int innerTop = GetVertexIndex(strip, z0);

            AddTriangle(anchor, prev, innerBottom);
            AddTriangle(anchor, innerBottom, innerTop);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += safeStepIncrement)
        {
            int z1 = Mathf.Min(z0 + safeStepIncrement, chunkSize - strip);

            int anchor = GetVertexIndex(chunkSize - strip, z0);

            int prev = GetVertexIndex(chunkSize - strip, z1);

            int first = GetVertexIndex(chunkSize, z1);
            AddTriangle(anchor, prev, first);
            prev = first;

            for (int z = z1 - 1; z >= z0; z--)
            {
                int next = GetVertexIndex(chunkSize, z);
                AddTriangle(anchor, prev, next);
                prev = next;
            }
        }

        return BuildTerrainMeshData(
            heightMap,
            surfaceTypeMap,
            waterStateMap,
            vertexCoordinates,
            triangles,
            chunkSize,
            heightMultiplier,
            worldScale);
    }

    private static int EstimateTerrainVertexCapacity(int chunkSize, int stepIncrement)
    {
        if (stepIncrement <= 1)
            return (chunkSize + 1) * (chunkSize + 1);

        int coarseVerticesPerLine = chunkSize / stepIncrement + 1;
        int coarseInteriorEstimate = coarseVerticesPerLine * coarseVerticesPerLine;
        int stitchedBorderEstimate = (chunkSize + 1) * 4;
        return Mathf.Min((chunkSize + 1) * (chunkSize + 1), coarseInteriorEstimate + stitchedBorderEstimate);
    }

    private static Vector3 CalculateHeightMapNormal(float[,] heightMap, int x, int z, float heightMultiplier)
    {
        float left = heightMap[x - 1, z];
        float right = heightMap[x + 1, z];
        float down = heightMap[x, z - 1];
        float up = heightMap[x, z + 1];

        float dx = (right - left) * heightMultiplier;
        float dz = (up - down) * heightMultiplier;

        return new Vector3(-dx, 2f, -dz).normalized;
    }

    private static MeshData BuildTerrainMeshData(
        float[,] heightMap,
        SurfaceType[,] surfaceTypeMap,
        WaterState[,] waterStateMap,
        List<int2> vertexCoordinates,
        List<int> triangleList,
        int chunkSize,
        float heightMultiplier,
        float worldScale)
    {
        NativeArray<float> heights = default;
        NativeArray<SurfaceType> surfaces = default;
        NativeArray<WaterState> waterStates = default;
        NativeArray<int2> coordinates = default;
        NativeArray<float3> nativeVertices = default;
        NativeArray<float3> nativeNormals = default;
        NativeArray<float2> nativeUvs = default;
        NativeArray<float4> nativeColors = default;

        try
        {
            heights = CopyFloatMapToNative(heightMap, Allocator.TempJob, out _, out int mapHeight);
            surfaces = CopyMapToNative(surfaceTypeMap, Allocator.TempJob, out _, out _);
            waterStates = CopyMapToNative(waterStateMap, Allocator.TempJob, out _, out _);
            coordinates =
                new NativeArray<int2>(vertexCoordinates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < vertexCoordinates.Count; i++)
                coordinates[i] = vertexCoordinates[i];

            nativeVertices =
                new NativeArray<float3>(vertexCoordinates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            nativeNormals =
                new NativeArray<float3>(vertexCoordinates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            nativeUvs =
                new NativeArray<float2>(vertexCoordinates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            nativeColors =
                new NativeArray<float4>(vertexCoordinates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            TerrainVertexBuildJob vertexJob = new TerrainVertexBuildJob
            {
                heightMap = heights,
                surfaceTypeMap = surfaces,
                waterStateMap = waterStates,
                vertexCoordinates = coordinates,
                mapHeight = mapHeight,
                chunkSize = chunkSize,
                topLeftX = chunkSize / -2f,
                bottomLeftZ = chunkSize / -2f,
                heightMultiplier = heightMultiplier,
                worldScale = worldScale,
                vertices = nativeVertices,
                normals = nativeNormals,
                uvs = nativeUvs,
                colors = nativeColors
            };
            JobHandle vertexHandle = vertexJob.Schedule(vertexCoordinates.Count, 64);
            vertexHandle.Complete();

            return CreateMeshData(nativeVertices, nativeNormals, nativeUvs, nativeColors, triangleList);
        }
        finally
        {
            if (heights.IsCreated)
                heights.Dispose();
            if (surfaces.IsCreated)
                surfaces.Dispose();
            if (waterStates.IsCreated)
                waterStates.Dispose();
            if (coordinates.IsCreated)
                coordinates.Dispose();
            if (nativeVertices.IsCreated)
                nativeVertices.Dispose();
            if (nativeNormals.IsCreated)
                nativeNormals.Dispose();
            if (nativeUvs.IsCreated)
                nativeUvs.Dispose();
            if (nativeColors.IsCreated)
                nativeColors.Dispose();
        }
    }

    private static NativeArray<float> CopyFloatMapToNative(float[,] source, Allocator allocator, out int width, out int height)
    {
        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<float> result =
            new NativeArray<float>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                result[rowOffset + z] = source[x, z];
        }

        return result;
    }

    private static NativeArray<T> CopyMapToNative<T>(T[,] source, Allocator allocator, out int width, out int height)
        where T : unmanaged
    {
        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<T> result =
            new NativeArray<T>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                result[rowOffset + z] = source[x, z];
        }

        return result;
    }

    private static MeshData CreateMeshData(
        NativeArray<float3> nativeVertices,
        NativeArray<float3> nativeNormals,
        NativeArray<float2> nativeUvs,
        NativeArray<float4> nativeColors,
        List<int> triangleList)
    {
        Vector3[] vertices = new Vector3[nativeVertices.Length];
        Vector3[] normals = new Vector3[nativeNormals.Length];
        Vector2[] uvs = new Vector2[nativeUvs.Length];
        Color[] colors = new Color[nativeColors.Length];
        int[] triangles = triangleList.ToArray();

        for (int i = 0; i < nativeVertices.Length; i++)
        {
            float3 vertex = nativeVertices[i];
            float3 normal = nativeNormals[i];
            float2 uv = nativeUvs[i];
            float4 color = nativeColors[i];

            vertices[i] = new Vector3(vertex.x, vertex.y, vertex.z);
            normals[i] = new Vector3(normal.x, normal.y, normal.z);
            uvs[i] = new Vector2(uv.x, uv.y);
            colors[i] = new Color(color.x, color.y, color.z, color.w);
        }

        return new MeshData(vertices, normals, uvs, colors, triangles);
    }

    [BurstCompile]
    private struct TerrainVertexBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> heightMap;
        [ReadOnly] public NativeArray<SurfaceType> surfaceTypeMap;
        [ReadOnly] public NativeArray<WaterState> waterStateMap;
        [ReadOnly] public NativeArray<int2> vertexCoordinates;
        public int mapHeight;
        public int chunkSize;
        public float topLeftX;
        public float bottomLeftZ;
        public float heightMultiplier;
        public float worldScale;

        [WriteOnly] public NativeArray<float3> vertices;
        [WriteOnly] public NativeArray<float3> normals;
        [WriteOnly] public NativeArray<float2> uvs;
        [WriteOnly] public NativeArray<float4> colors;

        public void Execute(int index)
        {
            int2 coordinate = vertexCoordinates[index];
            int x = coordinate.x;
            int z = coordinate.y;
            int paddedX = x + 1;
            int paddedZ = z + 1;
            int mapIndex = paddedX * mapHeight + paddedZ;

            vertices[index] = new float3(
                (topLeftX + x) * worldScale,
                heightMap[mapIndex] * heightMultiplier * worldScale,
                (bottomLeftZ + z) * worldScale);
            normals[index] = CalculateHeightMapNormal(heightMap, mapHeight, paddedX, paddedZ, heightMultiplier);
            uvs[index] = new float2(x / (float)chunkSize, z / (float)chunkSize);
            colors[index] = GenerateColor(surfaceTypeMap[mapIndex], waterStateMap[mapIndex]);
        }

        private static float3 CalculateHeightMapNormal(
            NativeArray<float> heightMap,
            int mapHeight,
            int x,
            int z,
            float heightMultiplier)
        {
            float left = heightMap[(x - 1) * mapHeight + z];
            float right = heightMap[(x + 1) * mapHeight + z];
            float down = heightMap[x * mapHeight + z - 1];
            float up = heightMap[x * mapHeight + z + 1];

            float dx = (right - left) * heightMultiplier;
            float dz = (up - down) * heightMultiplier;

            return math.normalize(new float3(-dx, 2f, -dz));
        }

        private static float4 GenerateColor(SurfaceType surfaceType, WaterState waterState)
        {
            float4 baseColor;

            switch (surfaceType)
            {
                case SurfaceType.Sand:
                    baseColor = new float4(0.80f, 0.75f, 0.55f, 1f);
                    break;
                case SurfaceType.Mud:
                    baseColor = new float4(0.42f, 0.32f, 0.22f, 1f);
                    break;
                case SurfaceType.Grass:
                    baseColor = new float4(0.1255f, 0.5451f, 0.1569f, 1f);
                    break;
                case SurfaceType.Rock:
                    baseColor = new float4(0.45f, 0.45f, 0.45f, 1f);
                    break;
                case SurfaceType.Snow:
                    baseColor = new float4(0.92f, 0.94f, 0.98f, 1f);
                    break;
                case SurfaceType.Cliff:
                    baseColor = new float4(0.30f, 0.30f, 0.30f, 1f);
                    break;
                case SurfaceType.Riverbed:
                    baseColor = new float4(0.35f, 0.30f, 0.24f, 1f);
                    break;
                default:
                    baseColor = new float4(1f, 0f, 1f, 1f);
                    break;
            }

            switch (waterState)
            {
                case WaterState.Wet:
                    return math.lerp(baseColor, new float4(0.10f, 0.18f, 0.22f, 1f), 0.25f);
                case WaterState.Shallow:
                    return math.lerp(baseColor, new float4(0.05f, 0.25f, 0.60f, 1f), 0.35f);
                case WaterState.Deep:
                    return math.lerp(baseColor, new float4(0.05f, 0.20f, 0.50f, 1f), 0.55f);
                default:
                    return baseColor;
            }
        }
    }
}

public class MeshData
{
    public Vector3[] vertices;
    public Vector3[] normals;
    public Vector2[] uvs;
    public Color[] colors;

    private readonly List<Vector3> vertexList;
    private readonly List<Vector3> normalList;
    private readonly List<Vector2> uvList;
    private readonly List<Color> colorList;
    private readonly List<int> triangles;

    public int VertexCapacity => vertexList != null ? vertexList.Capacity : vertices.Length;

    public MeshData(int meshWidth, int meshHeight)
    {
        vertices = new Vector3[meshWidth * meshHeight];
        normals = new Vector3[meshWidth * meshHeight];
        uvs = new Vector2[meshWidth * meshHeight];
        colors = new Color[meshWidth * meshHeight];
        triangles = new List<int>((meshWidth - 1) * (meshHeight - 1) * 6);
    }

    public MeshData(int initialVertexCapacity)
    {
        int safeVertexCapacity = Mathf.Max(4, initialVertexCapacity);
        vertexList = new List<Vector3>(safeVertexCapacity);
        normalList = new List<Vector3>(safeVertexCapacity);
        uvList = new List<Vector2>(safeVertexCapacity);
        colorList = new List<Color>(safeVertexCapacity);
        triangles = new List<int>(safeVertexCapacity * 6);
    }

    public MeshData(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, Color[] colors, int[] triangles)
    {
        this.vertices = vertices;
        this.normals = normals;
        this.uvs = uvs;
        this.colors = colors;
        this.triangles = new List<int>(triangles);
    }

    public int AddVertex(Vector3 vertex, Vector3 normal, Vector2 uv, Color color)
    {
        if (vertexList == null)
            throw new System.InvalidOperationException("AddVertex requires dynamic MeshData.");

        int index = vertexList.Count;
        vertexList.Add(vertex);
        normalList.Add(normal);
        uvList.Add(uv);
        colorList.Add(color);
        return index;
    }

    public Vector3 GetVertex(int index)
    {
        if (vertexList != null)
            return vertexList[index];

        return vertices[index];
    }

    public void AddTriangle(int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();

        if (vertexList != null)
        {
            mesh.SetVertices(vertexList);
            mesh.SetNormals(normalList);
            mesh.SetUVs(0, uvList);
            mesh.SetColors(colorList);
        }
        else
        {
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors = colors;
        }

        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
