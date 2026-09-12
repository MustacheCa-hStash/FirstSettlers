using System;
using UnityEngine;

// CPU reference for the selection rules mirrored in GrassCompact.compute.
public static class GrassStreamingPolicy
{
    public static float DistanceToSquare(Vector2 viewer, Vector2 center, float halfSize)
    {
        Vector2 d = new Vector2(Mathf.Max(0, Mathf.Abs(viewer.x - center.x) - halfSize),
            Mathf.Max(0, Mathf.Abs(viewer.y - center.y) - halfSize));
        return d.magnitude;
    }
    public static float Density(float distanceInSubChunks, Vector4 density)
    {
        float d = distanceInSubChunks;
        if (d <= 3) return density.x;
        if (d <= 6) return Mathf.Lerp(density.x, density.y, Mathf.InverseLerp(3, 6, d));
        if (d <= 10) return Mathf.Lerp(density.y, density.z, Mathf.InverseLerp(6, 10, d));
        return Mathf.Lerp(density.z, density.w, Mathf.InverseLerp(10, 14, d));
    }
    public static int Select(float rank, float representationRank, float distance, float subSize,
        Vector4 densities, float farDensity, float farBlend, float outerRadius, float edgeWidth)
    {
        float edge = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(outerRadius - edgeWidth, outerRadius, distance));
        float density = Mathf.Lerp(Density(distance / subSize, densities), farDensity, farBlend) * edge;
        if (rank >= density) return -1;
        return representationRank < farBlend ? 1 : 0;
    }
    public static float TargetBlend(float distance, float nearRadius, float transitionWidth)
        => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(nearRadius - transitionWidth, nearRadius + transitionWidth, distance));
    public static float AdvanceBlend(float current, float target, float dt, float seconds, float hysteresis)
        => target > 0f && target < 1f && Mathf.Abs(current - target) <= hysteresis ? current : Mathf.MoveTowards(current, target, dt / Mathf.Max(0.01f, seconds));
    // Waiting eventually outweighs distance, even with a constant supply of nearby work.
    public static float Priority(float distance, float age, bool visible, float radius)
        => distance + (visible ? 0 : radius) - age * Mathf.Max(1f, radius);
    public static float UnitRank(uint rank) => (rank >> 8) * (1f / 16777216f);
    public static float RepresentationRank(uint rank)
    {
        unchecked { rank ^= rank >> 16; rank *= 0x7feb352du; rank ^= rank >> 15; rank *= 0x846ca68bu; rank ^= rank >> 16; }
        return UnitRank(rank);
    }
}
