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
    private readonly bool packed;
    public float Strength { get; }

    internal float Group(Vector2 point) =>
        TreeStandField.Noise(point.x, point.y, .09f, seed + 89301);

    internal WoodlandEdgeField(List<Crown> crowns, int seed, float strength, bool packed)
    {
        this.crowns = crowns.ToArray();
        this.seed = seed;
        this.packed = packed;
        Strength = strength;
    }

    public float Gap(Vector2 point, bool treeBand = false)
    {
        float support = 0f, edge = 0f, lowCover = 0f;
        Vector2 interior = Vector2.zero;
        float patch = TreeStandField.Noise(point.x, point.y, .065f, seed + 89120);
        float width = packed
            ? treeBand ? Mathf.Lerp(4f, 20f, Group(point)) : Mathf.Lerp(10f, 16f, patch)
            : Mathf.Lerp(8f, 16f, patch);
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
        if (packed)
        {
            // Favor the open-facing side even where the backing trees form a
            // thin line on a slope rather than a deep stand.
            backing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.85f, 1.8f, support));
            openness = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.20f, .65f, interior.magnitude / Mathf.Max(.001f, support)));
            return edge * backing * openness * (1f - lowCover * .35f) * Mathf.Lerp(.80f, 1f, patch);
        }
        return edge * backing * openness * (1f - lowCover * .88f) * Mathf.Lerp(.35f, 1f, patch);
    }
}
