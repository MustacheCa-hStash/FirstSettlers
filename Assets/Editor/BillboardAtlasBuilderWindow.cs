using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class BillboardAtlasBuilderWindow : EditorWindow
{
    private Texture2D frontTexture;
    private Texture2D backTexture;
    private Texture2D leftTexture;
    private Texture2D rightTexture;

    private string frontPath = "";
    private string backPath = "";
    private string leftPath = "";
    private string rightPath = "";
    private string outputPath = "Assets/Textures/Trees/T_Spruce_Impostor4_AlbedoAlpha.png";
    private Color backgroundColor = new Color(0f, 0f, 0f, 0f);

    [MenuItem("Tools/Billboards/Build 4 View Atlas")]
    private static void Open()
    {
        GetWindow<BillboardAtlasBuilderWindow>("4 View Atlas");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("4 View Billboard Atlas", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Layout: front/back on the top row, left/right on the bottom row. Drag textures into the slots or paste file paths.", MessageType.Info);

        DrawImageField("Front", ref frontTexture, ref frontPath);
        DrawImageField("Back", ref backTexture, ref backPath);
        DrawImageField("Left", ref leftTexture, ref leftPath);
        DrawImageField("Right", ref rightTexture, ref rightPath);

        EditorGUILayout.Space();
        outputPath = EditorGUILayout.TextField("Output Path", outputPath);
        backgroundColor = EditorGUILayout.ColorField("Background", backgroundColor);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Transparent Background"))
            backgroundColor = new Color(0f, 0f, 0f, 0f);

        if (GUILayout.Button("Black Background"))
            backgroundColor = Color.black;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        if (GUILayout.Button("Build Atlas"))
            BuildAtlas();
    }

    private static void DrawImageField(string label, ref Texture2D texture, ref string path)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        texture = (Texture2D)EditorGUILayout.ObjectField("Texture", texture, typeof(Texture2D), false);
        path = EditorGUILayout.TextField("Path", path);
    }

    private void BuildAtlas()
    {
        Texture2D front = null;
        Texture2D back = null;
        Texture2D left = null;
        Texture2D right = null;
        Texture2D atlas = null;

        try
        {
            front = LoadTexture(frontTexture, frontPath, "Front");
            back = LoadTexture(backTexture, backPath, "Back");
            left = LoadTexture(leftTexture, leftPath, "Left");
            right = LoadTexture(rightTexture, rightPath, "Right");

            int quadrantWidth = Mathf.Max(Mathf.Max(front.width, back.width), Mathf.Max(left.width, right.width));
            int quadrantHeight = Mathf.Max(Mathf.Max(front.height, back.height), Mathf.Max(left.height, right.height));

            atlas = new Texture2D(quadrantWidth * 2, quadrantHeight * 2, TextureFormat.RGBA32, false);
            Fill(atlas, backgroundColor);

            CopyCentered(front, atlas, 0, quadrantHeight, quadrantWidth, quadrantHeight);
            CopyCentered(back, atlas, quadrantWidth, quadrantHeight, quadrantWidth, quadrantHeight);
            CopyCentered(left, atlas, 0, 0, quadrantWidth, quadrantHeight);
            CopyCentered(right, atlas, quadrantWidth, 0, quadrantWidth, quadrantHeight);

            atlas.Apply();

            if (string.IsNullOrWhiteSpace(outputPath) || !outputPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                Debug.LogError("Output path must be inside Assets/, for example Assets/Textures/Trees/T_Spruce_Impostor4_AlbedoAlpha.png");
                return;
            }

            string fullOutputPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(fullOutputPath, atlas.EncodeToPNG());
            AssetDatabase.ImportAsset(outputPath);

            Debug.Log($"Saved 4 view billboard atlas to {outputPath} ({atlas.width}x{atlas.height}).");
        }
        catch (Exception exception)
        {
            Debug.LogError(exception.Message);
        }
        finally
        {
            DestroyLoadedTexture(front, frontTexture);
            DestroyLoadedTexture(back, backTexture);
            DestroyLoadedTexture(left, leftTexture);
            DestroyLoadedTexture(right, rightTexture);

            if (atlas != null)
                DestroyImmediate(atlas);
        }
    }

    private static Texture2D LoadTexture(Texture2D assignedTexture, string explicitPath, string label)
    {
        string path = !string.IsNullOrWhiteSpace(explicitPath)
            ? explicitPath
            : assignedTexture != null
                ? AssetDatabase.GetAssetPath(assignedTexture)
                : "";

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{label} texture or path is missing.");

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"{label} image does not exist: {path}");

        byte[] bytes = File.ReadAllBytes(fullPath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            DestroyImmediate(texture);
            throw new InvalidOperationException($"{label} image could not be loaded: {path}");
        }

        texture.name = $"{label}_Source";
        return texture;
    }

    private static void Fill(Texture2D texture, Color color)
    {
        Color32 color32 = color;
        Color32[] pixels = new Color32[texture.width * texture.height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color32;

        texture.SetPixels32(pixels);
    }

    private static void CopyCentered(Texture2D source, Texture2D target, int quadrantX, int quadrantY, int quadrantWidth, int quadrantHeight)
    {
        int offsetX = quadrantX + (quadrantWidth - source.width) / 2;
        int offsetY = quadrantY + (quadrantHeight - source.height) / 2;
        Color32[] pixels = source.GetPixels32();
        target.SetPixels32(offsetX, offsetY, source.width, source.height, pixels);
    }

    private static void DestroyLoadedTexture(Texture2D loadedTexture, Texture2D assignedTexture)
    {
        if (loadedTexture != null && loadedTexture != assignedTexture)
            DestroyImmediate(loadedTexture);
    }
}
