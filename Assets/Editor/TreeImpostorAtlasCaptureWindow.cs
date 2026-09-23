using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class TreeImpostorAtlasCaptureWindow : EditorWindow
{
    private enum Output { AlbedoCoverage, Surface, Depth, MaterialId }

    [SerializeField] private TreeImpostorCaptureSetup setup;
    [SerializeField, Range(4, 24)] private int framesPerAxis = 12;
    [SerializeField, Range(64, 1024)] private int tileResolution = 256;
    [SerializeField, Range(1, 8)] private int paddingPixels = 2;
    [SerializeField, Range(1, 4)] private int captureSupersample = 2;
    [SerializeField, Range(0, 3)] private int coverageExpansionPixels = 1;
    [SerializeField, Range(0.01f, 1f)] private float coverageExpansionStrength = 0.75f;
    [SerializeField] private string outputFolder = "Assets/Textures/Trees/Impostors/Spruce";
    private const string RuntimeMaterialPath = "Assets/Materials/M_Trees/Spruce/Spruce_OctaImpostor_M.mat";

    private Material semanticLeaf;
    private Material semanticBark;
    // One entry per octa frame.  Each entry maps every destination texel to the
    // closest captured texel that carries leaf/bark data.  Reusing it for the
    // semantic captures is essential: expanded albedo must not sample empty
    // normal, depth, or material-ID texels at runtime.
    private int[][] coverageDonors;

    [MenuItem("Tools/Impostors/Capture Spruce Octa Atlases")]
    private static void Open() => GetWindow<TreeImpostorAtlasCaptureWindow>("Octa Atlas Capture");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Full-Sphere Octahedral Semantic Capture", EditorStyles.boldLabel);
        setup = (TreeImpostorCaptureSetup)EditorGUILayout.ObjectField("Capture Setup", setup, typeof(TreeImpostorCaptureSetup), true);
        framesPerAxis = EditorGUILayout.IntSlider("Frames Per Axis", framesPerAxis, 4, 24);
        tileResolution = EditorGUILayout.IntPopup("Tile Resolution", tileResolution, new[] { "64", "128", "256", "320", "336", "504", "512", "1024" }, new[] { 64, 128, 256, 320, 336, 504, 512, 1024 });
        paddingPixels = EditorGUILayout.IntSlider("Tile Padding", paddingPixels, 1, 8);
        captureSupersample = EditorGUILayout.IntSlider("Capture Supersample", captureSupersample, 1, 4);
        coverageExpansionPixels = EditorGUILayout.IntSlider("Coverage Expansion Pixels", coverageExpansionPixels, 0, 3);
        coverageExpansionStrength = EditorGUILayout.Slider("Coverage Expansion Strength", coverageExpansionStrength, 0.01f, 1f);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        int atlasSize = framesPerAxis * (tileResolution + paddingPixels * 2);
        EditorGUILayout.LabelField("Primary Atlas Size", atlasSize + " x " + atlasSize);
        if (atlasSize > 4096)
            EditorGUILayout.HelpBox("This layout exceeds the 4096 import cap and will be resampled. For high-detail spruce use 8 frames and 504-pixel tiles (4064px atlas).", MessageType.Warning);

        EditorGUILayout.HelpBox("Albedo/coverage uses the spruce's semantic material color path. Every frame is supersampled before alpha-aware downsampling; use 2x for this thin-needle spruce. Surface, depth, and material ID use matching semantic captures. Coverage expansion carries sub-pixel needle coverage into adjacent texels and copies matching semantic data; use one pixel for spruce. AO, bent normal, and sky visibility are deliberately a later bake step.", MessageType.Info);
        using (new EditorGUI.DisabledScope(setup == null))
        {
            if (GUILayout.Button("Capture All Four Atlases"))
                CaptureAll();
        }
    }

    private void CaptureAll()
    {
        foreach (string message in setup.ValidateSetup())
        {
            if (message.StartsWith("ERROR"))
            {
                Debug.LogError("Fix isolated-scene validation errors before capture: " + message, setup);
                return;
            }
        }

        if (!setup.RecalculateBounds(out string boundsMessage))
        {
            Debug.LogError(boundsMessage, setup);
            return;
        }

        Shader shader = Shader.Find("Hidden/TreeImpostor/SemanticCapture");
        if (shader == null)
        {
            Debug.LogError("Semantic capture shader was not found. Wait for Unity to import TreeImpostorSemanticCapture.shader.");
            return;
        }

        if (!outputFolder.StartsWith("Assets/", StringComparison.Ordinal))
        {
            Debug.LogError("Output Folder must be under Assets/.");
            return;
        }

        semanticLeaf = CreateSemanticMaterial(shader, setup.LeafMaterial, 0f);
        semanticBark = CreateSemanticMaterial(shader, setup.BarkMaterial, 1f);
        coverageDonors = new int[framesPerAxis * framesPerAxis][];

        try
        {
            Directory.CreateDirectory(Path.GetFullPath(outputFolder));
            Capture(Output.AlbedoCoverage, "Spruce_Octa_AlbedoCoverage.png", false);
            Capture(Output.Surface, "Spruce_Octa_Surface.png", false);
            // Depth is normalized over the capture range and consumers use
            // only R plus alpha validity. RGBA8 PNG avoids storing four full
            // float channels for every atlas pixel.
            Capture(Output.Depth, "Spruce_Octa_Depth.png", false);
            Capture(Output.MaterialId, "Spruce_Octa_MaterialId.png", false);
            WriteMetadata();
            AssetDatabase.Refresh();
            ConfigureImporters();
            UpdateRuntimeMaterialLayout();
            Debug.Log("Spruce octahedral semantic atlases captured successfully.", setup);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, setup);
        }
        finally
        {
            DestroyImmediate(semanticLeaf);
            DestroyImmediate(semanticBark);
            semanticLeaf = null;
            semanticBark = null;
            coverageDonors = null;
            EditorUtility.ClearProgressBar();
        }
    }

    private Material CreateSemanticMaterial(Shader shader, Material source, float kind)
    {
        var material = new Material(shader) { name = source.name + " Semantic Capture" };
        material.CopyPropertiesFromMaterial(source);
        material.SetFloat("_CaptureMaterialKind", kind);
        return material;
    }

    private void Capture(Output output, string fileName, bool halfPrecision)
    {
        int stride = tileResolution + paddingPixels * 2;
        int atlasSize = framesPerAxis * stride;
        RenderTextureFormat renderFormat = halfPrecision ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
        TextureFormat textureFormat = halfPrecision ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;
        var atlas = new Texture2D(atlasSize, atlasSize, textureFormat, false, true);
        int captureResolution = tileResolution * captureSupersample;
        var captureTile = new Texture2D(captureResolution, captureResolution, textureFormat, false, true);
        var rt = new RenderTexture(captureResolution, captureResolution, 24, renderFormat, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
        Camera camera = setup.CaptureCamera;
        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        float oldDistantTreeEnabled = Shader.GetGlobalFloat("_DistantTreeEnabled");
        float oldDistantTreeBillboard = Shader.GetGlobalFloat("_DistantTreeBillboard");
        float oldDistantTreeNearFade = Shader.GetGlobalFloat("_DistantTreeNearFade");
        // Capture all outputs with the semantic materials.  Albedo is thereby
        // the source's unlit/tintable material colour, rather than a dark
        // lighting result from the intentionally black capture scene.  The
        // 2x coverage-preserving capture retains the thin needle alpha that
        // was lost in the former single-sample semantic pass.
        Material[][] originalMaterials = OverrideTreeMaterials();

        try
        {
            // The source spruce shader participates in the project's runtime
            // near/far dither transition.  That transition intentionally clips
            // a screen-space pattern and must never be baked into an atlas.
            Shader.SetGlobalFloat("_DistantTreeEnabled", 0f);
            Shader.SetGlobalFloat("_DistantTreeBillboard", 0f);
            Shader.SetGlobalFloat("_DistantTreeNearFade", 1f);
            camera.targetTexture = rt;
            camera.backgroundColor = Color.clear;
            camera.clearFlags = CameraClearFlags.SolidColor;
            SetOutput(output);
            for (int y = 0; y < framesPerAxis; y++)
            for (int x = 0; x < framesPerAxis; x++)
            {
                EditorUtility.DisplayProgressBar("Capturing Spruce Octa Atlas", output + " " + (y * framesPerAxis + x + 1) + "/" + (framesPerAxis * framesPerAxis), (y * framesPerAxis + x) / (float)(framesPerAxis * framesPerAxis));
                PositionCameraForFrame(x, y);
                camera.Render();
                RenderTexture.active = rt;
                captureTile.ReadPixels(new Rect(0, 0, captureResolution, captureResolution), 0, 0, false);
                Color[] pixels = DownsampleCoverage(captureTile.GetPixels(), captureResolution, tileResolution);
                int frameIndex = y * framesPerAxis + x;

                if (output == Output.AlbedoCoverage)
                {
                    int[] donors = BuildCoverageDonors(pixels, tileResolution, coverageExpansionPixels);
                    coverageDonors[frameIndex] = donors;
                    StabilizeAlbedoCoverage(pixels, donors, coverageExpansionStrength);
                }
                else
                {
                    int[] donors = coverageDonors?[frameIndex];
                    if (donors == null)
                        throw new InvalidOperationException("Albedo/coverage must be captured before semantic atlas dilation.");
                    StabilizeSemanticData(pixels, donors);
                }

                CopyTileWithPadding(pixels, tileResolution, tileResolution, atlas, x * stride + paddingPixels, y * stride + paddingPixels, paddingPixels);
            }
            atlas.Apply(false, false);
            string path = Path.Combine(outputFolder, fileName).Replace('\\', '/');
            File.WriteAllBytes(Path.GetFullPath(path), halfPrecision ? atlas.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat) : atlas.EncodeToPNG());
        }
        finally
        {
            RestoreTreeMaterials(originalMaterials);
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            Shader.SetGlobalFloat("_DistantTreeEnabled", oldDistantTreeEnabled);
            Shader.SetGlobalFloat("_DistantTreeBillboard", oldDistantTreeBillboard);
            Shader.SetGlobalFloat("_DistantTreeNearFade", oldDistantTreeNearFade);
            DestroyImmediate(rt);
            DestroyImmediate(captureTile);
            DestroyImmediate(atlas);
        }
    }

    private Material[][] OverrideTreeMaterials()
    {
        Renderer[] renderers = setup.TreeRoot.GetComponentsInChildren<Renderer>(true);
        var originals = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            originals[i] = renderers[i].sharedMaterials;
            var replacement = new Material[originals[i].Length];
            for (int j = 0; j < replacement.Length; j++)
            {
                if (originals[i][j] == setup.LeafMaterial) replacement[j] = semanticLeaf;
                else if (originals[i][j] == setup.BarkMaterial) replacement[j] = semanticBark;
                else throw new InvalidOperationException($"Unexpected material on {renderers[i].name}: {originals[i][j]?.name}");
            }
            renderers[i].sharedMaterials = replacement;
        }
        return originals;
    }

    private void RestoreTreeMaterials(Material[][] originals)
    {
        Renderer[] renderers = setup.TreeRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length && i < originals.Length; i++) renderers[i].sharedMaterials = originals[i];
    }

    private void SetOutput(Output output)
    {
        float value = (float)output;
        semanticLeaf.SetFloat("_CaptureOutput", value);
        semanticBark.SetFloat("_CaptureOutput", value);
        float minDepth = setup.CaptureRadius * 1.25f;
        float maxDepth = setup.CaptureRadius * 3.75f;
        Vector4 depthRange = new Vector4(minDepth, maxDepth, 0f, 0f);
        semanticLeaf.SetVector("_CaptureDepthMinMax", depthRange);
        semanticBark.SetVector("_CaptureDepthMinMax", depthRange);
        Matrix4x4 worldToLocal = setup.TreeRoot.worldToLocalMatrix;
        semanticLeaf.SetMatrix("_CaptureWorldToLocal", worldToLocal);
        semanticBark.SetMatrix("_CaptureWorldToLocal", worldToLocal);
    }

    private void PositionCameraForFrame(int x, int y)
    {
        float u = (x + 0.5f) / framesPerAxis;
        float v = (y + 0.5f) / framesPerAxis;
        Vector3 localToCamera = OctDecode(new Vector2(u * 2f - 1f, v * 2f - 1f));
        Vector3 center = setup.TreeRoot.TransformPoint(setup.CaptureCenterLocal);
        float distance = setup.CaptureRadius * 2.5f;
        Camera camera = setup.CaptureCamera;
        camera.transform.position = center + setup.TreeRoot.TransformDirection(localToCamera) * distance;
        Vector3 up = Mathf.Abs(Vector3.Dot(localToCamera, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
        camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, setup.TreeRoot.TransformDirection(up));
        camera.orthographic = true;
        camera.orthographicSize = setup.CaptureRadius;
        camera.nearClipPlane = Mathf.Max(0.01f, distance - setup.CaptureRadius * 1.25f);
        camera.farClipPlane = distance + setup.CaptureRadius * 1.25f;
    }

    private static Vector3 OctDecode(Vector2 encoded)
    {
        Vector3 direction = new Vector3(encoded.x, encoded.y, 1f - Mathf.Abs(encoded.x) - Mathf.Abs(encoded.y));
        if (direction.z < 0f)
        {
            float x = (1f - Mathf.Abs(direction.y)) * Mathf.Sign(direction.x);
            float y = (1f - Mathf.Abs(direction.x)) * Mathf.Sign(direction.y);
            direction.x = x;
            direction.y = y;
        }
        return direction.normalized;
    }

    private static Color[] DownsampleCoverage(Color[] source, int sourceWidth, int targetWidth)
    {
        if (sourceWidth == targetWidth)
            return source;

        int scale = sourceWidth / targetWidth;
        if (scale < 1 || sourceWidth != targetWidth * scale)
            throw new InvalidOperationException("Capture supersample size must divide evenly into tile resolution.");

        var result = new Color[targetWidth * targetWidth];
        for (int y = 0; y < targetWidth; y++)
        for (int x = 0; x < targetWidth; x++)
        {
            // Alpha-tested needles can be thinner than one output texel.  Keep
            // the sample with greatest coverage instead of averaging it away;
            // this is the same coverage-preserving principle used for foliage
            // mipmaps and applies equally to all aligned semantic outputs.
            Color best = Color.clear;
            for (int sy = 0; sy < scale; sy++)
            for (int sx = 0; sx < scale; sx++)
            {
                Color candidate = source[(y * scale + sy) * sourceWidth + x * scale + sx];
                if (candidate.a > best.a)
                    best = candidate;
            }
            result[y * targetWidth + x] = best;
        }
        return result;
    }

    private static int[] BuildCoverageDonors(Color[] source, int width, int expansionPixels)
    {
        var donors = new int[source.Length];
        const float validCoverage = 0.008f;
        // RGB in the source card is black close to its transparent edge.  Bleed
        // a nearby valid needle colour into those edge texels even when their
        // existing alpha survives clipping, otherwise bilinear filtering turns
        // them into the visible black flecks seen on the proxy.
        const float colorBleedThreshold = 0.15f;
        for (int y = 0; y < width; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            donors[index] = index;
            if (expansionPixels == 0 || source[index].a >= colorBleedThreshold)
                continue;

            int best = -1;
            float bestAlpha = validCoverage;
            int bestDistance = int.MaxValue;
            for (int oy = -expansionPixels; oy <= expansionPixels; oy++)
            for (int ox = -expansionPixels; ox <= expansionPixels; ox++)
            {
                int sampleX = x + ox;
                int sampleY = y + oy;
                if (sampleX < 0 || sampleX >= width || sampleY < 0 || sampleY >= width)
                    continue;
                int candidate = sampleY * width + sampleX;
                float alpha = source[candidate].a;
                int distance = ox * ox + oy * oy;
                if (alpha > bestAlpha || (Mathf.Approximately(alpha, bestAlpha) && distance < bestDistance))
                {
                    best = candidate;
                    bestAlpha = alpha;
                    bestDistance = distance;
                }
            }
            if (best >= 0)
                donors[index] = best;
        }
        return donors;
    }

    private static void StabilizeAlbedoCoverage(Color[] pixels, int[] donors, float expansionStrength)
    {
        const float validCoverage = 0.008f;
        Color[] original = (Color[])pixels.Clone();
        for (int i = 0; i < pixels.Length; i++)
        {
            int donor = donors[i];
            if (donor == i)
                continue;

            Color donorColor = original[donor];
            float alpha = original[i].a < validCoverage
                ? Mathf.Max(original[i].a, donorColor.a * expansionStrength)
                : original[i].a;
            pixels[i] = new Color(donorColor.r, donorColor.g, donorColor.b, alpha);
        }
    }

    private static void StabilizeSemanticData(Color[] pixels, int[] donors)
    {
        Color[] original = (Color[])pixels.Clone();
        for (int i = 0; i < pixels.Length; i++)
            if (donors[i] != i)
                pixels[i] = original[donors[i]];
    }

    private static void CopyTileWithPadding(Color[] pixels, int width, int height, Texture2D atlas, int destinationX, int destinationY, int padding)
    {
        atlas.SetPixels(destinationX, destinationY, width, height, pixels);
        for (int p = 1; p <= padding; p++)
        {
            for (int i = 0; i < width; i++)
            {
                atlas.SetPixel(destinationX + i, destinationY - p, pixels[i]);
                atlas.SetPixel(destinationX + i, destinationY + height - 1 + p, pixels[(height - 1) * width + i]);
            }
            for (int i = 0; i < height; i++)
            {
                atlas.SetPixel(destinationX - p, destinationY + i, pixels[i * width]);
                atlas.SetPixel(destinationX + width - 1 + p, destinationY + i, pixels[i * width + width - 1]);
            }
        }
    }

    private void WriteMetadata()
    {
        string json = "{\n" +
            $"  \"framesPerAxis\": {framesPerAxis},\n" +
            $"  \"tileResolution\": {tileResolution},\n" +
            $"  \"paddingPixels\": {paddingPixels},\n" +
            $"  \"captureCenterLocal\": [{setup.CaptureCenterLocal.x:R}, {setup.CaptureCenterLocal.y:R}, {setup.CaptureCenterLocal.z:R}],\n" +
            $"  \"captureRadius\": {setup.CaptureRadius:R},\n" +
            "  \"viewDirectionConvention\": \"tree local center to camera\",\n" +
            "  \"surfaceEncoding\": \"RG octahedral local normal; B roughness; A leaf transmission\",\n" +
            "  \"materialIdEncoding\": \"R leaf; G bark\"\n}";
        File.WriteAllText(Path.GetFullPath(Path.Combine(outputFolder, "Spruce_Octa_Metadata.json")), json);
    }

    private void ConfigureImporters()
    {
        ConfigureImporter("Spruce_Octa_AlbedoCoverage.png", true);
        ConfigureImporter("Spruce_Octa_Surface.png", false);
        ConfigureImporter("Spruce_Octa_Depth.png", false);
        ConfigureImporter("Spruce_Octa_MaterialId.png", false);
    }

    private void UpdateRuntimeMaterialLayout()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RuntimeMaterialPath);
        if (material == null)
        {
            Debug.LogWarning("Spruce octa runtime material was not found; assign the new atlas layout manually.");
            return;
        }

        material.SetFloat("_FramesPerAxis", framesPerAxis);
        material.SetFloat("_AtlasTileResolution", tileResolution);
        material.SetFloat("_AtlasPadding", paddingPixels);
        Texture2D depthAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Path.Combine(outputFolder, "Spruce_Octa_Depth.png").Replace('\\', '/'));
        if (depthAtlas != null)
            material.SetTexture("_DepthAtlas", depthAtlas);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }

    private void ConfigureImporter(string fileName, bool sRgb)
    {
        string path = Path.Combine(outputFolder, fileName).Replace('\\', '/');
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.sRGBTexture = sRgb;
            importer.isReadable = true;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            if (sRgb)
            {
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.008f;
            }
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }
    }
}
