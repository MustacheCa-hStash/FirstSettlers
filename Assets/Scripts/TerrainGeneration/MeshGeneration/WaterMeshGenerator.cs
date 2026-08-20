using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class LakeMeshGenerator
{
    private const float LakeWaterLevel = TerrainWaterSettings.WaterLevel;
    private const float WaterSurfaceOffset = 0.02f;

    public static WaterMeshData GenerateLakeMesh(
        float[,] heightMap,
        WaterState[,] waterStateMap,
        float[,] riverMaskMap,
        float heightMultiplier,
        int stepIncrement,
        float worldScale)
    {
        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;
        int safeStepIncrement = Mathf.Max(1, stepIncrement);

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;
        float waterY = LakeWaterLevel * heightMultiplier * worldScale + WaterSurfaceOffset;
        int blockCountPerAxis = WaterMeshJobUtility.GetBlockCountPerAxis(chunkSize, safeStepIncrement);
        int blockCount = blockCountPerAxis * blockCountPerAxis;

        if (blockCount == 0)
            return new WaterMeshData(0);

        NativeArray<WaterState> waterStates = default;
        NativeArray<byte> renderableBlocks = default;
        NativeArray<int> compactBlockIndices = default;
        NativeArray<float3> vertices = default;
        NativeArray<float2> uvs = default;
        NativeArray<int> triangles = default;

        try
        {
            waterStates = WaterMeshJobUtility.CopyMapToNative(waterStateMap, Allocator.TempJob, out _, out int mapHeight);
            renderableBlocks = new NativeArray<byte>(blockCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            LakeRenderableBlockJob blockJob = new LakeRenderableBlockJob
            {
                waterStates = waterStates,
                mapHeight = mapHeight,
                chunkSize = chunkSize,
                stepIncrement = safeStepIncrement,
                blockCountPerAxis = blockCountPerAxis,
                renderableBlocks = renderableBlocks
            };
            JobHandle blockHandle = blockJob.Schedule(blockCount, 64);
            blockHandle.Complete();

            int renderableBlockCount = WaterMeshJobUtility.CountEnabled(renderableBlocks);
            if (renderableBlockCount == 0)
                return new WaterMeshData(0);

            compactBlockIndices =
                new NativeArray<int>(renderableBlockCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            WaterMeshJobUtility.FillEnabledIndices(renderableBlocks, compactBlockIndices);

            vertices =
                new NativeArray<float3>(renderableBlockCount * 4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            uvs =
                new NativeArray<float2>(renderableBlockCount * 4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            triangles =
                new NativeArray<int>(renderableBlockCount * 6, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            LakeMeshBuildJob meshJob = new LakeMeshBuildJob
            {
                renderableBlockIndices = compactBlockIndices,
                blockCountPerAxis = blockCountPerAxis,
                chunkSize = chunkSize,
                stepIncrement = safeStepIncrement,
                topLeftX = topLeftX,
                bottomLeftZ = bottomLeftZ,
                waterY = waterY,
                worldScale = worldScale,
                vertices = vertices,
                uvs = uvs,
                triangles = triangles
            };
            JobHandle meshHandle = meshJob.Schedule(renderableBlockCount, 64);
            meshHandle.Complete();

            return WaterMeshJobUtility.CreateWaterMeshData(vertices, uvs, triangles);
        }
        finally
        {
            if (waterStates.IsCreated)
                waterStates.Dispose();
            if (renderableBlocks.IsCreated)
                renderableBlocks.Dispose();
            if (compactBlockIndices.IsCreated)
                compactBlockIndices.Dispose();
            if (vertices.IsCreated)
                vertices.Dispose();
            if (uvs.IsCreated)
                uvs.Dispose();
            if (triangles.IsCreated)
                triangles.Dispose();
        }
    }

    [BurstCompile]
    private struct LakeRenderableBlockJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<WaterState> waterStates;
        public int mapHeight;
        public int chunkSize;
        public int stepIncrement;
        public int blockCountPerAxis;

        [WriteOnly] public NativeArray<byte> renderableBlocks;

        public void Execute(int blockIndex)
        {
            int blockX = blockIndex % blockCountPerAxis;
            int blockY = blockIndex / blockCountPerAxis;
            int startX = blockX * stepIncrement;
            int startY = blockY * stepIncrement;
            int maxX = math.min(startX + stepIncrement, chunkSize);
            int maxY = math.min(startY + stepIncrement, chunkSize);

            for (int y = startY; y < maxY; y++)
            {
                for (int x = startX; x < maxX; x++)
                {
                    int paddedX = x + 1;
                    int paddedY = y + 1;
                    int waterCornerCount = 0;

                    if (IsRenderableWater(waterStates[paddedX * mapHeight + paddedY])) waterCornerCount++;
                    if (IsRenderableWater(waterStates[(paddedX + 1) * mapHeight + paddedY])) waterCornerCount++;
                    if (IsRenderableWater(waterStates[paddedX * mapHeight + paddedY + 1])) waterCornerCount++;
                    if (IsRenderableWater(waterStates[(paddedX + 1) * mapHeight + paddedY + 1])) waterCornerCount++;

                    if (waterCornerCount >= 1)
                    {
                        renderableBlocks[blockIndex] = 1;
                        return;
                    }
                }
            }

            renderableBlocks[blockIndex] = 0;
        }

        private static bool IsRenderableWater(WaterState waterState)
        {
            return waterState == WaterState.Shallow || waterState == WaterState.Deep;
        }
    }

    [BurstCompile]
    private struct LakeMeshBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> renderableBlockIndices;
        public int blockCountPerAxis;
        public int chunkSize;
        public int stepIncrement;
        public float topLeftX;
        public float bottomLeftZ;
        public float waterY;
        public float worldScale;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> vertices;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float2> uvs;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> triangles;

        public void Execute(int renderableIndex)
        {
            int blockIndex = renderableBlockIndices[renderableIndex];
            int blockX = blockIndex % blockCountPerAxis;
            int blockY = blockIndex / blockCountPerAxis;
            int localX = blockX * stepIncrement;
            int localY = blockY * stepIncrement;

            int baseVertex = renderableIndex * 4;

            vertices[baseVertex] = new float3(
                (topLeftX + localX) * worldScale,
                waterY,
                (bottomLeftZ + localY) * worldScale);
            vertices[baseVertex + 1] = new float3(
                (topLeftX + localX + stepIncrement) * worldScale,
                waterY,
                (bottomLeftZ + localY) * worldScale);
            vertices[baseVertex + 2] = new float3(
                (topLeftX + localX) * worldScale,
                waterY,
                (bottomLeftZ + localY + stepIncrement) * worldScale);
            vertices[baseVertex + 3] = new float3(
                (topLeftX + localX + stepIncrement) * worldScale,
                waterY,
                (bottomLeftZ + localY + stepIncrement) * worldScale);

            uvs[baseVertex] = new float2(localX / (float)chunkSize, localY / (float)chunkSize);
            uvs[baseVertex + 1] = new float2((localX + stepIncrement) / (float)chunkSize, localY / (float)chunkSize);
            uvs[baseVertex + 2] = new float2(localX / (float)chunkSize, (localY + stepIncrement) / (float)chunkSize);
            uvs[baseVertex + 3] = new float2(
                (localX + stepIncrement) / (float)chunkSize,
                (localY + stepIncrement) / (float)chunkSize);

            int baseTriangle = renderableIndex * 6;
            triangles[baseTriangle] = baseVertex;
            triangles[baseTriangle + 1] = baseVertex + 2;
            triangles[baseTriangle + 2] = baseVertex + 1;
            triangles[baseTriangle + 3] = baseVertex + 1;
            triangles[baseTriangle + 4] = baseVertex + 2;
            triangles[baseTriangle + 5] = baseVertex + 3;
        }
    }
}

public static class RiverMeshGenerator
{
    private const float WaterSurfaceOffset = 0.02f;
    private const float RiverInclusionThreshold = 0.75f;

    public static WaterMeshData GenerateRiverMesh(
        float[,] heightMap,
        WaterState[,] waterStateMap,
        float[,] riverMaskMap,
        float heightMultiplier,
        int stepIncrement,
        float worldScale)
    {
        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;
        int safeStepIncrement = Mathf.Max(1, stepIncrement);

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        if (safeStepIncrement == 1)
        {
            return GenerateRiverMeshLOD0(heightMap, riverMaskMap, heightMultiplier, worldScale);
        }

        NativeArray<float> heightSamples = default;
        NativeArray<float> riverMaskSamples = default;
        NativeArray<byte> riverCellMask = default;
        NativeArray<float3> gridVertices = default;
        NativeArray<float2> gridUvs = default;
        NativeArray<RiverTriangleCoordinates> triangleCoordinates = default;
        NativeArray<float3> vertices = default;
        NativeArray<float2> uvs = default;
        NativeArray<int> triangles = default;

        try
        {
            heightSamples = WaterMeshJobUtility.CopyFloatMapToNative(heightMap, Allocator.TempJob, out _, out int mapHeight);
            riverMaskSamples = WaterMeshJobUtility.CopyFloatMapToNative(riverMaskMap, Allocator.TempJob, out _, out _);
            riverCellMask =
                new NativeArray<byte>(chunkSize * chunkSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            RiverCellMaskJob cellMaskJob = new RiverCellMaskJob
            {
                riverMaskMap = riverMaskSamples,
                mapHeight = mapHeight,
                chunkSize = chunkSize,
                riverCellMask = riverCellMask
            };
            JobHandle cellMaskHandle = cellMaskJob.Schedule(riverCellMask.Length, 64);

            int gridResolution = chunkSize + 1;
            gridVertices =
                new NativeArray<float3>(gridResolution * gridResolution, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            gridUvs =
                new NativeArray<float2>(gridResolution * gridResolution, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            RiverGridBuildJob gridBuildJob = new RiverGridBuildJob
            {
                heightMap = heightSamples,
                riverMaskMap = riverMaskSamples,
                mapHeight = mapHeight,
                chunkSize = chunkSize,
                gridResolution = gridResolution,
                topLeftX = topLeftX,
                bottomLeftZ = bottomLeftZ,
                heightMultiplier = heightMultiplier,
                worldScale = worldScale,
                vertices = gridVertices,
                uvs = gridUvs
            };
            JobHandle gridBuildHandle = gridBuildJob.Schedule(gridVertices.Length, 64);
            JobHandle.CombineDependencies(cellMaskHandle, gridBuildHandle).Complete();

            List<RiverTriangleCoordinates> triangleCoordinateList =
                BuildRiverTriangleCoordinates(riverCellMask, chunkSize, safeStepIncrement);
            int triangleCount = triangleCoordinateList.Count;

            if (triangleCount == 0)
                return new WaterMeshData(0);

            triangleCoordinates =
                new NativeArray<RiverTriangleCoordinates>(triangleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < triangleCount; i++)
                triangleCoordinates[i] = triangleCoordinateList[i];

            vertices = new NativeArray<float3>(triangleCount * 3, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            uvs = new NativeArray<float2>(triangleCount * 3, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            triangles = new NativeArray<int>(triangleCount * 3, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            RiverTriangleMeshBuildJob triangleMeshJob = new RiverTriangleMeshBuildJob
            {
                triangleCoordinates = triangleCoordinates,
                gridResolution = gridResolution,
                gridVertices = gridVertices,
                gridUvs = gridUvs,
                vertices = vertices,
                uvs = uvs,
                triangles = triangles
            };
            JobHandle triangleMeshHandle = triangleMeshJob.Schedule(triangleCount, 64);
            triangleMeshHandle.Complete();

            return WaterMeshJobUtility.CreateWaterMeshData(vertices, uvs, triangles);
        }
        finally
        {
            if (heightSamples.IsCreated)
                heightSamples.Dispose();
            if (riverMaskSamples.IsCreated)
                riverMaskSamples.Dispose();
            if (riverCellMask.IsCreated)
                riverCellMask.Dispose();
            if (gridVertices.IsCreated)
                gridVertices.Dispose();
            if (gridUvs.IsCreated)
                gridUvs.Dispose();
            if (triangleCoordinates.IsCreated)
                triangleCoordinates.Dispose();
            if (vertices.IsCreated)
                vertices.Dispose();
            if (uvs.IsCreated)
                uvs.Dispose();
            if (triangles.IsCreated)
                triangles.Dispose();
        }
    }

    private static List<RiverTriangleCoordinates> BuildRiverTriangleCoordinates(
        NativeArray<byte> riverCellMask,
        int chunkSize,
        int stepIncrement)
    {
        List<RiverTriangleCoordinates> triangleCoordinates = new List<RiverTriangleCoordinates>();
        int strip = Mathf.Max(1, stepIncrement);
        int interiorMin = strip;
        int interiorMax = chunkSize - strip;

        for (int z = interiorMin; z < interiorMax; z += stepIncrement)
        {
            for (int x = interiorMin; x < interiorMax; x += stepIncrement)
            {
                int spanX = Mathf.Min(stepIncrement, interiorMax - x);
                int spanZ = Mathf.Min(stepIncrement, interiorMax - z);

                if (!BlockContainsRenderableRect(riverCellMask, x, z, spanX, spanZ, chunkSize))
                    continue;

                AddTri(triangleCoordinates, x, z, x, z + spanZ, x + spanX, z + spanZ);
                AddTri(triangleCoordinates, x, z, x + spanX, z + spanZ, x + spanX, z);
            }
        }

        for (int x0 = 0; x0 < chunkSize; x0 += stepIncrement)
        {
            int x1 = Mathf.Min(x0 + stepIncrement, chunkSize);

            if (!BlockContainsRenderableRect(riverCellMask, x0, 0, x1 - x0, strip, chunkSize))
                continue;

            int anchorX = x0;
            int anchorZ = 0;

            int prevX = x0 + 1;
            int prevZ = 0;

            for (int x = x0 + 2; x <= x1; x++)
            {
                AddTri(triangleCoordinates, anchorX, anchorZ, x, 0, prevX, prevZ);
                prevX = x;
            }

            AddTri(triangleCoordinates, anchorX, anchorZ, x1, strip, prevX, prevZ);
            AddTri(triangleCoordinates, anchorX, anchorZ, x0, strip, x1, strip);
        }

        for (int x0 = 0; x0 < chunkSize; x0 += stepIncrement)
        {
            int x1 = Mathf.Min(x0 + stepIncrement, chunkSize);

            if (!BlockContainsRenderableRect(riverCellMask, x0, chunkSize - strip, x1 - x0, strip, chunkSize))
                continue;

            int anchorX = x0;
            int anchorZ = chunkSize - strip;

            int prevX = x0;
            int prevZ = chunkSize;

            for (int x = x0 + 1; x <= x1; x++)
            {
                AddTri(triangleCoordinates, anchorX, anchorZ, prevX, prevZ, x, chunkSize);
                prevX = x;
            }

            AddTri(triangleCoordinates, anchorX, anchorZ, prevX, prevZ, x1, chunkSize - strip);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += stepIncrement)
        {
            int z1 = Mathf.Min(z0 + stepIncrement, chunkSize - strip);

            if (!BlockContainsRenderableRect(riverCellMask, 0, z0, strip, z1 - z0, chunkSize))
                continue;

            int anchorX = 0;
            int anchorZ = z0;

            int prevX = 0;
            int prevZ = z0 + 1;

            for (int z = z0 + 2; z <= z1; z++)
            {
                AddTri(triangleCoordinates, anchorX, anchorZ, prevX, prevZ, 0, z);
                prevZ = z;
            }

            AddTri(triangleCoordinates, anchorX, anchorZ, 0, z1, strip, z1);
            AddTri(triangleCoordinates, anchorX, anchorZ, strip, z1, strip, z0);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += stepIncrement)
        {
            int z1 = Mathf.Min(z0 + stepIncrement, chunkSize - strip);

            if (!BlockContainsRenderableRect(riverCellMask, chunkSize - strip, z0, strip, z1 - z0, chunkSize))
                continue;

            int anchorX = chunkSize - strip;
            int anchorZ = z0;

            int prevX = chunkSize - strip;
            int prevZ = z1;

            AddTri(triangleCoordinates, anchorX, anchorZ, prevX, prevZ, chunkSize, z1);
            prevX = chunkSize;
            prevZ = z1;

            for (int z = z1 - 1; z >= z0; z--)
            {
                AddTri(triangleCoordinates, anchorX, anchorZ, prevX, prevZ, chunkSize, z);
                prevZ = z;
            }
        }

        return triangleCoordinates;
    }

    private static WaterMeshData GenerateRiverMeshLOD0(
        float[,] heightMap,
        float[,] riverMaskMap,
        float heightMultiplier,
        float worldScale)
    {
        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        NativeArray<float> heightSamples = default;
        NativeArray<float> riverMaskSamples = default;
        NativeArray<byte> riverCellMask = default;
        NativeArray<int> compactCellIndices = default;
        NativeArray<float3> vertices = default;
        NativeArray<float2> uvs = default;
        NativeArray<int> triangles = default;

        try
        {
            heightSamples = WaterMeshJobUtility.CopyFloatMapToNative(heightMap, Allocator.TempJob, out _, out int mapHeight);
            riverMaskSamples = WaterMeshJobUtility.CopyFloatMapToNative(riverMaskMap, Allocator.TempJob, out _, out _);
            riverCellMask =
                new NativeArray<byte>(chunkSize * chunkSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            RiverCellMaskJob cellMaskJob = new RiverCellMaskJob
            {
                riverMaskMap = riverMaskSamples,
                mapHeight = mapHeight,
                chunkSize = chunkSize,
                riverCellMask = riverCellMask
            };
            JobHandle cellMaskHandle = cellMaskJob.Schedule(riverCellMask.Length, 64);
            cellMaskHandle.Complete();

            int renderableCellCount = WaterMeshJobUtility.CountEnabled(riverCellMask);
            if (renderableCellCount == 0)
                return new WaterMeshData(0);

            compactCellIndices =
                new NativeArray<int>(renderableCellCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            WaterMeshJobUtility.FillEnabledIndices(riverCellMask, compactCellIndices);

            vertices =
                new NativeArray<float3>(renderableCellCount * 4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            uvs =
                new NativeArray<float2>(renderableCellCount * 4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            triangles =
                new NativeArray<int>(renderableCellCount * 6, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            RiverLOD0MeshBuildJob meshBuildJob = new RiverLOD0MeshBuildJob
            {
                renderableCellIndices = compactCellIndices,
                heightMap = heightSamples,
                riverMaskMap = riverMaskSamples,
                mapHeight = mapHeight,
                chunkSize = chunkSize,
                topLeftX = topLeftX,
                bottomLeftZ = bottomLeftZ,
                heightMultiplier = heightMultiplier,
                worldScale = worldScale,
                vertices = vertices,
                uvs = uvs,
                triangles = triangles
            };
            JobHandle meshBuildHandle = meshBuildJob.Schedule(renderableCellCount, 64);
            meshBuildHandle.Complete();

            return WaterMeshJobUtility.CreateWaterMeshData(vertices, uvs, triangles);
        }
        finally
        {
            if (heightSamples.IsCreated)
                heightSamples.Dispose();
            if (riverMaskSamples.IsCreated)
                riverMaskSamples.Dispose();
            if (riverCellMask.IsCreated)
                riverCellMask.Dispose();
            if (compactCellIndices.IsCreated)
                compactCellIndices.Dispose();
            if (vertices.IsCreated)
                vertices.Dispose();
            if (uvs.IsCreated)
                uvs.Dispose();
            if (triangles.IsCreated)
                triangles.Dispose();
        }
    }

    private static void AddTri(
        List<RiverTriangleCoordinates> triangleCoordinates,
        int ax, int az,
        int bx, int bz,
        int cx, int cz)
    {
        triangleCoordinates.Add(new RiverTriangleCoordinates
        {
            ax = ax,
            az = az,
            bx = bx,
            bz = bz,
            cx = cx,
            cz = cz
        });
    }

    private static bool BlockContainsRenderableRect(
        NativeArray<byte> cellMask,
        int startX,
        int startY,
        int width,
        int height,
        int chunkSize)
    {
        int maxX = Mathf.Min(startX + width, chunkSize);
        int maxY = Mathf.Min(startY + height, chunkSize);

        for (int y = startY; y < maxY; y++)
        {
            for (int x = startX; x < maxX; x++)
            {
                if (cellMask[y * chunkSize + x] != 0)
                    return true;
            }
        }

        return false;
    }

    private struct RiverTriangleCoordinates
    {
        public int ax;
        public int az;
        public int bx;
        public int bz;
        public int cx;
        public int cz;
    }

    [BurstCompile]
    private struct RiverCellMaskJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> riverMaskMap;
        public int mapHeight;
        public int chunkSize;

        [WriteOnly] public NativeArray<byte> riverCellMask;

        public void Execute(int index)
        {
            int localX = index % chunkSize;
            int localY = index / chunkSize;
            int x = localX + 1;
            int y = localY + 1;

            int count = 0;
            if (riverMaskMap[x * mapHeight + y] >= RiverInclusionThreshold) count++;
            if (riverMaskMap[(x + 1) * mapHeight + y] >= RiverInclusionThreshold) count++;
            if (riverMaskMap[x * mapHeight + y + 1] >= RiverInclusionThreshold) count++;
            if (riverMaskMap[(x + 1) * mapHeight + y + 1] >= RiverInclusionThreshold) count++;

            riverCellMask[index] = count >= 1 ? (byte)1 : (byte)0;
        }
    }

    [BurstCompile]
    private struct RiverGridBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> heightMap;
        [ReadOnly] public NativeArray<float> riverMaskMap;
        public int mapHeight;
        public int chunkSize;
        public int gridResolution;
        public float topLeftX;
        public float bottomLeftZ;
        public float heightMultiplier;
        public float worldScale;

        [WriteOnly] public NativeArray<float3> vertices;
        [WriteOnly] public NativeArray<float2> uvs;

        public void Execute(int index)
        {
            int x = index % gridResolution;
            int z = index / gridResolution;

            vertices[index] = BuildRiverVertex(
                heightMap,
                riverMaskMap,
                mapHeight,
                topLeftX,
                bottomLeftZ,
                x,
                z,
                heightMultiplier,
                worldScale);
            uvs[index] = new float2(x / (float)chunkSize, z / (float)chunkSize);
        }
    }

    [BurstCompile]
    private struct RiverLOD0MeshBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> renderableCellIndices;
        [ReadOnly] public NativeArray<float> heightMap;
        [ReadOnly] public NativeArray<float> riverMaskMap;
        public int mapHeight;
        public int chunkSize;
        public float topLeftX;
        public float bottomLeftZ;
        public float heightMultiplier;
        public float worldScale;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> vertices;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float2> uvs;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> triangles;

        public void Execute(int renderableIndex)
        {
            int cellIndex = renderableCellIndices[renderableIndex];
            int x = cellIndex % chunkSize;
            int z = cellIndex / chunkSize;
            int baseVertex = renderableIndex * 4;

            vertices[baseVertex] = BuildRiverVertex(
                heightMap,
                riverMaskMap,
                mapHeight,
                topLeftX,
                bottomLeftZ,
                x,
                z,
                heightMultiplier,
                worldScale);
            vertices[baseVertex + 1] = BuildRiverVertex(
                heightMap,
                riverMaskMap,
                mapHeight,
                topLeftX,
                bottomLeftZ,
                x + 1,
                z,
                heightMultiplier,
                worldScale);
            vertices[baseVertex + 2] = BuildRiverVertex(
                heightMap,
                riverMaskMap,
                mapHeight,
                topLeftX,
                bottomLeftZ,
                x,
                z + 1,
                heightMultiplier,
                worldScale);
            vertices[baseVertex + 3] = BuildRiverVertex(
                heightMap,
                riverMaskMap,
                mapHeight,
                topLeftX,
                bottomLeftZ,
                x + 1,
                z + 1,
                heightMultiplier,
                worldScale);

            uvs[baseVertex] = new float2(x / (float)chunkSize, z / (float)chunkSize);
            uvs[baseVertex + 1] = new float2((x + 1) / (float)chunkSize, z / (float)chunkSize);
            uvs[baseVertex + 2] = new float2(x / (float)chunkSize, (z + 1) / (float)chunkSize);
            uvs[baseVertex + 3] = new float2((x + 1) / (float)chunkSize, (z + 1) / (float)chunkSize);

            int baseTriangle = renderableIndex * 6;
            triangles[baseTriangle] = baseVertex;
            triangles[baseTriangle + 1] = baseVertex + 2;
            triangles[baseTriangle + 2] = baseVertex + 1;
            triangles[baseTriangle + 3] = baseVertex + 1;
            triangles[baseTriangle + 4] = baseVertex + 2;
            triangles[baseTriangle + 5] = baseVertex + 3;
        }
    }

    [BurstCompile]
    private struct RiverTriangleMeshBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<RiverTriangleCoordinates> triangleCoordinates;
        [ReadOnly] public NativeArray<float3> gridVertices;
        [ReadOnly] public NativeArray<float2> gridUvs;
        public int gridResolution;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> vertices;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float2> uvs;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> triangles;

        public void Execute(int triangleIndex)
        {
            RiverTriangleCoordinates coordinates = triangleCoordinates[triangleIndex];
            int baseVertex = triangleIndex * 3;

            int a = coordinates.ax + coordinates.az * gridResolution;
            int b = coordinates.bx + coordinates.bz * gridResolution;
            int c = coordinates.cx + coordinates.cz * gridResolution;

            vertices[baseVertex] = gridVertices[a];
            vertices[baseVertex + 1] = gridVertices[b];
            vertices[baseVertex + 2] = gridVertices[c];

            uvs[baseVertex] = gridUvs[a];
            uvs[baseVertex + 1] = gridUvs[b];
            uvs[baseVertex + 2] = gridUvs[c];

            triangles[baseVertex] = baseVertex;
            triangles[baseVertex + 1] = baseVertex + 1;
            triangles[baseVertex + 2] = baseVertex + 2;
        }
    }

    private static float3 BuildRiverVertex(
        NativeArray<float> heightMap,
        NativeArray<float> riverMaskMap,
        int mapHeight,
        float topLeftX,
        float bottomLeftZ,
        int x,
        int z,
        float heightMultiplier,
        float worldScale)
    {
        const float riverCoreExtraDepth = 0.5f;

        int mapIndex = (x + 1) * mapHeight + z + 1;
        float terrainHeight = heightMap[mapIndex];
        float riverMask = riverMaskMap[mapIndex];
        float riverCoreMask = InverseLerp(RiverInclusionThreshold, 1.0f, riverMask);
        riverCoreMask = SmoothStep(0f, 1f, riverCoreMask);
        float restoredWaterHeight = terrainHeight + riverCoreMask * riverCoreExtraDepth;
        float h = restoredWaterHeight * heightMultiplier * worldScale + WaterSurfaceOffset;

        return new float3((topLeftX + x) * worldScale, h, (bottomLeftZ + z) * worldScale);
    }

    private static float InverseLerp(float a, float b, float value)
    {
        return math.clamp((value - a) / (b - a), 0f, 1f);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = InverseLerp(edge0, edge1, value);
        return t * t * (3f - 2f * t);
    }
}

internal static class WaterMeshJobUtility
{
    public static int GetBlockCountPerAxis(int chunkSize, int stepIncrement)
    {
        if (chunkSize < stepIncrement)
            return 0;

        return (chunkSize - stepIncrement) / stepIncrement + 1;
    }

    public static NativeArray<float> CopyFloatMapToNative(
        float[,] source,
        Allocator allocator,
        out int width,
        out int height)
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

    public static NativeArray<T> CopyMapToNative<T>(
        T[,] source,
        Allocator allocator,
        out int width,
        out int height)
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

    public static int CountEnabled(NativeArray<byte> flags)
    {
        int count = 0;

        for (int i = 0; i < flags.Length; i++)
        {
            if (flags[i] != 0)
                count++;
        }

        return count;
    }

    public static void FillEnabledIndices(NativeArray<byte> flags, NativeArray<int> indices)
    {
        int index = 0;

        for (int i = 0; i < flags.Length; i++)
        {
            if (flags[i] == 0)
                continue;

            indices[index] = i;
            index++;
        }
    }

    public static WaterMeshData CreateWaterMeshData(
        NativeArray<float3> nativeVertices,
        NativeArray<float2> nativeUvs,
        NativeArray<int> nativeTriangles)
    {
        Vector3[] vertices = new Vector3[nativeVertices.Length];
        Vector2[] uvs = new Vector2[nativeUvs.Length];
        int[] triangles = new int[nativeTriangles.Length];

        for (int i = 0; i < nativeVertices.Length; i++)
        {
            float3 vertex = nativeVertices[i];
            vertices[i] = new Vector3(vertex.x, vertex.y, vertex.z);
        }

        for (int i = 0; i < nativeUvs.Length; i++)
        {
            float2 uv = nativeUvs[i];
            uvs[i] = new Vector2(uv.x, uv.y);
        }

        nativeTriangles.CopyTo(triangles);
        return new WaterMeshData(vertices, uvs, triangles);
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
        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.colors = colors.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
