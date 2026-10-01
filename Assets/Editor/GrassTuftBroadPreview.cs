using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Uses an isolated authoring scene and offscreen URP renders; never enters Play mode.
public static class GrassTuftBroadPreview
{
    [MenuItem("Tools/Foliage/Build and Preview Broad Grass (Edit Mode)")]
    public static void BuildAndPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Grass previews require Edit mode.");
        GrassTuftBroadPrefabBuilder.Build();
        Scene source = SceneManager.GetSceneByPath("Assets/Scenes/SmearScene.unity");
        bool openedSource = !source.isLoaded;
        if (openedSource)
            source = EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity", OpenSceneMode.Additive);
        GrassSettings settings = null;
        float spacing = 0f;
        foreach (GameObject root in source.GetRootGameObjects())
        {
            WorldManager manager = root.GetComponentInChildren<WorldManager>();
            if (manager == null) continue;
            var serialized = new SerializedObject(manager);
            SerializedProperty grassSettings = serialized.FindProperty("grassSettings");
            settings = new GrassSettings
            {
                grassPrefab = (GameObject)grassSettings.FindPropertyRelative("grassPrefab").objectReferenceValue,
                billboardGrassPrefab = (GameObject)grassSettings.FindPropertyRelative("billboardGrassPrefab").objectReferenceValue,
                cellsPerAxis = grassSettings.FindPropertyRelative("cellsPerAxis").intValue,
                cellJitter = grassSettings.FindPropertyRelative("cellJitter").floatValue,
                randomizeYaw = grassSettings.FindPropertyRelative("randomizeYaw").boolValue,
                uniformScaleRange = grassSettings.FindPropertyRelative("uniformScaleRange").vector2Value,
                densityRadius3 = grassSettings.FindPropertyRelative("densityRadius3").floatValue,
                billboardCoverage = grassSettings.FindPropertyRelative("billboardCoverage").floatValue
            };
            spacing = serialized.FindProperty("chunkSize").intValue *
                serialized.FindProperty("worldScale").floatValue / settings.cellsPerAxis;
            break;
        }
        if (openedSource) EditorSceneManager.CloseScene(source, true);
        if (settings == null || settings.grassPrefab == null || settings.billboardGrassPrefab == null)
            throw new InvalidOperationException("SmearScene grass references are incomplete.");
        Scene preview = EditorSceneManager.NewPreviewScene();
        Material grass = new Material(settings.grassPrefab.GetComponent<MeshRenderer>().sharedMaterial);
        Material groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        try
        {
            grass.SetFloat("_WindStrength", 0f);
            grass.SetFloat("_WindFlutterStrength", 0f);
            grass.SetFloat("_ReceiveShadows", 0f);
            groundMaterial.SetColor("_BaseColor", new Color(0.13f, 0.20f, 0.065f));
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(ground, preview);
            ground.transform.localScale = Vector3.one * 2f;
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            Light sun = PreviewObject("Grass preview sun", preview).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.85f;
            sun.color = new Color(1f, 0.96f, 0.86f);
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            Camera camera = PreviewObject("Grass preview camera", preview).AddComponent<Camera>();
            camera.scene = preview;
            camera.cameraType = CameraType.Preview;
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.19f, 0.23f, 0.25f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            camera.fieldOfView = 42f;
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;

            GameObject tuft = MakeTuft(settings.grassPrefab, grass, Vector3.zero, 0f, 1f, preview);
            camera.transform.position = new Vector3(0.58f, 0.45f, -0.8f);
            camera.transform.LookAt(new Vector3(0f, 0.18f, 0f));
            Capture(camera, "ArtReferences/GrassTuftBroad_Preview.png");
            Object.DestroyImmediate(tuft);

            GameObject field = PreviewObject("Grass coverage preview", preview);
            var random = new System.Random(47296);
            const int count = 36;
            for (int z = 0; z < count; z++)
            for (int x = 0; x < count; x++)
            {
                float rank = (float)random.NextDouble();
                float jitterX = ((float)random.NextDouble() - 0.5f) * settings.cellJitter;
                float jitterZ = ((float)random.NextDouble() - 0.5f) * settings.cellJitter;
                float yaw = settings.randomizeYaw ? (float)random.NextDouble() * 360f : 0f;
                float scale = Mathf.Lerp(settings.uniformScaleRange.x, settings.uniformScaleRange.y, (float)random.NextDouble());
                Vector3 position = new Vector3((x - count / 2f + jitterX) * spacing, 0f,
                    (z - count / 2f + jitterZ) * spacing);
                bool far = z >= count / 2;
                if (rank > (far ? settings.billboardCoverage : settings.densityRadius3)) continue;
                MakeTuft(far ? settings.billboardGrassPrefab : settings.grassPrefab,
                    grass, position, yaw, scale, preview).transform.SetParent(field.transform);
            }
            camera.transform.position = new Vector3(1.8f, 0.85f, -4.6f);
            camera.transform.LookAt(new Vector3(0f, 0.16f, 0.9f));
            Capture(camera, "ArtReferences/GrassTuftBroad_CoveragePreview.png");
            if (ShaderUtil.ShaderHasError(grass.shader))
                throw new InvalidOperationException("Grass shader has compilation errors.");
            Debug.Log($"Grass Edit-mode validation passed: shader {grass.shader.name}, spacing {spacing:F4} m, " +
                $"near density {settings.densityRadius3:F2}, far density {settings.billboardCoverage:F2}, " +
                $"{field.transform.childCount} preview tufts. Play mode: {EditorApplication.isPlaying}.");
        }
        finally
        {
            Object.DestroyImmediate(grass);
            Object.DestroyImmediate(groundMaterial);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static GameObject PreviewObject(string name, Scene preview)
    {
        GameObject result = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(result, preview);
        return result;
    }

    private static GameObject MakeTuft(GameObject prefab, Material material, Vector3 position, float yaw, float scale, Scene preview)
    {
        GameObject tuft = PreviewObject(prefab.name, preview);
        tuft.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        tuft.transform.localScale = Vector3.one * scale;
        tuft.AddComponent<MeshFilter>().sharedMesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        MeshRenderer renderer = tuft.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return tuft;
    }

    private static void Capture(Camera camera, string path)
    {
        // Supersample the cutout edges, then read back a resolved single-sample
        // target. Reading a multisampled target directly can produce a blank PNG.
        RenderTexture target = RenderTexture.GetTemporary(2800, 1800, 24, RenderTextureFormat.ARGB32);
        RenderTexture resolved = RenderTexture.GetTemporary(1400, 900, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D pixels = new Texture2D(resolved.width, resolved.height, TextureFormat.RGB24, false);
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            RenderPipeline.SubmitRenderRequest(camera, request);
            Graphics.Blit(target, resolved);
            RenderTexture.active = resolved;
            pixels.ReadPixels(new Rect(0, 0, resolved.width, resolved.height), 0, 0);
            pixels.Apply();
            Color32[] samples = pixels.GetPixels32();
            int minGreen = 255, maxGreen = 0;
            for (int i = 0; i < samples.Length; i += 53)
            {
                minGreen = Mathf.Min(minGreen, samples[i].g);
                maxGreen = Mathf.Max(maxGreen, samples[i].g);
            }
            if (maxGreen - minGreen < 10)
                throw new InvalidOperationException("Grass preview capture is blank: " + path);
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            RenderTexture.ReleaseTemporary(resolved);
            Object.DestroyImmediate(pixels);
        }
    }
}
