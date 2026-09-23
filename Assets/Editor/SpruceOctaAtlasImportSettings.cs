using UnityEditor;

public static class SpruceOctaAtlasImportSettings
{
    private static readonly string[] Paths =
    {
        "Assets/Textures/Trees/Impostors/Spruce/Spruce_Octa_AlbedoCoverage.png",
        "Assets/Textures/Trees/Impostors/Spruce/Spruce_Octa_Surface.png",
        "Assets/Textures/Trees/Impostors/Spruce/Spruce_Octa_Depth.png",
        "Assets/Textures/Trees/Impostors/Spruce/Spruce_Octa_MaterialId.png",
        "Assets/Textures/Trees/Impostors/Spruce/Spruce_Octa_Ambient.png"
    };

    [MenuItem("Tools/Impostors/Configure Spruce Octa Atlas Import Settings")]
    private static void Configure()
    {
        foreach (string path in Paths)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.alphaIsTransparency = false;
            importer.sRGBTexture = path.EndsWith("AlbedoCoverage.png");
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            if (path.EndsWith("AlbedoCoverage.png"))
            {
                // The atlas carries alpha-tested needle coverage. Resizing/compressing
                // it without coverage preservation removes most fine foliage.
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.008f;
            }
            importer.SaveAndReimport();
        }
        UnityEngine.Debug.Log("Configured spruce octa atlas import settings without rescaling or compression.");
    }
}
