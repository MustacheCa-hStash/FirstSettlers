using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit offscreen validation; never changes the open scene or runtime chunks.
public static class CliffTextureValidation
{
    private const string MaterialPath = "Assets/Materials/M_Terrain/M_TerrainBase.mat";

    [MenuItem("Tools/Terrain/Validate and Preview Cliff Texture")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Cliff preview requires Edit mode.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null || !material.IsKeywordEnabled("_ROCK_DETAIL"))
            throw new InvalidOperationException("Terrain material must enable rock detail.");
        CheckImporter("T_CliffFractured_Albedo.png", false, TextureImporterFormat.BC7);
        CheckImporter("T_CliffFractured_NormalGL.png", true, TextureImporterFormat.BC5);
        if (material.GetTexture("_RockAlbedo") == null || material.GetTexture("_RockNormal") == null)
            throw new InvalidOperationException("Cliff maps must be assigned.");
        if (material.GetFloat("_RockNormalFadeEnd") > material.GetFloat("_RockDetailFadeEnd"))
            throw new InvalidOperationException("Normal detail should fade before albedo.");
        bool async = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            CompileVariants(material.shader);
            Render(material);
            if (ShaderUtil.ShaderHasError(material.shader))
                throw new InvalidOperationException(string.Join("\n", ShaderUtil.GetShaderMessages(material.shader)
                    .Select(m => m.message + " at " + m.file + ":" + m.line)));
            Debug.Log("CLIFF VALIDATION PASS: BC7 albedo / BC5 normal; rock on/off, grass on/off, Forward/Forward+, shadows and fog; offscreen cliff captures. No FPS benchmark.");
        }
        finally { ShaderUtil.allowAsyncCompilation = async; }
    }

    private static void CheckImporter(string name, bool normal, TextureImporterFormat format)
    {
        string path = "Assets/Textures/Rock/" + name;
        var i = AssetImporter.GetAtPath(path) as TextureImporter;
        if (i == null || i.wrapMode != TextureWrapMode.Repeat || !i.mipmapEnabled ||
            i.sRGBTexture == normal || i.maxTextureSize != 1024 ||
            i.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) ||
            i.flipGreenChannel || i.GetPlatformTextureSettings("Standalone").format != format)
            throw new InvalidOperationException("Incorrect cliff texture import: " + path);
    }

    private static void CompileVariants(Shader shader)
    {
        var collection = new ShaderVariantCollection();
        try
        {
            foreach (bool rock in new[] { false, true })
            foreach (bool grass in new[] { false, true })
            foreach (bool cluster in new[] { false, true })
            foreach (string shadow in new[] { "", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN" })
            foreach (string fog in new[] { "", "FOG_LINEAR" })
            {
                var keywords = new List<string>();
                if (rock) keywords.Add("_ROCK_DETAIL");
                if (grass) keywords.Add("_GRASS_BLADE_GROUND");
                if (cluster) keywords.Add("_CLUSTER_LIGHT_LOOP");
                if (shadow.Length > 0) { keywords.Add(shadow); keywords.Add("_SHADOWS_SOFT_LOW"); }
                if (fog.Length > 0) keywords.Add(fog);
                collection.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipeline, keywords.ToArray()));
            }
            collection.WarmUp();
            Debug.Log("Cliff shader warmup: " + collection.variantCount + " variants on " + SystemInfo.graphicsDeviceType);
        }
        finally { Object.DestroyImmediate(collection); }
    }

    private static GameObject Make(string name, Scene scene)
    {
        var o = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(o, scene);
        return o;
    }

    private static Texture2D Control(Color value)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        t.SetPixels(new[] { value, value, value, value }); t.Apply();
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }

    private static void Render(Material source)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var cleanup = new List<Object>();
        var pipeline = GraphicsSettings.defaultRenderPipeline;
        var quality = QualitySettings.renderPipeline;
        bool fog = RenderSettings.fog;
        var ambientMode = RenderSettings.ambientMode;
        var ambient = RenderSettings.ambientLight;
        var sun = RenderSettings.sun;
        try
        {
            GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            QualitySettings.renderPipeline = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.32f, .36f, .4f);
            var cliff = new Material(source); cleanup.Add(cliff);
            var grass = new Material(source); cleanup.Add(grass);
            var empty = Control(Color.clear); cleanup.Add(empty);
            var cliffControl = Control(new Color(0, 1, 0, 0)); cleanup.Add(cliffControl);
            var grassControl = Control(new Color(0, 0, 1, 0)); cleanup.Add(grassControl);
            cliff.SetTexture("_ControlMap0", empty); cliff.SetTexture("_ControlMap1", cliffControl);
            grass.SetTexture("_ControlMap0", grassControl); grass.SetTexture("_ControlMap1", empty);
            foreach (var m in new[] { cliff, grass })
            { m.SetTexture("_ControlMap2", empty); m.SetFloat("_ReceiveShadows", 0); m.SetVector("_TerrainHorizonParams", Vector4.zero); }
            // A box tests both side orientations, signed faces, repeated tiles and
            // distance fallback with the actual terrain material and world UVs.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(wall, scene); cleanup.Add(wall);
            wall.transform.position = new Vector3(0, 5, 0); wall.transform.localScale = new Vector3(22, 10, 16);
            wall.GetComponent<Renderer>().sharedMaterial = cliff;
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(cap, scene); cleanup.Add(cap);
            cap.transform.position = new Vector3(0, 10.06f, 0); cap.transform.localScale = new Vector3(22, .12f, 16);
            cap.GetComponent<Renderer>().sharedMaterial = grass;
            var lightObject = Make("Cliff preview sun", scene); cleanup.Add(lightObject);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
            light.color = new Color(1, .96f, .9f); light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(45, -135, 0); RenderSettings.sun = light;
            var cameraObject = Make("Cliff preview camera", scene); cleanup.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.scene = scene;
            camera.cameraType = CameraType.Preview;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.33f,.46f,.57f);
            camera.farClipPlane = 1500;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            Directory.CreateDirectory("ArtReferences/Cliff");
            camera.transform.position = new Vector3(25, 20, 30); camera.transform.LookAt(new Vector3(0,5,0));
            Capture(camera, "ArtReferences/Cliff/Cliff_TerrainPreview.png");
            cliff.SetFloat("_RockNormalStrength", 0); cliff.SetFloat("_CliffNormalStrength", 0);
            Capture(camera, "ArtReferences/Cliff/Cliff_AlbedoOnlyPreview.png");
            cliff.SetFloat("_RockNormalStrength", source.GetFloat("_RockNormalStrength"));
            cliff.SetFloat("_CliffNormalStrength", source.GetFloat("_CliffNormalStrength"));
            camera.transform.position = new Vector3(-25, 18, -30); camera.transform.LookAt(new Vector3(0,5,0));
            light.transform.rotation = Quaternion.Euler(45,45,0);
            Capture(camera, "ArtReferences/Cliff/Cliff_NegativeFacesPreview.png");
            light.transform.rotation = Quaternion.Euler(45,-135,0);
            camera.transform.position = new Vector3(320, 240, 380); camera.transform.LookAt(new Vector3(0,5,0));
            camera.fieldOfView = 5;
            var distant = Capture(camera, "ArtReferences/Cliff/Cliff_DistancePreview.png");
            cliff.SetFloat("_RockTextureStrength", 0);
            cliff.SetTexture("_RockAlbedo", Texture2D.whiteTexture);
            var distantWithoutTexture = Capture(camera, "ArtReferences/Cliff/Cliff_DistanceWithoutTexturePreview.png");
            for (int i=0;i<distant.Length;i++)
                if (!distant[i].Equals(distantWithoutTexture[i]))
                    throw new InvalidOperationException("Distant rock color depends on nearby texture settings.");
            Debug.Log("CLIFF DISTANCE COLOR PASS: texture strength and albedo replacement have no effect beyond the fade.");
        }
        finally
        {
            foreach (var o in cleanup) Object.DestroyImmediate(o);
            EditorSceneManager.ClosePreviewScene(scene);
            RenderSettings.fog = fog; RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambient; RenderSettings.sun = sun;
            QualitySettings.renderPipeline = quality; GraphicsSettings.defaultRenderPipeline = pipeline;
        }
    }

    private static Color32[] Capture(Camera camera, string path)
    {
        var target = RenderTexture.GetTemporary(1400, 900, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var pixels = new Texture2D(1400,900,TextureFormat.RGB24,false);
        try
        {
            // Warm the changed pipeline/lighting state before taking the readback.
            for (int i=0;i<3;i++)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0,0,1400,900),0,0); pixels.Apply();
            var data = pixels.GetPixels32();
            int min = 255, max = 0, magenta = 0;
            for (int i=0;i<data.Length;i+=17)
            { min = Mathf.Min(min,data[i].g); max = Mathf.Max(max,data[i].g); if(data[i].r>180 && data[i].b>180 && data[i].g<80) magenta++; }
            if (max-min<15 || magenta>100) throw new InvalidOperationException("Blank or shader-error cliff preview: "+path);
            File.WriteAllBytes(path,pixels.EncodeToPNG());
            return data;
        }
        finally { RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(pixels); }
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
