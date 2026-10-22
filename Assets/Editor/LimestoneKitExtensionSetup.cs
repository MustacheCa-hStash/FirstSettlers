using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Scoped import of the L04-L09 pier, paving, footing, finish, plinth and doorway.</summary>
public static class LimestoneKitExtensionSetup
{
    public const string Colour = "Assets/Textures/Buildings/Limestone/LimestoneExtension_ColourAO.png";
    public const string Normal = "Assets/Textures/Buildings/Limestone/LimestoneExtension_Normal.png";
    public const string MaterialPath = "Assets/Materials/Buildings/Limestone/LimestoneExtension.mat";
    public static readonly string[] Slugs = { "l04-limestone-pier", "l05-limestone-paving", "l06-limestone-footing",
        "l07-limewashed-wall", "l08-limestone-plinth", "l09-limestone-doorway" };
    public static readonly string[] Ids = Array.ConvertAll(Slugs, s => "build.stone." + s);
    public static readonly string[] Names = { "L04 square limestone pier", "L05 limestone paving", "L06 limestone strip footing",
        "L07 limewashed wall", "L08 limestone plinth course", "L09 square limestone doorway" };
    public static readonly int[] Triangles = { 12, 12, 12, 12, 12, 36 };
    public static bool Available => File.Exists(LimestoneKitSetup.Models + "/L04_Baked.fbx") && File.Exists(Colour) && File.Exists(Normal);
    public static readonly Vector3[] Sizes = { new(.5f,3,.5f), new(4,.25f,4), new(4,.5f,1), new(4,3,.5f), new(4,.5f,.5f), new(4,3,.5f) };
    public static readonly Vector3[] Minima = { new(-.25f,0,-.25f), new(0,-.25f,0), new(0,-.5f,-.5f), new(0,0,-.25f), new(0,0,-.25f), new(0,0,-.25f) };
    public static Bounds Box(Vector3 lo, Vector3 hi) => new((lo + hi) * .5f, hi - lo);

    [MenuItem("Tools/Building/Install L04-L09 Limestone Components")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install limestone outside Play mode.");
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if (catalog == null) throw new InvalidOperationException("Existing building catalog is required.");
        var parts = Create(); var presets = new List<BuildDefinition>(catalog.presets);
        foreach (var d in parts) { int index = presets.FindIndex(p => p != null && p.contentId == d.contentId); if (index < 0) presets.Add(d); else presets[index] = d; }
        catalog.presets = presets.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        foreach (var d in parts) BuildStoneUiAuthoring.BakeThumbnail(d);
        AssetDatabase.SaveAssets();
        Debug.Log("LIMESTONE EXTENSION INSTALLED: L04-L09, shared maps, 18 UV layouts, compound doorway collision and icons.");
    }

    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/Limestone"); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var material = BakedStoneWallSetup.CreateMaterial(MaterialPath, Colour, Normal, "Limestone components");
        material.SetColor("_BaseColor", Color.white); material.SetFloat("_Saturation", 1); material.SetFloat("_Contrast", 1); material.SetFloat("_Brightness", 1);
        EditorUtility.SetDirty(material);
        var result = new BuildDefinition[6];
        for (int i = 0; i < result.Length; i++)
        {
            string path = BuildingPrototypeSetup.Folder + "/" + Slugs[i];
            var d = AssetDatabase.LoadAssetAtPath<BuildDefinition>(path + ".asset");
            if (d == null) { d = ScriptableObject.CreateInstance<BuildDefinition>(); AssetDatabase.CreateAsset(d, path + ".asset"); }
            d.contentId = Ids[i]; d.displayName = Names[i];
            d.kind = i == 0 ? BuildPartKind.Corner : i == 1 ? BuildPartKind.Floor : i == 2 ? BuildPartKind.Foundation : BuildPartKind.Wall;
            d.sizeUnits = Vector3Int.RoundToInt(Sizes[i] / BuildGeometry.Unit); d.minimumUnits = Vector3Int.zero; d.boundsOffset = Minima[i];
            d.stackRiseUnits = i == 1 ? 1 : i == 2 || i == 4 ? 2 : 12; d.wallEndInsetUnits = 0; d.wallPlacementMode = WallPlacementMode.Panel;
            d.requiresFullBearing = i == 1; d.canGroundOnTerrain = false; d.preserveAuthoredMesh = true; d.maxHealth = 100;
            d.material = material; d.jointCoverVariants = Array.Empty<Mesh>(); d.jointCoverMaterial = null;
            d.solidBoxes = Array.Empty<Bounds>(); d.wallOpenings = Array.Empty<Bounds>(); d.occupiedVolumes = Array.Empty<BuildConvexVolume>();
            if (i == 5)
            {
                d.solidBoxes = new[] { Box(new(0,0,-.25f), new(1.375f,2.25f,.25f)), Box(new(2.625f,0,-.25f), new(4,2.25f,.25f)), Box(new(0,2.25f,-.25f), new(4,3,.25f)) };
                d.wallOpenings = new[] { Box(new(1.375f,0,-.25f), new(2.625f,2.25f,.25f)) };
            }
            var sockets = new List<BuildJointSocket>();
            if (d.kind == BuildPartKind.Wall) for (int j = 0; j <= 16; j++)
                if (i != 5 || j * .25f < 1.375f || j * .25f > 2.625f)
                    sockets.Add(new BuildJointSocket { localPosition = new(j * .25f, 0, 0), outward = j == 0 ? Vector3.left : Vector3.right, interior = j > 0 && j < 16 });
            d.jointSockets = sockets.ToArray();
            BakedStoneWallSetup.ApplyMeshes(d, LimestoneKitSetup.Models + "/L0" + (i + 4) + "_Baked");
            BuildKitDimensions.Apply(d);
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab") != null;
            var root = existing ? PrefabUtility.LoadPrefabContents(path + ".prefab") : new GameObject(Names[i]);
            try
            {
                root.name = Names[i]; root.layer = GameplayLayers.WorldSolid; root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
                if (!root.TryGetComponent<MeshFilter>(out var filter)) filter = root.AddComponent<MeshFilter>();
                if (!root.TryGetComponent<MeshRenderer>(out var renderer)) renderer = root.AddComponent<MeshRenderer>();
                filter.sharedMesh = d.mesh; renderer.sharedMaterial = material;
                foreach (var old in root.GetComponents<Collider>()) Object.DestroyImmediate(old);
                foreach (var solid in d.solidBoxes.Length > 0 ? d.solidBoxes : new[] { d.LocalBounds })
                { var collider = root.AddComponent<BoxCollider>(); collider.center = solid.center; collider.size = solid.size; }
                d.authoringPrefab = PrefabUtility.SaveAsPrefabAsset(root, path + ".prefab");
            }
            finally { if (existing) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
            EditorUtility.SetDirty(d); result[i] = d;
        }
        return result;
    }
}
