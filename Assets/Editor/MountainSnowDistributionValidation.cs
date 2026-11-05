using System;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Controlled generated-mountain comparison, independent of the saved scene.</summary>
public static class MountainSnowDistributionValidation
{
    private const int Resolution = 257;
    private const float Span = 4096f;
    private struct Sample
    {
        public float height, mask, river, temperature, moisture, neighborMean;
        public float2 position, gradient;
    }

    public static void RunBatch()
    {
        bool asynchronousShaders = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            MountainSnowValidation.Run();
            SurfaceBlendValidation.Run();
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-snowOutput");
            string output = i >= 0 ? args[i + 1] : "Logs/MountainSnow";
            Directory.CreateDirectory(output);
            Compare(output, false);
            Compare(output, true);
            Debug.Log("MOUNTAIN SNOW DISTRIBUTION PASS: production rules/seams/near-far, identical-input alternatives, real URP renders and warmed Burst timings.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally { ShaderUtil.allowAsyncCompilation = asynchronousShaders; }
    }

    public static void RenderSeparationComparisons(string output)
    {
        Compare(output, false, true);
        Compare(output, true, true);
    }

    private static void Compare(string output, bool sceneSettings, bool separationStudy = false)
    {
        const int seed = 46;
        var erosion = WorldErosionSettings.Default;
        if (sceneSettings)
        {
            erosion.mountainRelief = 20f; erosion.lowlandHeightRatio = .3f;
            erosion.mountainSpatialScale = 3.3f; erosion.mountainSparsity = .01f;
            erosion.lowlandHillVariation = .2f;
        }
        var context = HeightMapGenerator.CreateSamplingContext(seed, .24f, 1.3f, erosion);
        float2 center = float2.zero;
        float best = float.MaxValue;
        for (int x = -12; x <= 12; x++) for (int z = -12; z <= 12; z++)
        {
            var terrain = HeightMapGenerator.SampleTerrainHeight(x * 512, z * 512, 600f, context);
            float score = math.abs(terrain.Height - 8f);
            if (terrain.MountainMask > .6f && score < best) { best = score; center = new float2(x, z) * 512; }
        }
        using var samples = new NativeArray<Sample>(Resolution * Resolution, Allocator.TempJob);
        using var temperatureOffsets = ClimateGenerator.CreateOctaveOffsets(seed + 2000, 1, Allocator.TempJob);
        using var moistureOffsets = ClimateGenerator.CreateOctaveOffsets(seed + 1000, 1, Allocator.TempJob);
        new SampleJob { samples = samples, origin = center - Span / 2, context = context,
            temperatureOffsets = temperatureOffsets, moistureOffsets = moistureOffsets }.Schedule(samples.Length, 64).Complete();
        using var coverage = new NativeArray<float2>(samples.Length, Allocator.TempJob);
        if (!separationStudy) Benchmark(samples, coverage);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        QualitySettings.renderPipeline = GraphicsSettings.defaultRenderPipeline;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.32f, .36f, .42f);
        var sun = new GameObject("Comparison sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.1f;
        sun.transform.rotation = Quaternion.Euler(40, -35, 0);
        RenderSettings.sun = sun;
        var camera = new GameObject("Comparison camera").AddComponent<Camera>();
        camera.enabled = false; camera.nearClipPlane = 1; camera.farClipPlane = 10000;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.35f, .46f, .57f);
        camera.fieldOfView = 45;
        var mesh = BuildMesh(samples);
        var ground = new GameObject("Generated mountains");
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat"));
        ground.AddComponent<MeshRenderer>().sharedMaterial = material;
        material.SetFloat("_ReceiveShadows", 0f);
        material.SetFloat("_SnowRockSeparation", 0f);
        material.SetVector("_TerrainHorizonParams", Vector4.zero);
        var maps = new[] { Control(), Control(), Control() };
        for (int m = 0; m < maps.Length; m++) material.SetTexture("_ControlMap" + m, maps[m]);
        string prefix = sceneSettings ? "scene" : "default";
        try
        {
            for (int mode = 0; mode < 3; mode++)
            {
                new CoverageJob { samples = samples, coverage = coverage, legacy = !separationStudy && mode < 2 }.Schedule(samples.Length, 64).Complete();
                var map0 = new Color32[samples.Length]; var map1 = new Color32[samples.Length];
                double mean = 0; int mixed = 0, snowy = 0, mountain = 0;
                for (int j = 0; j < samples.Length; j++)
                {
                    var s = samples[j];
                    float slope = TerrainSlopePolicy.FromGradient(math.length(s.gradient), 200f);
                    var biome = BiomeClassifier.Classify(s.height, s.moisture, s.temperature, slope, s.mask, s.river, .24f);
                    var surface = SurfaceTypeClassifier.Classify(s.height, slope, s.river, biome, .24f);
                    switch (surface)
                    {
                        case SurfaceType.Snow: map1[j] = new Color32(255, 0, 0, 0); break;
                        case SurfaceType.Cliff: map1[j] = new Color32(0, 255, 0, 0); break;
                        case SurfaceType.Grass: map0[j] = new Color32(0, 0, 255, 0); break;
                        case SurfaceType.Sand: map0[j] = new Color32(255, 0, 0, 0); break;
                        default: map0[j] = new Color32(0, 0, 0, 255); break;
                    }
                    MountainSnow.Apply(ref map0[j], ref map1[j], coverage[j], !separationStudy && mode == 1 ? 1.15f : .633f);
                    if (s.mask > .45f && s.height > 2f)
                    {
                        float weight = map1[j].r / 255f; mountain++; mean += weight;
                        if (weight > .1f && weight < .9f) mixed++;
                        if (weight >= .9f) snowy++;
                    }
                }
                maps[0].SetPixels32(map0); maps[0].Apply(); maps[1].SetPixels32(map1); maps[1].Apply();
                string label = mode == 0 ? "before" : mode == 1 ? "gamma-only" : "after";
                if (separationStudy)
                {
                    material.SetFloat("_SnowRockSeparation", mode == 0 ? 0f : 1f);
                    material.SetFloat("_SnowCoverageThreshold", .5f);
                    material.SetFloat("_SnowBoundaryWidth", mode == 2 ? .06f : .2f);
                    label = mode == 0 ? "blended" : mode == 1 ? "selected" : "narrow";
                }
                Debug.Log($"Snow comparison {prefix}/{label}, center {center}: mountain samples {mountain}, mean {mean / math.max(1,mountain):P1}, mixed {mixed / (float)math.max(1,mountain):P1}, solid snow {snowy / (float)math.max(1,mountain):P1}.");
                camera.orthographic = false;
                camera.transform.position = new Vector3(sceneSettings ? 1100 : -1100, 1100, 1600);
                camera.transform.LookAt(new Vector3(0, 260, 0));
                Capture(camera, Path.Combine(output, prefix + "-" + label + ".png"));
                camera.orthographic = true; camera.orthographicSize = Span * .3f * .52f;
                camera.transform.SetPositionAndRotation(new Vector3(0, 2500, 0), Quaternion.Euler(90, 0, 0));
                Capture(camera, Path.Combine(output, prefix + "-" + label + "-top.png"));
            }
            foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    throw new Exception(message.message);
        }
        finally
        {
            Object.DestroyImmediate(ground); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
            Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(sun.gameObject);
            foreach (var map in maps) Object.DestroyImmediate(map);
            GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQuality;
        }
    }

    private static Texture2D Control()
    {
        var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels32(new Color32[Resolution * Resolution]); texture.Apply(); return texture;
    }

    private static Mesh BuildMesh(NativeArray<Sample> samples)
    {
        var vertices = new Vector3[samples.Length]; var normals = new Vector3[samples.Length];
        var uv = new Vector2[samples.Length]; var colors = new Color[samples.Length];
        var triangles = new int[(Resolution - 1) * (Resolution - 1) * 6];
        int t = 0;
        for (int z = 0; z < Resolution; z++) for (int x = 0; x < Resolution; x++)
        {
            int i = z * Resolution + x; var s = samples[i];
            vertices[i] = new Vector3((x / (float)(Resolution - 1) - .5f) * Span * .3f, s.height * 60f,
                (z / (float)(Resolution - 1) - .5f) * Span * .3f);
            normals[i] = math.normalize(new float3(-s.gradient.x * 200, 1, -s.gradient.y * 200));
            uv[i] = new Vector2(x / (float)(Resolution - 1), z / (float)(Resolution - 1)); colors[i] = Color.white;
            if (x == Resolution - 1 || z == Resolution - 1) continue;
            triangles[t++] = i; triangles[t++] = i + Resolution; triangles[t++] = i + 1;
            triangles[t++] = i + 1; triangles[t++] = i + Resolution; triangles[t++] = i + Resolution + 1;
        }
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32, vertices = vertices, normals = normals, uv = uv, colors = colors, triangles = triangles };
        mesh.RecalculateBounds(); return mesh;
    }

    private static void Capture(Camera camera, string path)
    {
        var rt = new RenderTexture(1200, 900, 24); var texture = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = rt;
            // Warm the pipeline and all branches before reading the comparison frame.
            camera.Render(); camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); }
    }

    [BurstCompile(CompileSynchronously = true)]
    private struct SampleJob : IJobParallelFor
    {
        public float2 origin;
        public TerrainHeightSamplingContext context;
        [ReadOnly] public NativeArray<float2> temperatureOffsets, moistureOffsets;
        [WriteOnly] public NativeArray<Sample> samples;
        private float Height(float2 p) => HeightMapGenerator.SampleTerrainHeightNative(p.x, p.y, 600f,
            context.RiverSeed, .24f, context.MountainHorizontalScale, context.Erosion).Height;
        public void Execute(int i)
        {
            float2 p = origin + new float2(i % Resolution, i / Resolution) * (Span / (Resolution - 1));
            var terrain = HeightMapGenerator.SampleTerrainHeightNative(p.x, p.y, 600f, context.RiverSeed,
                .24f, context.MountainHorizontalScale, context.Erosion);
            float left = Height(p - new float2(4, 0)), right = Height(p + new float2(4, 0));
            float down = Height(p - new float2(0, 4)), up = Height(p + new float2(0, 4));
            samples[i] = new Sample { position = p, height = terrain.Height, mask = terrain.MountainMask,
                river = terrain.RiverMask, gradient = new float2(right - left, up - down) / 8,
                neighborMean = (left + right + down + up) / 4,
                temperature = ClimateGenerator.SampleWorldClimate01(p.x, p.y, 46, 600, true, .05f, 10, 1, temperatureOffsets),
                moisture = ClimateGenerator.SampleWorldClimate01(p.x, p.y, 46, 600, false, .05f, 10, 1, moistureOffsets) };
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    private struct CoverageJob : IJobParallelFor
    {
        public bool legacy;
        [ReadOnly] public NativeArray<Sample> samples;
        [WriteOnly] public NativeArray<float2> coverage;
        public void Execute(int i)
        {
            var s = samples[i];
            coverage[i] = legacy ? Legacy(s) : MountainSnow.Evaluate(s.position, 46, s.height, s.mask,
                s.river, .24f, s.temperature, s.moisture, s.gradient, s.neighborMean, 200);
        }
    }

    // Frozen pre-change reference, used only by this explicit editor comparison.
    private static float2 Legacy(Sample s)
    {
        float influence = math.smoothstep(.25f, .45f, s.mask) * math.smoothstep(.84f, 1.74f, s.height);
        if (influence <= 0) return float2.zero;
        if (s.height <= .24f + TerrainWaterSettings.ShoreBand || s.river >= TerrainWaterSettings.RiverBankThreshold) return new float2(0,influence);
        float3 normal = math.normalize(new float3(-s.gradient.x * 200, 1, -s.gradient.y * 200));
        float shelter = math.clamp((s.neighborMean - s.height) * 200 / 4, -1, 1);
        float2 offset = new float2(46,46);
        float line = MountainSnow.Snowline(s.temperature,s.moisture) - normal.z * .35f - normal.x * .18f
            - shelter * .55f + noise.cnoise(s.position / 180 + offset) * .4f;
        float altitude = math.smoothstep(line - .55f, line + 1.9f, s.height);
        altitude = math.saturate(altitude + noise.cnoise(s.position / 24 + offset + 31) * .22f * (4 * altitude * (1 - altitude)));
        float retention = math.sqrt(math.smoothstep(.0872f,.5736f,normal.y));
        return new float2(math.saturate(altitude * retention * (1 - math.saturate(-shelter) * .16f)),influence);
    }

    private static void Benchmark(NativeArray<Sample> samples, NativeArray<float2> coverage)
    {
        var old = new double[15]; var current = new double[15];
        // Warm both compiled paths before measurement; alternate order to reduce thermal/order bias.
        for (int iteration = -2; iteration < old.Length; iteration++) for (int pass = 0; pass < 2; pass++)
        {
            bool legacy = (iteration & 1) == pass;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            new CoverageJob { samples = samples, coverage = coverage, legacy = legacy }.Schedule(samples.Length,64).Complete();
            timer.Stop();
            if (iteration >= 0) (legacy ? old : current)[iteration] = timer.Elapsed.TotalMilliseconds;
        }
        Array.Sort(old); Array.Sort(current);
        Debug.Log($"Snow warmed Burst Evaluate, {samples.Length} identical samples: before median {old[7]:F3} ms, after {current[7]:F3} ms; no extra height samples, maps or runtime draw calls. This is generation work, not a live FPS benchmark.");
    }
}
