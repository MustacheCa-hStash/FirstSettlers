using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Wire the authored FBX into the existing wall slot without changing placement geometry.</summary>
public static class SplitPlankWallSetup
{
    public const string ModelPath = "Assets/Models/Buildings/Wood/Modular/SplitPlankWall_4.00Wx3.00Hx0.25D.fbx";
    public const string TexturePath = "Assets/Textures/Buildings/Wood/ordinary-wood-trim-albedo.png";
    public const string MaterialPath = "Assets/Materials/Buildings/Wood/SplitPlankWood.mat";
    public const string ShaderName = "Custom/BuildingWoodMatte";
    public const string InfillModelPath = "Assets/Models/Buildings/Wood/TwoPlankInfill_0.5Wx2.75Hx0.25D.fbx";
    public const string InfillDefinitionPath = "Assets/Resources/Building/two-plank-infill.asset";
    public const string InfillPrefabPath = "Assets/Resources/Building/two-plank-infill.prefab";
    public const string InfillContentId = "build.wood.two-plank-infill";

    [MenuItem("Tools/Building/Legacy/Rebuild Two-Plank Infill Wall")]
    public static void RebuildInfill()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (catalog == null || material == null)
            throw new InvalidOperationException("Install the split-plank wall before adding its infill.");
        var infill = CreateInfill(material);
        var presets = new List<BuildDefinition>(catalog.presets);
        int index = presets.FindIndex(p => p != null && p.contentId == InfillContentId);
        if (index < 0) presets.Add(infill); else presets[index] = infill;
        catalog.presets = presets.ToArray();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log("Two-plank infill wall added: 0.5 x 2.75 x 0.25 m, no end-post reservation.");
    }

    public static BuildDefinition CreateInfill(Material material)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(InfillModelPath);
        if (model == null || material == null)
            throw new InvalidOperationException("Two-plank infill model or shared wood material is missing.");
        var filters = model.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length != 1 || filters[0].sharedMesh == null || filters[0].sharedMesh.subMeshCount != 1)
            throw new InvalidOperationException("The infill requires one mesh with one material submesh.");
        var mesh = filters[0].sharedMesh;
        var bounds = new Bounds(new Vector3(.25f, 1.375f, .125f), new Vector3(.5f, 2.75f, .25f));
        if ((mesh.bounds.center - bounds.center).sqrMagnitude > .000001f ||
            (mesh.bounds.size - bounds.size).sqrMagnitude > .000001f || !Identity(filters[0].transform.localToWorldMatrix))
            throw new InvalidOperationException("Infill FBX must itself occupy (0,0,0)..(0.5,2.75,0.25) with identity transforms.");

        var definition = AssetDatabase.LoadAssetAtPath<BuildDefinition>(InfillDefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<BuildDefinition>();
            AssetDatabase.CreateAsset(definition, InfillDefinitionPath);
        }
        definition.contentId = InfillContentId;
        definition.displayName = "Two-plank infill wall";
        definition.kind = BuildPartKind.Wall;
        definition.wallPlacementMode = WallPlacementMode.Infill;
        definition.sizeUnits = new Vector3Int(2, 11, 1);
        definition.minimumUnits = Vector3Int.zero;
        definition.wallEndInsetUnits = 0;
        definition.mesh = mesh;
        definition.material = material;

        bool existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(InfillPrefabPath) != null;
        var root = existingPrefab ? PrefabUtility.LoadPrefabContents(InfillPrefabPath) : new GameObject(definition.displayName);
        try
        {
            root.layer = GameplayLayers.WorldSolid;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            if (!root.TryGetComponent<MeshFilter>(out var filter)) filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            if (!root.TryGetComponent<MeshRenderer>(out var renderer)) renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (!root.TryGetComponent<BoxCollider>(out var collider)) collider = root.AddComponent<BoxCollider>();
            collider.center = definition.LocalBounds.center;
            collider.size = definition.LocalBounds.size;
            collider.isTrigger = false;
            definition.authoringPrefab = PrefabUtility.SaveAsPrefabAsset(root, InfillPrefabPath);
        }
        finally
        {
            if (existingPrefab) PrefabUtility.UnloadPrefabContents(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }
        EditorUtility.SetDirty(definition);
        return definition;
    }

    [MenuItem("Tools/Building/Rebuild Split-Plank Wood Wall")]
    public static void Rebuild()
    {
        var wall = AssetDatabase.LoadAssetAtPath<BuildDefinition>(BuildingPrototypeSetup.Folder + "/wall.asset");
        if (wall == null) throw new InvalidOperationException("Install the building prototype before rebuilding its wall.");
        Apply(wall);
        AssetDatabase.SaveAssets();
        Debug.Log("Split-plank wood wall rebuilt from its FBX and atlas; full-span profile is 4 x 3 x 0.25 m.");
    }

    public static void Apply(BuildDefinition wall)
    {
        var model = ModularWoodSetup.Import(ModelPath);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var shader = Shader.Find(ShaderName);
        if (model == null || texture == null || shader == null)
            throw new InvalidOperationException("Split-plank model, trim atlas or matte shader is missing.");
        var filters = model.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length != 1 || filters[0].sharedMesh == null || filters[0].sharedMesh.subMeshCount != 1)
            throw new InvalidOperationException("The building renderer requires one wall mesh with one material submesh.");
        var mesh = filters[0].sharedMesh;
        var bounds = new Bounds(new Vector3(2, 1.5f, 0), new Vector3(4, 3, .25f));
        if ((mesh.bounds.center - bounds.center).sqrMagnitude > .000001f ||
            (mesh.bounds.size - bounds.size).sqrMagnitude > .000001f ||
            !Identity(filters[0].transform.localToWorldMatrix))
            throw new InvalidOperationException("FBX mesh must itself occupy (0,0,-0.125)..(4,3,0.125), with no compensating object transform. Check Bake Axis Conversion and applied export transforms.");

        Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "SplitPlankWood" };
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Saturation", .9f);
            material.SetFloat("_Contrast", .9f);
            material.SetFloat("_Brightness", 1f);
            material.SetFloat("_AmbientFloor", .18f);
            material.SetFloat("_DirectLightStrength", .85f);
            material.SetFloat("_LightWrap", .08f);
            material.SetFloat("_ShadowStrength", 1f);
            material.SetColor("_ShadowTint", new Color(.86f, .91f, 1f, 1f));
            material.SetFloat("_ShadowTintStrength", .12f);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", texture);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);

        // Retain the existing content ID and asset GUID, replacing the same picker option.
        wall.displayName = "Split-plank wood wall";
        wall.kind = BuildPartKind.Wall;
        ModularWoodSetup.Profile(wall,true);
        wall.mesh = mesh;
        wall.material = material;

        string prefabPath = BuildingPrototypeSetup.Folder + "/wall.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            root.name = wall.displayName;
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            var collider = root.GetComponent<BoxCollider>();
            collider.center = wall.LocalBounds.center;
            collider.size = wall.LocalBounds.size;
            collider.isTrigger = false;
            wall.authoringPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        EditorUtility.SetDirty(wall);
    }

    private static bool Identity(Matrix4x4 matrix)
    {
        for (int i = 0; i < 16; ++i)
            if (Mathf.Abs(matrix[i] - Matrix4x4.identity[i]) > .00001f) return false;
        return true;
    }
}
