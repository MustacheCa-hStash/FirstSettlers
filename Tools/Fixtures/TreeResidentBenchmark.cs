using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Temporary matched baseline experiment; retained with its CSV under .utmp/tree-resident.
public static class TreeResidentBenchmark
{
    static IEnumerator routine;
    static Camera camera;
    static bool resident;
    static DistantTreeGpuBatch batch;
    static TreeResidentBenchmarkBaseline baseline;
    static Matrix4x4[] matrices;
    static int[] slots, regions;
    static Bounds[] bounds;
    static readonly Vector4 ecology = new Vector4(.5f, 10, 0, 0);
    static readonly Vector4[] planes = new Vector4[6];
    static readonly TreeSettings settings = new TreeSettings { distantTreeDensity = 1, distantTreeDensityAware = false, distantTreeProtectedCount = 0 };
    static double cpu;
    static long allocations;
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.fog = false;
            camera = new GameObject("Resident benchmark camera").AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(0, 4, 0); camera.nearClipPlane = .1f; camera.farClipPlane = 1000;
            camera.aspect = 16f / 9; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            RenderPipelineManager.beginCameraRendering += Draw;
            routine = Run(); EditorApplication.update += Tick;
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Tick()
    {
        try { if (!routine.MoveNext()) Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Finish(int code)
    {
        EditorApplication.update -= Tick; RenderPipelineManager.beginCameraRendering -= Draw;
        (routine as IDisposable)?.Dispose(); batch?.Dispose(); baseline?.Dispose();
        if (camera != null) Object.DestroyImmediate(camera.gameObject);
        EditorApplication.Exit(code);
    }
    static void Draw(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering != camera || batch == null) return;
        long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        if (resident)
        {
            batch.BeginFrame();
            for (int i = 0; i < regions.Length; i++) batch.SubmitRegion(regions[i], 1, bounds[i], 32, true);
            batch.Draw(settings, camera, camera.transform.position, planes, 650, 50, 650, 1, 154, 192);
        }
        else
        {
            baseline.BeginFrame();
            var viewer = camera.transform.position;
            for (int i = 0; i < matrices.Length; i++)
            {
                var m = matrices[i];
                float dx = m.m03 - viewer.x, dz = m.m23 - viewer.z;
                float handoff = Mathf.InverseLerp(154, 192, Mathf.Sqrt(dx * dx + dz * dz));
                baseline.Submit(slots[i], m.m13, 1, handoff, ecology);
            }
            baseline.Draw(settings, camera, viewer, planes, 650, 50, 650, 1);
        }
        cpu = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        allocations = GC.GetAllocatedBytesForCurrentThread() - allocated;
    }
    static IEnumerator Run()
    {
        Directory.CreateDirectory(".utmp/tree-resident");
        using var csv = new StreamWriter(".utmp/tree-resident/benchmark.csv");
        csv.WriteLine("trees,regions,mode,pass,cpuMedianMs,cpuP95Ms,synchronizedRenderMedianMs,maxManagedBytes,sourceUploads,regionUploads,pixelDifferences");
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/DistantTreeCompact.compute");
        var oldCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Editor/TreeResidentBenchmarkBaseline.compute");
        var mesh = new Mesh { vertices = new[] { new Vector3(-1,0,0), new Vector3(1,0,0), new Vector3(-1,12,0), new Vector3(1,12,0) },
            uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }, triangles = new[] { 0,2,1,1,2,3 } };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var material = new Material(Shader.Find("Custom/TreeBillboardInstancedSimpleLit")) { enableInstancing = true };
        var oldMaterial = new Material(material);
        var target = new RenderTexture(1280, 720, 24); target.Create();
        Color32[] Pixels()
        {
            var prior = RenderTexture.active; var texture = new Texture2D(1280,720,TextureFormat.RGBA32,false);
            try { RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,1280,720),0,0); texture.Apply(); return texture.GetPixels32(); }
            finally { RenderTexture.active = prior; Object.DestroyImmediate(texture); }
        }
        try
        {
            foreach (int count in new[] { 4096, 16384, 65536 })
            {
                matrices = new Matrix4x4[count]; slots = new int[count]; regions = new int[count / 32]; bounds = new Bounds[regions.Length];
                batch = new DistantTreeGpuBatch(compute, mesh, material);
                baseline = new TreeResidentBenchmarkBaseline(oldCompute, mesh, oldMaterial);
                var random = new System.Random(1456);
                for (int i = 0; i < regions.Length; i++) regions[i] = batch.CreateRegion();
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2;
                    float radius = 250 + (float)random.NextDouble() * 320;
                    var m = matrices[i] = Matrix4x4.Translate(new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius));
                    var b = new Bounds(new Vector3(m.m03, 6, m.m23), new Vector3(2,12,2));
                    if (i % 32 == 0) bounds[i / 32] = b; else bounds[i / 32].Encapsulate(b);
                    batch.Register(m, Vector4.one, ecology, 1, regions[i / 32], 1);
                    slots[i] = baseline.Register(m, Vector4.one, ecology, 1);
                }
                var frustum = GeometryUtility.CalculateFrustumPlanes(camera);
                for (int i = 0; i < 6; i++) planes[i] = new Vector4(frustum[i].normal.x,frustum[i].normal.y,frustum[i].normal.z,frustum[i].distance);
                Color32[] reference = null;
                for (int pass = 0; pass < 2; pass++)
                foreach (bool next in pass == 0 ? new[] { false, true } : new[] { true, false })
                {
                    resident = next;
                    var times = new List<double>(); var render = new List<double>(); long maxBytes = 0;
                    for (int frame = 0; frame < 32; frame++)
                    {
                        long started = Stopwatch.GetTimestamp();
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        var request = AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1); request.WaitForCompletion();
                        if (request.hasError) throw new Exception("GPU synchronization failed");
                        if (frame >= 16) { times.Add(cpu); render.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency); maxBytes = Math.Max(maxBytes, allocations); }
                        yield return null;
                    }
                    var pixels = Pixels(); if (reference == null) reference = pixels;
                    int changed = 0;
                    for (int i = 0; i < pixels.Length; i++) if (!pixels[i].Equals(reference[i])) changed++;
                    if (changed != 0) throw new Exception("Baseline/resident rendered pixels differ: " + changed);
                    times.Sort(); render.Sort();
                    csv.WriteLine($"{count},{regions.Length},{(resident ? "resident" : "baseline")},{pass},{times[8]:F6},{times[15]:F6},{render[8]:F6},{maxBytes},{(resident ? batch.UploadedSourceCount : -1)},{(resident ? batch.UploadedRegionCount : -1)},{changed}"); csv.Flush();
                }
                Debug.Log("RESIDENT_BENCHMARK completed trees=" + count);
                batch.Dispose(); baseline.Dispose(); batch = null; baseline = null;
            }
            Debug.Log("RESIDENT_BENCHMARK_PASS: matched baseline rendering and submission timings saved to .utmp/tree-resident/benchmark.csv");
        }
        finally
        {
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(material); Object.DestroyImmediate(oldMaterial);
        }
    }
}
