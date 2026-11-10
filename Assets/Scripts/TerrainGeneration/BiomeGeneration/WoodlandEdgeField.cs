using System.Collections.Generic;
using UnityEngine;

/// <summary>Lower-canopy gaps at a stand boundary, sampled in world metres.</summary>
public sealed class WoodlandEdgeField
{
    internal readonly struct Crown
    {
        internal readonly Vector2 Root;
        internal readonly float Radius, Bottom;
        internal Crown(Vector2 root, float radius, float bottom)
        { Root = root; Radius = radius; Bottom = bottom; }
    }

    internal const float Neighborhood = 20f;
    internal const float MaxCrownRadius = 10f;
    private readonly Crown[] crowns;
    private readonly int seed;
    public float Strength { get; }

    internal WoodlandEdgeField(List<Crown> crowns, int seed, float strength)
    {
        this.crowns = crowns.ToArray();
        this.seed = seed;
        Strength = strength;
    }

    public float Gap(Vector2 point)
    {
        float support = 0f, edge = 0f, lowCover = 0f;
        Vector2 interior = Vector2.zero;
        // Keep broad openings between clumps and extend farther out in exposed pockets.
        float patch = TreeStandField.Noise(point.x, point.y, .065f, seed + 89120);
        float width = Mathf.Lerp(8f, 16f, patch);
        foreach (var crown in crowns)
        {
            var offset = crown.Root - point;
            float distance = offset.magnitude;
            float dripline = distance - crown.Radius;
            if (dripline >= Neighborhood) continue;
            float weight = 1f - Mathf.Clamp01(Mathf.Max(0f, dripline) / Neighborhood);
            weight *= weight;
            support += weight;
            interior += offset * (weight / Mathf.Max(distance, .5f));
            // Nearby upper crowns establish the edge. Low boughs and overlap
            // reduce demand for additional plants at the same point.
            edge = Mathf.Max(edge, (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, width, dripline))) *
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-crown.Radius, -1f, dripline)));
            float lowBoughs = 1f - Mathf.InverseLerp(2f, 6f, crown.Bottom);
            lowCover = Mathf.Max(lowCover, lowBoughs * (1f - Mathf.InverseLerp(-1.5f, 1.5f, dripline)));
        }
        float backing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.15f, 2.3f, support));
        float openness = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f, .78f, interior.magnitude / Mathf.Max(.001f, support)));
        return edge * backing * openness * (1f - lowCover * .88f) * Mathf.Lerp(.35f, 1f, patch);
    }
}
