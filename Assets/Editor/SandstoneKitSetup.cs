using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Scoped import of SS01-SS11. Existing definitions and world scenes are retained.</summary>
public static class SandstoneKitSetup
{
    public const string Art = "ArtReferences/SandstoneBuildKit/Models";
    public const string Models = "Assets/Models/Buildings/Sandstone";
    public const string MaterialPath = "Assets/Materials/Buildings/Sandstone/Sandstone.mat";
    public const string Colour = "Assets/Textures/Buildings/Sandstone/Sandstone_ColourAO.png";
    public const string Normal = "Assets/Textures/Buildings/Sandstone/Sandstone_Normal.png";
    public static bool Available => File.Exists(Art + "/sandstone-models.json") && File.Exists(Models + "/SS11F.fbx");
    [Serializable] public class Manifest { public Part[] parts; public int trianglesSelectable; }
    [Serializable] public class Part
    {
        public string tag, slug, name, description, file, slope;
        public int kind, triangles;
        public float[] size, minimum, opening;
        public Solid[] solids;
    }
    [Serializable] public class Solid { public Vector3[] vertices; public int[] triangles; }
    public static Manifest Source() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Art + "/sandstone-models.json"));
    public static string Id(string slug) => "build.stone." + slug;
    public static int PresentCount(BuildCatalog catalog)
    {
        int count = 0;
        if (Available) foreach (var part in Source().parts)
            if (part.slope != "falling" && catalog.Find(Id(part.slug)) != null) count++;
        return count;
    }
    private static Vector3 V(float[] a) => new(a[0], a[1], a[2]);

    [MenuItem("Tools/Building/Install SS01-SS11 Sandstone")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install outside Play mode.");
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if (catalog == null) throw new InvalidOperationException("Existing building catalog required.");
        var definitions = new List<BuildDefinition>(catalog.presets);
        foreach (var d in Create())
        {
            int index = definitions.FindIndex(p => p != null && p.contentId == d.contentId);
            if (index < 0) definitions.Add(d); else definitions[index] = d;
        }
        catalog.presets = definitions.ToArray();
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        foreach (var p in Source().parts)
        {
            var d = AssetDatabase.LoadAssetAtPath<BuildDefinition>(BuildingPrototypeSetup.Folder + "/" + p.slug + ".asset");
            BuildStoneUiAuthoring.BakeThumbnail(d);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("SANDSTONE INSTALLED: SS01-SS11; eleven catalog choices, gable flip, 180 selectable triangles.");
    }

    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/Sandstone");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var material = BakedStoneWallSetup.CreateMaterial(MaterialPath, Colour, Normal, "Warm apricot sandstone");
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Saturation", 1); material.SetFloat("_Contrast", 1); material.SetFloat("_Brightness", 1);
        EditorUtility.SetDirty(material);
        var result = new List<BuildDefinition>();
        BuildDefinition rising = null, falling = null;
        foreach (var p in Source().parts)
        {
            string path = BuildingPrototypeSetup.Folder + "/" + p.slug;
            var d = AssetDatabase.LoadAssetAtPath<BuildDefinition>(path + ".asset");
            if (d == null) { d = ScriptableObject.CreateInstance<BuildDefinition>(); AssetDatabase.CreateAsset(d, path + ".asset"); }
            d.contentId = Id(p.slug); d.displayName = p.name; d.kind = (BuildPartKind)p.kind;
            d.sizeUnits = Vector3Int.RoundToInt(V(p.size) / BuildGeometry.Unit);
            d.minimumUnits = Vector3Int.zero; d.boundsOffset = V(p.minimum);
            d.stackRiseUnits = d.kind == BuildPartKind.Wall || d.kind == BuildPartKind.Corner ? d.sizeUnits.y : 0;
            d.wallEndInsetUnits = 0; d.wallPlacementMode = WallPlacementMode.Panel;
            d.material = material; d.preserveAuthoredMesh = true; d.maxHealth = 100;
             d.canGroundOnTerrain = d.kind == BuildPartKind.Foundation;
            bool gable = !string.IsNullOrEmpty(p.slope);
            d.gableSlope = !gable ? GableSlope.None : p.slope == "falling" ? GableSlope.Falling : GableSlope.Rising;
            d.roofAttachment = gable ? RoofAttachmentMode.Gable : RoofAttachmentMode.None;
            d.flipVariant = null; d.gableRoofSeamMesh = null;
            d.topCourseDepth = 0; d.wallBaseAttachment = false;
            d.jointCoverVariants = Array.Empty<Mesh>(); d.jointCoverMaterial = null;
            d.floorParts = Array.Empty<BuildTimberPart>(); d.floorOpeningMesh = null;
            d.floorOpeningUvVariants = Array.Empty<Mesh>(); d.collisionMesh = null;
            var imported = ModularWoodSetup.Import(Models + "/" + p.file);
            var mesh = WattleWallSetup.BakeStaticMesh(imported, Vector3.zero, p.name, out _, true);
            mesh.RecalculateTangents(); d.mesh = Save(mesh, path + "-mesh.asset");
            d.uvVariants = new[] { d.mesh };
            if ((d.mesh.bounds.min - d.LocalBounds.min).sqrMagnitude > .000001f ||
                (d.mesh.bounds.max - d.LocalBounds.max).sqrMagnitude > .000001f)
                throw new InvalidOperationException("FBX bounds/origin mismatch: " + p.tag);
            bool shaped = p.tag is "SS08" or "SS11" or "SS11F";
            var volumes = new List<BuildConvexVolume>();
            var boxes = new List<Bounds>();
            for (int index = 0; index < p.solids.Length; index++)
            {
                var solid = p.solids[index];
                var hull = new Mesh { name = p.tag + " occupied solid " + index };
                hull.vertices = solid.vertices; hull.triangles = solid.triangles;
                // Unity XYZ reflects Blender's XZY coordinate system.
                var t = hull.triangles; Vector3 centre = Vector3.zero;
                foreach (var v in solid.vertices) centre += v / solid.vertices.Length;
                for (int j = 0; j < t.Length; j += 3)
                    if (Vector3.Dot(Vector3.Cross(solid.vertices[t[j + 1]] - solid.vertices[t[j]], solid.vertices[t[j + 2]] - solid.vertices[t[j]]),
                        (solid.vertices[t[j]] + solid.vertices[t[j + 1]] + solid.vertices[t[j + 2]]) / 3 - centre) < 0)
                        (t[j + 1], t[j + 2]) = (t[j + 2], t[j + 1]);
                hull.triangles = t; hull.RecalculateBounds(); hull.RecalculateNormals();
                if (shaped) volumes.Add(BuildCollisionAuthoring.Volume(Save(hull, path + "-solid-" + index + ".asset")));
                else { boxes.Add(hull.bounds); Object.DestroyImmediate(hull); }
            }
            d.occupiedVolumes = volumes.ToArray(); d.solidBoxes = boxes.ToArray();
            d.wallOpenings = Array.Empty<Bounds>();
            if (p.opening?.Length == 4)
                d.wallOpenings = new[] { new Bounds(new Vector3((p.opening[0] + p.opening[2]) * .5f,
                    (p.opening[1] + p.opening[3]) * .5f, 0), new Vector3(p.opening[2] - p.opening[0], p.opening[3] - p.opening[1], p.size[2])) };
            var sockets = new List<BuildJointSocket>();
            if (d.kind == BuildPartKind.Wall)
                for (int j = 0; j <= d.sizeUnits.x; j++)
                    if ((!gable || j == 0 || j == d.sizeUnits.x) &&
                        (p.opening == null || p.opening.Length == 0 || p.opening[1] > 0 || j * .25f < p.opening[0] || j * .25f > p.opening[2]))
                        sockets.Add(new BuildJointSocket { localPosition = new Vector3(j * .25f, 0, 0),
                            outward = j == 0 ? Vector3.left : Vector3.right, interior = !gable && j > 0 && j < d.sizeUnits.x });
            d.jointSockets = sockets.ToArray();
            var root = new GameObject(p.name) { layer = GameplayLayers.WorldSolid };
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = d.mesh;
                root.AddComponent<MeshRenderer>().sharedMaterial = material;
                foreach (var b in d.solidBoxes) { var c = root.AddComponent<BoxCollider>(); c.center = b.center; c.size = b.size; }
                foreach (var v in d.occupiedVolumes) { var c = root.AddComponent<MeshCollider>(); c.sharedMesh = v.mesh; c.convex = true; }
                d.authoringPrefab = PrefabUtility.SaveAsPrefabAsset(root, path + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
            if (gable) { if (p.slope == "falling") falling = d; else rising = d; }
            if (p.slope != "falling") result.Add(d);
            EditorUtility.SetDirty(d);
        }
        rising.flipVariant = falling; falling.flipVariant = rising;
        EditorUtility.SetDirty(rising); EditorUtility.SetDirty(falling);
        AssetDatabase.SaveAssets(); return result.ToArray();
    }

    private static Mesh Save(Mesh generated, string path)
    {
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null) { saved = generated; AssetDatabase.CreateAsset(saved, path); }
        else { EditorUtility.CopySerialized(generated, saved); Object.DestroyImmediate(generated); EditorUtility.SetDirty(saved); }
        return saved;
    }
}
