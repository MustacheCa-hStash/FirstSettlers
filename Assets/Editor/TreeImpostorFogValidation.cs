using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class TreeImpostorFogValidation
{
    [StructLayout(LayoutKind.Sequential)]
    struct VisibleInstance { public Matrix4x4 Transform; public Vector4 Tint, Fade; }

    [MenuItem("Tools/Impostors/Validate Night Fog")]
    public static void Run()
    {
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        var previousTarget = RenderTexture.active;
        bool previousFog = RenderSettings.fog, previousAsync = ShaderUtil.allowAsyncCompilation;
        FogMode previousMode = RenderSettings.fogMode;
        Color previousColor = RenderSettings.fogColor;
        float previousStart = RenderSettings.fogStartDistance, previousEnd = RenderSettings.fogEndDistance;
        float previousDim = Shader.GetGlobalFloat("_TreeNightAmbientFloorDimAmount");
        float previousScale = Shader.GetGlobalFloat("_TreeNightAmbientFloorScaleAtMidnight");
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        var cameraObject = new GameObject("Offscreen impostor fog validation") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
        camera.orthographic = true; camera.orthographicSize = 1; camera.aspect = 1;
        camera.nearClipPlane = .1f; camera.farClipPlane = 1000;
        camera.transform.position = new Vector3(0, 0, -20); camera.transform.LookAt(Vector3.zero);
        var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true);
        var mesh = new Mesh { vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) },
            uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
        var materialId = Solid(new Color(1, 0, .5f, 1));
        var surface = Solid(new Color(.5f, .5f, 1, 0));
        var ambient = Solid(new Color(.5f, .5f, 0, 0));
        var command = new CommandBuffer();
        var instances = new ComputeBuffer(1, 96);
        var args = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
        args.SetData(new uint[] { 6, 1, 0, 0, 0 });
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            // Match the current scene's midnight distances and fog color.
            RenderSettings.fogStartDistance = 70; RenderSettings.fogEndDistance = 400;
            RenderSettings.fogColor = new Color(.055f, .065f, .10f);
            Shader.SetGlobalFloat("_TreeNightAmbientFloorScaleAtMidnight", .48f);
            target.Create();
            int checks = 0;
            foreach (string species in new[] { "Spruce", "SugarMaple", "RedMaple" })
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/M_Trees/{species}/{species}_OctaImpostor_M.mat");
                Require(source != null, "Missing impostor: " + species);
                var material = new Material(source) { enableInstancing = true };
                try
                {
                    material.SetTexture("_AlbedoCoverage", Texture2D.whiteTexture); material.SetTexture("_MaterialIdAtlas", materialId);
                    material.SetTexture("_SurfaceAtlas", surface); material.SetTexture("_AmbientAtlas", ambient);
                    material.SetVector("_CaptureCenterLS", Vector4.zero); material.SetFloat("_CaptureRadius", 1);
                    material.SetFloat("_FramesPerAxis", 1); material.SetFloat("_AtlasPadding", 0);
                    material.SetFloat("_AmbientFloor", .5f); material.SetFloat("_SkyStrength", 1); material.SetFloat("_AOStrength", 0);
                    material.SetFloat("_FoliageBrightness", 1); material.SetFloat("_BarkBrightness", 1);
                    material.SetFloat("_BarkColorRemap", 0); material.SetFloat("_TransmissionStrength", 0);
                    material.SetFloat("_UseSeasonPalette", 0); material.SetColor("_LeafSeasonTint", Color.white);
                    material.SetColor("_TreeLeafTint", Color.white); material.SetFloat("_WindStrength", 0);
                    material.SetFloat("_DebugView", 0);
                    foreach (bool leaf in new[] { true, false })
                    foreach (float dim in new[] { 0f, .5f, 1f })
                    foreach (float distance in new[] { 40f, 70f, 235f, 383.5f, 400f, 600f })
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        Shader.SetGlobalFloat("_TreeNightAmbientFloorDimAmount", dim);
                        var idColors = new Color[16]; for (int i = 0; i < idColors.Length; i++) idColors[i] = leaf ? new Color(1,0,.5f,1) : new Color(0,1,0,1);
                        materialId.SetPixels(idColors); materialId.Apply();
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        material.DisableKeyword("FOG_EXP"); material.DisableKeyword("FOG_EXP2"); material.EnableKeyword("FOG_LINEAR");
                        var transform = Matrix4x4.Translate(new Vector3(0, 0, distance - 20));
                        instances.SetData(new[] { new VisibleInstance { Transform = transform, Tint = Vector4.one, Fade = Vector4.one } });
                        var properties = new MaterialPropertyBlock(); properties.SetBuffer("_DistantTreeInstances", instances);
                        properties.SetVectorArray("_TreeLeafTint", new[] { Vector4.one });
                        command.Clear(); command.SetRenderTarget(target); command.ClearRenderTarget(true, true, Color.clear);
                        var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                        command.SetViewProjectionMatrices(camera.worldToCameraMatrix, projection);
                        command.SetGlobalMatrix("unity_MatrixVP", projection * camera.worldToCameraMatrix);
                        command.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                        command.SetGlobalVector("_MainLightColor", Vector4.zero);
                        if (mode == 0) command.DrawMesh(mesh, transform, material, 0, 0, properties);
                        else if (mode == 1) command.DrawMeshInstanced(mesh, 0, material, 0, new[] { transform }, 1, properties);
                        else command.DrawMeshInstancedIndirect(mesh, 0, material, 0, args, 0, properties);
                        Graphics.ExecuteCommandBuffer(command); RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); pixels.Apply();
                        Color actual = pixels.GetPixel(16, 16);
                        float floor = .5f * Mathf.Lerp(1, .48f, dim);
                        Color fogColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderSettings.fogColor.linear : RenderSettings.fogColor;
                        Color expected = Color.Lerp(new Color(floor, floor, floor, 1), fogColor, Mathf.Clamp01((distance - 70) / 330));
                        float error = Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Abs(actual.g - expected.g), Mathf.Abs(actual.b - expected.b));
                        Require(actual.a > .99f && error < .01f, $"{species} fog mismatch: depth={distance}, dim={dim}, leaf={leaf}, mode={mode}, actual={actual}, expected={expected}, error={error:F4}");
                        checks++;
                    }
                    Require(!ShaderUtil.ShaderHasError(material.shader), "Impostor fog shader failed compilation.");
                }
                finally { Object.DestroyImmediate(material); }
            }
            Debug.Log($"IMPOSTOR NIGHT FOG PASS: {checks} GPU checks; spruce and both maples, leaf/bark, day/twilight/midnight floor, before/start/mid/95%/end/beyond fog, ordinary/instanced/indirect draws. At 400+ units, RGB equals scene fog color and coverage remains opaque.");
        }
        finally
        {
            RenderSettings.fog = previousFog; RenderSettings.fogMode = previousMode; RenderSettings.fogColor = previousColor;
            RenderSettings.fogStartDistance = previousStart; RenderSettings.fogEndDistance = previousEnd;
            Shader.SetGlobalFloat("_TreeNightAmbientFloorDimAmount", previousDim); Shader.SetGlobalFloat("_TreeNightAmbientFloorScaleAtMidnight", previousScale);
            RenderTexture.active = previousTarget; command.Dispose(); instances.Release(); args.Release(); target.Release();
            foreach (Object item in new Object[] { target, pixels, mesh, materialId, surface, ambient, cameraObject }) Object.DestroyImmediate(item);
            GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQuality;
            Object.DestroyImmediate(pipeline); Object.DestroyImmediate(rendererData); ShaderUtil.allowAsyncCompilation = previousAsync;
        }
    }
    static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
        var colors = new Color[16]; for (int i = 0; i < colors.Length; i++) colors[i] = color;
        texture.SetPixels(colors); texture.Apply(); return texture;
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
