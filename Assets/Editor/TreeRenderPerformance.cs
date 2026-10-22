using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Synchronized offscreen A/B workload; explicitly not a live-game FPS estimate.
public static class TreeRenderPerformance
{
    const string Folder = ".utmp/vegetation-performance";
    static IEnumerator routine;
    static Camera camera;
    static TreeSettings settings;
    static int mode;
    static bool spatialStudy;
    static bool spatialNoShadows;
    static double submissionCpuMs;
    static long submissionBytes;
    static StandingTreeRenderer standing;
    static readonly List<(TreeInstanceData tree, Matrix4x4 matrix, ChunkCoord chunk)> near = new();
    static readonly List<(WorldFeatureVariant variant, Matrix4x4 matrix, Color tint)> far = new();
    sealed class Proxy : IDisposable
    {
        public DistantTreeGpuBatch Batch;
        public List<int> Slots = new();
        public Material Material;
        public Mesh Mesh;
        public void Dispose() { Batch.Dispose(); Object.DestroyImmediate(Material); Object.DestroyImmediate(Mesh); }
    }
    static readonly List<Dictionary<WorldFeatureVariant, Proxy>> proxies = new();
    static readonly Vector4[] frustum = new Vector4[6];
    static readonly string[] modes = { "empty", "near", "near-no-shadows", "HQ", "HQ-no-shadow-sampling", "HQ-coverage", "HQ-alpha-only", "near-global-batches", "near-global-no-shadows", "near-spatial-2", "near-spatial-4", "near-spatial-8" };
    public static void RunSpatialBatch() { spatialStudy = true; RunBatch(); }
    public static void RunSpatialNoShadowsBatch() { spatialNoShadows = true; RunSpatialBatch(); }
    public static void RunBatch()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity");
            var world = Object.FindAnyObjectByType<WorldManager>();
            settings = (TreeSettings)typeof(WorldManager).GetField("treeSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 0;
            RenderSettings.fog = false; RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.gray;
            var sun = new GameObject("Benchmark sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45, -20, 0);
            // Keep the sun's shadow variant active in far-only and empty modes,
            // which otherwise have no shadow casters at all.
            var caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            caster.name = "Constant shadow-variant fixture";
            caster.transform.position = new Vector3(0, 10, 40);
            camera = new GameObject("Benchmark camera").AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 65; camera.nearClipPlane = .1f; camera.farClipPlane = 650;
            camera.transform.position = new Vector3(0, 1.7f, 0); camera.transform.rotation = Quaternion.identity;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
            if (!spatialStudy) GenerateShaders();
            routine = spatialStudy ? RunSpatialStudy() : Run(); EditorApplication.update += Tick;
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Tick()
    {
        try { if (routine.MoveNext()) return; EditorApplication.update -= Tick; CleanupShaders(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.update -= Tick; CleanupShaders(); EditorApplication.Exit(1); }
    }
    static void CleanupShaders()
    {
        foreach (string source in new[] { "TreeHQOctaImpostor", "SpruceOctaImpostor" })
            for (int variant = 1; variant <= 3; variant++) AssetDatabase.DeleteAsset("Assets/Editor/TreePerf" + source + variant + ".shader");
    }
    static void GenerateShaders()
    {
        foreach (string source in new[] { "TreeHQOctaImpostor", "SpruceOctaImpostor" })
        {
            string text = File.ReadAllText("Assets/Shaders/" + source + ".shader");
            for (int variant = 1; variant <= 3; variant++)
            {
                string modified = text.Replace("Shader \"Custom/" + source + "\"", "Shader \"Hidden/TreePerf/" + source + variant + "\"");
                if (variant == 1) modified = modified.Replace("GetMainLight(TransformWorldToShadowCoord(shadedPositionWS))", "GetMainLight()");
                else
                {
                    string body = "half4 frag(Varyings input) : SV_Target { UNITY_SETUP_INSTANCE_ID(input); ApplyDistantTreeFade(input.positionCS.xy); half4 a = SampleAtlas(TEXTURE2D_ARGS(_AlbedoCoverage, sampler_AlbedoCoverage), input.atlasFrame, input.proxyUV, _AlbedoCoverage_TexelSize.zw); clip(a.a-_Cutoff); ";
                    if (variant == 2) body += "half4 id = SampleMaterialIdAtlasPoint(input.atlasFrame, input.proxyUV); clip(max(id.r,id.g)-.01h); ";
                    body += "return half4(a.rgb,a.a); }";
                    // Locate against renamed source, whose prefix length differs.
                    int s = modified.IndexOf("half4 frag(Varyings input)", StringComparison.Ordinal), b = modified.IndexOf('{', s), e = b + 1, d = 1;
                    while (d > 0) { if (modified[e] == '{') d++; if (modified[e] == '}') d--; e++; }
                    modified = modified.Substring(0, s) + body + modified.Substring(e);
                }
                File.WriteAllText("Assets/Editor/TreePerf" + source + variant + ".shader", modified);
            }
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }
    static void Populate(BiomeType biome)
    {
        near.Clear(); far.Clear();
        var snapshot = TreeGenerationSnapshot.Create(settings, .3f);
        var site = new TreeHabitatSample { Biome = biome, Surface = SurfaceType.Grass, Height = .5f, Moisture = biome == BiomeType.Forest ? .8f : .5f, Temperature = .5f };
        var generator = new LayeredTreeDistribution(1456, snapshot, (x, z) => site, .24f);
        for (int z = -5; z <= 15; z++) for (int x = -15; x <= 15; x++)
        {
            var coord = new ChunkCoord(x, z); var plan = new WorldFeaturePlan(3, 3); generator.Append(plan, coord, 128);
            foreach (var p in plan.Placements)
            {
                var position = new Vector3((x * 128 + p.sampleX) * .3f, 0, (z * 128 + p.sampleZ) * .3f);
                float distance = new Vector2(position.x, position.z).magnitude;
                if (distance > 575) continue;
                FoliageGenerator.GetDeterministicTreeColors(p.variant, 1456, settings.seedOffset, coord, p.sampleX, p.sampleZ, out var leaf, out var bark);
                var tree = new TreeInstanceData(position, p.rotation, p.scale, p.variant, leaf, bark);
                var matrix = Matrix4x4.TRS(position, p.rotation, p.scale);
                if (distance < 192) near.Add((tree, matrix, coord));
                if (distance >= 154) far.Add((p.variant, matrix, leaf));
            }
        }
        for (int variant = 0; variant <= 3; variant++)
        {
            var dictionary = new Dictionary<WorldFeatureVariant, Proxy>(); proxies.Add(dictionary);
            foreach (var group in far.GroupBy(t => t.variant))
            {
                var species = TreeSpeciesCatalog.All.First(s => s.Variant == group.Key);
                var prefab = species.BillboardPrefab(settings); var filter = prefab.GetComponentInChildren<MeshFilter>();
                var material = new Material(prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial) { enableInstancing = true };
                if (variant > 0) material.shader = Shader.Find("Hidden/TreePerf/" + material.shader.name.Substring(7) + variant);
                material.SetFloat("_WindStrength", 0);
                var mesh = Object.Instantiate(filter.sharedMesh); mesh.bounds = DistantTreeGpuBatch.RenderingBounds(mesh.bounds, material);
                var proxy = new Proxy { Mesh = mesh, Material = material, Batch = new DistantTreeGpuBatch(settings.distantTreeCompactShader, mesh, material) };
                dictionary.Add(group.Key, proxy);
                foreach (var tree in group) proxy.Slots.Add(proxy.Batch.Register(tree.matrix, tree.tint, Vector4.zero, 1));
            }
        }
        standing = new StandingTreeRenderer(settings);
        Debug.Log($"TREE_RENDER_POPULATION {biome}: {near.Count} near trees, {far.Count} far trees (before culling; full density stress fixture)");
    }
    static void Draw(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering != camera || mode == 0) return;
        long started = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        if (mode <= 2 || mode >= 7)
        {
            settings.castTreeShadows = mode == 1 || mode == 7 || mode >= 9;
            if (spatialNoShadows) settings.castTreeShadows = false;
            if (mode >= 9) settings.standingTreeBatchSizeChunks = 1 << (mode - 8);
            standing.BeginFrame(camera);
            ChunkCoord? previous = null;
            foreach (var item in near)
            {
                if (mode >= 9) standing.SubmitSpatial(item.chunk, item.tree, item.matrix, 0, 1);
                else
                {
                    if (mode < 7 && previous.HasValue && previous.Value != item.chunk) standing.EndChunk();
                    standing.Submit(item.tree, item.matrix, 0, 1);
                }
                previous = item.chunk;
            }
            if (mode >= 9) standing.EndFrame(); else standing.EndChunk();
        }
        else
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            for (int p = 0; p < 6; p++) frustum[p] = new Vector4(planes[p].normal.x, planes[p].normal.y, planes[p].normal.z, planes[p].distance);
            foreach (var proxy in proxies[mode - 3].Values)
            {
                proxy.Batch.BeginFrame();
                foreach (int slot in proxy.Slots) proxy.Batch.Submit(slot, 0, 1, 1, Vector4.zero);
                proxy.Batch.Draw(settings, camera, camera.transform.position, frustum, 650, 1, 650, 1);
            }
        }
        submissionCpuMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        submissionBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    }
    static Color32[] ReadPixels(RenderTexture target)
    {
        var previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            return texture.GetPixels32();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }
    static IEnumerator RunSpatialStudy()
    {
        using var output = new StreamWriter(Folder + (spatialNoShadows ? "/spatial-batching-no-shadows.csv" : "/spatial-batching.csv"));
        output.WriteLine("workload,yaw,mode,pass,medianMs,p95Ms,submissionMedianMs,maxSubmissionBytes,trees,vertices,draws,meanRgbError,changedPixels");
        RenderPipelineManager.beginCameraRendering += Draw;
        var target = new RenderTexture(2560, 1440, 24); target.Create(); camera.aspect = 16f / 9;
        foreach (string workload in new[] { "meadow", "forest", "forest-4x" })
        {
            Populate(workload == "meadow" ? BiomeType.Grassland : BiomeType.Forest);
            if (workload == "forest-4x")
            {
                var original = near.ToArray();
                foreach (var offset in new[] { new Vector3(2, 0, 1), new Vector3(-2, 0, 1), new Vector3(1, 0, -2) })
                    foreach (var item in original)
                    {
                        var matrix = item.matrix; matrix.m03 += offset.x; matrix.m23 += offset.z;
                        near.Add((item.tree, matrix, item.chunk));
                    }
                near.Sort((a, b) => a.chunk.z != b.chunk.z ? a.chunk.z.CompareTo(b.chunk.z) : a.chunk.x.CompareTo(b.chunk.x));
            }
            var materials = (Dictionary<Material, Material>)typeof(StandingTreeRenderer).GetField("materials", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standing);
            foreach (var material in materials.Values)
                foreach (string name in new[] { "_WindStrength", "_WindFlutterStrength" }) if (material.HasProperty(name)) material.SetFloat(name, 0);
            for (int yaw = 0; yaw < 360; yaw += 45)
            {
                camera.transform.rotation = Quaternion.Euler(0, yaw, 0);
                Color32[] reference = null;
                for (int pass = 0; pass < 2; pass++)
                foreach (int next in (pass == 0 ? new[] { 1, 9, 10, 11, 7 } : new[] { 7, 11, 10, 9, 1 }))
                {
                    mode = next; var timings = new List<double>(); var cpu = new List<double>(); long bytes = 0;
                    for (int frame = 0; frame < 40; frame++)
                    {
                        long started = Stopwatch.GetTimestamp();
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        var request = AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1); request.WaitForCompletion();
                        if (request.hasError) throw new Exception("Spatial benchmark GPU synchronization failed");
                        if (frame >= 16)
                        {
                            timings.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
                            cpu.Add(submissionCpuMs); bytes = Math.Max(bytes, submissionBytes);
                        }
                        yield return null;
                    }
                    var pixels = ReadPixels(target);
                    if (reference == null) reference = pixels;
                    long error = 0; int changed = 0;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        int difference = Math.Abs(pixels[i].r - reference[i].r) + Math.Abs(pixels[i].g - reference[i].g) + Math.Abs(pixels[i].b - reference[i].b);
                        error += difference; if (difference > 9) changed++;
                    }
                    timings.Sort(); cpu.Sort();
                    output.WriteLine($"{workload},{yaw},{modes[mode]},{pass},{timings[12]:F4},{timings[22]:F4},{cpu[12]:F4},{bytes},{near.Count},{standing.RenderStats.vertices},{standing.DrawCalls},{error / (pixels.Length * 3.0 * 255):F8},{changed}"); output.Flush();
                }
                Debug.Log($"SPATIAL_BATCH_PROGRESS {workload} yaw={yaw}");
            }
            standing.Dispose(); foreach (var dictionary in proxies) foreach (var proxy in dictionary.Values) proxy.Dispose(); proxies.Clear();
        }
        target.Release(); Object.DestroyImmediate(target); RenderPipelineManager.beginCameraRendering -= Draw;
        Debug.Log("SPATIAL_BATCH_STUDY_PASS");
    }
    static IEnumerator Run()
    {
        using var output = new StreamWriter(Folder + "/render-ab.csv");
        output.WriteLine("biome,width,height,mode,pass,medianMs,p95Ms,instances,nearVertices,nearDrawCalls");
        RenderPipelineManager.beginCameraRendering += Draw;
        foreach (var biome in new[] { BiomeType.Grassland, BiomeType.Forest })
        {
            Populate(biome);
            foreach (int width in new[] { 1280, 2560 })
            {
                var target = new RenderTexture(width, width * 9 / 16, 24); target.Create(); camera.aspect = 16f / 9;
                for (int pass = 0; pass < 2; pass++)
                foreach (int next in (pass == 0 ? Enumerable.Range(0, modes.Length) : Enumerable.Range(0, modes.Length).Reverse()))
                {
                    mode = next; var timings = new List<double>();
                    for (int frame = 0; frame < 64; frame++)
                    {
                        var timer = Stopwatch.StartNew();
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        var request = AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1); request.WaitForCompletion();
                        if (request.hasError) throw new Exception("GPU synchronization failed");
                        if (frame >= 16) timings.Add(timer.Elapsed.TotalMilliseconds);
                        yield return null;
                    }
                    timings.Sort();
                    bool nearMode = mode is 1 or 2 or 7 or 8;
                    output.WriteLine($"{biome},{width},{target.height},{modes[mode]},{pass},{timings[24]:F4},{timings[45]:F4},{(mode == 0 ? 0 : nearMode ? near.Count : far.Count)},{(nearMode ? standing.RenderStats.vertices : 0)},{(nearMode ? standing.DrawCalls : 0)}"); output.Flush();
                    Debug.Log($"TREE_RENDER_STAGE {biome} {width} {modes[mode]}: {timings[24]:F3} ms synchronized render");
                }
                target.Release(); Object.DestroyImmediate(target);
            }
            standing.Dispose(); foreach (var dictionary in proxies) foreach (var proxy in dictionary.Values) proxy.Dispose(); proxies.Clear();
        }
        RenderPipelineManager.beginCameraRendering -= Draw;
        Debug.Log("TREE_RENDER_PERFORMANCE_PASS");
    }
}
