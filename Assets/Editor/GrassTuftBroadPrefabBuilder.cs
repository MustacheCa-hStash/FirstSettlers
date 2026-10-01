using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class GrassTuftBroadPrefabBuilder
{
    private const string TexturePath = "Assets/Textures/Grass/T_GrassTuftBroad_Cutout.png";
    private const string SourceMaterialPath = "Assets/Materials/M_Grass.mat";
    private const string MaterialPath = "Assets/Materials/M_Grass/M_GrassTuftBroad.mat";

    [MenuItem("Tools/Foliage/Build Broad Grass Tuft Prefabs")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Grass authoring runs only in Edit mode.");
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter textureImporter = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (textureImporter == null)
            throw new System.InvalidOperationException("Broad grass texture could not be imported.");
        textureImporter.alphaIsTransparency = true;
        textureImporter.mipmapEnabled = true;
        textureImporter.mipMapsPreserveCoverage = true;
        textureImporter.alphaTestReferenceValue = 0.5f;
        textureImporter.wrapMode = TextureWrapMode.Clamp;
        textureImporter.filterMode = FilterMode.Trilinear;
        textureImporter.SaveAndReimport();

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath);
        if (texture == null || source == null)
            throw new System.InvalidOperationException("Broad grass texture or base grass material is missing.");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(source);
            material.name = "M_GrassTuftBroad";
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_RootTint", new Color(0.68f, 0.76f, 0.60f, 1f));
        material.SetColor("_TipTint", new Color(1.05f, 1.06f, 0.92f, 1f));
        material.SetFloat("_HeightGradientPower", 0.85f);
        material.SetFloat("_Cutoff", 0.5f);
        material.SetFloat("_BladeMinY", 0f);
        material.SetFloat("_BladeMaxY", 0.370f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);

        BuildOne("GrassTuftBroad_LOD0", material);
        BuildOne("GrassTuftBroad_LOD1", material);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void BuildOne(string name, Material material)
    {
        string modelPath = "Assets/Models/Grass+Flowers/" + name + ".fbx";
        AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
        ModelImporter modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (modelImporter == null)
            throw new System.InvalidOperationException("Model importer missing for " + modelPath);
        modelImporter.useFileUnits = false;
        modelImporter.bakeAxisConversion = true;
        modelImporter.SaveAndReimport();
        Mesh mesh = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is Mesh candidate)
            {
                mesh = candidate;
                break;
            }
        }
        if (mesh == null)
            throw new System.InvalidOperationException("No mesh found in " + modelPath);
        GameObject importedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        MeshFilter importedFilter = importedRoot != null ? importedRoot.GetComponentInChildren<MeshFilter>() : null;
        if (importedFilter == null)
            throw new System.InvalidOperationException("No model transform found for " + modelPath);

        // Grass rendering pulls only MeshFilter.sharedMesh from the prefab and
        // never applies prefab transforms. FBX units and axis rotation therefore
        // have to be baked into a separate runtime mesh subasset.
        Mesh baked = Object.Instantiate(mesh);
        baked.name = name + "_Runtime";
        Matrix4x4 modelTransform = importedFilter.transform.localToWorldMatrix;
        Vector3[] vertices = baked.vertices;
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = modelTransform.MultiplyPoint3x4(vertices[i]);
        baked.vertices = vertices;
        baked.RecalculateNormals();
        baked.RecalculateBounds();

        string runtimeMeshPath = "Assets/Models/Grass+Flowers/" + name + "_Runtime.asset";
        Mesh runtimeMesh = AssetDatabase.LoadAssetAtPath<Mesh>(runtimeMeshPath);
        if (runtimeMesh == null)
        {
            AssetDatabase.CreateAsset(baked, runtimeMeshPath);
            runtimeMesh = baked;
        }
        else
        {
            EditorUtility.CopySerialized(baked, runtimeMesh);
            EditorUtility.SetDirty(runtimeMesh);
            Object.DestroyImmediate(baked);
        }

        GameObject root = new GameObject(name);
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = runtimeMesh;
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            string prefabPath = "Assets/Prefabs/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"Created {prefabPath}: {runtimeMesh.triangles.Length / 3} triangles, " +
                $"mesh min {runtimeMesh.bounds.min.x:F4}, {runtimeMesh.bounds.min.y:F4}, {runtimeMesh.bounds.min.z:F4}, " +
                $"mesh max {runtimeMesh.bounds.max.x:F4}, {runtimeMesh.bounds.max.y:F4}, {runtimeMesh.bounds.max.z:F4}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
