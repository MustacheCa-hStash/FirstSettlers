using UnityEditor;
using UnityEngine;

public static class SugarMapleOctaImpostorProxyBuilder
{
    private const string MeshPath = "Assets/Models/Trees/SugarMaple_OctaImpostor_Runtime.mesh";
    private const string MaterialPath = "Assets/Materials/M_Trees/SugarMaple/SugarMaple_OctaImpostor_M.mat";
    private const string PrefabPath = "Assets/Prefabs/SugarMaple_OctaImpostor_Runtime.prefab";
    private const string AtlasFolder = "Assets/Textures/Trees/Impostors/SugarMaple";

    private static readonly Vector3 CaptureCenter = new Vector3(-0.053984642f, 7.71235847f, 0.465250015f);
    private const float CaptureRadius = 11.6226435f;

    [MenuItem("Tools/Impostors/Create or Update Sugar Maple Runtime Proxy")]
    private static void CreateOrUpdate()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "SugarMaple_OctaImpostor_Runtime" };
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

        var root = new GameObject("SugarMaple_OctaImpostor_Runtime");
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created Sugar Maple octa impostor proxy: " + PrefabPath);
    }

    private static Material LoadOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Custom/SpruceOctaImpostor");
            if (shader == null)
            {
                Debug.LogError("The shared octa impostor shader was not found.");
                return null;
            }

            material = new Material(shader) { name = "SugarMaple_OctaImpostor_M" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        AssignAtlas(material, "_AlbedoCoverage", "SugarMaple_Octa_AlbedoCoverage.png");
        AssignAtlas(material, "_SurfaceAtlas", "SugarMaple_Octa_Surface.png");
        AssignAtlas(material, "_DepthAtlas", "SugarMaple_Octa_Depth.png");
        AssignAtlas(material, "_MaterialIdAtlas", "SugarMaple_Octa_MaterialId.png");
        AssignAtlas(material, "_AmbientAtlas", "SugarMaple_Octa_Ambient.png");

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
        // Keep the captured bark's structure, but constrain its near-black trunk
        // and near-white twig texels into the authored mid-grey bark range.
        material.SetFloat("_BarkBrightness", 1.1f);
        material.SetColor("_BarkShadowColor", new Color(0.20f, 0.12f, 0.07f, 1f));
        material.SetColor("_BarkHighlightColor", new Color(0.50f, 0.34f, 0.20f, 1f));
        material.SetFloat("_BarkColorRemap", 1f);
        material.SetFloat("_BarkRemapBlackPoint", 0.38f);
        material.SetFloat("_BarkRemapWhitePoint", 0.96f);
        // Discard only raster-fringe coverage. Surviving fractional alpha is
        // still consumed by AlphaToMask in the runtime shader.
        material.SetFloat("_Cutoff", 0.04f);
        material.SetFloat("_TransmissionStrength", 0.55f);
        material.SetFloat("_WindStrength", 0.035f);
        material.SetFloat("_WindSpeed", 1.2f);
        MapleImpostorMaterialSettings.Apply(material,
            "Assets/Materials/M_Trees/SugarMaple/M_SugarMapleLeaf_Stylized.mat", 1f);
        material.SetColor("_LeafSeasonTint", Color.white);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AssignAtlas(Material material, string property, string fileName)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasFolder + "/" + fileName);
        if (texture == null)
            Debug.LogError("Missing Sugar Maple impostor atlas: " + fileName);
        else
            material.SetTexture(property, texture);
    }
}
