using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>S02 only: a closed box with baked masonry, never individual runtime stones.</summary>
public static class BakedStoneWallSetup
{
    public const string Model = "Assets/Models/Buildings/Stone/S02_BakedCoursedWall_4x3x0.50";
    public const string Colour = "Assets/Textures/Buildings/Stone/S02_BakedWall_ColourAO.png";
    public const string Normal = "Assets/Textures/Buildings/Stone/S02_BakedWall_Normal.png";
    public const string MaterialPath = "Assets/Materials/Buildings/Stone/BakedStoneWall.mat";
    public const string Definition = "Assets/Resources/Building/s02-coursed-wall.asset";

    private static void Texture(string path, bool normal)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Run Tools/BakeStoneWall.py first: " + path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normal;
        importer.alphaSource = normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false; // Colour alpha is AO data, never transparency.
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 2048;
        importer.isReadable = false;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        var platform = importer.GetPlatformTextureSettings("Standalone");
        platform.overridden = true;
        platform.maxTextureSize = 2048;
        platform.format = normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7;
        importer.SetPlatformTextureSettings(platform);
        importer.SaveAndReimport();
    }

    public static void Apply(BuildDefinition definition)
    {
        Texture(Colour, false); Texture(Normal, true);
        var shader = Shader.Find("Custom/BakedStoneWallMatte");
        if (shader == null) throw new InvalidOperationException("Baked stone wall shader missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            var original = AssetDatabase.LoadAssetAtPath<Material>(StoneKitSetup.MaterialPath);
            material = original != null ? new Material(original) : new Material(shader);
            material.name = "Baked stone wall";
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Colour));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Normal));
        material.SetFloat("_BumpScale", 1);
        material.SetFloat("_OcclusionStrength", .65f);
        material.SetFloat("_AlphaClip", 0);
        material.SetFloat("_Cull", 2);
        material.DisableKeyword("_ALPHATEST_ON");
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        definition.material = material;
        definition.uvVariants = new Mesh[3];
        for (int i = 0; i < 3; i++)
        {
            string path = Model + (i == 0 ? "" : "_UV" + i) + ".fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Baked S02 model missing: " + path);
            importer.bakeAxisConversion = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.isReadable = false;
            importer.importAnimation = false;
            importer.SaveAndReimport();
            definition.uvVariants[i] = ModularWoodSetup.StructuralMesh(
                AssetDatabase.LoadAssetAtPath<GameObject>(path), definition.LocalBounds);
        }
        definition.mesh = definition.uvVariants[0];
        definition.preserveAuthoredMesh = true;
        EditorUtility.SetDirty(definition);
    }

    [MenuItem("Tools/Building/Rebuild Baked S02 Stone Wall")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Rebuild S02 outside Play mode.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var definition = AssetDatabase.LoadAssetAtPath<BuildDefinition>(Definition);
        if (definition == null) throw new InvalidOperationException("Existing S02 definition missing.");
        Apply(definition);
        string prefabPath = AssetDatabase.GetAssetPath(definition.authoringPrefab);
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            prefab.GetComponent<MeshFilter>().sharedMesh = definition.mesh;
            prefab.GetComponent<MeshRenderer>().sharedMaterial = definition.material;
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        AssetDatabase.SaveAssets();
        BuildStoneUiAuthoring.BakeThumbnail(definition);
        AssetDatabase.SaveAssets();
        Debug.Log("S02 baked wall installed: 12 triangles, colour/AO + normal atlas, three coherent UV variants.");
    }
}
