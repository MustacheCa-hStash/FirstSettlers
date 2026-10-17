using System;
using UnityEditor;
using UnityEngine;

/// <summary>Small baked atlases for the mounted torch and open cord sleeping platform.</summary>
public static class BakedFurnishingsSetup
{
    public const string BedId = "build.furniture.f05-sleeping-platform";
    public static void Apply(BuildDefinition d)
    {
        bool torch = d.contentId == TorchKitSetup.Id;
        if (!torch && d.contentId != BedId) throw new ArgumentException("Unsupported baked furnishing");
        string folder = torch ? "Lighting" : "Furniture", name = torch ? "MountedTorch" : "SleepingPlatform";
        string model = "Assets/Models/Buildings/" + folder + "/" + name + "_Baked.fbx";
        string textures = "Assets/Textures/Buildings/" + folder + "/" + name + "_Baked_";
        string materialPath = torch ? "Assets/Resources/Lighting/BakedMountedTorch.mat" : "Assets/Materials/Buildings/Wood/BakedSleepingPlatform.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
            AssetDatabase.CreateAsset(new Material(d.material) { name = "Baked " + name }, materialPath);
        d.material = BakedStoneWallSetup.CreateMaterial(materialPath, textures + "ColourAO.png", textures + "Normal.png", name);
        AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(model);
        importer.bakeAxisConversion = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.isReadable = false; importer.importAnimation = false; importer.SaveAndReimport();
        d.mesh = AssetDatabase.LoadAssetAtPath<GameObject>(model).GetComponentInChildren<MeshFilter>(true).sharedMesh;
        d.uvVariants = Array.Empty<Mesh>(); d.preserveAuthoredMesh = true; EditorUtility.SetDirty(d);
    }
    [MenuItem("Tools/Building/Rebuild Baked Torch and Sleeping Platform")]
    public static void Install()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var slug in new[] { "wall-torch", "f05-sleeping-platform" })
        {
            var d = AssetDatabase.LoadAssetAtPath<BuildDefinition>(BuildingPrototypeSetup.Folder + "/" + slug + ".asset");
            Apply(d);
            string path = AssetDatabase.GetAssetPath(d.authoringPrefab);
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                prefab.GetComponent<MeshFilter>().sharedMesh = d.mesh;
                prefab.GetComponent<MeshRenderer>().sharedMaterial = d.material;
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            BuildStoneUiAuthoring.BakeThumbnail(d);
        }
        AssetDatabase.SaveAssets();
    }
}
