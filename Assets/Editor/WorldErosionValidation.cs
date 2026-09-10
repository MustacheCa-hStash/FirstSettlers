using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public static class WorldErosionValidation
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    [MenuItem("Tools/Terrain/Validate World Erosion")]
    public static void Run()
    {
        ValidateField();
        ValidateValleys();
        ValidateHash();
        ValidateHandoffVisibility();
        foreach (int seed in new[] { 7, 42, 12345 })
        {
            var settings = WorldErosionSettings.Default;
            ValidateIntegration(seed, settings);
            settings.amplitude = 1.2f; settings.wavelength = 384f; settings.rotation = 37f;
            settings.stretch = new Vector2(1.4f, 0.8f); settings.offset = new Vector2(137f, -231f);
            settings.seedOffset = -733; settings.maxMeshSpacing = 4;
            ValidateIntegration(seed, settings);
        }
        Debug.Log("WORLD EROSION PASS: analytic base gradients, lowlands/mountains/seabed, zero/off bypass, settings snapshots, river valley flattening, cell-hash diversity, terrain handoff visibility, near/far/macro/collider agreement and X/Z seams with default and customized settings across three seeds.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static void ValidateField()
    {
        var settings = WorldErosionSettings.Default; settings.carveRivers = false;
        int low = 0, high = 0, wet = 0;
        float gradientError = 0f;
        foreach (int seed in new[] { 7, 42, 12345 })
            for (int x = -20; x <= 20; x++) for (int z = -20; z <= 20; z++)
            {
                float2 p = new float2(x * 237.39f, z * 251.21f);
                var b = WorldTerrainHeight.Base(p, 600f, seed, 1.3f, settings);
                float h = WorldTerrainHeight.Erode(p, b, 600f, seed, 1.3f, settings);
                Check(math.isfinite(h) && math.abs(h - b.Height) <= settings.amplitude + 0.0001f, "Invalid world erosion displacement.");
                var dx = new float2(1f, 0f); var dz = new float2(0f, 1f);
                float2 numeric = new float2(WorldTerrainHeight.Base(p + dx, 600f, seed, 1.3f, settings).Height - WorldTerrainHeight.Base(p - dx, 600f, seed, 1.3f, settings).Height,
                    WorldTerrainHeight.Base(p + dz, 600f, seed, 1.3f, settings).Height - WorldTerrainHeight.Base(p - dz, 600f, seed, 1.3f, settings).Height) * 0.5f;
                gradientError = math.max(gradientError, math.distance(numeric, b.Gradient));
                if (math.abs(h - b.Height) > 0.001f)
                { if (b.MountainMask < 0.01f) low++; if (b.MountainMask > 0.45f) high++; if (b.Height < 0.24f) wet++; }
                var off = settings; off.enabled = false;
                Check(WorldTerrainHeight.Erode(p, b, 600f, seed, 1.3f, off) == b.Height, "Disabled erosion modifies terrain.");
                off.enabled = true; off.amplitude = 0f;
                Check(WorldTerrainHeight.Erode(p, b, 600f, seed, 1.3f, off) == b.Height, "Zero amplitude modifies terrain.");
            }
        Check(low > 100 && high > 100 && wet > 100, "Erosion did not affect every terrain category.");
        Check(gradientError < 0.0002f, "Analytical base gradient disagrees with finite difference.");
        Debug.Log($"World coverage: {low} lowland, {high} mountain, {wet} seabed samples changed; maximum input-gradient error {gradientError:G4}.");
    }

    private static void ValidateValleys()
    {
        var method = typeof(HeightMapGenerator).GetMethod("CarveRiverBasin", BindingFlags.NonPublic | BindingFlags.Static);
        float Carve(float h, float basin, float river, float width, float flatten) =>
            (float)method.Invoke(null, new object[] { h, basin, river, 0.24f, width, flatten });
        float shoulder = 0.24f + TerrainWaterSettings.RiverShoulderHeight;
        Check(math.abs(Carve(2f, 1f, 0f, 1.4f, 1f) - shoulder) < 0.00001f, "Broad dry valley is not flat.");
        Check(Carve(2f, 1f, 0f, 1.4f, 0f) == 2f, "Valley flattening toggle ignored.");
        Check(Carve(2f, 0.5f, 0f, 2f, 1f) < Carve(2f, 0.5f, 0f, 0.5f, 1f), "Valley width does not broaden the floor.");
        Check(Carve(2f, 1f, 1f, 1.4f, 1f) < 0.24f, "River channel no longer cuts below water.");
        var a = WorldErosionSettings.Default; a.carveRivers = true;
        var b = a; b.riverValleyFlattening = 0f;
        var ca = HeightMapGenerator.CreateSamplingContext(42, 0.24f, 1.3f, a);
        var cb = HeightMapGenerator.CreateSamplingContext(42, 0.24f, 1.3f, b);
        int changed = 0;
        for (int x = -12; x <= 12; x++) for (int z = -12; z <= 12; z++)
        {
            var ha = HeightMapGenerator.SampleTerrainHeight(x * 251f, z * 237f, 600f, ca);
            var hb = HeightMapGenerator.SampleTerrainHeight(x * 251f, z * 237f, 600f, cb);
            Check(math.abs(ha.RiverMask - hb.RiverMask) < 0.00001f, "Valley shaping moved river paths.");
            if (math.abs(ha.Height - hb.Height) > 0.001f) changed++;
        }
        Check(changed > 0, "Valley settings were not propagated to the public sampler.");
    }

    private static void ValidateHash()
    {
        var hash = typeof(LPMGames.Terrain.Erosion.AdvancedTerrainErosion).GetMethod("Hash2", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (int seed in new[] { 568317, -928417, int.MaxValue })
        {
            var values = new HashSet<float2>();
            for (int x = -16; x < 16; x++) for (int z = -16; z < 16; z++)
                values.Add((float2)hash.Invoke(null, new object[] { new float2(x, z), seed }));
            Check(values.Count > 1000, "Erosion cell hash collapses with large seeds.");
        }
    }

    private static void ValidateHandoffVisibility()
    {
        Shader shader = Shader.Find("Hidden/InternalErrorShader");
        var material = new Material(shader);
        var runtime = new ChunkRuntime(new ChunkRecord(new ChunkCoord(0, 0)), 32, 1f, null, material, material, false);
        try
        {
            var renderer = runtime.Root.GetComponent<MeshRenderer>();
            runtime.SetTerrainHandoffHidden(true); runtime.SetRenderVisible(false); runtime.SetRenderVisible(true);
            Check(!renderer.enabled && !runtime.IsRenderVisible, "Visibility refresh exposes a mesh under the outgoing macro tile.");
            runtime.SetTerrainHandoffHidden(false);
            Check(renderer.enabled && runtime.IsRenderVisible, "Completed handoff leaves terrain hidden.");
            runtime.SetRenderVisible(false); runtime.SetTerrainHandoffHidden(true); runtime.SetTerrainHandoffHidden(false);
            Check(!renderer.enabled, "Handoff overrides frustum visibility.");
        }
        finally
        {
            foreach (var renderer in runtime.Root.GetComponentsInChildren<MeshRenderer>())
                UnityEngine.Object.DestroyImmediate(renderer.sharedMaterial);
            UnityEngine.Object.DestroyImmediate(runtime.Root); UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void ValidateIntegration(int seed, WorldErosionSettings settings)
    {
        const int size = 32;
        var coord = new ChunkCoord(-13, 7);
        var context = HeightMapGenerator.CreateSamplingContext(seed, 0.24f, 1.3f, settings);
        HeightFieldResult Field(ChunkCoord c) => HeightMapGenerator.GenerateTerrainHeightField(size, seed, 600f, c, 0.24f, 1.3f, 200f, settings);
        var near = Field(coord); var nx = Field(new ChunkCoord(coord.x + 1, coord.z)); var nz = Field(new ChunkCoord(coord.x, coord.z + 1));
        for (int i = 1; i <= size + 1; i++)
        {
            Check(math.abs(near.HeightMap[size + 1, i] - nx.HeightMap[1, i]) < 0.0002f && math.abs(near.HeightMap[i, size + 1] - nz.HeightMap[i, 1]) < 0.0002f, "Height seam.");
            Check(math.abs(near.SlopeMap[size + 1, i] - nx.SlopeMap[1, i]) < 0.002f && math.abs(near.SlopeMap[i, size + 1] - nz.SlopeMap[i, 1]) < 0.002f, "Slope seam.");
        }
        for (int x = 1; x <= size + 1; x++) for (int z = 1; z <= size + 1; z++)
        {
            float direct = HeightMapGenerator.SampleTerrainHeight(coord.x * size + x - 1, coord.z * size + z - 1, 600f, context).Height;
            Check(math.abs(near.HeightMap[x, z] - direct) < 0.003f, "Managed/Burst settings mismatch.");
        }
        var far = FarTerrainGenerator.Generate(coord, 1, size, seed, 600f, 200f, 0.3f, 9, 9, 0f, 0.24f,
            mountainHorizontalScale: 1.3f, erosion: settings);
        var macro = FarTerrainGenerator.Generate(new ChunkCoord(-4, 2), 1, size * 4, seed, 600f, 200f, 0.3f, 9, 9, 0f, 0.24f,
            isMacroTile: true, mountainHorizontalScale: 1.3f, erosion: settings);
        void CheckGrid(FarTerrainRequestResult tile, int tileSize)
        {
            int n = tile.HeightGrid.GetLength(0);
            Check(tileSize / (float)(n - 1) <= settings.maxMeshSpacing + 0.001f, "Far geometry ignores maximum vertex spacing.");
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++)
            {
                float wx = tile.ChunkCoord.x * tileSize + x * tileSize / (float)(n - 1);
                float wz = tile.ChunkCoord.z * tileSize + z * tileSize / (float)(n - 1);
                Check(math.abs(tile.HeightGrid[x, z] - HeightMapGenerator.SampleTerrainHeight(wx, wz, 600f, context).Height) < 0.003f, "Far/macro settings mismatch.");
            }
        }
        CheckGrid(far, size); CheckGrid(macro, size * 4);
        var collider = ColliderMeshGenerator.GenerateColliderMesh(near.HeightMap, 200f, 4, 0.3f);
        for (int x = 0; x < 9; x++) for (int z = 0; z < 9; z++)
            Check(math.abs(collider.vertices[z * 9 + x].y - near.HeightMap[x * 4 + 1, z * 4 + 1] * 60f) < 0.001f, "Collider mismatch.");
    }
}
