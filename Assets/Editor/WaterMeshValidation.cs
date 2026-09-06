using System;
using UnityEditor;
using UnityEngine;

public static class WaterMeshValidation
{
    [MenuItem("Tools/Terrain/Validate Unified Water Mesh")]
    public static void Run()
    {
        foreach (int size in new[] { 16, 19, 128 })
        {
            foreach (int pattern in new[] { 0, 1, 2, 3, 4 })
            {
                WaterState[,] states = CreatePattern(size, pattern);
                foreach (int step in new[] { 1, 2, 4, 8, 16 })
                    ValidateCoverage(states, step, pattern == 1);
            }
        }
        ValidateChunkBoundary();
        ValidateRuntime();
        WaterGenerationValidation.Run();
        Debug.Log("Unified water validation passed: exact coverage, no overlap, holes, UVs, winding, partial blocks, mixed-LOD chunk edges, and global-water regression checks.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void ValidateRuntime()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterMeshBaseMaterial.mat");
        Require(material != null, "Water material is missing.");
        var record = new ChunkRecord(new ChunkCoord(0, 0));
        var runtime = new ChunkRuntime(record, 16, 0.3f, null, material, material, true);
        Mesh terrain = new Mesh();
        Mesh water = WaterMeshGenerator.GenerateWaterMesh(CreatePattern(16, 1), 1, 0.3f, 14.4f).CreateMesh();
        MeshRenderer[] renderers = runtime.Root.GetComponentsInChildren<MeshRenderer>(true);
        try
        {
            Require(renderers.Length == 2, "Chunk does not have exactly one terrain renderer and one water renderer.");
            int version = record.BeginMeshRequest(0);
            Require(record.TryCompleteMeshRequest(0, version, terrain, water), "Unified mesh result was rejected.");
            Require(record.TryGetLODWaterMesh(0, out Mesh cached) && cached == water, "Unified water cache lost its mesh.");
            runtime.SetMeshes(terrain, cached, 0);
            Require(runtime.Root.transform.Find("Water").gameObject.activeSelf, "Water renderer was not activated.");
            runtime.SetRenderVisible(false);
            foreach (MeshRenderer renderer in renderers) Require(!renderer.enabled, "Hidden water still renders.");
            runtime.SetRenderVisible(true);
            foreach (MeshRenderer renderer in renderers) Require(renderer.enabled, "Water visibility did not recover.");
            runtime.SetMeshes(terrain, null, 4);
            Require(!runtime.Root.transform.Find("Water").gameObject.activeSelf, "Empty/far LOD retained old water.");
            runtime.SetMeshes(terrain, water, 0);
            runtime.ClearMeshes();
            Require(!runtime.Root.transform.Find("Water").gameObject.activeSelf, "Clearing meshes retained water.");
            record.ClearAllLODMeshes();
            Require(!record.TryGetLODWaterMesh(0, out _), "Water cache was not cleared.");
        }
        finally
        {
            foreach (MeshRenderer renderer in renderers) UnityEngine.Object.DestroyImmediate(renderer.sharedMaterial);
            UnityEngine.Object.DestroyImmediate(runtime.Root);
            UnityEngine.Object.DestroyImmediate(terrain);
            UnityEngine.Object.DestroyImmediate(water);
        }
    }

    public static WaterState[,] CreatePattern(int size, int pattern)
    {
        var states = new WaterState[size + 3, size + 3];
        var random = new System.Random(817);
        for (int x = 0; x < size + 3; x++)
        {
            for (int z = 0; z < size + 3; z++)
            {
                bool wet = pattern == 1 ||
                    (pattern == 2 && (Math.Abs(x - z) < 2 || (x > size / 3 && z > size / 2))) ||
                    (pattern == 3 && random.NextDouble() < 0.08) ||
                    (pattern == 4 && (x < 3 || z < 3 || x > size - 1 || z > size - 1));
                states[x, z] = wet ? WaterState.Shallow : WaterState.Dry;
            }
        }
        return states;
    }

    private static void ValidateCoverage(WaterState[,] states, int step, bool full)
    {
        int size = states.GetLength(0) - 3;
        Mesh mesh = WaterMeshGenerator.GenerateWaterMesh(states, step, 0.3f, 14.4f).CreateMesh();
        try
        {
            var hits = new int[size, size];
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            int[] indices = mesh.triangles;
            Require(vertices.Length % 4 == 0 && indices.Length == vertices.Length / 4 * 6, "Invalid quad topology.");
            for (int i = 0; i < vertices.Length; i++)
            {
                Require(vertices[i].y == 14.4f, "Water surface moved.");
                Require(Mathf.Abs(uvs[i].x - (vertices[i].x / 0.3f + size / 2f) / size) < 0.00001f, "Merged quad stretches U.");
                Require(Mathf.Abs(uvs[i].y - (vertices[i].z / 0.3f + size / 2f) / size) < 0.00001f, "Merged quad stretches V.");
            }
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                Require(Vector3.Cross(b - a, c - a).y > 0f, "Degenerate or inverted water triangle.");
            }
            for (int i = 0; i < vertices.Length; i += 4)
            {
                int x0 = Mathf.RoundToInt(vertices[i].x / 0.3f + size / 2f);
                int z0 = Mathf.RoundToInt(vertices[i].z / 0.3f + size / 2f);
                int x1 = Mathf.RoundToInt(vertices[i + 3].x / 0.3f + size / 2f);
                int z1 = Mathf.RoundToInt(vertices[i + 3].z / 0.3f + size / 2f);
                Require(x0 >= 0 && z0 >= 0 && x1 <= size && z1 <= size, "Water extends beyond chunk bounds.");
                for (int x = x0; x < x1; x++)
                    for (int z = z0; z < z1; z++)
                        hits[x, z]++;
            }
            int occupiedBlocks = 0;
            for (int x0 = 0; x0 < size; x0 += step)
            {
                for (int z0 = 0; z0 < size; z0 += step)
                {
                    int x1 = Math.Min(x0 + step, size), z1 = Math.Min(z0 + step, size);
                    bool wet = false;
                    for (int x = x0; x < x1; x++)
                        for (int z = z0; z < z1; z++)
                            wet |= IsWet(states[x + 1, z + 1]) || IsWet(states[x + 2, z + 1]) ||
                                   IsWet(states[x + 1, z + 2]) || IsWet(states[x + 2, z + 2]);
                    if (wet) occupiedBlocks++;
                    for (int x = x0; x < x1; x++)
                        for (int z = z0; z < z1; z++)
                            Require(hits[x, z] == (wet ? 1 : 0), $"Changed coverage or overlap at {x},{z}, size={size}, step={step}.");
                }
            }
            Require(vertices.Length <= occupiedBlocks * 4, "Greedy meshing increased geometry.");
            if (full) Require(vertices.Length == 4, "A full chunk did not merge to one quad.");
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }

    private static void ValidateChunkBoundary()
    {
        const int size = 32;
        var left = new WaterState[size + 3, size + 3];
        var right = new WaterState[size + 3, size + 3];
        for (int x = 0; x < size + 3; x++)
            for (int z = 14; z <= 18; z++)
                left[x, z] = right[x, z] = WaterState.Deep;
        foreach (int leftStep in new[] { 1, 2, 4, 8, 16 })
        {
            foreach (int rightStep in new[] { 1, 2, 4, 8, 16 })
            {
                Mesh a = WaterMeshGenerator.GenerateWaterMesh(left, leftStep, 0.3f, 14.4f).CreateMesh();
                Mesh b = WaterMeshGenerator.GenerateWaterMesh(right, rightStep, 0.3f, 14.4f).CreateMesh();
                try
                {
                    for (int z = 13; z < 18; z++)
                    {
                        float worldZ = (z + 0.5f - size / 2f) * 0.3f;
                        Require(CoversEdge(a, size * 0.15f, worldZ) && CoversEdge(b, -size * 0.15f, worldZ), "Mixed LODs leave a gap across the river.");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
            }
        }
    }

    private static bool CoversEdge(Mesh mesh, float x, float z)
    {
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i += 4)
            if (x >= vertices[i].x - 0.0001f && x <= vertices[i + 3].x + 0.0001f && z >= vertices[i].z && z <= vertices[i + 3].z)
                return true;
        return false;
    }

    private static bool IsWet(WaterState state) => state == WaterState.Shallow || state == WaterState.Deep;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
