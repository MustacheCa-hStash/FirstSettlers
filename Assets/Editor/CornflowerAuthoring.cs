using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class CornflowerAuthoring
{
    public const string PrefabPath = "Assets/Prefabs/Cornflower.prefab";
    public const string MaterialPath = "Assets/Materials/M_Flowers/M_Cornflower.mat";
    public const string TexturePath = "Assets/Textures/Flowers/T_Cornflower_Painted_Floret.png";
    public const string MeshPath = "Assets/Models/Grass+Flowers/Cornflower_Painted_Runtime.asset";
    [Serializable] private sealed class Blueprint
    { public Vector3[] positions, normals; public Vector2[] uvs; public Color[] colors; public int[] triangles; }

    [MenuItem("Tools/Foliage/Cornflowers/Build Assets")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Cornflower authoring requires Edit mode.");
        AssetDatabase.Refresh();
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType = TextureImporterType.Default; importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true; importer.mipMapsPreserveCoverage = true; importer.alphaTestReferenceValue = .45f;
        importer.maxTextureSize = 512; importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 4;
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var shader = Shader.Find("Custom/PoppyPainted");
        if (texture == null || shader == null) throw new Exception("Cornflower atlas/shader missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
        material.shader = shader; material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", Vector2.one); material.SetTextureOffset("_BaseMap", Vector2.zero);
        material.SetColor("_BaseColor", Color.white); material.SetColor("_StemColor", new Color(.28f, .43f, .09f));
        material.SetColor("_CenterColor", new Color(.025f, .035f, .12f));
        material.SetFloat("_Cutoff", .45f); material.SetFloat("_AmbientStrength", .18f);
        material.SetFloat("_NormalUpBlend", .2f); material.SetFloat("_InstanceTintStrength", .65f);
        material.SetFloat("_WindStrength", .018f); material.SetVector("_FlowerPetalColor", Vector4.one);
        material.enableInstancing = true; material.doubleSidedGI = true; material.renderQueue = 2450;
        EditorUtility.SetDirty(material);

        var data = JsonUtility.FromJson<Blueprint>(File.ReadAllText("Assets/Models/Grass+Flowers/Cornflower_Painted.json"));
        if (data.positions.Length != data.normals.Length || data.positions.Length != data.uvs.Length ||
            data.positions.Length != data.colors.Length) throw new Exception("Cornflower mesh streams differ.");
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null) { mesh = new Mesh { name = "Cornflower_Painted_Runtime" }; AssetDatabase.CreateAsset(mesh, MeshPath); }
        else mesh.Clear();
        mesh.vertices = data.positions; mesh.normals = data.normals; mesh.uv = data.uvs;
        mesh.colors = data.colors; mesh.triangles = data.triangles; mesh.RecalculateBounds();
        var bounds = mesh.bounds; bounds.Expand(.05f); mesh.bounds = bounds;
        mesh.UploadMeshData(false); EditorUtility.SetDirty(mesh);
        var root = new GameObject("Cornflower");
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),
            out string guid, out long id);
        Directory.CreateDirectory(".utmp/cornflower");
        File.WriteAllText(".utmp/cornflower/identity.txt", $"guid={guid}\nroot={id}\nvertices={mesh.vertexCount}\ntriangles={mesh.GetIndexCount(0)/3}\n");
        Debug.Log($"CORNFLOWER_ASSETS_PASS: {mesh.vertexCount} vertices / {mesh.GetIndexCount(0)/3} triangles, prefab {guid}:{id}");
    }

    [MenuItem("Tools/Foliage/Cornflowers/Assign To Selected World Manager")]
    public static void AssignToSelectedWorldManager()
    {
        var manager = Selection.activeGameObject?.GetComponent<WorldManager>();
        if (manager == null) throw new Exception("Select a WorldManager in Edit mode.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Assign cornflowers in Edit mode.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new Exception("Build cornflower assets first.");
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("flowerSettings.cornflowers.enabled").boolValue = true;
        serialized.FindProperty("flowerSettings.cornflowers.prefab").objectReferenceValue = prefab;
        serialized.ApplyModifiedProperties();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
    }
    public static void BuildBatch()
    { try { Build(); EditorApplication.Exit(0); } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }
}
