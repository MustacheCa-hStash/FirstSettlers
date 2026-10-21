using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class FoliageExclusionProfileAuthoring
{
    public const string Folder = "Assets/ScriptableObjects/FoliageExclusions";

    // Finishes of the same rock share a footprint. Existing artist edits are preserved on rebuild.
    public static void AttachRock(GameObject root, string shape)
    {
        float radius = shape switch
        {
            "Pebble" => .30f, "Fieldstone" => .50f, "FlatStone" => .70f,
            "RoundBoulder" => 1.35f, "SplitBoulder" => 1.75f, "LongBoulder" => 2.05f,
            _ => throw new ArgumentException("Unknown rock shape: " + shape)
        };
        var profile = Attach(root, shape, radius, 0f);
        BakeRockHull(profile, root.GetComponent<RockPreset>()?.collisionHull);
    }

    public static void AttachClover(GameObject root, string name)
    {
        Vector2 zone = name switch
        {
            "CloverClump_v02_A" => new Vector2(.60f, .20f),
            "CloverClump_v02_B_Flowering" => new Vector2(.62f, .20f),
            "CloverClump_v02_Edge" => new Vector2(.42f, .15f),
            "CloverSingle_v02" => new Vector2(.08f, .04f),
            _ => new Vector2(.65f, .65f)
        };
        Attach(root, name, zone.x, zone.y);
    }

    private static FoliageExclusionProfile Attach(GameObject root, string name, float radius, float fade)
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        string path = Folder + "/" + name + ".asset";
        var profile = AssetDatabase.LoadAssetAtPath<FoliageExclusionProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<FoliageExclusionProfile>();
            profile.grassRadius = radius;
            profile.grassFadePadding = fade;
            AssetDatabase.CreateAsset(profile, path);
        }
        var zone = root.GetComponent<FoliageExclusionZone>();
        if (zone == null) zone = root.AddComponent<FoliageExclusionZone>();
        zone.profile = profile;
        return profile;
    }

    public static void BakeRockHull(FoliageExclusionProfile profile, Mesh hull)
    {
        if (hull == null) throw new InvalidOperationException("Rock exclusion requires a collision hull: " + profile.name);
        var vertices = hull.vertices;
        var triangles = hull.triangles;
        Vector3 center = Vector3.zero;
        foreach (var vertex in vertices) center += vertex;
        center /= vertices.Length;
        var planes = new List<Vector4>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i+1]], c = vertices[triangles[i+2]];
            Vector3 normal = Vector3.Cross(b-a, c-a);
            if (normal.sqrMagnitude < .0000000001f) continue;
            normal.Normalize();
            if (Vector3.Dot(normal, center-a) > 0f) normal = -normal;
            float distance = -Vector3.Dot(normal, a);
            bool duplicate = false;
            foreach (var plane in planes)
                if (Vector3.Dot(normal, new Vector3(plane.x, plane.y, plane.z)) > .99999f && Mathf.Abs(distance-plane.w) < .0001f)
                { duplicate = true; break; }
            if (!duplicate) planes.Add(new Vector4(normal.x, normal.y, normal.z, distance));
        }
        if (planes.Count < 4) throw new InvalidOperationException("Invalid rock exclusion hull: " + profile.name);
        profile.rockHullPlanes = planes.ToArray();
        profile.rockHullBounds = hull.bounds;
        EditorUtility.SetDirty(profile);
    }

    [MenuItem("Tools/Foliage/Set Up Grass Exclusion Profiles")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exclusion authoring requires Edit mode.");
        foreach (var shape in RockPresetSetup.Names)
            foreach (var finish in new[] { "Bare", "Moss", "Snow" })
                EditPrefab(RockPresetSetup.Folder + "/" + shape + "_" + finish + ".prefab", root => AttachRock(root, shape));
        foreach (var name in new[] { "CloverClump", "CloverClump_v01", "CloverClump_v02_A",
            "CloverClump_v02_B_Flowering", "CloverClump_v02_Edge", "CloverSingle_v02" })
            EditPrefab("Assets/Prefabs/" + name + ".prefab", root => AttachClover(root, name));
        AssetDatabase.SaveAssets();
    }

    private static void EditPrefab(string path, Action<GameObject> edit)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
