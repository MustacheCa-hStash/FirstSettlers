using System;
using UnityEditor;
using UnityEngine;

/// <summary>One profiled plank panel and three rails; shared trim timber retains its own material.</summary>
public static class BakedSplitPlankWallSetup
{
    public const string Model = "Assets/Models/Buildings/Wood/Modular/SplitPlankWall_Baked.fbx";
    public const string Colour = "Assets/Textures/Buildings/Wood/SplitPlankWall_Baked_ColourAO.png";
    public const string Normal = "Assets/Textures/Buildings/Wood/SplitPlankWall_Baked_Normal.png";
    public const string MaterialPath = "Assets/Materials/Buildings/Wood/BakedSplitPlankWall.mat";
    public static void Apply(BuildDefinition wall)
    {
        var original = SplitPlankWallSetup.TrimMaterial;
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
        {
            var clone = new Material(original) { name = "Baked split plank wall" };
            AssetDatabase.CreateAsset(clone, MaterialPath);
        }
        wall.material = BakedStoneWallSetup.CreateMaterial(MaterialPath, Colour, Normal, "Baked split plank wall");
        var importer = AssetImporter.GetAtPath(Model) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Run Tools/BakeSplitPlankWall.py first.");
        importer.bakeAxisConversion = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.isReadable = false; importer.importAnimation = false; importer.SaveAndReimport();
        wall.mesh = ModularWoodSetup.WallMesh(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
        wall.uvVariants = Array.Empty<Mesh>();
        // Keep the existing generated bearing bands; their trim pixels are retained in the bake.
        wall.preserveAuthoredMesh = false;
        wall.jointCoverMaterial = original;
        EditorUtility.SetDirty(wall);
    }
    [MenuItem("Tools/Building/Rebuild Baked Split-Plank Wall")]
    public static void Install()
    {
        SplitPlankWallSetup.Rebuild();
        var wall = AssetDatabase.LoadAssetAtPath<BuildDefinition>(BuildingPrototypeSetup.Folder + "/wall.asset");
        BuildStoneUiAuthoring.BakeThumbnail(wall); AssetDatabase.SaveAssets();
    }
}
