using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class ForestVarietyStudy
{
    const string Models = "Assets/Models/Trees/ForestVariety/";
    const string Materials = "Assets/Materials/M_Trees/ForestVariety/";
    const string Textures = "Assets/Textures/Trees/";
    const string Prefabs = "Assets/Prefabs/";
    const string Review = ".utmp/forest-variety";
    const string BirchLeaf = "Assets/Materials/M_Trees/Birch/M_Birch_Leaf.mat";
    const string BirchBark = "Assets/Materials/M_Trees/Birch/M_Birch_Bark.mat";
    const string OakLeaf = "Assets/Materials/M_Trees/Oak/M_Oak_Leaf.mat";
    const string OakBark = "Assets/Materials/M_Trees/Oak/M_Oak_Bark.mat";
    public const string OutlineAsset = "Assets/ScriptableObjects/ForestVarietyOutlines.asset";

    static readonly TreeHQSpec[] Trees = {
        Spec("Beech", "Beech_Stand_v01", "BeechTree", "ForestVariety/T_BeechLeafCard.png", "ForestVariety/T_BeechBark.png"),
        Spec("OakOpenCrown", "Oak_OpenCrown_v05", "OakTree", "Oak/T_OakLeafCard.png", "Bark/T_WhiteOakBark_Stylized.png", oak: true),
        Spec("Willow", "Willow_River_v01", "GrasslandWillowTree", "leafClumpAtlas_Willow.png", "Bark/OakBarkTextureSeamless.png"),
    };

    static TreeHQSpec Spec(string name, string model, string variant, string leaf, string bark, bool oak = false) =>
        new TreeHQSpec {
            species = name, sourcePrefab = Prefabs + model + ".prefab", variant = variant,
            leafPath = oak ? OakLeaf : Materials + "M_" + name + "_Leaf.mat",
            barkPath = oak ? OakBark : Materials + "M_" + name + "_Bark.mat",
            leafTexture = Textures + leaf, barkTexture = Textures + bark, paletteMode = oak ? 2 : 3,
            expansion = oak ? .5f : .6f, shaderName = oak ? "Hidden/TreeImpostor/OakSemanticCapture" : "Hidden/TreeImpostor/BirchSemanticCapture"
        };

    public static void BuildPreviewBatch() => Run(() =>
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildModels();
        using var viewer = new ModelPreview();
        foreach (var spec in Trees) viewer.Render(spec.species, spec.sourcePrefab, spec.variant);
        viewer.Render("OakPrevious", Prefabs + "OakCrown_v04.prefab", "OakTree");
    });

    public static void BakeBatch() => Run(() =>
    {
        BakeOrValidate(bake: true);
        BuildOutlines();
    });

    public static void ValidateBatch() => Run(() => BakeOrValidate(bake: false));
    public static void BuildOutlinesBatch() => Run(BuildOutlines);
    public static void BakeOakBatch() => Run(() =>
    {
        TreeHQImpostorBaker.PrepareBatchHost();
        BakeOrValidate(Trees[1], bake: true);
        var library = Load<TreeImpostorOutlineLibrary>(OutlineAsset);
        var oak = Load<Material>(TreeHQImpostorBaker.MaterialPath).GetTexture("_AlbedoCoverage");
        TreeImpostorOutlineBaker.BakeSelected(OutlineAsset, library.entries.Where(entry => entry.albedo != oak), "OakOpenCrown");
    });

    static void BakeOrValidate(bool bake)
    {
        TreeHQImpostorBaker.PrepareBatchHost();
        foreach (var spec in Trees)
        {
            BakeOrValidate(spec, bake);
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
        }
    }

    static void BakeOrValidate(TreeHQSpec spec, bool bake)
    {
        TreeHQImpostorBaker.Current = spec;
        bool oak = spec.species == "OakOpenCrown";
        if (bake)
        {
            TreeHQImpostorBaker.Bake(Load<GameObject>(spec.sourcePrefab), oak ? 8192 : 4096);
            TreeHQImpostorBaker.ConfigureRuntimeImports();
        }
        TreeHQImpostorValidation.Run(oak ? Prefabs + "OakHQ_OctaImpostor_Runtime.prefab" : null,
            calibrateFoliage: bake);
    }

    static void BuildOutlines()
    {
        var standard = Load<TreeImpostorOutlineLibrary>(TreeImpostorOutlineBaker.AssetPath);
        var previousOak = Load<Material>("Assets/Materials/M_Trees/OakHQ/OakHQ_OctaImpostor_M.mat").GetTexture("_AlbedoCoverage");
        var sharedEntries = standard.entries.Where(entry => entry.albedo != previousOak);
        TreeImpostorOutlineBaker.BakeSelected(OutlineAsset, sharedEntries, Trees.Select(spec => spec.species).ToArray());
    }

    static void Run(Action action)
    {
        try { action(); Debug.Log("FOREST VARIETY STUDY PASS"); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!asset) throw new InvalidOperationException("Missing forest study asset " + path);
        return asset;
    }

    static void BuildModels()
    {
        Directory.CreateDirectory(Materials);
        AssetDatabase.Refresh();
        var leafImporter = (TextureImporter)AssetImporter.GetAtPath(Trees[0].leafTexture);
        leafImporter.alphaIsTransparency = true;
        leafImporter.mipMapsPreserveCoverage = true;
        leafImporter.alphaTestReferenceValue = .5f;
        leafImporter.wrapMode = TextureWrapMode.Clamp;
        leafImporter.SaveAndReimport();
        foreach (var spec in Trees)
        {
            if (spec.species != "OakOpenCrown")
            {
                var leaf = CopyMaterial(BirchLeaf, spec.leafPath, spec.leafTexture);
                bool willow = spec.species == "Willow";
                leaf.SetColor("_SummerLeafColor", willow ? new Color(.43f,.55f,.25f) : new Color(.33f,.48f,.18f));
                leaf.SetColor("_AutumnGoldColor", willow ? new Color(.90f,.72f,.24f) : new Color(.93f,.61f,.20f));
                leaf.SetColor("_AutumnOchreColor", willow ? new Color(.68f,.57f,.18f) : new Color(.67f,.36f,.12f));
                leaf.SetColor("_AutumnPaleGoldColor", willow ? new Color(1f,.85f,.39f) : new Color(.88f,.74f,.32f));
                leaf.SetFloat("_SeasonAutumnAmount", 0);
                leaf.SetFloat("_WindHeightMax", willow ? 8f : 15f);
                leaf.SetFloat("_WindStrength", willow ? .16f : .10f);
                leaf.SetFloat("_WindFlutterStrength", willow ? .07f : .045f);
                if (!willow) leaf.SetFloat("_TreeTintStrength", 0);
                var bark = CopyMaterial(BirchBark, spec.barkPath, spec.barkTexture);
                bark.SetColor("_BaseColor", willow ? new Color(.71f,.64f,.51f) : new Color(.82f,.84f,.80f));
                if (!willow) bark.SetFloat("_ScarStrength", 0);
                EditorUtility.SetDirty(leaf);
                EditorUtility.SetDirty(bark);
            }

            string name = Path.GetFileNameWithoutExtension(spec.sourcePrefab);
            var model = Load<GameObject>(Models + name + ".fbx");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                root.name = name;
                var parts = root.GetComponentsInChildren<MeshRenderer>();
                if (parts.Length != 2) throw new InvalidOperationException("Expected bark and foliage renderers for " + name);
                foreach (var part in parts)
                    part.sharedMaterial = Load<Material>(part.name == "Bark" ? spec.barkPath : spec.leafPath);
                PrefabUtility.SaveAsPrefabAsset(root, spec.sourcePrefab);
                Debug.Log($"FOREST VARIETY MODEL {name}: {string.Join(", ", parts.Select(p => $"{p.name} {p.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0)/3} triangles"))}");
            }
            finally { Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
    }

    static Material CopyMaterial(string template, string path, string texture)
    {
        var source = Load<Material>(template);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        else material.CopyPropertiesFromMaterial(source);
        var map = Load<Texture2D>(texture);
        material.SetTexture("_BaseMap", map);
        material.SetTexture("_MainTex", map);
        return material;
    }

    sealed class ModelPreview : IDisposable
    {
        const int Size = 1024;
        readonly Camera camera;
        readonly RenderTexture target;
        readonly Texture2D pixels;
        StandingTreeRenderer trees;
        TreeInstanceData tree;
        Matrix4x4 placement;

        public ModelPreview()
        {
            Directory.CreateDirectory(Review);
            QualitySettings.SetQualityLevel(Array.IndexOf(QualitySettings.names, "PC"));
            QualitySettings.renderPipeline = GraphicsSettings.defaultRenderPipeline =
                Load<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.51f,.54f,.59f);
            var sun = new GameObject("Model review sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(43,-39,0);
            camera = new GameObject("Model review camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 36;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.47f,.63f,.75f);
            target = new RenderTexture(Size, Size, 24);
            target.Create();
            pixels = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            RenderPipelineManager.beginCameraRendering += Submit;
        }

        public void Render(string name, string path, string variantName)
        {
            trees?.Dispose();
            var source = Load<GameObject>(path);
            var variant = Enum.Parse<WorldFeatureVariant>(variantName);
            string card = name.StartsWith("Oak", StringComparison.Ordinal) ? "OakTree" :
                name == "Willow" ? "WillowTree" : "BeechTree";
            var definition = Load<WorldObjectDefinition>("Assets/ScriptableObjects/WorldObjects/" + card + ".asset");
            float scale = (definition.TreeUniformScaleRange.x + definition.TreeUniformScaleRange.y) * .5f;
            FoliageGenerator.GetDeterministicTreeColors(variant, 1937, 7, default, 5, 7, out var leaf, out var bark);
            tree = new TreeInstanceData(Vector3.zero, Quaternion.identity, Vector3.one * scale, WorldFeatureVariant.MapleTree, leaf, bark);
            trees = new StandingTreeRenderer(new TreeSettings { mapleTreePrefab = source, enableStandingTreeMeshLods = false, castTreeShadows = false });
            Shader.SetGlobalFloat("_TreeSeasonSimulationEnabled", 1);
            float height = name.StartsWith("Oak", StringComparison.Ordinal) ? 88 : name == "Willow" ? 18 : 28;
            foreach (int season in new[] { 0, 1 })
            foreach (int heading in new[] { 0, 66, 147 })
            foreach (bool above in new[] { false, true })
            {
                Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount", season);
                placement = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, heading, 0), tree.localScale);
                camera.transform.position = new Vector3(0, above ? height * 1.13f : 1.7f, -height * 1.85f);
                camera.transform.LookAt(new Vector3(0, height * .44f, 0));
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,Size,Size),0,0);
                RenderTexture.active = previous;
                File.WriteAllBytes($"{Review}/{name}-{season}-{heading}-{(above ? "high" : "eye")}.png", pixels.EncodeToPNG());
            }
        }

        void Submit(ScriptableRenderContext _, Camera view)
        {
            if (view != camera || trees == null) return;
            trees.BeginFrame(view);
            trees.Submit(tree, placement, 0, 1);
            trees.EndFrame();
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= Submit;
            trees?.Dispose();
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
        }
    }
}
