using System;
using UnityEditor;
using UnityEngine;

/// <summary>Minimal S03/S04/S06/S07/S12 solids sharing one baked colour/AO + normal atlas.</summary>
public static class BakedStoneKitSetup
{
    public const string Colour = "Assets/Textures/Buildings/Stone/BakedStoneKit_ColourAO.png";
    public const string Normal = "Assets/Textures/Buildings/Stone/BakedStoneKit_Normal.png";
    public const string MaterialPath = "Assets/Materials/Buildings/Stone/BakedStoneKit.mat";
    public static readonly string[] Tags = { "S03", "S04", "S06", "S07", "S12" };
    public static readonly int[] Triangles = { 12, 12, 72, 12, 24 };
    public static Material CreateMaterial() => BakedStoneWallSetup.CreateMaterial(MaterialPath, Colour, Normal, "Baked stone kit");

    public static void Apply(BuildDefinition definition, int index, Material material)
    {
        definition.material = material;
        BakedStoneWallSetup.ApplyMeshes(definition, StoneKitSetup.Models + "/" + Tags[index] + "_Baked");
    }

    [MenuItem("Tools/Building/Rebuild Baked Stone Kit")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Rebuild baked stone outside Play mode.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var material = CreateMaterial();
        for (int index = 0; index < Tags.Length; index++)
        {
            string path = BuildingPrototypeSetup.Folder + "/" + StoneKitSetup.Ids[index + 1].Replace("build.stone.", "");
            var definition = AssetDatabase.LoadAssetAtPath<BuildDefinition>(path + ".asset");
            if (definition == null) throw new InvalidOperationException("Existing stone definition missing: " + path);
            Apply(definition, index, material);
            var prefab = PrefabUtility.LoadPrefabContents(path + ".prefab");
            try
            {
                prefab.GetComponent<MeshFilter>().sharedMesh = definition.mesh;
                prefab.GetComponent<MeshRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(prefab, path + ".prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            BuildStoneUiAuthoring.BakeThumbnail(definition);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("BAKED STONE KIT: S03/S04/S06/S07/S12 installed, 132 triangles total; one shared texture pair, existing collision and support retained.");
    }
}
