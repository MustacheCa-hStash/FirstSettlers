using System;
using UnityEditor;
using UnityEngine;

public static class ForestFernPrefabBuilder
{
    public const string NearPath = "Assets/Resources/Foliage/ForestFern_LOD0.prefab";
    public const string FarPath = "Assets/Resources/Foliage/ForestFern_LOD1.prefab";
    public const string CoarsePath = "Assets/Resources/Foliage/ForestFern_LOD2.prefab";

    [MenuItem("Tools/Foliage/Build Forest Fern")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author ferns in Edit mode.");
        var prefabs = WoodlandSurfaceAuthoring.BuildFerns();
        BindFallbacks(prefabs);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Foliage/Build Coarse Forest Fern")]
    public static void BuildCoarse() => Build();

    internal static void BindFallbacks((GameObject near, GameObject middle, GameObject far) prefabs)
    {
        Replace(NearPath, prefabs.near);
        Replace(FarPath, prefabs.middle);
        Replace(CoarsePath, prefabs.far);
    }

    private static void Replace(string path, GameObject source)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.GetComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            root.GetComponent<MeshRenderer>().sharedMaterial = source.GetComponent<MeshRenderer>().sharedMaterial;
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved) throw new InvalidOperationException("Could not save " + path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
