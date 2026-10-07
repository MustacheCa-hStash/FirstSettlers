using UnityEditor;
using UnityEngine;

public static class RedMapleOctaImpostorProxyBuilder
{
    private const string MeshPath = "Assets/Models/Trees/RedMaple_OctaImpostor_Runtime.mesh";
    private const string MaterialPath = "Assets/Materials/M_Trees/RedMaple/RedMaple_OctaImpostor_M.mat";
    private const string PrefabPath = "Assets/Prefabs/RedMaple_OctaImpostor_Runtime.prefab";
    private const string AtlasFolder = "Assets/Textures/Trees/Impostors/RedMaple";

    private static readonly Vector3 CaptureCenter = new Vector3(-0.60078f, 8.058559f, -0.0983839f);
    private const float CaptureRadius = 12.175477f;

    [MenuItem("Tools/Impostors/Create or Update Red Maple Runtime Proxy")]
    private static void CreateOrUpdate()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "RedMaple_OctaImpostor_Runtime" };
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }

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

        Material material = LoadOrCreateMaterial();
        if (material == null)
            return;

        var root = new GameObject("RedMaple_OctaImpostor_Runtime");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created Red Maple octa impostor proxy: " + PrefabPath);
    }

    private static Material LoadOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Custom/SpruceOctaImpostor");
            if (shader == null)
            {
                Debug.LogError("The shared octa impostor shader was not found. Wait for SpruceOctaImpostor.shader to import.");
                return null;
            }

            material = new Material(shader) { name = "RedMaple_OctaImpostor_M" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        AssignAtlas(material, "_AlbedoCoverage", "RedMaple_Octa_AlbedoCoverage.png");
        AssignAtlas(material, "_SurfaceAtlas", "RedMaple_Octa_Surface.png");
        AssignAtlas(material, "_DepthAtlas", "RedMaple_Octa_Depth.png");
        AssignAtlas(material, "_MaterialIdAtlas", "RedMaple_Octa_MaterialId.png");
        AssignAtlas(material, "_AmbientAtlas", "RedMaple_Octa_Ambient.png");
        material.SetVector("_CaptureCenterLS", CaptureCenter);
        material.SetFloat("_CaptureRadius", CaptureRadius);
        material.SetFloat("_FramesPerAxis", 8f);
        material.SetFloat("_AtlasTileResolution", 504f);
        material.SetFloat("_AmbientTileResolution", 128f);
        material.SetFloat("_AtlasPadding", 2f);
        material.SetFloat("_DepthParallax", 0.45f);
        material.SetFloat("_AOStrength", 0.55f);
        material.SetFloat("_SkyStrength", 1f);
        material.SetFloat("_AmbientFloor", 0.45f);
        material.SetFloat("_LeafLightWrap", 0.6f);
        material.SetFloat("_FoliageBrightness", 0.8f);
        material.SetFloat("_BarkBrightness", 1.1f);
        material.SetColor("_BarkShadowColor", new Color(0.32f, 0.36f, 0.38f, 1f));
        material.SetColor("_BarkHighlightColor", new Color(0.52f, 0.58f, 0.6f, 1f));
        material.SetFloat("_BarkColorRemap", 1f);
        material.SetFloat("_BarkRemapBlackPoint", 0.38f);
        material.SetFloat("_BarkRemapWhitePoint", 0.96f);
        material.SetFloat("_TransmissionStrength", 0.55f);
        material.SetFloat("_WindStrength", 0.035f);
        material.SetFloat("_WindSpeed", 1.2f);
        material.SetFloat("_UseSeasonPalette", 1f);
        material.SetFloat("_SeasonAutumnAmount", 1f);
        material.SetColor("_SummerLeafColor", new Color(0.18f, 0.42f, 0.12f, 1f));
        material.SetColor("_AutumnRedColor", new Color(0.88f, 0.06f, 0.035f, 1f));
        material.SetColor("_AutumnCrimsonColor", new Color(0.48f, 0.025f, 0.04f, 1f));
        material.SetColor("_AutumnOrangeColor", new Color(1f, 0.25f, 0.055f, 1f));
        material.SetFloat("_AutumnVariationStrength", 0.62f);
        material.SetColor("_LeafSeasonTint", Color.white);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AssignAtlas(Material material, string property, string fileName)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasFolder + "/" + fileName);
        if (texture == null)
            Debug.LogError("Missing Red Maple impostor atlas: " + fileName);
        else
            material.SetTexture(property, texture);
    }
}
