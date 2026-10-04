using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

// Editor-only measurements. No changes to terrain generation or runtime LOD selection.
public struct TerrainErrorSamplingSettings
{
    public float WorldScale, HeightMultiplier, SampleScale;
    public TerrainHeightSamplingContext Context;

    public static TerrainErrorSamplingSettings Read(WorldManager world)
    {
        var source = new SerializedObject(world);
        float scale = source.FindProperty("worldScale").floatValue;
        float multiplier = source.FindProperty("meshHeightMultiplier").floatValue;
        var water = new TerrainWaterSettings(source.FindProperty("globalWaterY").floatValue, multiplier, scale);
        return new TerrainErrorSamplingSettings
        {
            WorldScale = scale,
            HeightMultiplier = multiplier,
            SampleScale = source.FindProperty("sampleScale").floatValue,
            Context = HeightMapGenerator.CreateSamplingContext(source.FindProperty("worldSeed").intValue,
                water.WaterLevel, source.FindProperty("mountainWidth").floatValue,
                (WorldErosionSettings)source.FindProperty("erosion").boxedValue)
        };
    }
}

public struct TerrainErrorView
{
    public Matrix4x4 WorldToCamera, Projection;
    public Vector3 Position;
    public Quaternion Rotation;
    public float VerticalFov, Aspect, NearPlane, FarPlane, OrthographicSize;
    public bool Orthographic;

    public static TerrainErrorView Capture(Camera camera) => new TerrainErrorView
    {
        WorldToCamera = camera.worldToCameraMatrix, Projection = camera.projectionMatrix,
        Position = camera.transform.position, Rotation = camera.transform.rotation,
        VerticalFov = camera.fieldOfView, Aspect = camera.aspect,
        NearPlane = camera.nearClipPlane, FarPlane = camera.farClipPlane,
        Orthographic = camera.orthographic, OrthographicSize = camera.orthographicSize
    };

    public bool Matches(Camera camera) => camera && WorldToCamera == camera.worldToCameraMatrix &&
        Projection == camera.projectionMatrix && Mathf.Approximately(Aspect, camera.aspect);
}

public static class TerrainErrorMeasurement
{
    public const int QuickSamplesPerTriangle = 4;
    public const int DenseSamplesPerTriangle = 10;

    public static Vector3 TriangleSample(Vector3 a, Vector3 b, Vector3 c, int index)
    {
        switch (index)
        {
            case 0: return (a + b + c) / 3f;
            case 1: return (a + b) * 0.5f;
            case 2: return (b + c) * 0.5f;
            case 3: return (c + a) * 0.5f;
            case 4: return a;
            case 5: return b;
            case 6: return c;
            case 7: return a * 0.6f + b * 0.2f + c * 0.2f;
            case 8: return a * 0.2f + b * 0.6f + c * 0.2f;
            case 9: return a * 0.2f + b * 0.2f + c * 0.6f;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public static Vector3[] BuildSamples(Mesh mesh, Matrix4x4 localToWorld, bool dense, out int surfaceTriangles)
    {
        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.triangles;
        int perTriangle = dense ? DenseSamplesPerTriangle : QuickSamplesPerTriangle;
        var samples = new List<Vector3>(indices.Length / 3 * perTriangle);
        surfaceTriangles = 0;
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = localToWorld.MultiplyPoint3x4(vertices[indices[i]]);
            Vector3 b = localToWorld.MultiplyPoint3x4(vertices[indices[i + 1]]);
            Vector3 c = localToWorld.MultiplyPoint3x4(vertices[indices[i + 2]]);
            // Vertical skirts have no terrain footprint and must not count as shape error.
            float areaXZ = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
            if (Mathf.Abs(areaXZ) < 1e-7f) continue;
            surfaceTriangles++;
            for (int s = 0; s < perTriangle; s++) samples.Add(TriangleSample(a, b, c, s));
        }
        return samples.ToArray();
    }

    // Perspective proxy, not an exact silhouette or image-difference bound.
    public static float PerspectivePixels(float errorWorld, float distance, int viewportHeight, float verticalFov)
    {
        if (errorWorld <= 0f) return 0f;
        if (distance <= 0f) return float.PositiveInfinity;
        return errorWorld * viewportHeight / (2f * distance * Mathf.Tan(verticalFov * Mathf.Deg2Rad * 0.5f));
    }

    public static float OrthographicPixels(float errorWorld, int viewportHeight, float orthographicSize)
        => errorWorld * viewportHeight / (2f * orthographicSize);

    public static float EstimatedPixels(Camera camera, Bounds bounds, float maxError, int viewportHeight)
        => EstimatedPixels(TerrainErrorView.Capture(camera), bounds, maxError, viewportHeight);

    public static float EstimatedPixels(TerrainErrorView view, Bounds bounds, float maxError, int viewportHeight)
    {
        if (view.Orthographic) return OrthographicPixels(maxError, viewportHeight, view.OrthographicSize);
        // Include the sampled residual in the vertical extent before finding nearest distance.
        bounds.Expand(new Vector3(0f, 2f * maxError, 0f));
        float distance = Vector3.Distance(view.Position, bounds.ClosestPoint(view.Position));
        return PerspectivePixels(maxError, distance, viewportHeight, view.VerticalFov);
    }

    // Project both endpoints of the real sampled residual. This accounts for view direction,
    // height and perspective depth changes; a height-only cosine factor does not capture all three.
    // NaN means neither endpoint is on screen. Infinity flags a visible near/far clip crossing.
    public static float ProjectedPixels(TerrainErrorView view, Vector3 meshPoint, float signedError, int viewportHeight)
    {
        Vector3 intendedPoint = meshPoint + Vector3.up * signedError;
        Vector3 meshView = view.WorldToCamera.MultiplyPoint3x4(meshPoint);
        Vector3 intendedView = view.WorldToCamera.MultiplyPoint3x4(intendedPoint);
        bool meshDepth = -meshView.z >= view.NearPlane && -meshView.z <= view.FarPlane;
        bool intendedDepth = -intendedView.z >= view.NearPlane && -intendedView.z <= view.FarPlane;
        if (!meshDepth && !intendedDepth) return float.NaN;
        Vector2 meshNdc = meshDepth ? Project(view.Projection, meshView) : Vector2.zero;
        Vector2 intendedNdc = intendedDepth ? Project(view.Projection, intendedView) : Vector2.zero;
        bool meshOnScreen = meshDepth && InViewport(meshNdc);
        bool intendedOnScreen = intendedDepth && InViewport(intendedNdc);
        if (!meshOnScreen && !intendedOnScreen) return float.NaN;
        if (!meshDepth || !intendedDepth) return float.PositiveInfinity;
        Vector2 delta = meshNdc - intendedNdc;
        delta.x *= viewportHeight * view.Aspect * 0.5f;
        delta.y *= viewportHeight * 0.5f;
        return delta.magnitude;
    }

    private static bool InViewport(Vector2 point) => Mathf.Abs(point.x) <= 1f && Mathf.Abs(point.y) <= 1f;

    private static Vector2 Project(Matrix4x4 projection, Vector3 point)
    {
        Vector4 clip = projection * new Vector4(point.x, point.y, point.z, 1f);
        return new Vector2(clip.x / clip.w, clip.y / clip.w);
    }
}

[BurstCompile]
public struct TerrainErrorMeasurementJob : IJobParallelFor
{
    public TerrainErrorSamplingSettings Settings;
    [ReadOnly] public NativeArray<float3> SurfacePoints;
    [WriteOnly] public NativeArray<float> SignedErrors;

    public void Execute(int index)
    {
        float3 point = SurfacePoints[index];
        TerrainHeightSamplingContext context = Settings.Context;
        TerrainHeightSample reference = HeightMapGenerator.SampleTerrainHeightNative(
            point.x / Settings.WorldScale, point.z / Settings.WorldScale, Settings.SampleScale,
            context.RiverSeed, context.WaterLevel, context.MountainHorizontalScale, context.Erosion);
        SignedErrors[index] = reference.Height * Settings.HeightMultiplier * Settings.WorldScale - point.y;
    }
}
