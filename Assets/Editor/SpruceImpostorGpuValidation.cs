using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Explicit offscreen correctness checks. No scene/Play mode automation or benchmark.
public static class SpruceImpostorGpuValidation
{
    const string PrefabPath = "Assets/Prefabs/Spruce_OctaImpostor_Runtime.prefab";
    const string ComputePath = "Assets/Shaders/DistantTreeCompact.compute";

    [MenuItem("Tools/Impostors/Validate Spruce GPU Rendering")]
    public static void Run() => WithPipeline(RunChecks);

    static void RunChecks()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Require(prefab != null, "Missing runtime spruce impostor prefab.");
        var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        var material = prefab.GetComponent<MeshRenderer>().sharedMaterial;
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
        Require(string.Equals(material.GetTag("DistantTreeIndirect", false, "False"), "True", StringComparison.OrdinalIgnoreCase),
            "Spruce does not advertise indirect support: " + material.shader.name + ", supported=" + material.shader.isSupported +
            ", passes=" + material.passCount + ", path=" + AssetDatabase.GetAssetPath(material.shader) +
            ", pipeline=" + GraphicsSettings.currentRenderPipeline);
        Require(DistantTreeGpuBatch.IsSupported(compute), "Compute rendering unavailable.");
        ValidateRouting(prefab, compute);
        ValidateBounds(mesh, material);
        ValidatePixels(mesh, material);
        Debug.Log("SPRUCE GPU PASS: actual prefab routes to compute; CPU/indirect pixels match at three camera angles with varied scale/yaw/tint; load/handoff fades and camera-facing bounds verified. No FPS benchmark.");
    }

    public static void RunBatch()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            WithPipeline(() => { DistantTreeGpuValidation.Run(); RunChecks(); });
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static void WithPipeline(Action checks)
    {
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        var cameraObject = new GameObject("Spruce validation pipeline initialization");
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
        var target = RenderTexture.GetTemporary(16, 16, 24);
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Require(pipeline != null, "Missing game URP pipeline.");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            // Initialize URP before querying the active shader subshader's tags.
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            checks();
        }
        finally
        {
            RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(cameraObject);
            QualitySettings.renderPipeline = previousQuality; GraphicsSettings.defaultRenderPipeline = previousPipeline;
        }
    }

    static void ValidateRouting(GameObject prefab, ComputeShader compute)
    {
        var settings = new TreeSettings { treeBillboardPrefab = prefab, distantTreeCompactShader = compute, distantTreeGpuCompaction = true };
        using (var manager = new DistantTreeManager(settings, 1, 128, 600, 4, .5f, 2, .3f, 10, .4f, 3,
            WorldFeatureGenerationSettings.Default))
        {
            // Exercise the manager's actual material gate, including its CPU fallback.
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var configure = typeof(DistantTreeManager).GetMethod("ConfigureGpu", flags);
            var batches = (IList)typeof(DistantTreeManager).GetField("uniqueBatches", flags).GetValue(manager);
            Require(batches.Count == 1, "Fallback species unexpectedly split the shared spruce batch.");
            var manifest = typeof(DistantTreeManager).GetMethod("Build", flags).Invoke(manager, new object[] {
                new ChunkCoord(0, 0), new[] { new TreeInstanceData(Vector3.zero, Quaternion.identity, Vector3.one, WorldFeatureVariant.SpruceTree) } });
            var crownAreas = (float[])manifest.GetType().GetField("CrownArea").GetValue(manifest);
            float area = 0; foreach (float value in crownAreas) area += value;
            var originalBounds = prefab.GetComponent<MeshFilter>().sharedMesh.bounds;
            float originalRadius = Mathf.Max(Mathf.Abs(originalBounds.min.x), Mathf.Abs(originalBounds.max.x),
                Mathf.Abs(originalBounds.min.z), Mathf.Abs(originalBounds.max.z));
            Require(Mathf.Abs(area - Mathf.PI * originalRadius * originalRadius) < .001f,
                "Conservative rendering bounds altered ecological tree thinning.");
            configure.Invoke(manager, null);
            var batch = batches[0];
            var gpu = batch.GetType().GetField("Gpu");
            Require(gpu.GetValue(batch) is DistantTreeGpuBatch, "GPU-enabled manager still fell back to CPU for spruce.");
            settings.distantTreeGpuCompaction = false;
            configure.Invoke(manager, null);
            Require(gpu.GetValue(batch) == null, "CPU fallback did not release the GPU batch.");
        }
    }

    static void ValidateBounds(Mesh mesh, Material material)
    {
        Bounds local = DistantTreeGpuBatch.RenderingBounds(mesh.bounds, material);
        Vector3 captureCenter = material.GetVector("_CaptureCenterLS");
        float radius = material.GetFloat("_CaptureRadius");
        foreach (var scale in new[] { Vector3.one, new Vector3(2, .5f, .8f), new Vector3(-1.5f, 2, .4f) })
        {
            var transform = Matrix4x4.TRS(new Vector3(5, -2, 11), Quaternion.Euler(13, 67, -8), scale);
            var envelope = DistantTreeGpuBatch.CalculateBounds(local, transform, material);
            Vector3 origin = transform.GetColumn(3);
            Vector3 center = transform.MultiplyPoint3x4(captureCenter);
            float size = ((Vector3)transform.GetColumn(0)).magnitude;
            foreach (var direction in new[] { Vector3.forward, new Vector3(1, 1, 1).normalized, Vector3.up })
            {
                Vector3 upReference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .98f
                    ? ((Vector3)transform.GetColumn(2)).normalized : Vector3.up;
                Vector3 right = Vector3.Cross(upReference, direction).normalized;
                Vector3 up = Vector3.Cross(direction, right).normalized;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
                {
                    Vector3 p = center + (right * x + up * y) * radius * size - origin;
                    Require(Mathf.Abs(p.x) <= envelope.x + .001f && Mathf.Abs(p.z) <= envelope.x + .001f &&
                        p.y >= envelope.y - .001f && p.y <= envelope.z + .001f, "Angled/scaled impostor escaped the compute bounds.");
                }
            }
        }
    }

    static void ValidatePixels(Mesh mesh, Material source)
    {
        var cpu = new Material(source) { enableInstancing = true };
        var gpu = new Material(source) { enableInstancing = true };
        cpu.SetFloat("_WindStrength", 0); gpu.SetFloat("_WindStrength", 0);
        cpu.DisableKeyword("PROCEDURAL_INSTANCING_ON"); gpu.DisableKeyword("PROCEDURAL_INSTANCING_ON");
        var transforms = new[] {
            Matrix4x4.TRS(new Vector3(-4, -1, 8), Quaternion.Euler(0, 23, 0), new Vector3(.8f, 1.1f, .7f)),
            Matrix4x4.TRS(new Vector3(3, .5f, 11), Quaternion.Euler(0, 147, 0), new Vector3(1.2f, .9f, 1.4f)) };
        var tints = new[] { new Vector4(1, .55f, .6f, 1), new Vector4(.55f, 1, .65f, 1) };
        var instances = new DistantTreeGpuBatch.VisibleInstance[2];
        var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        var cameraObject = new GameObject("Offscreen spruce GPU correctness");
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        camera.aspect = 1; camera.fieldOfView = 50; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
        var properties = new MaterialPropertyBlock();
        var command = new CommandBuffer();
        var visible = new ComputeBuffer(2, 96);
        var args = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
        args.SetData(new uint[] { mesh.GetIndexCount(0), 2, mesh.GetIndexStart(0), (uint)mesh.GetBaseVertex(0), 0 });
        RenderTexture previous = RenderTexture.active;
        try
        {
            target.Create();
            Color32[] Render(bool indirect, float coverage, float handoff, bool fadeEnabled = true)
            {
                var fade = new Vector4(coverage, handoff, 0, 0);
                for (int i = 0; i < 2; i++) instances[i] = new DistantTreeGpuBatch.VisibleInstance {
                    ObjectToWorld = transforms[i], Tint = tints[i], Fade = fade };
                visible.SetData(instances);
                properties.Clear();
                properties.SetFloat("_DistantTreeEnabled", fadeEnabled ? 1 : 0);
                properties.SetFloat("_DistantTreeBillboard", 1);
                properties.SetVectorArray("_TreeLeafTint", tints);
                properties.SetVectorArray("_DistantTreeFade", new[] { fade, fade });
                properties.SetBuffer("_DistantTreeInstances", visible);
                command.Clear(); command.SetRenderTarget(target);
                command.ClearRenderTarget(true, true, Color.black);
                command.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                command.SetGlobalMatrix("unity_MatrixVP", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix);
                command.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                command.SetGlobalVector("_MainLightPosition", new Vector4(0, 1, 0, 0));
                command.SetGlobalVector("_MainLightColor", Vector4.one);
                if (indirect) command.DrawMeshInstancedIndirect(mesh, 0, gpu, 0, args, 0, properties);
                else command.DrawMeshInstanced(mesh, 0, cpu, 0, transforms, 2, properties);
                Graphics.ExecuteCommandBuffer(command);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply();
                return pixels.GetPixels32();
            }
            foreach (Vector3 position in new[] { new Vector3(0, 4, -10), new Vector3(13, 13, -8), new Vector3(0, 24, 8) })
            {
                camera.transform.position = position; camera.transform.LookAt(new Vector3(0, 4, 8));
                var baseline = Render(false, 1, 1);
                int full = Coverage(baseline); Require(full > 100, "CPU spruce fixture did not render.");
                Compare(baseline, Render(true, 1, 1), "full visibility");
                Compare(baseline, Render(false, 0, 0, false), "standalone material default");
                var half = Render(false, 1, .5f);
                Compare(half, Render(true, 1, .5f), "handoff fade");
                Require(Coverage(half) > full * .35f && Coverage(half) < full * .65f, "Handoff fade did not halve coverage.");
                Compare(Render(false, .3f, 1), Render(true, .3f, 1), "load/thinning fade");
                Require(Coverage(Render(true, 0, 1)) == 0 && Coverage(Render(false, 1, 0)) == 0,
                    "Zero coverage/handoff still rendered.");
            }
        }
        finally
        {
            RenderTexture.active = previous;
            command.Dispose(); visible.Release(); args.Release(); target.Release();
            Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(cpu); Object.DestroyImmediate(gpu);
        }
    }

    static int Coverage(Color32[] pixels)
    {
        int count = 0; foreach (var p in pixels) if (p.r + p.g + p.b > 15) count++; return count;
    }
    static void Compare(Color32[] cpu, Color32[] gpu, string scenario)
    {
        long difference = 0;
        for (int i = 0; i < cpu.Length; i++) difference += Math.Abs(cpu[i].r - gpu[i].r) +
            Math.Abs(cpu[i].g - gpu[i].g) + Math.Abs(cpu[i].b - gpu[i].b);
        Require(difference / (double)(cpu.Length * 3) < .75, "CPU/GPU spruce pixels differ: " + scenario);
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
