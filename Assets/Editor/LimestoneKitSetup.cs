using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Scoped L01-L03 import. No saved world or unrelated asset is rebuilt.</summary>
public static class LimestoneKitSetup
{
    public const string Models = "Assets/Models/Buildings/Limestone";
    public const string Colour = "Assets/Textures/Buildings/Limestone/Limestone_ColourAO.png";
    public const string Normal = "Assets/Textures/Buildings/Limestone/Limestone_Normal.png";
    public const string MaterialPath = "Assets/Materials/Buildings/Limestone/Limestone.mat";
    public static readonly string[] Ids = {
        "build.stone.l01-coursed-limestone", "build.stone.l02-dressed-limestone", "build.stone.l03-narrow-limestone"
    };
    public static readonly string[] Slugs = { "l01-coursed-limestone", "l02-dressed-limestone", "l03-narrow-limestone" };
    public static bool Available => File.Exists(Models + "/L01_BakedWall.fbx") && File.Exists(Colour) && File.Exists(Normal);

    [MenuItem("Tools/Building/Install L01-L03 Limestone Walls")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Install limestone outside Play mode.");
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if (catalog == null) throw new InvalidOperationException("The existing building catalog is required.");
        var presets = new List<BuildDefinition>(catalog.presets);
        var parts = Create();
        foreach (var part in parts)
        {
            int index = presets.FindIndex(d => d != null && d.contentId == part.contentId);
            if (index < 0) presets.Add(part); else presets[index] = part;
        }
        catalog.presets = presets.ToArray();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        foreach (var part in parts) BuildStoneUiAuthoring.BakeThumbnail(part);
        AssetDatabase.SaveAssets();
        Debug.Log("LIMESTONE INSTALLED: L01-L03, three UV variants each, shared colour/AO + normal pair and actual-mesh icons.");
    }

    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/Limestone");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var material = BakedStoneWallSetup.CreateMaterial(MaterialPath, Colour, Normal, "Pale limestone");
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Saturation", 1);
        material.SetFloat("_Contrast", 1);
        material.SetFloat("_Brightness", 1);
        EditorUtility.SetDirty(material);
        var names = new[] { "L01 coursed limestone wall", "L02 dressed limestone wall", "L03 narrow limestone wall" };
        var result = new BuildDefinition[3];
        for (int i = 0; i < result.Length; i++)
        {
            string path = BuildingPrototypeSetup.Folder + "/" + Slugs[i];
            var d = AssetDatabase.LoadAssetAtPath<BuildDefinition>(path + ".asset");
            if (d == null) { d = ScriptableObject.CreateInstance<BuildDefinition>(); AssetDatabase.CreateAsset(d, path + ".asset"); }
            int span = i == 2 ? 8 : 16;
            d.contentId = Ids[i]; d.displayName = names[i]; d.kind = BuildPartKind.Wall;
            d.wallPlacementMode = WallPlacementMode.Panel;
            d.sizeUnits = new Vector3Int(span, 12, 2); d.minimumUnits = Vector3Int.zero;
            d.boundsOffset = new Vector3(0, 0, -.25f); d.stackRiseUnits = 12; d.wallEndInsetUnits = 0;
            d.canGroundOnTerrain = false; d.requiresFullBearing = false; d.maxHealth = 100;
            d.material = material; d.preserveAuthoredMesh = true;
            d.jointSockets = new BuildJointSocket[span + 1];
            for (int j = 0; j <= span; j++)
                d.jointSockets[j] = new BuildJointSocket { localPosition = new Vector3(j * .25f, 0, 0),
                    outward = j == 0 ? Vector3.left : Vector3.right, interior = j > 0 && j < span };
            d.jointCoverVariants = Array.Empty<Mesh>(); d.jointCoverMaterial = null;
            d.solidBoxes = Array.Empty<Bounds>(); d.wallOpenings = Array.Empty<Bounds>();
            d.occupiedVolumes = Array.Empty<BuildConvexVolume>();
            BakedStoneWallSetup.ApplyMeshes(d, Models + "/L0" + (i + 1) + "_BakedWall");
            BuildKitDimensions.Apply(d);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab") != null;
            var root = existing ? PrefabUtility.LoadPrefabContents(path + ".prefab") : new GameObject(names[i]);
            try
            {
                root.name = names[i]; root.layer = GameplayLayers.WorldSolid;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
                if (!root.TryGetComponent<MeshFilter>(out var filter)) filter = root.AddComponent<MeshFilter>();
                if (!root.TryGetComponent<MeshRenderer>(out var renderer)) renderer = root.AddComponent<MeshRenderer>();
                filter.sharedMesh = d.mesh; renderer.sharedMaterial = material;
                foreach (var old in root.GetComponents<Collider>()) Object.DestroyImmediate(old);
                var collider = root.AddComponent<BoxCollider>(); collider.center = d.LocalBounds.center; collider.size = d.LocalBounds.size;
                d.authoringPrefab = PrefabUtility.SaveAsPrefabAsset(root, path + ".prefab");
            }
            finally { if (existing) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
            EditorUtility.SetDirty(d); result[i] = d;
        }
        return result;
    }
}
