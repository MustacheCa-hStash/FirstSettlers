using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Numeric grass-job snapshot. The circle only rejects distant queries; the hull defines contact.</summary>
public struct RockGrassExclusion
{
    public float2 center;
    public float radius;
    public float4x4 chunkToRock;
    public float3 contactSegment;
    public float padding;
    public int firstPlane, planeCount;

    public static RockGrassExclusion Create(RockInstanceData instance, float fallbackRadius, int firstPlane)
    {
        var result = new RockGrassExclusion
        {
            center = new float2(instance.localPosition.x, instance.localPosition.z),
            radius = instance.grassExclusionRadius >= 0f ? instance.grassExclusionRadius : Mathf.Max(0f, fallbackRadius),
            firstPlane = firstPlane
        };
        if (instance.grassExclusionHull == null || instance.grassExclusionHull.Length < 4 ||
            Mathf.Min(Mathf.Abs(instance.localScale.x), Mathf.Min(Mathf.Abs(instance.localScale.y), Mathf.Abs(instance.localScale.z))) < .00001f)
            return result;

        Vector3 position = instance.localPosition;
        position.y -= instance.groundEmbed * Mathf.Abs(instance.localScale.y);
        var matrix = Matrix4x4.TRS(position, instance.localRotation, instance.localScale);
        var inverse = matrix.inverse;
        result.chunkToRock = inverse;
        result.planeCount = instance.grassExclusionHull.Length;
        result.padding = instance.grassContactPadding;
        float height = instance.grassContactHeight * Mathf.Abs(instance.localScale.y);
        result.contactSegment = inverse.MultiplyVector(Vector3.up * height);
        Vector3 center = matrix.MultiplyPoint3x4(instance.grassHullBounds.center);
        Vector3 extent = instance.grassHullBounds.extents + Vector3.one * result.padding;
        float x = Mathf.Abs(matrix.m00)*extent.x + Mathf.Abs(matrix.m01)*extent.y + Mathf.Abs(matrix.m02)*extent.z;
        float z = Mathf.Abs(matrix.m20)*extent.x + Mathf.Abs(matrix.m21)*extent.y + Mathf.Abs(matrix.m22)*extent.z;
        result.center = new float2(center.x, center.z);
        result.radius = Mathf.Sqrt(x*x + z*z);
        return result;
    }

    public bool Contains(float3 grassRoot, NativeArray<float4> planes)
    {
        if (math.lengthsq(grassRoot.xz - center) >= radius * radius) return false;
        if (planeCount == 0) return true; // Compatibility circle for unconfigured prefabs.
        float3 point = math.transform(chunkToRock, grassRoot);
        float enter = 0f, leave = 1f;
        // Clip the short vertical blade segment against the convex collision hull. Grass on the
        // actual terrain is checked in the same embedded/rotated/scaled frame as the rock mesh.
        for (int i = 0; i < planeCount; i++)
        {
            float4 plane = planes[firstPlane + i];
            float distance = math.dot(plane.xyz, point) + plane.w - padding;
            float direction = math.dot(plane.xyz, contactSegment);
            if (math.abs(direction) < .000001f)
            {
                if (distance > 0f) return false;
                continue;
            }
            float t = -distance / direction;
            if (direction > 0f) leave = math.min(leave, t);
            else enter = math.max(enter, t);
            if (enter > leave) return false;
        }
        return true;
    }
}
