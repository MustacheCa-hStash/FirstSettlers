using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class WoodlandEdgeImpostorBake
{
    private static readonly string[] Names = { "LowBranchedWhiteOak", "MultistemEdgeBeechV2", "HazelThicket" };
    private const string Models = "Assets/Models/Trees/WoodlandEdgeLOD0/";
    private const string Masters = "Assets/WoodlandEdgeCaptureMasters/";
    private const string Semantic = "Hidden/TreeImpostor/OakSemanticCapture";
    private const string PilotOutlines = "Assets/ScriptableObjects/WoodlandPilotOutlines.asset";

    public static void RebuildPilotLOD0Batch()
    {
        try
        {
            TreeHQImpostorBaker.PrepareBatchHost();
            string[] names = Environment.GetCommandLineArgs().Contains("-edgeLowOakOnly")
                ? new[] { "LowBranchedWhiteOak" }
                : new[] { "HeroPaperBirch", "HeroQuakingAspen", "LowBranchedWhiteOak" };
            foreach (string name in names)
            {
                var source = ConfigurePilotLOD0(name);
                if (!Environment.GetCommandLineArgs().Contains("-edgeValidateLOD0"))
                {
                    TreeHQImpostorBaker.Bake(source, 4096);
                    TreeHQImpostorBaker.ConfigureRuntimeImports();
                }
                EditorUtility.UnloadUnusedAssetsImmediate();
            }
            foreach (string name in names)
            {
                string path = "Assets/Prefabs/" + name + "LOD0.prefab";
                float direct = name == "HeroPaperBirch" ? .23f : name == "HeroQuakingAspen" ? .22f : .32f;
                MatchColors(name, path, direct, shadowed: true);
            }
            var library = AssetDatabase.LoadAssetAtPath<TreeImpostorOutlineLibrary>(PilotOutlines);
            var keep = library.entries.Where(entry => entry.albedo && entry.materialIds &&
                !names.Any(name => entry.albedo.name.StartsWith(name + "HQ_", StringComparison.Ordinal))).ToArray();
            TreeImpostorOutlineBaker.BakeSelected(PilotOutlines, keep, names);
            TreeHQImpostorBaker.PrepareBatchHost();
            foreach (string name in names)
            {
                ConfigurePilotLOD0(name);
                TreeHQImpostorValidation.Run("", calibrateFoliage: false);
                var material = AssetDatabase.LoadAssetAtPath<Material>(
                    $"Assets/Materials/M_Trees/{name}HQ/{name}HQ_OctaImpostor_M.mat");
                if (library.Find(material) == null) throw new InvalidOperationException("Missing revised outline for " + name);
            }
            Debug.Log("PILOT_LOD0_IMPOSTORS_PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static GameObject ConfigurePilotLOD0(string name)
    {
        string path = "Assets/Prefabs/" + name + "LOD0.prefab";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!source) throw new InvalidOperationException("Missing LOD0 capture source " + path);
        var renderers = source.GetComponentsInChildren<MeshRenderer>();
        var leaf = renderers.Single(renderer => renderer.name == "Foliage").sharedMaterial;
        var bark = renderers.Single(renderer => renderer.name == "Bark").sharedMaterial;
        TreeHQImpostorBaker.Current = new TreeHQSpec
        {
            species = name, sourcePrefab = path, shaderName = Semantic, paletteMode = -1,
            variant = name == "HeroPaperBirch" ? nameof(WorldFeatureVariant.BirchAspenTree) :
                name == "HeroQuakingAspen" ? nameof(WorldFeatureVariant.GrasslandBirchAspenTree) : nameof(WorldFeatureVariant.OakTree),
            expansion = name == "LowBranchedWhiteOak" ? .24f : .40f,
            ambientTileResolution = name == "LowBranchedWhiteOak" ? 256 : 128,
            ambientVolumeResolution = name == "LowBranchedWhiteOak" ? 160 : 128,
            leafPath = AssetDatabase.GetAssetPath(leaf), barkPath = AssetDatabase.GetAssetPath(bark),
            leafTexture = AssetDatabase.GetAssetPath(leaf.GetTexture("_BaseMap")),
            barkTexture = AssetDatabase.GetAssetPath(bark.GetTexture("_BaseMap"))
        };
        return source;
    }

    // Run in the isolated bake project. EdgeMasterGeometry is extracted directly
    // from the approved .blend files by ExtractWoodlandEdgeImpostorMasters.py.
    public static void RunBatch()
    {
        try
        {
            TreeHQImpostorBaker.PrepareBatchHost();
            if (!Environment.GetCommandLineArgs().Contains("-edgeMatchOnly"))
            {
                foreach (string name in Names)
                {
                    Bake(name);
                    EditorUtility.UnloadUnusedAssetsImmediate();
                    GC.Collect();
                }
            }
            foreach (string name in Names) MatchColors(name);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void Bake(string name)
    {
        string directory = Masters + name + "/";
        Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();
        var source = BuildMaster(name, directory);
        TreeHQImpostorBaker.Current = new TreeHQSpec
        {
            species = name, sourcePrefab = directory + name + ".prefab",
            leafPath = directory + "Leaf.mat", barkPath = directory + "Bark.mat",
            leafTexture = directory + "LeafAlbedo.png", barkTexture = Models + name + "/BarkAlbedo.png",
            shaderName = Semantic, paletteMode = -1, expansion = .24f
        };
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(TreeHQImpostorBaker.PrefabPath))
            TreeHQImpostorBaker.Bake(source, name == "HazelThicket" ? 2048 : 4096);
        TreeHQImpostorBaker.ConfigureRuntimeImports();
    }

    private static GameObject BuildMaster(string name, string directory)
    {
        string prefabPath = directory + name + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing) return existing;
        var root = new GameObject(name + " full-detail capture");
        try
        {
            foreach (string part in new[] { "Bark", "Foliage" })
            {
                bool foliage = part == "Foliage";
                var geometry = ReadMasterMesh("EdgeMasterGeometry/" + name + part + ".bin", foliage);
                AssetDatabase.CreateAsset(geometry, directory + part + ".asset");
                var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(
                    Models + name + (foliage ? "/Leaf.mat" : "/Bark.mat")));
                if (foliage)
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(directory + "LeafAlbedo.png");
                    if (!texture) throw new InvalidOperationException("Missing full-detail leaf-space albedo for " + name);
                    material.SetTexture("_BaseMap", texture);
                    material.SetFloat("_CrownNormalBlend", 0f);
                    material.DisableKeyword("_LEAF_CARD_SURFACE");
                }
                AssetDatabase.CreateAsset(material, directory + (foliage ? "Leaf.mat" : "Bark.mat"));
                var child = new GameObject(part);
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = geometry;
                child.AddComponent<MeshRenderer>().sharedMaterial = material;
                Debug.Log($"EDGE MASTER {name}/{part}: {geometry.GetIndexCount(0) / 3} triangles");
            }
            return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static Mesh ReadMasterMesh(string path, bool foliage)
    {
        using var stream = new BinaryReader(File.OpenRead(path));
        int vertexCount = stream.ReadInt32(), corners = stream.ReadInt32(), indices = stream.ReadInt32();
        Vector3 ReadVector()
        {
            float x = stream.ReadSingle(), y = stream.ReadSingle(), z = stream.ReadSingle();
            return new Vector3(x, z, y);
        }
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++) positions[i] = ReadVector();
        for (int i = 0; i < vertexCount; i++) normals[i] = ReadVector();
        var cornerVertices = new int[corners];
        for (int i = 0; i < corners; i++) cornerVertices[i] = stream.ReadInt32();
        var uv = new Vector2[foliage ? vertexCount : corners];
        var woodPositions = foliage ? positions : new Vector3[corners];
        var woodNormals = foliage ? normals : new Vector3[corners];
        for (int i = 0; i < corners; i++)
        {
            int vertex = cornerVertices[i];
            uv[foliage ? vertex : i] = new Vector2(stream.ReadSingle(), stream.ReadSingle());
            if (foliage) continue;
            woodPositions[i] = positions[vertex];
            woodNormals[i] = normals[vertex];
        }
        var triangles = new int[indices];
        int ReadIndex()
        {
            int corner = stream.ReadInt32();
            return foliage ? cornerVertices[corner] : corner;
        }
        for (int i = 0; i < indices; i += 3)
        {
            triangles[i] = ReadIndex();
            triangles[i + 2] = ReadIndex();
            triangles[i + 1] = ReadIndex();
        }
        return new Mesh { indexFormat = IndexFormat.UInt32, vertices = woodPositions, normals = woodNormals,
            uv = uv, triangles = triangles };
    }

    private static void MatchColors(string name, string nearPath = null, float direct = 1f, bool shadowed = false)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        var previous = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        ShaderUtil.allowAsyncCompilation = false;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.54f, .63f, .75f);
        RenderSettings.ambientEquatorColor = new Color(.4f, .44f, .5f);
        RenderSettings.ambientGroundColor = new Color(.23f, .218f, .167f);
        var sun = new GameObject("Comparison sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        // Isolate material color. The near mesh can cast detailed shadows onto
        // itself, while the impostor represents that enclosure in its AO atlas.
        sun.shadows = shadowed ? LightShadows.Soft : LightShadows.None;
        sun.shadowStrength = .85f;
        RenderSettings.sun = sun;
        Shader.SetGlobalVector("_FSGroundDaylight", new Vector4(1f, 1f, 0f, 0f));
        Shader.SetGlobalVector("_FSForestDetailLighting", new Vector4(0f, 2.3f, 0f, 0f));

        var camera = new GameObject("Comparison camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.allowHDR = false;
        var near = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
            nearPath ?? Models + name + "/" + name + "LOD0.fbx"));
        var far = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/" + name + "HQ_OctaImpostor_Runtime.prefab"));
        var nearRenderers = near.GetComponentsInChildren<MeshRenderer>();
        var nearMaterials = nearRenderers.Select(renderer => renderer.sharedMaterial).ToArray();
        var bounds = nearRenderers.Select(renderer => renderer.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        var farMaterial = far.GetComponent<MeshRenderer>().sharedMaterial;
        bool fitCrownContrast = name == "LowBranchedWhiteOak" && shadowed;
        if (fitCrownContrast)
        {
            farMaterial.SetFloat("_AmbientFloor", .045f);
            farMaterial.SetFloat("_AOStrength", 1f);
            farMaterial.SetFloat("_LeafShelterStrength", 0f);
        }
        farMaterial.SetFloat("_StableLeafTransmission", 0f);
        farMaterial.SetFloat("_CrownTransmission", 1f);
        farMaterial.SetFloat("_WindStrength", 0f);
        farMaterial.SetFloat("_SeasonStrength", 0f);
        farMaterial.SetVector("_FoliageColorMatch", new Vector4(1f, 1f, 1f, direct));
        farMaterial.SetVector("_BarkColorMatch", Vector4.one);
        var ids = nearRenderers.Select((renderer, i) =>
        {
            var id = new Material(Shader.Find(Semantic));
            id.CopyPropertiesFromMaterial(nearMaterials[i]);
            id.SetFloat("_CaptureMaterialKind", renderer.name == "Foliage" ? 0f : 1f);
            id.SetFloat("_CaptureOutput", 3f);
            return id;
        }).ToArray();
        using var capture = new ComparisonCapture(camera, bounds);
        string reviews = "EdgeImpostorReviews/" + name;
        Directory.CreateDirectory(reviews);
        try
        {
            var target = Measure(true, "near");
            var before = Measure(false, "before");
            // The in-scene oak needs a darker foliage grade than the neutral
            // studio match. Keep the bark target and direct-light response.
            Vector3 leafTarget = fitCrownContrast ? target.leaf * .78f : target.leaf;
            if (fitCrownContrast)
            {
                float bestShelter = 0f;
                float bestError = float.PositiveInfinity;
                foreach (float strength in new[] { .75f, .9f, 1f })
                {
                    farMaterial.SetFloat("_LeafShelterStrength", strength);
                    var sample = Measure(false, null);
                    float error = ToneError(sample.shade.y, sample.leaf.y, target.shade.y, target.leaf.y)
                        + ToneError(sample.tip.y, sample.leaf.y, target.tip.y, target.leaf.y);
                    if (error >= bestError) continue;
                    bestShelter = strength;
                    bestError = error;
                }
                farMaterial.SetFloat("_LeafShelterStrength", bestShelter);
            }
            for (int iteration = 0; iteration < 2; iteration++)
            {
                var measured = iteration == 0 && !fitCrownContrast ? before : Measure(false, null);
                Revise("_FoliageColorMatch", leafTarget, measured.leaf);
                Revise("_BarkColorMatch", target.bark, measured.bark);
            }
            var after = Measure(false, "after");
            string report = $"EDGE COLOR {name}: leaf {Error(leafTarget, before.leaf):P1} -> {Error(leafTarget, after.leaf):P1}; " +
                $"bark {Error(target.bark, before.bark):P1} -> {Error(target.bark, after.bark):P1} mean relative linear RGB error";
            if (fitCrownContrast)
                report += $"; foliage linear green P25/P50/P90: near {Tones(target)}, after {Tones(after)} " +
                    $"(graded median target {leafTarget.y:F3})";
            Debug.Log(report);
            File.WriteAllText(reviews + "/ColorMatch.txt", report);
            if (Error(leafTarget, after.leaf) > .10f || Error(target.bark, after.bark) > .12f)
                throw new InvalidOperationException("Color match exceeded tolerance for " + name);
            EditorUtility.SetDirty(farMaterial);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            foreach (var id in ids) Object.DestroyImmediate(id);
            QualitySettings.renderPipeline = previousQuality;
            GraphicsSettings.defaultRenderPipeline = previous;
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(rendererData);
        }

        static float ToneError(float sample, float sampleMedian, float target, float targetMedian)
        {
            float relative = Mathf.Max(.001f, sample) / Mathf.Max(.001f, sampleMedian);
            float desired = Mathf.Max(.001f, target) / Mathf.Max(.001f, targetMedian);
            return Mathf.Abs(Mathf.Log(relative / desired));
        }

        static string Tones((Vector3 leaf, Vector3 bark, Vector3 shade, Vector3 tip) sample) =>
            $"{sample.shade.y:F3}/{sample.leaf.y:F3}/{sample.tip.y:F3}";

        (Vector3 leaf, Vector3 bark, Vector3 shade, Vector3 tip) Measure(bool useNear, string label)
        {
            near.SetActive(useNear);
            far.SetActive(!useNear);
            var leaf = new ColorSamples();
            var bark = new ColorSamples();
            for (int angle = 0; angle < 4; angle++)
            {
                capture.Aim(angle);
                sun.transform.rotation = Quaternion.LookRotation(
                    Quaternion.Euler(0f, angle * 90f + (fitCrownContrast && angle % 2 == 1 ? 180f : 0f), 0f) *
                    new Vector3(.38f, -.85f, .36f));
                Color32[] lit = capture.Read(label != null && angle < 2 ? $"{reviews}/{label}-{angle}.png" : null);
                if (useNear)
                    for (int i = 0; i < ids.Length; i++) nearRenderers[i].sharedMaterial = ids[i];
                else farMaterial.SetFloat("_DebugView", 3f);
                Color32[] mask = capture.Read(label == "before" || label == "near" ? $"{reviews}/{label}-id-{angle}.png" : null);
                if (useNear)
                    for (int i = 0; i < ids.Length; i++) nearRenderers[i].sharedMaterial = nearMaterials[i];
                else farMaterial.SetFloat("_DebugView", 0f);
                leaf.Add(lit, mask, true);
                bark.Add(lit, mask, false);
            }
            return (leaf.Quantile(.50f), bark.Quantile(.50f),
                fitCrownContrast ? leaf.Quantile(.25f) : Vector3.zero,
                fitCrownContrast ? leaf.Quantile(.90f) : Vector3.zero);
        }

        void Revise(string property, Vector3 target, Vector3 measured)
        {
            var scale = farMaterial.GetVector(property);
            for (int i = 0; i < 3; i++)
                scale[i] = Mathf.Clamp(scale[i] * target[i] / Mathf.Max(.001f, measured[i]), .08f, 4f);
            farMaterial.SetVector(property, scale);
        }
    }

    private static float Error(Vector3 target, Vector3 actual)
    {
        Vector3 d = target - actual;
        return (Mathf.Abs(d.x) / Mathf.Max(.001f, target.x) +
            Mathf.Abs(d.y) / Mathf.Max(.001f, target.y) + Mathf.Abs(d.z) / Mathf.Max(.001f, target.z)) / 3f;
    }

    private sealed class ColorSamples
    {
        private readonly int[,] histogram = new int[3, 256];
        private int count;

        public void Add(Color32[] lit, Color32[] mask, bool foliage)
        {
            for (int i = 0; i < mask.Length; i++)
            {
                var id = mask[i];
                if (id.a < 190 || (foliage ? id.r < 128 || id.r <= id.g * 2 : id.g < 128 || id.g <= id.r * 2))
                    continue;
                count++;
                histogram[0, lit[i].r]++;
                histogram[1, lit[i].g]++;
                histogram[2, lit[i].b]++;
            }
        }

        public Vector3 Quantile(float percentile)
        {
            if (count < 20) throw new InvalidOperationException($"Only {count} isolated pixels for color calibration.");
            float Channel(int c)
            {
                int running = 0;
                for (int i = 0; i < 256; i++)
                    if ((running += histogram[c, i]) > (int)(count * percentile)) return Mathf.GammaToLinearSpace(i / 255f);
                return 0f;
            }
            return new Vector3(Channel(0), Channel(1), Channel(2));
        }
    }

    private sealed class ComparisonCapture : IDisposable
    {
        private const int Size = 640;
        private readonly Camera camera;
        private readonly Bounds bounds;
        private readonly RenderTexture target = new(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        private readonly Texture2D image = new(Size, Size, TextureFormat.RGBA32, false);

        public ComparisonCapture(Camera camera, Bounds bounds)
        {
            this.camera = camera;
            this.bounds = bounds;
            camera.orthographicSize = bounds.size.magnitude * .43f;
            target.Create();
            camera.targetTexture = target;
        }

        public void Aim(int angle)
        {
            var offset = Quaternion.Euler(0f, angle * 90f, 0f) * new Vector3(.3f, .14f, -1f);
            camera.transform.position = bounds.center + offset * bounds.size.magnitude * 2f;
            camera.transform.LookAt(bounds.center);
        }

        public Color32[] Read(string file)
        {
            for (int frame = 0; frame < 3; frame++)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var active = RenderTexture.active;
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            if (file != null) File.WriteAllBytes(file, image.EncodeToPNG());
            return image.GetPixels32();
        }

        public void Dispose()
        {
            target.Release();
            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
        }
    }
}
