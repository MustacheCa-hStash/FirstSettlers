using UnityEditor;
using UnityEngine;

public static class SpruceOctaImpostorProxyBuilder
{
    private const string MeshPath = "Assets/Models/Trees/Spruce_OctaImpostor_Runtime.mesh";
    private const string MaterialPath = "Assets/Materials/M_Trees/Spruce/Spruce_OctaImpostor_M.mat";
    private const string PrefabPath = "Assets/Prefabs/Spruce_OctaImpostor_Runtime.prefab";

    private static readonly Vector3 CaptureCenter = new Vector3(0.0910787f, 3.0420964f, -0.07994366f);
    private const float CaptureRadius = 4.2999973f;

    [MenuItem("Tools/Impostors/Create or Update Spruce Runtime Proxy")]
    private static void CreateOrUpdate()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "Spruce_OctaImpostor_Runtime" };
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }

        // The shader uses these UVs as canonical billboard coordinates. Keep this
        // mesh intentionally independent of the Blender FBX's topology and UVs.
        mesh.Clear();
        mesh.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
        mesh.bounds = new Bounds(CaptureCenter, Vector3.one * (CaptureRadius * 2f));
        EditorUtility.SetDirty(mesh);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogError("The octa impostor material is missing: " + MaterialPath);
            return;
        }

        var root = new GameObject("Spruce_OctaImpostor_Runtime");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;
        var filter = root.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created canonical spruce octa impostor runtime proxy. Use " + PrefabPath);
    }
}
