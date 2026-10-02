using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class TerrainHorizonShadowValidation
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type SystemType = typeof(TerrainHorizonShadowSystem);
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Horizon Shadows")]
    public static void Run()
    {
        checks = 0;
        Shader shader = Shader.Find("Custom/StylizedTerrainURP");
        Check(shader != null, "Missing terrain shader.");
        var material = new Material(shader);
        try
        {
            ValidateShader(material);
            ValidateGpu(material);
            ValidateFields(material);
            ValidateWorker(material);
            ValidatePooling(material);
            Check(TerrainHorizonShadowSystem.GetResolution(128, 128) == 5, "Near grid too dense.");
            foreach (int size in new[] { 512, 1024, 2048 })
                Check(TerrainHorizonShadowSystem.GetResolution(size, 128) == 17, "Far grid too dense.");
            Check(TerrainHorizonShadowSystem.GetResolution(4096, 128) == 33, "Largest patch spacing changed.");
            Debug.Log($"TERRAIN HORIZON PASS: {checks} checks; flat terrain, off-tile ridge directionality, negative coordinates, shared near/far edges, actual background/Burst sampling, cache bounds, pooling, and terrain shader variants.");
        }
        finally { Object.DestroyImmediate(material); }
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void RunRegressionBatch()
    {
        try
        {
            Run();
            SurfaceBlendValidation.Run();
            TerrainSlopeValidation.Validate();
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static TerrainHorizonShadowSystem Create(float scale = 1f, float range = 300f, int capacity = 8192) =>
        new TerrainHorizonShadowSystem(128, 42, 600f, scale, 100f,
            new TerrainWaterSettings(0.24f * 100f * scale, 100f, scale), 1.3f, WorldErosionSettings.Default,
            new TerrainHorizonShadowSettings { searchDistance = range, heightBias = 0f,
                cachedHeightSamples = capacity, cachedInactiveTiles = 0 });

    private static float[,] Source(int x, int z, int size, int resolution, int halo, Func<float, float, float> height)
    {
        var result = new float[resolution + 2 * halo, resolution + 2 * halo];
        for (int ix = 0; ix < result.GetLength(0); ix++)
            for (int iz = 0; iz < result.GetLength(1); iz++)
                result[ix, iz] = height(x + (ix - halo) * size / (float)(resolution - 1),
                    z + (iz - halo) * size / (float)(resolution - 1));
        return result;
    }

    private static object LastTile(TerrainHorizonShadowSystem system)
    {
        object last = null;
        foreach (DictionaryEntry entry in (IDictionary)SystemType.GetField("tiles", Private).GetValue(system)) last = entry.Value;
        return last;
    }

    private static Color32[][] Generate(TerrainHorizonShadowSystem system, object tile, float[,] source)
    {
        SystemType.GetMethod("Generate", Private).Invoke(system, new[] { tile, source });
        object result = SystemType.GetField("completed", Private).GetValue(system);
        Type type = result.GetType();
        var error = (Exception)type.GetField("Error").GetValue(result);
        if (error != null) throw error;
        return new[] { (Color32[])type.GetField("Map0").GetValue(result), (Color32[])type.GetField("Map1").GetValue(result) };
    }

    private static void Fill(TerrainHorizonShadowSystem system, Func<float, float, float> height)
    {
        var cache = (Dictionary<int2, float>)SystemType.GetField("heightCache", Private).GetValue(system);
        var order = (Queue<int2>)SystemType.GetField("heightCacheOrder", Private).GetValue(system);
        // Covers every bilinear corner in these synthetic fixtures, avoiding procedural overrides.
        for (int x = -1024; x <= 2048; x += 32)
            for (int z = -1024; z <= 2048; z += 32)
            {
                var key = new int2(x, z);
                cache.Add(key, height(x, z)); order.Enqueue(key);
            }
    }

    private static void ValidateFields(Material material)
    {
        float Flat(float x, float z) => 0.25f;
        using (var flat = Create())
        {
            Fill(flat, Flat);
            var source = Source(-128, -128, 128, 129, 1, Flat);
            using var binding = flat.Bind(material, new int2(-128, -128), 128, source, 1);
            var result = Generate(flat, LastTile(flat), source);
            foreach (Color32[] map in result)
                foreach (Color32 pixel in map)
                    Check(pixel.r == 0 && pixel.g == 0 && pixel.b == 0 && pixel.a == 0, "Flat terrain falsely occluded.");
            Check(Math.Abs(TerrainHorizonShadowSystem.SampleSource(source, 1, 0f, 1f) - 0.25f) < 1e-6f, "Padded corner sampling failed.");
        }

        float Ridge(float x, float z) => x >= 256f && x <= 384f ? 2f : 0f;
        using var ridge = Create(capacity: 262144);
        Fill(ridge, Ridge);
        var nearSource = Source(0, 0, 128, 129, 1, Ridge);
        using var nearBinding = ridge.Bind(material, int2.zero, 128, nearSource, 1);
        var near = Generate(ridge, LastTile(ridge), nearSource);
        Color32 centerEast = near[0][2 * 5 + 2], centerWest = near[1][2 * 5 + 2];
        Check(centerEast.r > 50, "An off-tile eastern ridge did not obstruct the sun.");
        Check(centerWest.r == 0, "An eastern ridge incorrectly shadows western light.");

        // The near tile's east edge and a far tile's west edge share receiver positions.
        var farMaterial = new Material(material);
        try
        {
            var farSource = Source(128, 0, 512, 65, 0, Ridge);
            using var farBinding = ridge.Bind(farMaterial, new int2(128, 0), 512, farSource, 0);
            var far = Generate(ridge, LastTile(ridge), farSource);
            for (int z = 0; z < 5; z++)
                for (int map = 0; map < 2; map++)
                    Check(near[map][z * 5 + 4].Equals(far[map][z * 17]), "Near/far horizon boundary mismatch.");
        }
        finally { Object.DestroyImmediate(farMaterial); }
        Check(Math.Abs(TerrainHorizonShadowSystem.EncodeSlope(1f) - 128) <= 1, "45 degree horizon encoding failed.");
    }

    private static void ValidateWorker(Material material)
    {
        // Exercise cache misses against the real eroded procedural world on the actual worker.
        using var system = Create(0.3f, 6000f);
        var context = HeightMapGenerator.CreateSamplingContext(42, 0.24f, 1.3f, WorldErosionSettings.Default);
        float Height(float x, float z) => HeightMapGenerator.SampleTerrainHeight(x, z, 600f, context).Height;
        var source = Source(0, 0, 512, 65, 0, Height);
        using var binding = system.Bind(material, int2.zero, 512, source, 0);
        var timer = Stopwatch.StartNew();
        system.Update(Vector3.zero, false);
        Check(!(bool)SystemType.GetField("busy", Private).GetValue(system), "Shadow task ignored foreground-work gate.");
        system.Update(Vector3.zero, true);
        while (SystemType.GetField("completed", Private).GetValue(system) == null && timer.Elapsed.TotalSeconds < 60)
            Thread.Sleep(10);
        Check(SystemType.GetField("completed", Private).GetValue(system) != null, "Background horizon worker timed out.");
        system.Update(Vector3.zero, false);
        Check(material.GetTexture("_TerrainHorizon0") != null && material.GetTexture("_TerrainHorizon1") != null, "Worker result not uploaded.");
        var cache = (Dictionary<int2, float>)SystemType.GetField("heightCache", Private).GetValue(system);
        Check(cache.Count > 0 && cache.Count <= 8192, "Shared height cache is unbounded.");
        Vector4 uv = material.GetVector("_TerrainHorizonUV");
        Check(Math.Abs(uv.z - 0.5f / 17f) < 1e-6f, "Corner is not mapped to texel center.");
        Debug.Log($"HORIZON BENCHMARK: 17x17, 8 directions, 32 steps, 6000-unit range: cold worker + upload {timer.Elapsed.TotalMilliseconds:F1} ms; retained heights {cache.Count}. Includes first Burst dispatch; not a steady-state frame cost.");
    }

    private static void ValidatePooling(Material material)
    {
        using var system = Create();
        var water = new Material(material);
        var runtime = new ChunkRuntime(new ChunkRecord(new ChunkCoord(0, 0)), 128, 1f, null, material, water, true);
        try
        {
            var source = Source(0, 0, 128, 129, 1, (x, z) => 0f);
            runtime.SetTerrainHorizon(system, 0, 0, 128, source, 1);
            object tile = LastTile(system);
            var materials = (HashSet<Material>)tile.GetType().GetField("Materials").GetValue(tile);
            Check(materials.Count == 1, "Runtime did not bind horizon data.");
            runtime.ReleaseToPool(null);
            Check(materials.Count == 0, "Pooled runtime retains old horizon binding.");
            runtime.Reinitialize(new ChunkRecord(new ChunkCoord(-1, -1)), 128, 1f, null, true);
            Check(materials.Count == 0, "Reused runtime restored stale horizon binding.");
            system.Update(Vector3.zero, false);
            Check(((IDictionary)SystemType.GetField("tiles", Private).GetValue(system)).Count == 0, "Unbound pending tile was not evicted.");
            var runtimeMaterial = typeof(ChunkRuntime).GetField("runtimeTerrainMaterial", Private).GetValue(runtime) as Material;
            runtimeMaterial.SetFloat("_ReceiveShadows", 0f);
            runtime.SetTerrainHorizon(system, 0, 0, 128, source, 1);
            Check(((IDictionary)SystemType.GetField("tiles", Private).GetValue(system)).Count == 0, "Shadow-disabled material still generates horizon data.");
        }
        finally
        {
            // Runtime destruction uses deferred Destroy in Play mode; validation runs in edit mode.
            ((TerrainHorizonShadowSystem.Binding)typeof(ChunkRuntime).GetField("terrainHorizonBinding", Private).GetValue(runtime))?.Dispose();
            Object.DestroyImmediate(typeof(ChunkRuntime).GetField("runtimeTerrainMaterial", Private).GetValue(runtime) as Material);
            Object.DestroyImmediate(typeof(ChunkRuntime).GetField("runtimeWaterMaterial", Private).GetValue(runtime) as Material);
            Object.DestroyImmediate(runtime.Root);
            Object.DestroyImmediate(water);
        }
    }

    private static void ValidateGpu(Material source)
    {
        var material = new Material(source);
        var mesh = new Mesh();
        var cameraObject = new GameObject("Horizon GPU validation camera");
        var camera = cameraObject.AddComponent<Camera>();
        var surface = new GameObject("Horizon GPU validation surface") { layer = 31 };
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = surface.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        var lightObject = new GameObject("Horizon GPU validation light") { layer = 31 };
        var sun = lightObject.AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 2; sun.color = Color.white;
        sun.shadows = LightShadows.None; sun.cullingMask = 1 << 31;
        Light previousSun = RenderSettings.sun;
        bool previousFog = RenderSettings.fog;
        RenderSettings.sun = sun; RenderSettings.fog = false;
        var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true);
        var first = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        var zero = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        var control = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.enabled = false;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            camera.transform.position = new Vector3(64, 100, 64);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            camera.orthographic = true; camera.orthographicSize = 64; camera.aspect = 1;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 200;
            mesh.vertices = new[] { Vector3.zero, new Vector3(128, 0, 0), new Vector3(128, 0, 128), new Vector3(0, 0, 128) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            first.SetPixel(0, 0, new Color(0.5f, 0f, 0f, 0f)); first.Apply();
            zero.SetPixel(0, 0, Color.clear); zero.Apply();
            control.SetPixel(0, 0, new Color(0, 0, 1, 0)); control.Apply();
            material.SetTexture("_TerrainHorizon0", first); material.SetTexture("_TerrainHorizon1", zero);
            material.SetTexture("_ControlMap0", control); material.SetTexture("_ControlMap1", zero); material.SetTexture("_ControlMap2", zero);
            material.SetVector("_TerrainHorizonUV", new Vector4(0, 0, 0.5f, 0.5f));
            material.SetFloat("_AmbientStrength", 0.1f); material.SetFloat("_ReceiveShadows", 1f);
            material.DisableKeyword("_MAIN_LIGHT_SHADOWS"); material.DisableKeyword("_MAIN_LIGHT_SHADOWS_CASCADE");
            material.DisableKeyword("_MAIN_LIGHT_SHADOWS_SCREEN"); material.EnableKeyword("_CLUSTER_LIGHT_LOOP");
            target.Create();

            Color lastPixel = Color.black;
            float Render(float x, float elevation, float strength, float tint = 0f)
            {
                material.SetVector("_TerrainHorizonParams", new Vector4(strength, 3 * Mathf.Deg2Rad, tint, 0));
                Vector3 light = new Vector3(x * Mathf.Cos(elevation * Mathf.Deg2Rad), Mathf.Sin(elevation * Mathf.Deg2Rad), 0);
                sun.transform.rotation = Quaternion.LookRotation(-light, Vector3.forward);
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); readback.Apply();
                Color pixel = readback.GetPixel(16, 16);
                lastPixel = pixel;
                return pixel.r + pixel.g + pixel.b;
            }
            float unshadowed = Render(1, 15, 0), blocked = Render(1, 15, 1), west = Render(-1, 15, 1), highSun = Render(1, 70, 1);
            Check(unshadowed > 0.01f, "GPU terrain produced no lighting.");
            Check(blocked > 0 && blocked < unshadowed * 0.8f, "GPU horizon did not reduce direct light while retaining ambient.");
            Check(Math.Abs(west - unshadowed) < unshadowed * 0.05f, "GPU light azimuth/channel mapping is incorrect.");
            Check(highSun > unshadowed, "GPU high sun remains obstructed by a lower horizon.");
            Render(1, 15, 1);
            Color beforeTint = lastPixel;
            material.SetColor("_TerrainHorizonTint", new Color(0.2f, 0.3f, 0.5f, 1f));
            float tinted = Render(1, 15, 1, 1);
            Check(tinted < blocked * 0.6f, "Shadow tint does not darken ambient illumination.");
            Color expectedTint = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? new Color(0.2f, 0.3f, 0.5f).linear : new Color(0.2f, 0.3f, 0.5f);
            Debug.Log($"HORIZON TINT GPU: R multiplier {lastPixel.r / beforeTint.r:F4}, B multiplier {lastPixel.b / beforeTint.b:F4}; expected {expectedTint.r:F4}/{expectedTint.b:F4}.");
            Check(Math.Abs(lastPixel.r / beforeTint.r - expectedTint.r) < 0.025f &&
                Math.Abs(lastPixel.b / beforeTint.b - expectedTint.b) < 0.025f, "Shadow tint color multiplier is incorrect.");
            Check(Math.Abs(Render(-1, 15, 1, 1) - west) < west * 0.05f, "Tint darkens sunlit terrain.");
            material.SetFloat("_ReceiveShadows", 0f);
            Check(Math.Abs(Render(1, 15, 1, 1) - unshadowed) < unshadowed * 0.05f, "Receive Shadows toggle does not bypass horizon shadows/tint.");
            Debug.Log($"HORIZON GPU PASS: low eastern sun {unshadowed:F3} -> {blocked:F3}, western light {west:F3}, high sun {highSun:F3}.");
        }
        finally
        {
            RenderTexture.active = previous;
            RenderSettings.sun = previousSun; RenderSettings.fog = previousFog;
            Object.DestroyImmediate(surface); Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(material); Object.DestroyImmediate(mesh); Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(first); Object.DestroyImmediate(zero); Object.DestroyImmediate(control);
            Object.DestroyImmediate(readback); target.Release(); Object.DestroyImmediate(target);
        }
    }

    private static void ValidateShader(Material material)
    {
        foreach (string shadow in new[] { "", "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE" })
            foreach (bool clustered in new[] { false, true })
            {
                material.DisableKeyword("_MAIN_LIGHT_SHADOWS");
                material.DisableKeyword("_MAIN_LIGHT_SHADOWS_CASCADE");
                if (shadow.Length > 0) material.EnableKeyword(shadow);
                if (clustered) material.EnableKeyword("_CLUSTER_LIGHT_LOOP"); else material.DisableKeyword("_CLUSTER_LIGHT_LOOP");
                ShaderUtil.CompilePass(material, 0, true);
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    Check(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            }
    }

    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception(message);
    }
}
