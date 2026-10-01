using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class GrassGroundAuthoring
{
    private const string Folder = "Assets/Textures/Ground/Grass/";
    private const string SurfacePath = Folder + "T_GrassGround_BladeSurface.png";
    private const string TerrainPath = "Assets/Materials/M_Terrain/M_TerrainBase.mat";

    [MenuItem("Tools/Foliage/Configure and Preview Matching Grass Ground (Edit Mode)")]
    public static void BuildAndPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Grass ground authoring requires Edit mode.");
        ConfigureTexture("T_GrassGround_BladeSurface.png", false, false, TextureImporterFormat.BC7);
        ConfigureTexture("T_GrassGround_BladeAlbedo.png", true, false, TextureImporterFormat.BC7);
        ConfigureTexture("T_GrassGround_BladeNormal.png", false, true, TextureImporterFormat.BC5);
        ConfigureTexture("T_GrassGround_BladeHeight.png", false, false, TextureImporterFormat.BC4);
        Material terrain = AssetDatabase.LoadAssetAtPath<Material>(TerrainPath);
        Material blades = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Grass/M_GrassTuftBroad.mat");
        if (terrain == null || blades == null) throw new InvalidOperationException("Grass materials are missing.");
        ValidateShaderVariants(terrain.shader);
        terrain.SetTexture("_GrassSurfaceMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SurfacePath));
        terrain.EnableKeyword("_GRASS_BLADE_GROUND");
        terrain.SetFloat("_GrassBladeGround", 1f);
        terrain.SetFloat("_GrassGroundTiling", 0.45f);
        terrain.SetFloat("_GrassGroundFarTiling", 0.06f);
        terrain.SetFloat("_GrassGroundScaleFadeStart", 30f);
        terrain.SetFloat("_GrassGroundScaleFadeEnd", 100f);
        terrain.SetFloat("_GrassGroundGridScale", 0.65f);
        terrain.SetFloat("_GrassGroundDetailStrength", 0.65f);
        terrain.SetFloat("_GrassGroundDetailContrast", 1.6f);
        terrain.SetFloat("_GrassNormalStrength", 0.25f);
        terrain.SetFloat("_GrassGroundDetailFadeStart", 180f);
        terrain.SetFloat("_GrassGroundDetailFadeEnd", 400f);
        terrain.SetFloat("_GrassGroundNormalFadeStart", 10f);
        terrain.SetFloat("_GrassGroundNormalFadeEnd", 30f);
        terrain.SetFloat("_GrassGroundHeightDepth", 0.008f);
        terrain.SetFloat("_GrassGroundHeightFadeStart", 4f);
        terrain.SetFloat("_GrassGroundHeightFadeEnd", 12f);
        foreach (string name in new[] { "_DarkGrassColor", "_MidGrassColor", "_LightGrassColor" })
            terrain.SetColor(name, blades.GetColor(name));
        terrain.SetColor("_GroundDarkGrassColor", blades.GetColor("_ForestDarkGrassColor"));
        terrain.SetColor("_GroundMidGrassColor", blades.GetColor("_ForestMidGrassColor"));
        terrain.SetColor("_GroundLightGrassColor", blades.GetColor("_ForestLightGrassColor"));
        foreach (string name in new[] { "_NoiseScale", "_NoiseStrength", "_BlendSharpness" })
            terrain.SetFloat(name, blades.GetFloat(name));
        EditorUtility.SetDirty(terrain);
        AssetDatabase.SaveAssets();
        Preview(terrain, blades);
    }

    [MenuItem("Tools/Foliage/Validate and Preview Current Grass Ground (Edit Mode)")]
    public static void ValidateAndPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Grass ground validation requires Edit mode.");
        Material terrain = AssetDatabase.LoadAssetAtPath<Material>(TerrainPath);
        Material blades = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Grass/M_GrassTuftBroad.mat");
        if (terrain == null || blades == null) throw new InvalidOperationException("Grass materials are missing.");
        ValidateShaderVariants(terrain.shader);
        // Preview clones preserve the user's current material adjustments.
        Preview(terrain, blades);
    }

    private static void ValidateShaderVariants(Shader shader)
    {
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(shader), ImportAssetOptions.ForceSynchronousImport);
        var collection = new ShaderVariantCollection();
        try
        {
            foreach (bool matchingGround in new[] { false, true })
            foreach (bool rock in new[] { false, true })
            foreach (string fog in new[] { "", "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" })
            foreach (string shadow in new[] { "", "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN" })
            foreach (string soft in new[] { "", "_SHADOWS_SOFT", "_SHADOWS_SOFT_LOW", "_SHADOWS_SOFT_MEDIUM", "_SHADOWS_SOFT_HIGH" })
            {
                if (shadow.Length == 0 && soft.Length != 0) continue;
                var keywords = new System.Collections.Generic.List<string>();
                if (matchingGround) keywords.Add("_GRASS_BLADE_GROUND");
                if (rock) keywords.Add("_ROCK_DETAIL");
                if (fog.Length != 0) keywords.Add(fog);
                if (shadow.Length != 0) keywords.Add(shadow);
                if (soft.Length != 0) keywords.Add(soft);
                collection.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipeline, keywords.ToArray()));
            }
            collection.WarmUp();
            if (ShaderUtil.ShaderHasError(shader))
            {
                string errors = string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(message =>
                    $"{message.severity}: {message.message} ({message.file}:{message.line})"));
                throw new InvalidOperationException("Terrain shader variant compilation failed:\n" + errors);
            }
            Debug.Log($"Grass ground shader validation passed: {collection.variantCount} variants, {SystemInfo.graphicsDeviceType}.");
        }
        finally { Object.DestroyImmediate(collection); }
    }

    private static void ConfigureTexture(string name, bool srgb, bool normal, TextureImporterFormat format)
    {
        string path = Folder + name;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.alphaIsTransparency = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.anisoLevel = 4;
        importer.isReadable = false;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Standalone", overridden = true, maxTextureSize = 1024,
            format = format, compressionQuality = 80
        });
        importer.SaveAndReimport();
    }

    private static GameObject MakeObject(string name, Scene scene)
    {
        var result = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(result, scene);
        return result;
    }

    private static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        texture.SetPixels(new[] { color, color, color, color });
        texture.Apply();
        return texture;
    }

    private static Mesh GroundMesh(float minX, float maxX, float halfDepth = 15f, bool hills = false)
    {
        var mesh = new Mesh { name = "Grass ground preview" };
        if (hills)
        {
            const int cells = 48, stride = cells + 1;
            var vertices = new Vector3[stride * stride];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[cells * cells * 6];
            for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                float wx = Mathf.Lerp(minX, maxX, (float)x / cells);
                float wz = Mathf.Lerp(-halfDepth, halfDepth, (float)z / cells);
                int index = z * stride + x;
                vertices[index] = new Vector3(wx, 12f * Mathf.Sin(wx * 0.018f) + 18f * Mathf.Sin(wz * 0.012f), wz);
                normals[index] = new Vector3(-0.216f * Mathf.Cos(wx * 0.018f), 1f,
                    -0.216f * Mathf.Cos(wz * 0.012f)).normalized;
                uvs[index] = new Vector2((float)x / cells, (float)z / cells);
                if (x == cells || z == cells) continue;
                int t = (z * cells + x) * 6;
                triangles[t] = index; triangles[t + 1] = index + stride; triangles[t + 2] = index + 1;
                triangles[t + 3] = index + 1; triangles[t + 4] = index + stride; triangles[t + 5] = index + stride + 1;
            }
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uvs; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
        mesh.vertices = new[] { new Vector3(minX, 0, -halfDepth), new Vector3(minX, 0, halfDepth),
            new Vector3(maxX, 0, -halfDepth), new Vector3(maxX, 0, halfDepth) };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Preview(Material source, Material bladeSource)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        Material ground = new Material(source);
        Material blades = new Material(bladeSource);
        Texture2D grassControl = Solid(new Color(0, 0, 1, 0)), emptyControl = Solid(Color.clear);
        Texture2D darkSurface = Solid(new Color(0, 0.5f, 0.5f, 0.4f));
        Texture2D lightSurface = Solid(new Color(1, 0.5f, 0.5f, 0.4f));
        Mesh left = GroundMesh(-15, 0), right = GroundMesh(0, 15);
        Mesh distanceLeft = GroundMesh(-180, 0, 180f, true), distanceRight = GroundMesh(0, 180, 180f, true);
        try
        {
            ground.SetTexture("_ControlMap0", grassControl);
            ground.SetTexture("_ControlMap1", emptyControl);
            ground.SetTexture("_ControlMap2", emptyControl);
            ground.SetFloat("_ReceiveShadows", 0f);
            blades.SetFloat("_WindStrength", 0f);
            blades.SetFloat("_WindFlutterStrength", 0f);
            blades.SetFloat("_ReceiveShadows", 0f);
            foreach (Mesh mesh in new[] { left, right })
            {
                GameObject patch = MakeObject("Ground patch", scene);
                patch.AddComponent<MeshFilter>().sharedMesh = mesh;
                patch.AddComponent<MeshRenderer>().sharedMaterial = ground;
            }
            Light sun = MakeObject("Ground preview sun", scene).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.87f);
            sun.intensity = 0.9f;
            sun.transform.rotation = Quaternion.Euler(46f, -30f, 0f);
            Camera camera = MakeObject("Ground preview camera", scene).AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.cameraType = CameraType.Preview;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.19f, 0.23f, 0.25f);
            camera.fieldOfView = 42f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            camera.GetUniversalAdditionalCameraData().renderShadows = false;

            camera.transform.position = new Vector3(0.9f, 1.7f, -2.1f);
            camera.transform.LookAt(Vector3.zero);
            GrassTuftBroadPreview.Capture(camera, "ArtReferences/GrassGround_ClosePreview.png");
            camera.transform.position = new Vector3(0, 16, -12);
            camera.transform.LookAt(Vector3.zero);
            GrassTuftBroadPreview.Capture(camera, "ArtReferences/GrassGround_RepeatPreview.png");

            // Beyond the old cutoff, texture and the far scale must still matter.
            float midDistance = (ground.GetFloat("_GrassGroundScaleFadeEnd") + ground.GetFloat("_GrassGroundDetailFadeStart")) * 0.5f;
            camera.transform.position = new Vector3(0, midDistance, -10);
            camera.transform.LookAt(Vector3.zero);
            VerifyTextureInfluence(camera, ground, darkSurface, lightSurface, true, "MidRangeTexture");
            VerifyPropertyInfluence(camera, ground, "_GrassGroundFarTiling", 0.06f, 0.45f, true, "FarScale");
            VerifyPropertyIgnored(camera, ground, "_GrassGroundTiling", 0.45f, 0.9f, "NearScaleFade");

            // Outside the new cutoff, the two extremes must give identical pixels.
            camera.transform.position = new Vector3(0, ground.GetFloat("_GrassGroundDetailFadeEnd") + 20f, -10);
            camera.transform.LookAt(Vector3.zero);
            VerifyTextureInfluence(camera, ground, darkSurface, lightSurface, false, "FarTextureCutoff");

            camera.transform.position = new Vector3(0, 19, -8);
            camera.transform.LookAt(Vector3.zero);
            VerifyPropertyIgnored(camera, ground, "_GrassGroundHeightDepth", 0f, 0.02f, "HeightFade");
            camera.transform.position = new Vector3(0, 38, -8);
            camera.transform.LookAt(Vector3.zero);
            VerifyPropertyIgnored(camera, ground, "_GrassNormalStrength", 0f, 1f, "NormalFade");

            // A much larger, sloped patch makes the 100-300 m appearance reviewable.
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "Ground patch") root.SetActive(false);
            foreach (Mesh mesh in new[] { distanceLeft, distanceRight })
            {
                GameObject patch = MakeObject("Distant ground patch", scene);
                patch.AddComponent<MeshFilter>().sharedMesh = mesh;
                patch.AddComponent<MeshRenderer>().sharedMaterial = ground;
            }
            camera.transform.position = new Vector3(0, 45, -95);
            camera.transform.LookAt(new Vector3(0, 0, 70));
            GrassTuftBroadPreview.Capture(camera, "ArtReferences/GrassGround_DistancePreview.png");
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Distant ground patch") root.SetActive(false);
                if (root.name == "Ground patch") root.SetActive(true);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GrassTuftBroad_LOD0.prefab");
            var random = new System.Random(47296);
            for (int z = -12; z <= 12; z++)
            for (int x = -12; x <= 12; x++)
            {
                if (random.NextDouble() > 0.45) continue;
                GameObject tuft = MakeObject("Live blade", scene);
                tuft.transform.position = new Vector3((x + (float)random.NextDouble() - 0.5f) * 0.38f, 0,
                    (z + (float)random.NextDouble() - 0.5f) * 0.38f);
                tuft.transform.rotation = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);
                tuft.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.3f, (float)random.NextDouble());
                tuft.AddComponent<MeshFilter>().sharedMesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                var renderer = tuft.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = blades;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            camera.transform.position = new Vector3(1.6f, 1.15f, -3.3f);
            camera.transform.LookAt(new Vector3(0, 0.08f, 0.5f));
            GrassTuftBroadPreview.Capture(camera, "ArtReferences/GrassGround_WithBladesPreview.png");
            if (ShaderUtil.ShaderHasError(ground.shader)) throw new InvalidOperationException("Terrain shader failed to compile.");
            Texture2D surface = AssetDatabase.LoadAssetAtPath<Texture2D>(SurfacePath);
            long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(surface);
            Debug.Log($"Grass ground Edit-mode validation passed. Mid-range texture, far scale, near-scale fade, far texture cutoff, height fade and normal fade passed. " +
                $"Runtime map: {surface.width}x{surface.height}, {surface.format}, {surface.mipmapCount} mips, {bytes} bytes. " +
                $"Play mode: {EditorApplication.isPlaying}.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (Object value in new Object[] { ground, blades, grassControl, emptyControl, darkSurface, lightSurface, left, right, distanceLeft, distanceRight })
                Object.DestroyImmediate(value);
        }
    }

    private static void VerifyPropertyIgnored(Camera camera, Material material, string property,
        float first, float second, string name)
    {
        VerifyPropertyInfluence(camera, material, property, first, second, false, name);
    }

    private static void VerifyPropertyInfluence(Camera camera, Material material, string property,
        float first, float second, bool expectedInfluence, string name)
    {
        float original = material.GetFloat(property);
        string firstPath = ".utmp/GrassGround_" + name + "A.png";
        string secondPath = ".utmp/GrassGround_" + name + "B.png";
        try
        {
            material.SetFloat(property, first);
            GrassTuftBroadPreview.Capture(camera, firstPath);
            material.SetFloat(property, second);
            GrassTuftBroadPreview.Capture(camera, secondPath);
            bool changed = !File.ReadAllBytes(firstPath).SequenceEqual(File.ReadAllBytes(secondPath));
            if (changed != expectedInfluence)
                throw new InvalidOperationException("Grass ground distance/scale validation failed: " + name);
        }
        finally { material.SetFloat(property, original); }
    }

    private static void VerifyTextureInfluence(Camera camera, Material material, Texture2D first,
        Texture2D second, bool expectedInfluence, string name)
    {
        Texture original = material.GetTexture("_GrassSurfaceMap");
        string firstPath = ".utmp/GrassGround_" + name + "A.png";
        string secondPath = ".utmp/GrassGround_" + name + "B.png";
        try
        {
            material.SetTexture("_GrassSurfaceMap", first);
            GrassTuftBroadPreview.Capture(camera, firstPath);
            material.SetTexture("_GrassSurfaceMap", second);
            GrassTuftBroadPreview.Capture(camera, secondPath);
            bool changed = !File.ReadAllBytes(firstPath).SequenceEqual(File.ReadAllBytes(secondPath));
            if (changed != expectedInfluence)
                throw new InvalidOperationException("Grass ground texture range validation failed: " + name);
        }
        finally { material.SetTexture("_GrassSurfaceMap", original); }
    }
}
