using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Render a marker baked at the model origin through the actual impostor shader.
// This checks root registration independently of a tree's silhouette or lighting.
public static class TreeImpostorGroundingValidation
{
    [StructLayout(LayoutKind.Sequential)]
    struct VisibleInstance
    {
        public Matrix4x4 ObjectToWorld;
        public Vector4 Tint, Fade;
    }

    [MenuItem("Tools/Impostors/Validate Tree Root Anchoring")]
    public static void Run()
    {
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        var cameraObject = new GameObject("Offscreen impostor root validation") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false; camera.cullingMask = 0; camera.aspect = 1;
        camera.nearClipPlane = .1f; camera.farClipPlane = 1000;
        var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
        var mesh = new Mesh { vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) },
            uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
        var command = new CommandBuffer();
        var visible = new ComputeBuffer(1, 96);
        var args = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
        args.SetData(new uint[] { 6, 1, 0, 0, 0 });
        RenderTexture previousTarget = RenderTexture.active;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var shader = Shader.Find("Custom/SpruceOctaImpostor");
            Require(shader != null && shader.isSupported, "Impostor shader unavailable.");
            int checks = 0;
            foreach (string species in new[] { "Spruce", "SugarMaple", "RedMaple" })
            {
                string path = $"Assets/Materials/M_Trees/{species}/{species}_OctaImpostor_M.mat";
                var source = AssetDatabase.LoadAssetAtPath<Material>(path);
                Require(source != null, "Missing material: " + path);
                Vector3 center = source.GetVector("_CaptureCenterLS");
                float radius = source.GetFloat("_CaptureRadius");
                var atlas = BakeRootMarker(center, radius);
                var material = new Material(source) { enableInstancing = true };
                try
                {
                    material.SetTexture("_AlbedoCoverage", atlas);
                    material.SetFloat("_FramesPerAxis", 8); material.SetFloat("_AtlasPadding", 2);
                    material.SetFloat("_DebugView", 1); material.SetFloat("_Cutoff", .5f);
                    foreach (bool orthographic in new[] { false, true })
                    foreach (float scale in new[] { .8f, 3.5f })
                    foreach (float pitch in new[] { -15f, 0f, 5f, 25f, 65f, 89f })
                    foreach (float yaw in new[] { 0f, 44f, 91f, 179f, 271f })
                    {
                        Vector3 root = new Vector3(3, 2, -7);
                        var transform = Matrix4x4.TRS(root, Quaternion.Euler(0, 37, 0), Vector3.one * scale);
                        Vector3 centerWS = transform.MultiplyPoint3x4(center);
                        Vector3 direction = Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
                        camera.orthographic = orthographic; camera.orthographicSize = radius * scale * 1.5f;
                        camera.fieldOfView = 40;
                        camera.transform.position = centerWS + direction * radius * scale * 4;
                        camera.transform.LookAt(root);
                        var properties = new MaterialPropertyBlock();
                        properties.SetBuffer("_DistantTreeInstances", visible);
                        visible.SetData(new[] { new VisibleInstance { ObjectToWorld = transform, Tint = Vector4.one, Fade = Vector4.one } });
                        foreach (int debugView in new[] { 1, 4 })
                        foreach (int mode in new[] { 0, 1, 2 })
                        {
                            material.SetFloat("_DebugView", debugView);
                            material.SetFloat("_DebugFrameX", 0); material.SetFloat("_DebugFrameY", 7);
                            material.DisableKeyword("INSTANCING_ON"); material.DisableKeyword("PROCEDURAL_INSTANCING_ON");
                            command.Clear(); command.SetRenderTarget(target); command.ClearRenderTarget(true, true, Color.clear);
                            var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                            command.SetViewProjectionMatrices(camera.worldToCameraMatrix, projection);
                            command.SetGlobalMatrix("unity_MatrixVP", projection * camera.worldToCameraMatrix);
                            command.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                            // Max wind exposes any nonzero movement at the root.
                            material.SetFloat("_WindStrength", .5f); material.SetFloat("_WindSpeed", 0);
                            if (mode == 0) command.DrawMesh(mesh, transform, material, 0, 0, properties);
                            else if (mode == 1) command.DrawMeshInstanced(mesh, 0, material, 0, new[] { transform }, 1, properties);
                            else command.DrawMeshInstancedIndirect(mesh, 0, material, 0, args, 0, properties);
                            Graphics.ExecuteCommandBuffer(command);
                            RenderTexture.active = target;
                            pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply();
                            var image = pixels.GetPixels32();
                            float xSum = 0, ySum = 0; int count = 0;
                            for (int i = 0; i < image.Length; i++)
                                if (image[i].r > 128 && image[i].g < 32)
                                { xSum += i % 256 + .5f; ySum += i / 256 + .5f; count++; }
                            Require(count > 0, $"Root marker missing: {species}, pitch={pitch}, yaw={yaw}, mode={mode}.");
                            Vector3 expected = camera.WorldToViewportPoint(root);
                            float error = Vector2.Distance(new Vector2(xSum / count, ySum / count), new Vector2(expected.x, expected.y) * 256);
                            Require(error < 1.25f, $"Root moved {error:F3} pixels: {species}, pitch={pitch}, yaw={yaw}, scale={scale}, ortho={orthographic}, mode={mode}, debug={debugView}.");
                            checks++;
                        }
                    }
                    ShaderUtil.CompilePass(material, 0, true);
                    Require(!ShaderUtil.ShaderHasError(shader), "Impostor shader compilation failed.");
                }
                finally { Object.DestroyImmediate(material); Object.DestroyImmediate(atlas); }
            }
            Debug.Log($"IMPOSTOR ROOT PASS: {checks} rendered root checks across spruce and both maples, camera pitch/yaw, scale, perspective/orthographic, maximum wind, ordinary/instanced/indirect draws.");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            command.Dispose(); visible.Release(); args.Release(); target.Release();
            Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(mesh); Object.DestroyImmediate(cameraObject);
            GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQuality;
            Object.DestroyImmediate(pipeline); Object.DestroyImmediate(rendererData);
            ShaderUtil.allowAsyncCompilation = previousAsync;
        }
    }

    static Texture2D BakeRootMarker(Vector3 center, float radius)
    {
        const int stride = 256, tile = 252, padding = 2, frames = 8;
        var atlas = new Texture2D(stride * frames, stride * frames, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear };
        var colors = new Color32[atlas.width * atlas.height];
        for (int y = 0; y < frames; y++) for (int x = 0; x < frames; x++)
        {
            Vector3 direction = DecodeFrame(x, y, frames);
            Vector3 upReference = Mathf.Abs(direction.y) > .98f ? Vector3.forward : Vector3.up;
            // Use the baker's actual camera rotation, independently of the
            // runtime shader's cross products, to catch mirrored frame axes.
            Quaternion captureRotation = Quaternion.LookRotation(-direction, upReference);
            Vector3 right = captureRotation * Vector3.right;
            Vector3 up = captureRotation * Vector3.up;
            Vector2 rootUV = new Vector2(Vector3.Dot(-center, right), Vector3.Dot(-center, up)) / (radius * 2) + Vector2.one * .5f;
            Vector2 rootPixel = rootUV * tile + new Vector2(x * stride + padding, y * stride + padding);
            for (int py = Mathf.FloorToInt(rootPixel.y - 4); py <= Mathf.CeilToInt(rootPixel.y + 4); py++)
            for (int px = Mathf.FloorToInt(rootPixel.x - 4); px <= Mathf.CeilToInt(rootPixel.x + 4); px++)
                if (Vector2.Distance(new Vector2(px + .5f, py + .5f), rootPixel) < 3.5f)
                    colors[py * atlas.width + px] = new Color32(255, 0, 0, 255);
        }
        atlas.SetPixels32(colors); atlas.Apply(); return atlas;
    }

    public static Vector3 DecodeFrame(int x, int y, int frames)
    {
        Vector2 e = new Vector2((x + .5f) / frames, (y + .5f) / frames) * 2 - Vector2.one;
        Vector3 n = new Vector3(e.x, e.y, 1 - Mathf.Abs(e.x) - Mathf.Abs(e.y));
        if (n.z < 0)
        {
            float oldX = n.x;
            n.x = (1 - Mathf.Abs(n.y)) * (oldX >= 0 ? 1 : -1);
            n.y = (1 - Mathf.Abs(oldX)) * (n.y >= 0 ? 1 : -1);
        }
        return n.normalized;
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
