using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WoodlandPilotBuilder
{
    const string Models = "Assets/Models/Trees/Woodland/";
    const string Textures = "Assets/Textures/Trees/Woodland/";
    const string Materials = "Assets/Materials/M_Trees/Woodland/";
    const string Prefabs = "Assets/Prefabs/";
    const string Outlines = "Assets/ScriptableObjects/WoodlandPilotOutlines.asset";

    static readonly (string name, WorldFeatureVariant variant, float trunkRadius, float trunkHeight)[] Trees = {
        ("RedMaple", WorldFeatureVariant.MapleTree, .34f, 7),
        ("SugarMaple", WorldFeatureVariant.SugarMapleTree, .46f, 8),
        ("PaperBirch", WorldFeatureVariant.BirchAspenTree, .19f, 6),
        ("QuakingAspen", WorldFeatureVariant.GrasslandBirchAspenTree, .21f, 9),
        ("AmericanBeech", WorldFeatureVariant.BeechTree, .37f, 7),
        ("WhiteOak", WorldFeatureVariant.OakTree, .68f, 6),
        ("NorwaySpruce", WorldFeatureVariant.SpruceTree, .27f, 6),
        ("EasternWhitePine", WorldFeatureVariant.WhitePineTree, .39f, 10)
    };

    static string Name(string name) => "Woodland" + name;
    static string NearPath(string name) => Prefabs + Name(name) + ".prefab";
    static string FarPath(string name) => Prefabs + Name(name) + "HQ_OctaImpostor_Runtime.prefab";

    public static void BuildBatch()
    {
        try
        {
            TreeHQImpostorBaker.PrepareBatchHost();
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            foreach (var (name, variant, trunkRadius, trunkHeight) in Trees)
            {
                var spec = CreatePrefab(name, variant, trunkRadius, trunkHeight);
                TreeHQImpostorBaker.Current = spec;
                TreeHQImpostorBaker.Bake(Load<GameObject>(spec.sourcePrefab), name == "WhiteOak" ? 8192 : 4096);
                TreeHQImpostorBaker.ConfigureRuntimeImports();
                ValidateSummerBillboard(spec);
                EditorUtility.UnloadUnusedAssetsImmediate();
                GC.Collect();
            }
            TreeImpostorOutlineBaker.BakeSelected(Outlines, Array.Empty<TreeImpostorOutlineLibrary.Entry>(),
                Trees.Select(tree => Name(tree.name)).ToArray());
            InstallPilot();
            Debug.Log("WOODLAND PILOT BUILD PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void VerifyBatch()
    {
        try
        {
            TreeHQImpostorBaker.PrepareBatchHost();
            foreach (var (name, variant, _, _) in Trees)
            {
                ValidateSummerBillboard(Spec(name, variant));
                EditorUtility.UnloadUnusedAssetsImmediate();
                GC.Collect();
            }
            InstallPilot();
            Debug.Log("WOODLAND SUMMER IMPOSTOR PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void ValidateSummerBillboard(TreeHQSpec spec)
    {
        TreeHQImpostorBaker.Current = spec;
        var billboard = Load<Material>(TreeHQImpostorBaker.MaterialPath);
        billboard.SetFloat("_WindStrength", 0);
        billboard.SetFloat("_SeasonStrength", 0);
        billboard.SetFloat("_StableLeafTransmission", 0);
        EditorUtility.SetDirty(billboard);
        AssetDatabase.SaveAssetIfDirty(billboard);
        TreeHQImpostorValidation.Run();
    }

    static TreeHQSpec Spec(string name, WorldFeatureVariant variant) => new()
    {
        species = Name(name), sourcePrefab = NearPath(name), variant = variant.ToString(),
        leafPath = Materials + name + "_Leaf.mat", barkPath = Materials + name + "_Bark.mat",
        leafTexture = Textures + name + "_Foliage.png", barkTexture = Textures + name + "_Bark.png",
        paletteMode = -1, expansion = .40f, shaderName = "Hidden/TreeImpostor/OakSemanticCapture"
    };

    static TreeHQSpec CreatePrefab(string name, WorldFeatureVariant variant, float trunkRadius, float trunkHeight)
    {
        var spec = Spec(name, variant);
        var importer = (TextureImporter)AssetImporter.GetAtPath(spec.leafTexture);
        importer.alphaIsTransparency = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = .32f;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        var leaf = MaterialFor(spec.leafPath, "Custom/OakLeafSimpleLitCutout", spec.leafTexture);
        leaf.SetFloat("_AuthoredSummer", 1);
        leaf.SetFloat("_Cutoff", .32f);
        leaf.SetFloat("_AmbientStrength", .20f);
        leaf.SetFloat("_LightWrap", .35f);
        leaf.SetFloat("_CrownNormalBlend", .78f);
        leaf.SetFloat("_BacklightingStrength", .17f);
        leaf.SetFloat("_StableBacklighting", 0);
        leaf.SetFloat("_WindStrength", 0);
        leaf.SetFloat("_WindFlutterStrength", 0);
        var bark = MaterialFor(spec.barkPath, "Custom/OakBarkSimpleLit", spec.barkTexture);
        bark.SetColor("_BaseColor", Color.white);
        bark.SetFloat("_ColorVariationStrength", 0);
        bark.SetFloat("_VerticalGradientStrength", 0);
        EditorUtility.SetDirty(leaf);
        EditorUtility.SetDirty(bark);

        var root = new GameObject(Name(name));
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Models + name + ".fbx"));
            model.transform.SetParent(root.transform, false);
            var parts = model.GetComponentsInChildren<MeshRenderer>();
            if (parts.Length != 2 || !parts.Any(part => part.name == "Foliage"))
                throw new InvalidOperationException("Expected bark and foliage in " + name);
            foreach (var part in parts)
                part.sharedMaterial = part.name == "Bark" ? bark : leaf;
            var trunk = root.AddComponent<CapsuleCollider>();
            trunk.radius = trunkRadius;
            trunk.height = trunkHeight;
            trunk.center = Vector3.up * trunkHeight * .5f;
            var canopy = new GameObject("Canopy query");
            canopy.transform.SetParent(root.transform, false);
            var query = canopy.AddComponent<BoxCollider>();
            var crown = parts.First(part => part.name == "Foliage").bounds;
            query.center = crown.center;
            query.size = crown.size * .76f;
            query.isTrigger = true;
            var authoring = new SerializedObject(root.AddComponent<TreeGameplayAuthoring>());
            AssignCollider(authoring, "physicalTrunkColliders", trunk);
            AssignCollider(authoring, "queryCanopyColliders", query);
            authoring.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, NearPath(name));
            AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(root); }
        return spec;
    }

    static void AssignCollider(SerializedObject authoring, string field, Collider collider)
    {
        var array = authoring.FindProperty(field);
        array.arraySize = 1;
        array.GetArrayElementAtIndex(0).objectReferenceValue = collider;
    }

    static Material MaterialFor(string path, string shaderName, string map)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            var shader = Shader.Find(shaderName);
            if (!shader) throw new InvalidOperationException("Missing shader " + shaderName);
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", Load<Texture2D>(map));
        material.enableInstancing = true;
        return material;
    }

    static void InstallPilot()
    {
        var scene = EditorSceneManager.OpenScene(ForestVarietyWorldStudy.Pilot);
        var world = Object.FindAnyObjectByType<WorldManager>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var settings = (TreeSettings)typeof(WorldManager).GetField("treeSettings", flags).GetValue(world);
        var lighting = (ForestLightingSettings)typeof(WorldManager).GetField("forestLighting", flags).GetValue(world);
        GameObject Near(string name) => Load<GameObject>(NearPath(name));
        GameObject Far(string name) => Load<GameObject>(FarPath(name));
        settings.mapleTreePrefab = settings.grasslandMapleTreePrefab = Near("RedMaple");
        settings.mapleTreeBillboardPrefab = settings.grasslandMapleTreeBillboardPrefab = Far("RedMaple");
        settings.sugarMapleTreePrefab = Near("SugarMaple");
        settings.sugarMapleTreeBillboardPrefab = Far("SugarMaple");
        settings.birchAspenTreePrefab = Near("PaperBirch");
        settings.birchAspenTreeBillboardPrefab = Far("PaperBirch");
        settings.grasslandBirchAspenTreePrefab = Near("QuakingAspen");
        settings.grasslandBirchAspenTreeBillboardPrefab = Far("QuakingAspen");
        settings.beechTreePrefab = Near("AmericanBeech");
        settings.beechTreeBillboardPrefab = Far("AmericanBeech");
        settings.oakTreePrefab = settings.grasslandOakTreePrefab = Near("WhiteOak");
        settings.oakTreeBillboardPrefab = settings.grasslandOakTreeBillboardPrefab = Far("WhiteOak");
        settings.spruceTreePrefab = settings.treeLOD0GameObjectPrefab = settings.grasslandTreeFallbackPrefab = Near("NorwaySpruce");
        settings.spruceTreeBillboardPrefab = settings.treeBillboardPrefab = settings.grasslandTreeBillboardFallbackPrefab = Far("NorwaySpruce");
        settings.whitePineTreePrefab = settings.grasslandWhitePineTreePrefab = Near("EasternWhitePine");
        settings.whitePineTreeBillboardPrefab = settings.grasslandWhitePineTreeBillboardPrefab = Far("EasternWhitePine");
        settings.grasslandWillowTreePrefab = settings.grasslandWillowTreeBillboardPrefab = null;
        settings.distantTreeOutlineLibrary = Load<TreeImpostorOutlineLibrary>(Outlines);
        settings.gameObjectTreeChunkRingRadius = 1;
        lighting.leafPaletteBlend = .18f;
        lighting.barkPaletteBlend = .15f;
        settings.distributionProfile.useWoodlandSpeciesMix = true;
        settings.distributionProfile.forestBackground = 9f;
        settings.distributionProfile.forestStand = 78f;
        settings.distributionProfile.youngTreeShare = .12f;
        settings.distributionProfile.grasslandBackground = .65f;
        settings.distributionProfile.grasslandGrove = 19f;
        EditorUtility.SetDirty(settings.distributionProfile);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    static T Load<T>(string path) where T : Object =>
        AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing woodland asset " + path);
}
