using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Replace the corner slot's visuals, retaining its logical identity and quarter-metre box.</summary>
public static class BayPostSetup
{
    public const string ModelPath = "Assets/Models/Buildings/Wood/Modular/BayPost_0.26Wx3.00Hx0.26D.fbx";
    public const string DefinitionPath = BuildingPrototypeSetup.Folder + "/bay-post.asset";
    public const string MeshPath = BuildingPrototypeSetup.Folder + "/bay-post-mesh.asset";
    public const string PrefabPath = BuildingPrototypeSetup.Folder + "/bay-post.prefab";
    public static readonly Vector3 Size = new(.25f, 3, .25f);

    [MenuItem("Tools/Building/Rebuild Bay Post")]
    public static void Rebuild()
    {
        MigrateLegacyAssets();
        var definition = AssetDatabase.LoadAssetAtPath<BuildDefinition>(DefinitionPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(SplitPlankWallSetup.MaterialPath);
        if (definition == null || material == null) throw new InvalidOperationException("Install the wood building kit first.");
        Apply(definition, material); AssetDatabase.SaveAssets();
    }
    public static void MigrateLegacyAssets()
    {
        foreach (string suffix in new[] { ".asset", "-mesh.asset", ".prefab" })
        {
            string oldPath = BuildingPrototypeSetup.Folder + "/corner" + suffix;
            string newPath = BuildingPrototypeSetup.Folder + "/bay-post" + suffix;
            if (AssetDatabase.LoadMainAssetAtPath(newPath) != null || AssetDatabase.LoadMainAssetAtPath(oldPath) == null) continue;
            string error = AssetDatabase.MoveAsset(oldPath, newPath);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }
    }
    public static void Apply(BuildDefinition definition, Material material)
    {
        var model = ModularWoodSetup.Import(ModelPath);
        if (model == null || material == null) throw new InvalidOperationException("Bay post FBX or shared wood material is missing.");
        var baked = WattleWallSetup.BakeStaticMesh(model, Vector3.zero, "Bay post", out string inspection,true);
        Debug.Log(inspection);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null) { mesh = baked; AssetDatabase.CreateAsset(mesh, MeshPath); }
        else { EditorUtility.CopySerialized(baked, mesh); Object.DestroyImmediate(baked); EditorUtility.SetDirty(mesh); }
        // Stable IDs/GUIDs and catalog position let existing corner records resolve to the new art.
        definition.displayName = "Bay post"; definition.kind = BuildPartKind.Corner;
        ModularWoodSetup.Profile(definition,false); definition.mesh = mesh; definition.material = material;
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.name = definition.displayName; root.layer = GameplayLayers.WorldSolid;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            var collider = root.GetComponent<BoxCollider>(); collider.center = definition.LocalBounds.center;
            collider.size = definition.LocalBounds.size; collider.isTrigger = false;
            definition.authoringPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        EditorUtility.SetDirty(definition);
    }
}
