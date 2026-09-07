using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class DistantTreeValidation
{
    [MenuItem("Tools/Terrain/Validate Distant Trees (correctness only)")]
    public static void Run()
    {
        ValidateCircularFoliageRanges();
        ValidateSparsePlanner();
        ValidateTerrainSampler();
        var grid = new float[,] { { 0f, 2f }, { 4f, 9f } };
        Require(Mathf.Abs(DistantTreeManager.SampleSurface(grid, Vector2.zero, 1f, 0.75f, 0.25f) - 4.25f) < 0.0001f,
            "Far ground sampling must follow mesh triangles, not a bilinear patch.");
        Require(DistantTreeManager.SampleSurface(grid, Vector2.zero, 1f, 1f, 1f) == 9f, "Far grid edge mismatch.");
        var tree = new TreeInstanceData(new Vector3(3f, 20f, -5f), Quaternion.identity, Vector3.one, WorldFeatureVariant.MapleTree);
        float priority = DistantTreeManager.StablePriority(new ChunkCoord(-2, 4), tree);
        tree.localPosition.y = 400f;
        Require(priority == DistantTreeManager.StablePriority(new ChunkCoord(-2, 4), tree), "Terrain seating changed tree identity.");
        Debug.Log("Distant tree correctness validation passed: sparse/full placement parity, real terrain sampling, negative coordinates, triangle seating and stable priorities. No performance tests run.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    public static void RunGraphicsBatch()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            Run();
            ValidateShaders();
            ValidateRenderedFade();
            Debug.Log("Distant tree graphics correctness checks passed.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateShaders()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string source = System.IO.File.ReadAllText(path);
            if (!source.Contains("TreeSimpleLitCommon.hlsl") && !source.Contains("DistantTreeFade.hlsl")) continue;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            var material = new Material(shader) { enableInstancing = true };
            try
            {
                ShaderUtil.CompilePass(material, 0, true);

                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,
                        path + ": " + message.message);
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }
    }

    private static void ValidateRenderedFade()
    {
        var shader = Shader.Find("Hidden/DistantTreeFadeValidation");
        Require(shader != null && shader.isSupported, "Test billboard shader is unsupported.");
        var material = new Material(shader) { enableInstancing = true };
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(0, -1, -1), new Vector3(0, 1, -1), new Vector3(0, 1, 1), new Vector3(0, -1, 1) };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var cameraObject = new GameObject("Distant tree validation camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.5f;
        camera.aspect = 1f; camera.transform.position = new Vector3(0, 0, -5);
        var properties = new MaterialPropertyBlock();
        var command = new CommandBuffer();
        RenderTexture previous = RenderTexture.active;
        try
        {
            target.Create();
            int Render(float coverage, float transition, bool enabled = true, bool near = false, bool combined = false)
            {
                properties.SetFloat("_DistantTreeEnabled", enabled ? 1f : 0f);
                properties.SetFloat("_DistantTreeBillboard", near ? 0f : 1f);
                properties.SetFloat("_DistantTreeNearFade", transition);
                properties.SetVectorArray("_DistantTreeFade", new[] { new Vector4(coverage, transition, 0, 0), new Vector4(coverage, transition, 0, 0) });
                command.Clear();
                command.SetRenderTarget(target);
                command.ClearRenderTarget(true, true, Color.black);
                command.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                command.SetGlobalMatrix("unity_MatrixVP", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix);
                command.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                command.DrawMeshInstanced(mesh, 0, material, 0, new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.right * 100f) }, 2, properties);
                if (combined)
                {
                    properties.SetFloat("_DistantTreeBillboard", 0f);
                    command.DrawMeshInstanced(mesh, 0, material, 0, new[] { Matrix4x4.identity }, 1, properties);
                }
                Graphics.ExecuteCommandBuffer(command);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                return pixels.GetPixels32().Count(c => c.r > 20);
            }
            int baseline = Render(1f, 1f, false), visible = Render(1f, 1f), hidden = Render(0f, 1f), half = Render(1f, 0.5f);
            Debug.Log($"Fade pixel coverage: baseline={baseline}, full={visible}, hidden={hidden}, half={half}");
            Require(visible > 100, "Instanced fade helper failed to render in the controlled graphics check.");
            Require(hidden == 0, "Zero-coverage billboard still rendered pixels.");
            int nearHalf = Render(1f, 0.5f, true, true);
            int combined = Render(1f, 0.5f, true, false, true);
            Require(half + nearHalf == visible && combined == visible, "Near and billboard dither masks left a hole or overlap.");
            Require(half > visible * 0.35f && half < visible * 0.65f, "Dither transition did not produce approximately half coverage.");
        }
        finally
        {
            RenderTexture.active = previous;
            command.Dispose(); target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void ValidateCircularFoliageRanges()
    {
        var center = new ChunkCoord(-12, 9);
        Require(FoliageManager.IsWithinChunkRadius(center, center, 0), "Radius zero lost the player chunk.");
        Require(!FoliageManager.IsWithinChunkRadius(center, new ChunkCoord(-11, 10), 1), "Radius one still includes square corners.");
        Require(FoliageManager.IsWithinChunkRadius(center, new ChunkCoord(-9, 13), 5), "3-4-5 boundary was excluded.");
        int included = 0;
        for (int x = -10; x <= 10; x++) for (int z = -10; z <= 10; z++)
        {
            var target = new ChunkCoord(center.x + x, center.z + z);
            bool inside = FoliageManager.IsWithinChunkRadius(center, target, 7);
            Require(inside == (x * x + z * z <= 49), "Foliage range is not circular.");
            Require(inside == (FoliageManager.GetChunkRadialRing(center, target) <= 7), "Tree and ground-cover radius checks disagree.");
            if (inside) included++;
        }
        Require(included == 149, "Unexpected radius-seven chunk footprint.");
        Debug.Log("Circular foliage range checks passed: radius 7 selects 149 chunks instead of the square's 225.");
    }

    private static void ValidateSparsePlanner()
    {
        const int chunkSize = 128, size = chunkSize + 3;
        int treesChecked = 0;
        // Synthetic ecological fields exercise forest/grassland candidates and rock exclusions
        // even when the selected real-world samples happen to be bare or underwater.
        for (int scenario = 0; scenario < 12; scenario++)
        {
            var coord = new ChunkCoord(scenario - 6, scenario * 3 - 12);
            var b = new BiomeType[size, size]; var s = new SurfaceType[size, size];
            var m = new float[size, size]; var t = new float[size, size];
            var slope = new float[size, size]; var river = new float[size, size];
            for (int x = 0; x < size; x++) for (int z = 0; z < size; z++)
            {
                b[x, z] = scenario % 3 == 0 ? BiomeType.Forest : scenario % 3 == 1 ? BiomeType.Grassland
                    : (x / 16 % 2 == 0 ? BiomeType.Forest : BiomeType.Grassland);
                s[x, z] = z > 48 && z < 58 ? SurfaceType.Riverbed : SurfaceType.Grass;
                m[x, z] = 0.75f; t[x, z] = 0.5f;
                slope[x, z] = x > 100 ? 0.15f : 0.005f;
                river[x, z] = z > 48 && z < 58 ? 0.9f : 0.1f;
            }
            var settings = WorldFeatureGenerationSettings.Default;
            settings.grasslandLargeRockPrefabCount = 2;
            var full = WorldFeaturePlanGenerator.Generate(coord, chunkSize, 12345, b, s, m, t, slope, river, settings);
            var sb = new BiomeType[size, size]; var ss = new SurfaceType[size, size];
            var sm = new float[size, size]; var st = new float[size, size];
            var sl = new float[size, size]; var sr = new float[size, size];
            var sparse = WorldFeaturePlanGenerator.GenerateTreePlacements(coord, chunkSize, 12345,
                sb, ss, sm, st, sl, sr, settings, (x, z) =>
                { sb[x, z] = b[x, z]; ss[x, z] = s[x, z]; sm[x, z] = m[x, z]; st[x, z] = t[x, z]; sl[x, z] = slope[x, z]; sr[x, z] = river[x, z]; });
            var expected = full.Placements.Where(p => p.featureType != WorldFeatureType.Bush).ToArray();
            Require(expected.Length == sparse.Placements.Count, "Sparse/full placement count mismatch at " + coord);
            for (int i = 0; i < expected.Length; i++)
            {
                var a = expected[i]; var c = sparse.Placements[i];
                Require(a.featureType == c.featureType && a.variant == c.variant && a.sampleX == c.sampleX && a.sampleZ == c.sampleZ &&
                    a.scale == c.scale && a.rotation == c.rotation, "Sparse/full placement mismatch at " + coord);
                if (a.featureType == WorldFeatureType.Tree) treesChecked++;
            }
        }
        Require(treesChecked > 0, "Planner tests did not exercise accepted trees.");
        Debug.Log("Sparse planner matched " + treesChecked + " accepted trees across 12 ecological cases.");
    }

    private static void ValidateTerrainSampler()
    {
        const int size = 128, seed = 12345;
        const float scale = 100f, water = 0.24f;
        foreach (var coord in new[] { new ChunkCoord(0, 0), new ChunkCoord(-3, 2), new ChunkCoord(8, -7), new ChunkCoord(30, 12) })
        {
            var h = HeightMapGenerator.GenerateTerrainHeightField(size, seed, scale, coord, water, 1.5f);
            var m = ClimateGenerator.GenerateTerrainMoistureMap(size, seed, scale, 5, 0.5f, 2f, coord);
            var t = ClimateGenerator.GenerateTerrainTemperatureMap(size, seed, scale, 5, 0.5f, 2f, coord);
            var b = BiomeMapGenerator.GenerateBiomeMap(h.HeightMap, m, t, h.SlopeMap, h.MountainMaskMap, h.RiverMaskMap, water);
            var s = SurfaceMapGenerator.GenerateSurfaceTypeMap(h.HeightMap, h.SlopeMap, h.RiverMaskMap, b, water);
            var full = WorldFeaturePlanGenerator.Generate(coord, size, seed, b, s, m, t, h.SlopeMap, h.RiverMaskMap, WorldFeatureGenerationSettings.Default);
            var expected = full.Placements.Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
            var sparse = System.Threading.Tasks.Task.Run(() => DistantTreePlacement.Generate(coord, size, seed, scale, 5, 0.5f, 2f, 1f, 10f, water, 1.5f, 12000, WorldFeatureGenerationSettings.Default)).GetAwaiter().GetResult();
            Require(expected.Length == sparse.Length, "Real terrain tree count mismatch at " + coord);
            for (int i = 0; i < expected.Length; i++)
            {
                var p = expected[i]; var tree = sparse[i];
                Require(p.variant == tree.variant && Mathf.Abs(tree.localPosition.x - (p.sampleX - size * 0.5f)) < 0.0001f &&
                    Mathf.Abs(tree.localPosition.z - (p.sampleZ - size * 0.5f)) < 0.0001f, "Real terrain placement mismatch.");
                int x = Mathf.FloorToInt(p.sampleX), z = Mathf.FloorToInt(p.sampleZ);
                float expectedHeight = Mathf.Lerp(Mathf.Lerp(h.HeightMap[x + 1, z + 1], h.HeightMap[x + 2, z + 1], p.sampleX - x),
                    Mathf.Lerp(h.HeightMap[x + 1, z + 2], h.HeightMap[x + 2, z + 2], p.sampleX - x), p.sampleZ - z) * 10f;
                Require(Mathf.Abs(expectedHeight - tree.localPosition.y) < 0.001f, "Real terrain tree height mismatch.");
            }
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
