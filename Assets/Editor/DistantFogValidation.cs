using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>GPU checks through the actual URP RenderGraph feature, without loading a gameplay world.</summary>
public static class DistantFogValidation
{
    const string Output = ".utmp/distant-fog";
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static void InstallAndValidateBatch()
    {
        try { DistantFogSetup.Install(); Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void ValidateBatch()
    {
        try { EditorSceneManager.OpenScene(DistantFogSetup.ScenePath); Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Atmosphere/Validate Distant Fog Rendering")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        var previousSetup = EditorSceneManager.GetSceneManagerSetup();
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        var previousTarget = RenderTexture.active;
        Material unlit = null, waterMaterial = null, terrainMaterial = null, rockMaterial = null, grassMaterial = null;
        RenderTexture target = null;
        Texture2D baseline = null, actual = null;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.fog = false;
            RenderSettings.fogColor = new Color(.5f, .6f, .7f);

            var layer = new GameObject("Fog fixture").AddComponent<DistantFogLayer>();
            layer.noiseTexture = AssetDatabase.LoadAssetAtPath<Texture3D>(DistantFogSetup.NoisePath);
            layer.noiseStrength = 0; layer.detailStrength = 0; layer.billowHeight = 0;
            layer.shading = 0; layer.dithering = 0;
            layer.density = .025f;
            layer.heightFeather = 10;
            layer.clearHeights = new Vector2(110, 150);
            layer.steps = 48;

            var camera = new GameObject("Fog fixture camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = (1 << 31) | (1 << 4);
            camera.transform.position = new Vector3(0, 30, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .045f, .09f);
            camera.fieldOfView = 40; camera.aspect = 1.5f;
            camera.farClipPlane = 6500;
            var additional = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            additional.requiresDepthTexture = true; additional.requiresColorTexture = true;
            target = new RenderTexture(600, 400, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();

            unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            unlit.SetColor("_BaseColor", new Color(.7f, .05f, .025f));
            var near = Box("Near red", new Vector3(-85, 30, 300), new Vector3(65, 40, 5), unlit);
            var low = Box("Distant red", new Vector3(0, 35, 1400), new Vector3(120, 65, 5), unlit);
            var peak = Box("High red", new Vector3(180, 220, 1400), new Vector3(150, 80, 5), unlit);
            var farPeak = Box("Far-plane peak", new Vector3(-700, 260, 6400), new Vector3(600, 180, 30), unlit);
            var crossing = Box("Crossing ceiling", new Vector3(-210, 125, 1300), new Vector3(65, 240, 5), unlit);
            terrainMaterial = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat"));
            var nearTerrain = Box("Near actual terrain shader", new Vector3(-35, 10, 250), new Vector3(40, 15, 5), terrainMaterial);
            var farTerrain = Box("Far actual terrain shader", new Vector3(350, 0, 1400), new Vector3(90, 25, 5), terrainMaterial);
            var rockAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VincentPrefabs/Boulders/Boulder1.fbx");
            rockMaterial = Object.Instantiate(rockAsset.GetComponentInChildren<Renderer>().sharedMaterial);
            var nearRock = Box("Near actual rock shader", new Vector3(65, 10, 250), new Vector3(30, 15, 5), rockMaterial);
            var farRock = Box("Far actual rock shader", new Vector3(-620, 70, 1400), new Vector3(100, 50, 5), rockMaterial);
            grassMaterial = new Material(Shader.Find("Custom/GrassInstancedTerrainTint"));
            grassMaterial.SetFloat("_WindStrength", 0);
            grassMaterial.SetFloat("_WindFlutterStrength", 0);
            var nearGrass = Box("Near grass shader", new Vector3(110, 25, 350), new Vector3(30, 12, 5), grassMaterial);
            var farGrass = Box("Far grass shader", new Vector3(0, -15, 1400), new Vector3(110, 16, 5), grassMaterial);

            // The bed is much lower than water. Surface protection must use water Y, not bed Y.
            var bed = Box("River bed", new Vector3(480, -65, 1300), new Vector3(520, 5, 600), unlit);
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water fixture"; water.layer = 4;
            water.transform.position = new Vector3(480, 14.4f, 1300);
            water.transform.localScale = new Vector3(52, 1, 60);
            waterMaterial = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterMeshBaseMaterial.mat"));
            Require(waterMaterial != null && waterMaterial.shader.name == "FirstSettlers/Murky Planar Water", "Water fixture needs the real river material.");
            water.GetComponent<Renderer>().sharedMaterial = waterMaterial;
            Shader.SetGlobalFloat("_WaterReflectionValid", 0);
            Shader.SetGlobalFloat("_WaterNormalValid", 0);

            layer.enabled = false;
            baseline = Capture(camera, target, "baseline");
            layer.enabled = true;
            actual = Capture(camera, target, "dense-half");
            foreach (var message in ShaderUtil.GetShaderMessages(Shader.Find("Hidden/FirstSettlers/DistantFog")))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            Debug.Log($"FOG TERRAIN REGRESSION: shader={terrainMaterial.shader.name}, near baseline={Pixel(camera, baseline, nearTerrain.transform.position)}, fog={Pixel(camera, actual, nearTerrain.transform.position)}, far baseline={Pixel(camera, baseline, farTerrain.transform.position)}, fog={Pixel(camera, actual, farTerrain.transform.position)}");
            CheckUnchanged(camera, baseline, actual, near.transform.position, "Foreground fogged before start distance");
            CheckUnchanged(camera, baseline, actual, nearTerrain.transform.position, "Actual terrain shader fogged before 600 units");
            CheckUnchanged(camera, baseline, actual, nearRock.transform.position, "Actual rock shader fogged before 600 units");
            CheckUnchanged(camera, baseline, actual, nearGrass.transform.position, "Grass shader fogged before 600 units");
            Require(Error(Pixel(camera, baseline, farTerrain.transform.position), Pixel(camera, actual, farTerrain.transform.position)) > .1f,
                "Actual terrain shader did not receive distant fog");
            Require(Error(Pixel(camera, baseline, farRock.transform.position), Pixel(camera, actual, farRock.transform.position)) > .1f,
                "Actual rock shader did not receive distant fog");
            Debug.Log($"FOG ROCK REGRESSION: shader={rockMaterial.shader.name}; near clear, far fogged.");
            Require(Error(Pixel(camera, baseline, farGrass.transform.position), Pixel(camera, actual, farGrass.transform.position)) > .1f,
                "Grass shader did not receive distant fog");
            CheckUnchanged(camera, baseline, actual, peak.transform.position, "High terrain lost its detail");
            CheckUnchanged(camera, baseline, actual, farPeak.transform.position, "Far terrain was mistaken for sky");
            CheckUnchanged(camera, baseline, actual, new Vector3(-210, 220, 1300), "Upper portion of crossing object fogged");
            Require(Error(Pixel(camera, baseline, low.transform.position), Pixel(camera, actual, low.transform.position)) > .2f,
                "Distant low terrain did not receive dense fog.");
            Require(Error(baseline.GetPixel(570, 200), actual.GetPixel(570, 200)) > .2f, "Horizon sky did not receive fog.");
            Require(Error(baseline.GetPixel(570, 375), actual.GetPixel(570, 375)) < .02f, "Upper sky fogged outside the layer.");
            Object.DestroyImmediate(actual);

            layer.clearHeights = new Vector2(5, 10);
            actual = Capture(camera, target, "water-protected");
            CheckUnchanged(camera, baseline, actual, new Vector3(480, 14.4f, 1300), "Water used riverbed height instead of the global water plane");
            Object.DestroyImmediate(actual);
            layer.clearHeights = new Vector2(110, 150);
            layer.fogWater = false;
            actual = Capture(camera, target, "water-mask-disabled");
            Require(Error(Pixel(camera, baseline, new Vector3(480, 14.4f, 1300)), Pixel(camera, actual, new Vector3(480, 14.4f, 1300))) > .1f,
                "Water test did not exercise fog at the river surface.");
            Object.DestroyImmediate(actual);
            layer.fogWater = true;

            foreach (var resolution in new[] { DistantFogLayer.Resolution.Full, DistantFogLayer.Resolution.Quarter })
            {
                layer.resolution = resolution;
                actual = Capture(camera, target, "dense-" + resolution.ToString().ToLowerInvariant());
                CheckUnchanged(camera, baseline, actual, near.transform.position, "Quality setting leaked fog into foreground");
                CheckUnchanged(camera, baseline, actual, peak.transform.position, "Quality setting erased protected peak");
                Object.DestroyImmediate(actual);
            }
            layer.resolution = DistantFogLayer.Resolution.Half;
            // Check the actual cutoff, not just a generic nearby object.
            var boundary = Box("600-unit boundary", new Vector3(0, 30, 599), new Vector3(40, 20, 2), unlit);
            layer.distanceFeather = 1;
            layer.enabled = false;
            var boundaryBaseline = Capture(camera, target, "boundary-baseline");
            layer.enabled = true;
            actual = Capture(camera, target, "boundary-599");
            CheckUnchanged(camera, boundaryBaseline, actual, boundary.transform.position, "Surface inside 600-unit cutoff fogged");
            Object.DestroyImmediate(actual);
            boundary.transform.position = new Vector3(0, 30, 622);
            actual = Capture(camera, target, "boundary-622");
            Require(Error(Pixel(camera, boundaryBaseline, new Vector3(0, 30, 599)), Pixel(camera, actual, boundary.transform.position)) > .1f,
                "Fog did not begin beyond the 600-unit cutoff");
            Object.DestroyImmediate(actual); Object.DestroyImmediate(boundaryBaseline); Object.DestroyImmediate(boundary);
            layer.distanceFeather = 200;
            layer.noiseStrength = .8f; layer.detailStrength = .2f; layer.billowHeight = 20;
            layer.shading = .3f; layer.density = .006f; layer.dithering = .7f; layer.steps = 32;
            actual = Capture(camera, target, "noisy-day");
            Color day = actual.GetPixel(570, 200);
            Object.DestroyImmediate(actual);
            RenderSettings.fogColor = new Color(.055f, .065f, .1f);
            actual = Capture(camera, target, "noisy-night");
            Require(actual.GetPixel(570, 200).grayscale < day.grayscale * .5f, "Day/night fog color did not update");
            Object.DestroyImmediate(actual);
            actual = null;

            // Orthographic and oblique/mirrored projections exercise different ray reconstruction.
            camera.orthographic = true; camera.orthographicSize = 240;
            actual = Capture(camera, target, "orthographic"); Object.DestroyImmediate(actual); actual = null;
            camera.orthographic = false;
            Matrix4x4 mirror = Matrix4x4.identity; mirror.m11 = -1; mirror.m13 = 28.8f;
            camera.worldToCameraMatrix = camera.worldToCameraMatrix * mirror;
            actual = Capture(camera, target, "mirrored"); Object.DestroyImmediate(actual); actual = null;

            var shader = Shader.Find("Hidden/FirstSettlers/DistantFog");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Fog shader has compilation errors");
            Require(!ShaderUtil.ShaderHasError(waterMaterial.shader), "Water shader has compilation errors");
            Debug.Log($"DISTANT FOG GPU PASS: real URP RenderGraph; actual terrain/rock/grass shaders near and far; 599/622-unit cutoff; near/high surfaces, mixed-height object, horizon/upper sky, water-plane protection, full/half/quarter resolution, animated noise, day/night, orthographic/mirrored cameras. GPU={SystemInfo.graphicsDeviceName}. Captures={Output}. These are correctness checks, not a 4K/240 FPS benchmark.");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (baseline != null) Object.DestroyImmediate(baseline);
            if (actual != null) Object.DestroyImmediate(actual);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (unlit != null) Object.DestroyImmediate(unlit);
            if (waterMaterial != null) Object.DestroyImmediate(waterMaterial);
            if (terrainMaterial != null) Object.DestroyImmediate(terrainMaterial);
            if (rockMaterial != null) Object.DestroyImmediate(rockMaterial);
            if (grassMaterial != null) Object.DestroyImmediate(grassMaterial);
            ShaderUtil.allowAsyncCompilation = previousAsync;
            GraphicsSettings.defaultRenderPipeline = previousPipeline;
            QualitySettings.renderPipeline = previousQuality;
            EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
    }

    static GameObject Box(string name, Vector3 position, Vector3 size, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name; box.layer = 31;
        box.transform.position = position; box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }
    static Texture2D Capture(Camera camera, RenderTexture target, string name)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
        var png = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        Color[] colors = pixels.GetPixels();
        for (int i = 0; i < colors.Length; i++) colors[i] = colors[i].gamma;
        png.SetPixels(colors); png.Apply();
        File.WriteAllBytes(Output + "/" + name + ".png", png.EncodeToPNG());
        Object.DestroyImmediate(png);
        return pixels;
    }
    static Color Pixel(Camera camera, Texture2D texture, Vector3 position)
    {
        Vector3 p = camera.WorldToViewportPoint(position);
        Require(p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1, "Fixture point outside view: " + position);
        return texture.GetPixel(Mathf.Clamp((int)(p.x * texture.width), 0, texture.width - 1),
            Mathf.Clamp((int)(p.y * texture.height), 0, texture.height - 1));
    }
    static float Error(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
    static void CheckUnchanged(Camera camera, Texture2D baseline, Texture2D actual, Vector3 position, string message)
        => Require(Error(Pixel(camera, baseline, position), Pixel(camera, actual, position)) < .02f, message);
}
