using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WoodlandEdgeAuthoring
{
    private const string Prefabs = "Assets/Prefabs/";

    [MenuItem("Tools/Foliage/Build Woodland Edge Forms")]
    public static void Build()
    {
        BuildBeech();
        BuildHazel(false);
        BuildHazel(true);
        AssetDatabase.SaveAssets();
        Verify();
    }

    public static void BuildBatch()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void Verify()
    {
        MeshRenderer Foliage(string name) => Array.Find(LoadPrefab(name).GetComponentsInChildren<MeshRenderer>(),
            renderer => renderer.name == "Foliage");
        var mature = Foliage("WoodlandAmericanBeech");
        var edge = Foliage("WoodlandEdgeBeech");
        if (edge.sharedMaterial != mature.sharedMaterial || edge.bounds.min.y > mature.bounds.min.y - 1f ||
            edge.bounds.size.x <= mature.bounds.size.x)
            throw new InvalidOperationException("Edge beech must retain the leaf material and have lower, wider foliage.");
        var hazel = LoadPrefab("WoodlandEdgeHazel").GetComponent<MeshFilter>().sharedMesh.bounds;
        var farHazel = LoadPrefab("WoodlandEdgeHazel_Far").GetComponent<MeshFilter>().sharedMesh.bounds;
        if (hazel.size.y < 1.5f || Mathf.Abs(hazel.size.y - farHazel.size.y) > .1f)
            throw new InvalidOperationException("Near and distant hazel crowns should fill the same vertical band.");
        Debug.Log($"WOODLAND_EDGE_FORMS_PASS: beech foliage begins at {edge.bounds.min.y:F2} m " +
            $"instead of {mature.bounds.min.y:F2} m in the source mesh; hazel height {hazel.size.y:F2} m.");
    }

    private static void BuildBeech()
    {
        var tree = Object.Instantiate(LoadPrefab("WoodlandAmericanBeech"));
        try
        {
            const string name = "WoodlandEdgeBeech";
            tree.name = name;
            foreach (var filter in tree.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = Object.Instantiate(filter.sharedMesh);
                var vertices = mesh.vertices;
                var toRoot = tree.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var toMesh = toRoot.inverse;
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = toMesh.MultiplyPoint3x4(LowerBoughs(toRoot.MultiplyPoint3x4(vertices[i])));
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                filter.sharedMesh = WoodlandFloorAuthoring.Save(mesh,
                    "Assets/Models/Trees/Woodland/" + name + filter.name + ".asset");
            }
            var canopy = tree.GetComponentInChildren<BoxCollider>();
            var lower = LowerBoughs(canopy.bounds.min);
            var upper = LowerBoughs(canopy.bounds.max);
            canopy.center = canopy.transform.InverseTransformPoint((lower + upper) * .5f);
            canopy.size = upper - lower;
            PrefabUtility.SaveAsPrefabAsset(tree, Prefabs + name + ".prefab");
        }
        finally { Object.DestroyImmediate(tree); }
    }

    // Use the same deformation on wood and leaf cards so the lower foliage
    // stays attached to the branches. Keep the trunk grounded and the top tall.
    private static Vector3 LowerBoughs(Vector3 point)
    {
        float lower = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 16f, point.y));
        float spread = 1f + .20f * lower;
        point.y -= Mathf.Min(4f, Mathf.Max(0f, point.y) * .50f) * lower;
        point.x *= spread;
        point.z *= spread;
        return point;
    }

    private static void BuildHazel(bool far)
    {
        string suffix = far ? "_Far" : "";
        var source = LoadPrefab("WoodlandHazel" + suffix);
        var mesh = source.GetComponent<MeshFilter>().sharedMesh;
        // Offset several crowns on separate stems, with a lower outward skirt.
        var lobes = new[]
        {
            Lobe(new Vector3(-.42f, 0f, -.08f), 22f, new Vector3(1.24f, 1.72f, 1.14f)),
            Lobe(new Vector3(.40f, 0f, .18f), 88f, new Vector3(1.33f, 2.12f, 1.08f)),
            Lobe(new Vector3(-.03f, 0f, -.49f), 151f, new Vector3(1.44f, 1.23f, 1.20f)),
            Lobe(new Vector3(.03f, 0f, .48f), 239f, new Vector3(1.19f, 1.52f, 1.11f))
        };
        var combined = new Mesh();
        combined.CombineMeshes(lobes);
        WoodlandFloorAuthoring.Prefab("WoodlandEdgeHazel" + suffix, combined,
            source.GetComponent<MeshRenderer>().sharedMaterial);

        CombineInstance Lobe(Vector3 position, float yaw, Vector3 scale) => new()
        {
            mesh = mesh,
            transform = Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw, 0f), scale)
        };
    }

    private static GameObject LoadPrefab(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab")
        ?? throw new InvalidOperationException("Missing woodland source prefab " + name);
}
