using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Compare the actual 3D leaf and octa shaders under neutral lighting/detail.
// This isolates hue, seasonal preview and per-instance tint from LOD lighting.
public static class MapleImpostorColorValidation
{
    [StructLayout(LayoutKind.Sequential)]
    struct VisibleInstance { public Matrix4x4 Transform; public Vector4 Tint, Fade; }

    [MenuItem("Tools/Impostors/Validate Maple Color Consistency")]
    public static void Run()
    {
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        var previousTarget = RenderTexture.active;
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        float previousPreview = Shader.GetGlobalFloat("_TreeSeasonSimulationEnabled");
        float previousAutumn = Shader.GetGlobalFloat("_TreeSeasonSimulationAutumnAmount");
        float previousStanding = Shader.GetGlobalFloat("_StandingTreeEnabled");
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        var cameraObject = new GameObject("Offscreen maple color validation") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
        camera.orthographic = true; camera.orthographicSize = 1; camera.aspect = 1;
        camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
        var mesh = new Mesh { vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) },
            uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }, triangles = new[] { 0, 1, 2, 0, 2, 3 },
            normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back } };
        var materialId = Solid(new Color(1, 0, .5f, 1));
        var surface = Solid(new Color(.5f, .5f, 1, 0));
        var ambient = Solid(new Color(.5f, .5f, 1, 0));
        var command = new CommandBuffer();
        var instances = new ComputeBuffer(1, 96);
        var args = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
        args.SetData(new uint[] { 6, 1, 0, 0, 0 });
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            int checks = 0;
            foreach (string species in new[] { "SugarMaple", "RedMaple" })
            {
                string folder = "Assets/Materials/M_Trees/" + species + "/";
                var source = AssetDatabase.LoadAssetAtPath<Material>(folder + "M_" + species + "Leaf_Stylized.mat");
                var impostor = AssetDatabase.LoadAssetAtPath<Material>(folder + species + "_OctaImpostor_M.mat");
                Require(source != null && impostor != null, "Missing maple materials: " + species);
                foreach (string property in new[] { "_SummerLeafColor", "_AutumnYellowColor", "_AutumnOrangeColor", "_AutumnRedColor", "_TreeLeafTint" })
                    Require(source.GetColor(property) == impostor.GetColor(property), species + " palette differs: " + property);
                foreach (string property in new[] { "_SeasonAutumnAmount", "_AutumnVariationStrength", "_TreeTintStrength" })
                    Require(Mathf.Abs(source.GetFloat(property) - impostor.GetFloat(property)) < .0001f, species + " settings differ: " + property);
                Require(impostor.GetFloat("_SeasonPaletteMode") == (species == "SugarMaple" ? 1 : 0), species + " uses the wrong palette.");
                var near = new Material(source) { enableInstancing = true };
                var far = new Material(impostor) { enableInstancing = true };
                try
                {
                    near.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    near.SetFloat("_LeafDetailStrength", 0); near.SetFloat("_ColorVariationStrength", 0);
                    near.SetFloat("_AmbientStrength", 1); near.SetFloat("_SpecularStrength", 0);
                    near.SetFloat("_TranslucencyStrength", 0); near.SetFloat("_SnowCoverage", 0);
                    near.SetFloat("_UseVertexColor", 0); near.SetFloat("_WindFlutterStrength", 0);
                    far.SetTexture("_AlbedoCoverage", Texture2D.whiteTexture); far.SetTexture("_MaterialIdAtlas", materialId);
                    far.SetTexture("_SurfaceAtlas", surface); far.SetTexture("_AmbientAtlas", ambient);
                    far.SetVector("_CaptureCenterLS", Vector4.zero); far.SetFloat("_CaptureRadius", 1);
                    far.SetFloat("_FramesPerAxis", 1); far.SetFloat("_AtlasPadding", 0);
                    far.SetFloat("_AmbientFloor", 1); far.SetFloat("_SkyStrength", 1);
                    far.SetFloat("_AOStrength", 0); far.SetFloat("_FoliageBrightness", 1); far.SetFloat("_TransmissionStrength", 0);
                    // A fixed mid-palette coordinate removes unrelated mesh/atlas noise.
                    near.SetFloat("_AutumnVariationStrength", 0); far.SetFloat("_AutumnVariationStrength", 0);
                    near.SetFloat("_WindStrength", 0); far.SetFloat("_WindStrength", 0);
                    Color Render(Material material, Color tint, int mode, bool isNear)
                    {
                        material.SetColor("_TreeLeafTint", tint);
                        var properties = new MaterialPropertyBlock();
                        properties.SetVectorArray("_TreeLeafTint", new[] { (Vector4)tint });
                        properties.SetVectorArray("_StandingTreeLeaves", new[] { (Vector4)tint });
                        properties.SetVectorArray("_StandingTreeAppearance", new[] { Vector4.zero });
                        properties.SetVectorArray("_StandingTreeFade", new[] { new Vector4(0, 1, 1, 0) });
                        properties.SetBuffer("_DistantTreeInstances", instances);
                        instances.SetData(new[] { new VisibleInstance { Transform = Matrix4x4.identity, Tint = tint, Fade = Vector4.one } });
                        command.Clear(); command.SetRenderTarget(target); command.ClearRenderTarget(true, true, Color.clear);
                        var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                        command.SetViewProjectionMatrices(camera.worldToCameraMatrix, projection);
                        command.SetGlobalMatrix("unity_MatrixVP", projection * camera.worldToCameraMatrix);
                        command.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                        command.SetGlobalVector("_MainLightColor", Vector4.zero);
                        command.SetGlobalFloat("_StandingTreeEnabled", isNear && mode == 1 ? 1 : 0);
                        command.SetGlobalFloat("_DistantTreeEnabled", 0);
                        if (mode == 0 || (isNear && mode == 2)) command.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, properties);
                        else if (mode == 1) command.DrawMeshInstanced(mesh, 0, material, 0, new[] { Matrix4x4.identity }, 1, properties);
                        else command.DrawMeshInstancedIndirect(mesh, 0, material, 0, args, 0, properties);
                        Graphics.ExecuteCommandBuffer(command); RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                        Color color = pixels.GetPixel(32, 32);
                        Require(color.a > .9f, "Leaf fixture failed to render: " + material.shader.name);
                        return color;
                    }
                    var tints = new[] { source.GetColor("_TreeLeafTint"), Color.white, new Color(1, .74f, .22f, 1),
                        new Color(1, .52f, .18f, 1), new Color(.82f, .22f, .14f, 1), new Color(.25f, .65f, .15f, 0) };
                    foreach (bool preview in new[] { false, true })
                    foreach (float season in new[] { 0f, .5f, 1f })
                    foreach (Color tint in tints)
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        Shader.SetGlobalFloat("_TreeSeasonSimulationEnabled", preview ? 1 : 0);
                        Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount", season);
                        near.SetFloat("_SeasonAutumnAmount", preview ? 0 : season); far.SetFloat("_SeasonAutumnAmount", preview ? 0 : season);
                        Color a = Render(near, tint, mode, true), b = Render(far, tint, mode, false);
                        float error = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
                        Require(error < .015f, $"{species} color mismatch {error:F4}: season={season}, preview={preview}, mode={mode}, tint={tint}, 3D={a}, impostor={b}");
                        checks++;
                    }
                    foreach (var material in new[] { near, far })
                    {
                        ShaderUtil.CompilePass(material, 0, true);
                        Require(!ShaderUtil.ShaderHasError(material.shader), material.shader.name + " failed compilation.");
                    }
                }
                finally { Object.DestroyImmediate(near); Object.DestroyImmediate(far); }
            }
            Debug.Log($"MAPLE COLOR PASS: {checks} comparisons of actual 3D/impostor shaders; both species, summer/half/autumn, authored/global preview, neutral/yellow/orange/red/alpha-zero tints, ordinary/instanced/indirect rendering; source palette settings match.");
        }
        finally
        {
            Shader.SetGlobalFloat("_TreeSeasonSimulationEnabled", previousPreview); Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount", previousAutumn);
            Shader.SetGlobalFloat("_StandingTreeEnabled", previousStanding); RenderTexture.active = previousTarget;
            command.Dispose(); instances.Release(); args.Release(); target.Release();
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
