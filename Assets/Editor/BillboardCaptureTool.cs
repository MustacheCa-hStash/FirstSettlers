using System.IO;
using UnityEditor;
using UnityEngine;

public static class BillboardCaptureTool
{
    [MenuItem("Tools/Capture Selected Camera Billboard PNG")]
    private static void CaptureSelectedCamera()
    {
        Camera camera = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<Camera>()
            : null;

        if (camera == null)
        {
            Debug.LogError("Select the billboard capture Camera first.");
            return;
        }

        int size = 1024;
        string outputPath = "Assets/Textures/RedMaple_LOD2_Mask_270d.png";

        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        RenderTexture renderTexture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        renderTexture.antiAliasing = 8;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        try
        {
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;

            camera.Render();

            texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            texture.Apply();

            byte[] png = texture.EncodeToPNG();
            File.WriteAllBytes(outputPath, png);

            AssetDatabase.ImportAsset(outputPath);
            Debug.Log($"Saved billboard PNG to {outputPath}");
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;

            Object.DestroyImmediate(texture);
            renderTexture.Release();
            Object.DestroyImmediate(renderTexture);
        }
    }
}