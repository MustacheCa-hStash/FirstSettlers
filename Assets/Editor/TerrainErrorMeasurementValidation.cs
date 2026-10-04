using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public static class TerrainErrorMeasurementValidation
{
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Screen Error Measurements")]
    public static void Run()
    {
        checks = 0;
        float hd = TerrainErrorMeasurement.PerspectivePixels(2f, 1000f, 1080, 60f);
        float uhd = TerrainErrorMeasurement.PerspectivePixels(2f, 1000f, 2160, 60f);
        Check(Mathf.Abs(hd - 1.870615f) < 0.0001f, "1080p projection reference");
        Check(Mathf.Abs(uhd - 2f * hd) < 0.0001f, "4K doubles pixel error");
        Check(Mathf.Abs(TerrainErrorMeasurement.PerspectivePixels(2f, 4000f, 2160, 60f) - uhd / 4f) < 0.0001f, "Distance projection");
        Check(TerrainErrorMeasurement.PerspectivePixels(2f, 1000f, 2160, 30f) > uhd, "Zoom increases error");
        Check(float.IsPositiveInfinity(TerrainErrorMeasurement.PerspectivePixels(1f, 0f, 2160, 60f)), "Inside-bound refinement");
        Check(TerrainErrorMeasurement.PerspectivePixels(0f, 0f, 2160, 60f) == 0f, "Exact flat surface");
        Check(TerrainErrorMeasurement.OrthographicPixels(2f, 2160, 100f) == 21.6f, "Orthographic projection");
        ValidateViewAngles();
        ValidateTriangleSampling();
        foreach (float scale in new[] { 0.3f, 1f }) ValidateGeneratedMacro(scale);
        Debug.Log($"TERRAIN SCREEN ERROR PASS: {checks} checks; triangle rather than bilinear reconstruction, skirts, " +
            "flat planes, transforms, negative coordinates, world scaling, live height evaluator, Burst jobs, ground/elevated/overhead camera angles, " +
            "clipping, and 4K/FOV/distance/orthographic projection.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void ValidateViewAngles()
    {
        var root = new GameObject("Terrain error projection validation");
        root.SetActive(false);
        Camera camera = root.AddComponent<Camera>();
        camera.fieldOfView = 60f;
        camera.aspect = 16f / 9f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 5000f;
        try
        {
            TerrainErrorView level = TerrainErrorView.Capture(camera);
            float ground = TerrainErrorMeasurement.ProjectedPixels(level, new Vector3(0f, 0f, 1000f), 2f, 2160);
            Check(Mathf.Abs(ground - TerrainErrorMeasurement.PerspectivePixels(2f, 1000f, 2160, 60f)) < 0.001f,
                "Ground-level projection agrees with reference formula");
            Check(float.IsNaN(TerrainErrorMeasurement.ProjectedPixels(level, new Vector3(0f, 0f, -10f), 2f, 2160)),
                "Behind-camera probes excluded");
            Check(float.IsNaN(TerrainErrorMeasurement.ProjectedPixels(level, new Vector3(10000f, 0f, 100f), 2f, 2160)),
                "Off-screen probes excluded");
            camera.transform.position = new Vector3(0f, 800f, 0f);
            camera.transform.LookAt(new Vector3(0f, 0f, 1000f));
            Check(!level.Matches(camera), "Cached camera view detects motion");
            TerrainErrorView elevated = TerrainErrorView.Capture(camera);
            Vector3 point = new Vector3(50f, 0f, 1000f);
            Vector3 a = camera.WorldToViewportPoint(point);
            Vector3 b = camera.WorldToViewportPoint(point + Vector3.up * 2f);
            float expected = new Vector2((a.x - b.x) * 3840f, (a.y - b.y) * 2160f).magnitude;
            float measured = TerrainErrorMeasurement.ProjectedPixels(elevated, point, 2f, 2160);
            Check(Mathf.Abs(measured - expected) < 0.001f, "Elevated projection matches Unity camera projection");
            Check(measured < ground, "Elevated viewing angle reduces this vertical displacement");
            Check(Mathf.Abs(TerrainErrorMeasurement.ProjectedPixels(elevated, point, 2f, 1080) * 2f - measured) < 0.001f,
                "Angle-aware result rescales to 4K");
            camera.transform.position = new Vector3(0f, 1000f, 0f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            TerrainErrorView overhead = TerrainErrorView.Capture(camera);
            Check(TerrainErrorMeasurement.ProjectedPixels(overhead, Vector3.zero, 2f, 2160) < 0.001f,
                "Height displacement along centre viewing ray has no screen displacement");
            Check(TerrainErrorMeasurement.ProjectedPixels(overhead, new Vector3(100f, 0f, 200f), 2f, 2160) > 0.01f,
                "Overhead off-axis perspective depth changes still matter");
            Check(float.IsPositiveInfinity(TerrainErrorMeasurement.ProjectedPixels(overhead, new Vector3(0f, 999.89f, 0f), 0.02f, 2160)),
                "Visible near-plane crossings flagged rather than divided by tiny depth");
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.identity;
            camera.orthographic = true;
            camera.orthographicSize = 100f;
            float ortho = TerrainErrorMeasurement.ProjectedPixels(TerrainErrorView.Capture(camera), new Vector3(0f, 0f, 1000f), 2f, 2160);
            Check(Mathf.Abs(ortho - 21.6f) < 0.001f, "Angle-aware orthographic result");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void ValidateTriangleSampling()
    {
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f),
                new Vector3(1f, 1f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, -10f, 0f) },
            triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 4, 1 }
        };
        try
        {
            Vector3[] quick = TerrainErrorMeasurement.BuildSamples(mesh, Matrix4x4.identity, false, out int triangles);
            Check(triangles == 2 && quick.Length == 8, "Skirts excluded from residuals");
            Check(quick[3] == new Vector3(0.5f, 0.5f, 0.5f), "Actual diagonal height is 0.5, not bilinear 0.25");
            Check(Mathf.Abs(quick[0].y - 1f / 3f) < 1e-6f, "Triangle centroid height");
            Matrix4x4 transform = Matrix4x4.TRS(new Vector3(-40f, 0f, -80f), Quaternion.identity, Vector3.one * 0.3f);
            Vector3[] dense = TerrainErrorMeasurement.BuildSamples(mesh, transform, true, out triangles);
            Check(triangles == 2 && dense.Length == 20, "Dense sample count");
            Check(Vector3.Distance(dense[3], transform.MultiplyPoint3x4(quick[3])) < 1e-5f, "Translation/scaling applied");
            mesh.vertices = new[] { new Vector3(0f, 2f, 0f), new Vector3(0f, 5f, 1f),
                new Vector3(1f, 9f, 1f), new Vector3(1f, 6f, 0f), new Vector3(0f, -10f, 0f) };
            foreach (Vector3 sample in TerrainErrorMeasurement.BuildSamples(mesh, Matrix4x4.identity, true, out triangles))
                Check(Mathf.Abs(sample.y - (2f + 4f * sample.x + 3f * sample.z)) < 1e-5f, "Plane interpolation exact");
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }

    private static void ValidateGeneratedMacro(float worldScale)
    {
        const int size = 512, seed = 42, resolution = 9;
        var origin = new ChunkCoord(-2, 1);
        var erosion = WorldErosionSettings.Default;
        var settings = new TerrainErrorSamplingSettings { WorldScale = worldScale, HeightMultiplier = 200f, SampleScale = 600f,
            Context = HeightMapGenerator.CreateSamplingContext(seed, 0.24f, 1.3f, erosion) };
        FarTerrainRequestResult generated = FarTerrainGenerator.Generate(origin, 1, size, seed, 600f, 200f,
            worldScale, resolution, 2, 6f, 0.24f, isMacroTile: true, patchSizeInChunks: 4,
            mountainHorizontalScale: 1.3f, erosion: erosion);
        Mesh mesh = generated.TerrainMeshData.CreateMesh();
        try
        {
            var offset = new Vector3((origin.x * size + size * 0.5f) * worldScale, 0f,
                (origin.z * size + size * 0.5f) * worldScale);
            Vector3[] samples = TerrainErrorMeasurement.BuildSamples(mesh, Matrix4x4.Translate(offset), true, out int triangles);
            Check(triangles == 2 * (resolution - 1) * (resolution - 1), "Real macro surface topology and skirts");
            var pointData = new float3[samples.Length];
            for (int i = 0; i < samples.Length; i++) pointData[i] = (float3)samples[i];
            using var points = new NativeArray<float3>(pointData, Allocator.TempJob);
            using var errors = new NativeArray<float>(samples.Length, Allocator.TempJob);
            new TerrainErrorMeasurementJob { Settings = settings, SurfacePoints = points, SignedErrors = errors }
                .Schedule(samples.Length, 64).Complete();
            float max = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                Check(math.isfinite(errors[i]), "Finite procedural residual");
                max = Mathf.Max(max, Mathf.Abs(errors[i]));
                // Dense samples 4/5/6 are the triangle's real vertices: they must reproduce the source field.
                int withinTriangle = i % TerrainErrorMeasurement.DenseSamplesPerTriangle;
                if (withinTriangle >= 4 && withinTriangle <= 6)
                    Check(Mathf.Abs(errors[i]) < 0.005f, "World-to-terrain conversion agrees at real macro vertices");
            }
            Check(max > 0.001f, "Interior samples detect genuine coarse interpolation error");
            Debug.Log($"Diagnostic fixture (seed {seed}, negative macro coordinates, scale {worldScale}): max sampled error {max:F4} world units.");
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
}
