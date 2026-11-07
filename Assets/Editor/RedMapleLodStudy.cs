using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Run via Unity batch mode with graphics enabled. The fixture uses the same
// instanced draw path and materials as the streaming world.
public static class RedMapleLodStudy
{
    const string Full = "Assets/Prefabs/RedMaple_LOD0_v03.prefab";
    const string Models = "Assets/Models/Trees/RedMapleLevels/";
    const string Prototype = "Assets/Prefabs/RedMaple_v03_DetailLevels.prefab";
    const string Output = ".utmp/red-maple-lods";
    const float Scale = 3.5f;
    const WorldFeatureVariant Variant = WorldFeatureVariant.GrasslandMapleTree;

    static GameObject Level(int level, Transform parent)
    {
        string path = level == 0 ? Full : Models + $"RedMaple_v03_LOD{level}.fbx";
        var obj = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        obj.transform.SetParent(parent, false);
        obj.name = "LOD" + level;
        if (level == 0) return obj;
        foreach (var part in obj.GetComponentsInChildren<MeshRenderer>())
        {
            string name = part.name.StartsWith("Bark", StringComparison.Ordinal) ?
                "M_RedMapleBark_Stylized" : "M_RedMaple_Leaf";
            part.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                $"Assets/Materials/M_Trees/RedMaple/{name}.mat");
        }
        return obj;
    }

    static void BuildPrefab()
    {
        var thresholds = Environment.GetCommandLineArgs()
            .SkipWhile(arg => arg != "-mapleThresholds").Skip(1).Take(2)
            .Select(arg => float.Parse(arg, CultureInfo.InvariantCulture)).ToArray();
        if (thresholds.Length != 2 || thresholds[0] > 1 || thresholds[0] <= thresholds[1] || thresholds[1] <= 0)
            throw new ArgumentException("Supply two descending -mapleThresholds values from the study.");
        var root = new GameObject("RedMaple_v03_DetailLevels");
        try
        {
            var group = root.AddComponent<LODGroup>();
            var lods = new LOD[3];
            for (int i = 0; i < lods.Length; ++i)
            {
                var obj = Level(i, root.transform);
                lods[i] = new LOD(i < thresholds.Length ? thresholds[i] : 0f,
                    obj.GetComponentsInChildren<MeshRenderer>())
                {
                    fadeTransitionWidth = i == 0 ? .30f : i == 1 ? .08f : 0f
                };
            }
            group.fadeMode = LODFadeMode.CrossFade;
            group.SetLODs(lods);
            group.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, Prototype);
            Debug.Log("RED MAPLE PREFAB SAVED " + Prototype);
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static void RunBatch()
    {
        try { new Fixture().Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    public static void ValidateGpuBatch()
    {
        try { new Fixture().Run(validateGpu: true); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    public static void ValidateAllStandingGpuBatch()
    {
        try { new Fixture().Run(validateGpu: true, allSpecies: true); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    public static void BuildAndRunBatch()
    {
        try
        {
            BuildPrefab();
            new Fixture().Run();
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    sealed class Fixture
    {
        const int Width = 2560, Height = 1440;
        readonly StandingTreeRenderer[] renderers = new StandingTreeRenderer[4];
        readonly TreeSettings[] settings = new TreeSettings[4];
        readonly Vector4[] frustum = new Vector4[6];
        readonly Matrix4x4[] grove = new Matrix4x4[144];
        readonly System.Random random = new System.Random(731);
        Camera camera;
        Light sun;
        RenderTexture target;
        Texture2D pixels;
        DistantTreeGpuBatch billboard;
        Material billboardMaterial;
        Mesh billboardMesh;
        int slot;
        int mode;
        bool forest;
        bool validateGpu;
        float handoff;
        Bounds bounds;
        TreeInstanceData single;

        public void Run(bool validateGpu = false, bool allSpecies = false)
        {
            this.validateGpu = validateGpu;
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            QualitySettings.SetQualityLevel(Array.IndexOf(QualitySettings.names, "PC"));
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            QualitySettings.renderPipeline = pipeline;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.vSyncCount = 0;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .56f, .60f);
            ShaderUtil.allowAsyncCompilation = false;
            sun = new GameObject("Study sunlight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(42, -30, 0);
            sun.shadows = LightShadows.Soft;
            camera = new GameObject("Study camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 60;
            camera.farClipPlane = 680;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.53f, .69f, .83f);
            target = new RenderTexture(Width, Height, 24);
            target.Create();
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            single = new TreeInstanceData(Vector3.zero, Quaternion.identity, Vector3.one * Scale, Variant,
                new Color32(130, 158, 76, 255), new Color32(132, 111, 86, 255));
            for (int level = 0; level < 4; ++level)
            {
                GameObject prefab;
                if (level < 3)
                {
                    prefab = Level(level, null);
                    prefab.transform.position = Vector3.one * 10000;
                }
                else
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prototype);
                    if (prefab == null) break;
                    var group = prefab.GetComponent<LODGroup>();
                    Debug.Log("RED MAPLE PREFAB LEVELS " + string.Join(", ", group.GetLODs()
                        .Select(lod => $"{lod.screenRelativeTransitionHeight}: {lod.renderers.Length} renderers")));
                }
                settings[level] = new TreeSettings { grasslandMapleTreePrefab = prefab, castTreeShadows = true,
                    receiveTreeShadows = true, distantTreeDensity = 1f,
                    standingTreeCompactShader = validateGpu
                        ? AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/StandingTreeCompact.compute") : null };
                renderers[level] = validateGpu ? CreateWithoutMovingWind(prefab, settings[level]) :
                    new StandingTreeRenderer(settings[level]);
            }
            var billboardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/RedMapleHQ_OctaImpostor_Runtime.prefab");
            billboardMaterial = new Material(billboardPrefab.GetComponent<MeshRenderer>().sharedMaterial) { enableInstancing = true };
            billboardMaterial.SetFloat("_DistantTreeEnabled", 1);
            billboardMaterial.SetFloat("_DistantTreeBillboard", 1);
            billboardMesh = Object.Instantiate(billboardPrefab.GetComponent<MeshFilter>().sharedMesh);
            billboardMesh.bounds = DistantTreeGpuBatch.RenderingBounds(billboardMesh.bounds, billboardMaterial);
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/DistantTreeCompact.compute");
            billboard = new DistantTreeGpuBatch(compute, billboardMesh, billboardMaterial);
            bounds = renderers[0].BoundsFor(Variant, Matrix(single));
            Debug.Log($"RED MAPLE BOUNDS {bounds} lodBias={QualitySettings.lodBias} gpu={SystemInfo.graphicsDeviceName}");
            if (renderers[3] != null)
            {
                Bounds prototype = renderers[3].BoundsFor(Variant, Matrix(single));
                Debug.Log($"RED MAPLE PROTOTYPE BOUNDS {prototype}");
                if (prototype.size.y > bounds.size.y * 1.1f)
                    throw new InvalidOperationException("Reduced meshes inflated the instanced tree bounds.");
            }
            RenderPipelineManager.beginCameraRendering += Draw;
            try
            {
                if (validateGpu) CompareGpu(allSpecies);
                else
                {
                    CompareViews();
                    CompareShadows();
                    TimeGrove();
                }
                Debug.Log("RED MAPLE STUDY COMPLETE");
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= Draw;
                foreach (var renderer in renderers) renderer?.Dispose();
                billboard.Dispose();
                Object.DestroyImmediate(billboardMesh);
                Object.DestroyImmediate(billboardMaterial);
                Object.DestroyImmediate(pixels);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        static Matrix4x4 Matrix(TreeInstanceData tree) =>
            Matrix4x4.TRS(tree.localPosition, tree.localRotation, tree.localScale);

        static StandingTreeRenderer CreateWithoutMovingWind(GameObject prefab, TreeSettings settings)
        {
            var values = new List<(Material material, string property, float value)>();
            foreach (var material in prefab.GetComponentsInChildren<MeshRenderer>()
                .SelectMany(renderer => renderer.sharedMaterials).Distinct())
            foreach (var property in new[] { "_WindSpeed", "_WindFlutterSpeed" })
            {
                if (!material.HasProperty(property)) continue;
                values.Add((material, property, material.GetFloat(property)));
                material.SetFloat(property, 0);
            }
            try { return new StandingTreeRenderer(settings); }
            finally
            {
                foreach (var value in values) value.material.SetFloat(value.property, value.value);
            }
        }

        void Draw(ScriptableRenderContext _, Camera rendering)
        {
            if (rendering != camera || mode == -2) return;
            if (mode >= 0)
            {
                var standing = renderers[mode];
                standing.BeginFrame(camera);
                if (forest)
                {
                    foreach (var matrix in grove)
                    {
                        var tree = single;
                        tree.localScale = new Vector3(matrix.m00, matrix.m10, matrix.m20).magnitude * Vector3.one;
                        var chunk = new ChunkCoord(Mathf.FloorToInt(matrix.m03 / 38.4f),
                            Mathf.FloorToInt(matrix.m23 / 38.4f));
                        standing.SubmitSpatial(chunk, tree, matrix, 0, 1);
                    }
                }
                else if (validateGpu) standing.SubmitSpatial(default, single, Matrix(single), handoff, 1);
                else standing.Submit(single, Matrix(single), handoff, 1);
                standing.EndFrame();
            }
            if (handoff > 0 && !forest)
            {
                billboard.BeginFrame();
                billboard.Submit(slot, 0, 1, handoff, Vector4.zero);
                billboard.Draw(settings[0], camera, camera.transform.position, frustum, 650, 1, 650, 1);
            }
        }

        void Render() => RenderPipeline.SubmitRenderRequest(camera,
            new UniversalRenderPipeline.SingleCameraRequest { destination = target });

        Color32[] Capture(int level, float fade = 0)
        {
            mode = level;
            handoff = fade;
            Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            RenderTexture.active = previous;
            return pixels.GetPixels32();
        }

        static int Difference(Color32 a, Color32 b) =>
            Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);

        static string Compare(Color32[] reference, Color32[] sample, Color32 clear)
        {
            int count = 0, total = 0, intersection = 0, union = 0;
            long error = 0;
            for (int i = 0; i < reference.Length; ++i)
            {
                bool a = Difference(reference[i], clear) > 12, b = Difference(sample[i], clear) > 12;
                if (a) count++;
                if (b) total++;
                if (a && b) intersection++;
                if (a || b) { union++; error += Difference(reference[i], sample[i]); }
            }
            if (count == 0 || total == 0) throw new InvalidOperationException("Empty tree capture.");
            return FormattableString.Invariant($"{count},{total / (double)count:F4},{intersection / (double)union:F4},{error / (765.0 * union):F4}");
        }

        void CompareViews()
        {
            sun.shadows = LightShadows.None;
            using var csv = new StreamWriter(Output + "/views.csv");
            csv.WriteLine("yaw,distance,representation,biasAdjustedHeight,referencePixels,coverageRatio,silhouetteOverlap,meanRgbError,meshTriangles");
            var clear = Capture(-2)[0];
            var selectedTriangleCounts = new HashSet<long>();
            foreach (int yaw in new[] { 0, 69, 142 })
            {
                single.localRotation = Quaternion.Euler(0, yaw, 0);
                slot = billboard.Register(Matrix(single), (Color)single.leafTint, Vector4.zero, 1);
                foreach (int distance in new[] { 44, 70, 90, 100, 110, 124, 136, 145, 154, 164, 173, 192, 216, 230 })
                {
                    camera.transform.position = new Vector3(0, 1.7f, -distance);
                    camera.transform.LookAt(bounds.center);
                    float height = bounds.size.y * QualitySettings.lodBias /
                        (2 * Mathf.Tan(Mathf.PI / 6) * Vector3.Distance(bounds.center, camera.transform.position));
                    var reference = Capture(0);
                    bool save = yaw == 0 && (distance == 110 || distance == 145 || distance == 173);
                    if (save)
                        SaveCrop($"{distance}m-lod0");
                    for (int level = 1; level <= (renderers[3] == null ? 2 : 3); ++level)
                    {
                        var sample = Capture(level);
                        if (level == 3) selectedTriangleCounts.Add(renderers[level].RenderStats.triangles);
                        csv.WriteLine(FormattableString.Invariant($"{yaw},{distance},{level},{height:F4},{Compare(reference, sample, clear)},{renderers[level].RenderStats.triangles}"));
                        if (save)
                            SaveCrop($"{distance}m-lod{level}");
                    }
                    if (renderers[3] != null && distance >= 154)
                    {
                        float blend = Mathf.InverseLerp(153.6f, 192f, distance);
                        var sample = Capture(3, blend);
                        csv.WriteLine(FormattableString.Invariant($"{yaw},{distance},world,{height:F4},{Compare(reference, sample, clear)},{renderers[3].RenderStats.triangles}"));
                        if (yaw == 0 && distance == 173) SaveCrop("173m-world");
                    }
                    if (distance < 136) continue;
                    foreach (float fade in new[] { .5f, 1f })
                    {
                        var sample = Capture(fade == 1 ? -1 : 2, fade);
                        csv.WriteLine(FormattableString.Invariant($"{yaw},{distance},card-{fade:F1},{height:F4},{Compare(reference, sample, clear)},{(fade == 1 ? 0 : renderers[2].RenderStats.triangles)}"));
                        if (yaw == 0 && distance == 173) SaveCrop($"173m-card-{fade:F1}");
                    }
                }
                billboard.Release(slot);
                Debug.Log($"RED MAPLE SWEEP yaw {yaw}");
            }
            if (renderers[3] != null && !new long[] { 2910, 1570, 773 }.All(selectedTriangleCounts.Contains))
                throw new InvalidOperationException("The sweep did not select each authored mesh through the standing renderer.");
        }

        void SaveCrop(string name)
        {
            // Same centred crop for each representation at a given distance.
            const int size = 1024;
            var crop = new Texture2D(size, size, TextureFormat.RGB24, false);
            crop.SetPixels(pixels.GetPixels((Width - size) / 2, (Height - size) / 2, size, size));
            File.WriteAllBytes(Output + "/" + name + ".png", crop.EncodeToPNG());
            Object.DestroyImmediate(crop);
        }

        void CompareShadows()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(80, 1, 80);
            ground.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            using var csv = new StreamWriter(Output + "/shadows.csv");
            csv.WriteLine("distance,yaw,level,referenceShadowPixels,coverageRatio,shadowOverlap");
            foreach (int distance in new[] { 48, 74, 104, 114, 126 })
            foreach (int yaw in new[] { 0, 79 })
            {
                single.localRotation = Quaternion.Euler(0, yaw, 0);
                camera.transform.position = new Vector3(12, 28, -distance);
                camera.transform.LookAt(new Vector3(-9, 5, 10));
                bool[] reference = null;
                for (int level = 0; level <= (renderers[3] == null ? 2 : 3); ++level)
                {
                    sun.shadows = LightShadows.None;
                    var unshadowed = Capture(level);
                    sun.shadows = LightShadows.Soft;
                    var shadowed = Capture(level);
                    var mask = new bool[shadowed.Length];
                    int count = 0, total = 0, overlap = 0, union = 0;
                    for (int i = 0; i < mask.Length; ++i)
                    {
                        var pixel = unshadowed[i];
                        bool onGround = pixel.r > 160 && Mathf.Abs(pixel.r - pixel.g) < 20 &&
                            Mathf.Abs(pixel.r - pixel.b) < 20;
                        mask[i] = onGround && pixel.r - shadowed[i].r > 9;
                        bool a = reference == null ? mask[i] : reference[i];
                        if (a) count++;
                        if (mask[i]) total++;
                        if (a && mask[i]) overlap++;
                        if (a || mask[i]) union++;
                    }
                    if (count == 0) throw new InvalidOperationException("Empty reference shadow capture.");
                    reference ??= mask;
                    csv.WriteLine(FormattableString.Invariant($"{distance},{yaw},{level},{count},{total/(double)count:F4},{overlap/(double)union:F4}"));
                    if (distance == 114 && yaw == 0) SaveCrop($"shadow-114m-lod{level}");
                }
            }
        }

        void CompareGpu(bool allSpecies)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(80, 1, 80);
            ground.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (renderers[3] == null) throw new InvalidOperationException("Missing the Red Maple LOD prototype.");
            single.localRotation = Quaternion.Euler(0, 67, 0);
            slot = billboard.Register(Matrix(single), (Color)single.leafTint, Vector4.zero, 1);
            foreach (var (distance, fade, snow) in new[] { (66, 0f, 0f), (110, 0f, 0f),
                (135, 0f, .7f), (146, 0f, 0f), (171, .45f, 0f) })
                CheckGpuView("RedMaple", distance, fade, snow, StandingTreeGpuMode.RedMaple);
            billboard.Release(slot);
            if (!allSpecies) return;

            foreach (var name in new[] { "SugarMaple", "Birch", "Spruce", "WhitePine", "OakCrown_v04" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{name}_DetailLevels.prefab");
                if (prefab == null) throw new InvalidOperationException("Missing standing GPU test prefab " + name);
                renderers[3].Dispose();
                settings[3].grasslandMapleTreePrefab = prefab;
                renderers[3] = CreateWithoutMovingWind(prefab, settings[3]);
                foreach (int distance in new[] { 60, 95, 130 })
                    CheckGpuView(name, distance, 0, 0, StandingTreeGpuMode.All);
            }
        }

        void CheckGpuView(string name, int distance, float fade, float snow, StandingTreeGpuMode gpuMode)
        {
            camera.transform.position = new Vector3(0, 17, -distance);
            camera.transform.LookAt(new Vector3(-2, 16, 0));
            single.snowCoverage = snow;
            sun.shadows = LightShadows.None;
            var noShadows = Capture(3, fade);
            sun.shadows = LightShadows.Soft;
            var cpu = Capture(3, fade);
            settings[3].standingTreeGpuMode = gpuMode;
            var gpu = Capture(3, fade);
            settings[3].standingTreeGpuMode = StandingTreeGpuMode.Disabled;
            if (renderers[3].GpuDrawCalls != 6 || renderers[3].GpuCandidateCount != 1)
            {
                var compute = settings[3].standingTreeCompactShader;
                var tags = settings[3].grasslandMapleTreePrefab.GetComponentsInChildren<MeshRenderer>()
                    .SelectMany(renderer => renderer.sharedMaterials).Distinct()
                    .Select(material => $"{material.shader.name}={material.GetTag("StandingTreeIndirect", false, "missing")}");
                throw new InvalidOperationException($"{name} indirect path produced {renderers[3].GpuDrawCalls} draws for {renderers[3].GpuCandidateCount} candidates, compute={compute != null}, kernel={compute != null && compute.HasKernel("SelectStandingTreeLods")}, supported={SystemInfo.supportsComputeShaders}/{SystemInfo.supportsIndirectArgumentsBuffer}/{SystemInfo.graphicsShaderLevel}, shaders={string.Join(", ", tags)}.");
            }
            int different = 0, shadowPixels = 0;
            for (int i = 0; i < cpu.Length; ++i)
            {
                if (noShadows[i].r > cpu[i].r + 12) shadowPixels++;
                if (Math.Abs(cpu[i].r - gpu[i].r) + Math.Abs(cpu[i].g - gpu[i].g) +
                    Math.Abs(cpu[i].b - gpu[i].b) > 12) different++;
            }
            Debug.Log($"STANDING GPU PARITY {name} {distance}m fade={fade} snow={snow} differing={different}/{cpu.Length} shadowPixels={shadowPixels}");
            if (different > cpu.Length * .001 || (name == "RedMaple" && distance == 66 && shadowPixels == 0))
            {
                pixels.SetPixels32(cpu); File.WriteAllBytes(Output + "/gpu-parity-cpu.png", pixels.EncodeToPNG());
                pixels.SetPixels32(gpu); File.WriteAllBytes(Output + "/gpu-parity-indirect.png", pixels.EncodeToPNG());
                throw new InvalidOperationException("Standing-tree GPU color/shadow comparison failed.");
            }
        }

        void TimeGrove()
        {
            forest = true;
            handoff = 0;
            camera.transform.position = new Vector3(0, 2.1f, -5);
            camera.transform.rotation = Quaternion.identity;
            for (int i = 0; i < grove.Length; ++i)
            {
                float x = ((i % 12) - 5.5f) * 9 + (float)random.NextDouble() * 4;
                float z = 16 + (i / 12) * 13 + (float)random.NextDouble() * 5;
                var rotation = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);
                float scale = 3 + (float)random.NextDouble();
                grove[i] = Matrix4x4.TRS(new Vector3(x, 0, z), rotation, scale * Vector3.one);
            }
            using var csv = new StreamWriter(Output + "/grove.csv");
            csv.WriteLine("shadows,level,pass,medianMs,p95Ms,submittedTriangles,draws");
            int count = renderers[3] == null ? 3 : 4;
            foreach (bool shadows in new[] { false, true })
            {
                sun.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                for (int pass = 0; pass < 2; ++pass)
                for (int i = 0; i < count; ++i)
                {
                    mode = pass == 0 ? i : count - i - 1;
                    var timings = new double[48];
                    for (int frame = -12; frame < timings.Length; ++frame)
                    {
                        long started = Stopwatch.GetTimestamp();
                        Render();
                        var request = AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1);
                        request.WaitForCompletion();
                        if (request.hasError) throw new InvalidOperationException("GPU synchronization failed.");
                        if (frame >= 0) timings[frame] = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                    }
                    Array.Sort(timings);
                    var standing = renderers[mode];
                    csv.WriteLine(FormattableString.Invariant($"{shadows},{mode},{pass},{timings[timings.Length / 2]:F4},{timings[(int)(timings.Length * .95)]:F4},{standing.RenderStats.triangles},{standing.DrawCalls}"));
                    csv.Flush();
                    Debug.Log($"RED MAPLE GROVE shadows={shadows} mode={mode} pass={pass}");
                }
            }
        }
    }
}
