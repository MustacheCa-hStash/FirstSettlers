using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Explicit batch visual audit of the generated world. Play-mode camera edits are never saved.</summary>
[InitializeOnLoad]
public static class DistantFogSceneCapture
{
    const string Pending = "FS.DistantFog.CapturePending";
    const string Start = "FS.DistantFog.CaptureStart";
    static int stage;
    static double lastStageTime;
    static Vector3 initialPosition;
    static Quaternion initialRotation;
    static bool oldAsync;
    static Camera benchmarkCamera;
    static RenderTexture benchmarkTarget;
    static ProfilingSampler[] samplers;
    static readonly List<Vector4> timings = new List<Vector4>();
    static int benchmarkFrames;

    static DistantFogSceneCapture()
    {
        EditorApplication.update += Update;
    }
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene(DistantFogSetup.ScenePath);
        DistantFogValidation.Run();
        SessionState.SetBool(Pending + ".Profiler", UnityEngine.Profiling.Profiler.enabled);
        SessionState.SetBool(Pending + ".GpuProfiler", ProfilerDriver.IsAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU));
        ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU, true);
        UnityEngine.Profiling.Profiler.enabled = true;
        SessionState.SetBool(Pending, true);
        SessionState.SetFloat(Start, (float)EditorApplication.timeSinceStartup);
        EditorApplication.EnterPlaymode();
    }
    static void Update()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        double elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat(Start, 0);
        if (elapsed > 150) { Finish(false, "Timed out waiting for generated world."); return; }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            var camera = benchmarkCamera != null ? benchmarkCamera : Camera.main;
            var world = UnityEngine.Object.FindAnyObjectByType<WorldManager>();
            var layer = UnityEngine.Object.FindAnyObjectByType<DistantFogLayer>();
            if (camera == null || world == null || layer == null) return;
            if (stage == 0)
            {
                if (elapsed < 25) return;
                initialPosition = camera.transform.position;
                initialRotation = camera.transform.rotation;
                oldAsync = ShaderUtil.allowAsyncCompilation;
                ShaderUtil.allowAsyncCompilation = false;
                camera.transform.position = initialPosition + Vector3.up * 95;
                camera.transform.rotation = Quaternion.Euler(7, 30, 0);
                lastStageTime = EditorApplication.timeSinceStartup; stage = 1;
                return;
            }
            if (stage < 3 && EditorApplication.timeSinceStartup - lastStageTime < 5) return;
            if (stage == 1)
            {
                Write(camera, "smear-noisy-4k");
                layer.enabled = false;
                Write(camera, "smear-clear-4k");
                layer.enabled = true;
                lastStageTime = EditorApplication.timeSinceStartup; stage = 2;
                return;
            }
            if (stage == 2)
            {
                Write(camera, "smear-noisy-moved-4k");
                // One camera/request per editor tick: isolate 4K fog samples from Game View.
                benchmarkCamera = camera;
                benchmarkCamera.enabled = false;
                benchmarkTarget = new RenderTexture(3840, 2160, 24, RenderTextureFormat.ARGB32);
                benchmarkTarget.Create();
                var type = typeof(DistantFogRendererFeature).GetNestedType("FogPass", BindingFlags.NonPublic);
                samplers = new ProfilingSampler[4];
                string[] names = { "DepthSampler", "WaterSampler", "MarchSampler", "CompositeSampler" };
                for (int i = 0; i < 4; i++)
                {
                    samplers[i] = (ProfilingSampler)type.GetField(names[i], BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    samplers[i].enableRecording = true;
                }
                stage = 3;
                return;
            }
            if (benchmarkFrames < 70)
            {
                if (benchmarkFrames > 10 && samplers[2].gpuSampleCount > 0)
                    timings.Add(new Vector4(Average(samplers[0]), Average(samplers[1]), Average(samplers[2]), Average(samplers[3])));
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = benchmarkTarget });
                benchmarkFrames++;
                return;
            }
            ReportTimings(layer);
            foreach (var sampler in samplers) sampler.enableRecording = false;
            benchmarkTarget.Release(); UnityEngine.Object.DestroyImmediate(benchmarkTarget);
            benchmarkCamera.enabled = true;
            camera.transform.SetPositionAndRotation(initialPosition, initialRotation);
            ShaderUtil.allowAsyncCompilation = oldAsync;
            Debug.Log("DISTANT FOG SCENE CAPTURE: generated SmearScene at 3840x2160, fog on/off and drifting noise. " + world.GetVisibleRenderStatsDebugInfo());
            Finish(true, "Visual captures completed.");
        }
        catch (Exception error) { Debug.LogException(error); Finish(false, error.Message); }
    }
    static float Average(ProfilingSampler sampler) => sampler.gpuSampleCount > 0 ? sampler.gpuElapsedTime / sampler.gpuSampleCount : 0;
    static void ReportTimings(DistantFogLayer layer)
    {
        string report = $"GPU: {SystemInfo.graphicsDeviceName}\nAPI: {SystemInfo.graphicsDeviceType}\n" +
            $"Output: 3840x2160\nFog: {layer.resolution}, {layer.steps} steps\nValid GPU samples: {timings.Count}\n";
        if (timings.Count > 0)
        {
            Vector4 mean = Vector4.zero;
            var totals = new List<float>();
            foreach (var timing in timings) { mean += timing; totals.Add(timing.x + timing.y + timing.z + timing.w); }
            mean /= timings.Count; totals.Sort();
            report += $"Mean finished depth copy: {mean.x:F3} ms\nMean water mask: {mean.y:F3} ms\nMean raymarch: {mean.z:F3} ms\nMean composite: {mean.w:F3} ms\n" +
                $"Median sum of passes: {totals[totals.Count / 2]:F3} ms\nP90 sum of passes: {totals[Mathf.Min(totals.Count - 1, (int)(totals.Count * .9f))]:F3} ms\n";
        }
        report += "Editor GPU timestamps at one generated-world viewpoint. Excludes scene rendering, reflection updates, CPU work and total game frame time. Not a 240 FPS player benchmark.\n";
        File.WriteAllText(".utmp/distant-fog/gpu-timings.txt", report);
        Debug.Log("DISTANT FOG GPU TIMINGS\n" + report);
    }
    static void Write(Camera camera, string name)
    {
        const int width = 3840, height = 2160;
        Directory.CreateDirectory(".utmp/distant-fog");
        var previous = RenderTexture.active;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(".utmp/distant-fog/" + name + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
    static void Finish(bool success, string message)
    {
        SessionState.SetBool(Pending, false);
        UnityEngine.Profiling.Profiler.enabled = SessionState.GetBool(Pending + ".Profiler", false);
        ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU, SessionState.GetBool(Pending + ".GpuProfiler", false));
        Debug.Log("DISTANT FOG CAPTURE: " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
