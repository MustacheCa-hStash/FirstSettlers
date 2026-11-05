using System;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct WorldLandformSettings
{
    [SerializeField, HideInInspector] private int version;
    public bool enabled;
    [Tooltip("Keep desert provinces above the water plane and replace wet channels with dry drainage. Restart or regenerate terrain.")]
    public bool dryDeserts;
    [Tooltip("Broad connected hot/dry provinces, relative to Sample Scale. Shared by early landforms and final climate.")]
    [Range(8f, 40f)] public float desertRegionScale;
    [Tooltip("Height of sedimentary plateaus in normalized height units.")]
    [Range(0f, 5f)] public float plateauRelief;
    [Tooltip("Plateau and escarpment width relative to Sample Scale.")]
    [Range(0.5f, 4f)] public float plateauScale;
    [Tooltip("Width of the cliff transition in the plateau field. Smaller values make steeper edges.")]
    [Range(0.04f, 0.2f)] public float cliffWidth;
    [Tooltip("Maximum accumulated dune height in normalized height units. Wind-aligned dunes only form on suitable dry ground.")]
    [Range(0f, 0.3f)] public float duneHeight;
    [Tooltip("Dune spacing in terrain XZ units, independent of render LOD.")]
    [Range(96f, 512f)] public float duneWavelength;
    [Range(0f, 0.5f)] public float dryWashDepth;

    public static WorldLandformSettings Default => new WorldLandformSettings
    {
        version = 2, enabled = true, dryDeserts = true, desertRegionScale = 18f,
        plateauRelief = 1.8f, plateauScale = 1.4f, cliffWidth = 0.10f,
        duneHeight = 0.12f, duneWavelength = 200f, dryWashDepth = 0.18f
    };

    private static float Safe(float v, float fallback, float low, float high) =>
        math.clamp(math.isfinite(v) ? v : fallback, low, high);

    public WorldLandformSettings Sanitized()
    {
        if (version == 0) return Default;
        var s = this;
        if (s.version < 2) { s.dryDeserts = true; s.version = 2; }
        s.desertRegionScale = Safe(s.desertRegionScale, 18f, 8f, 40f);
        s.plateauRelief = Safe(s.plateauRelief, 1.8f, 0f, 5f);
        s.plateauScale = Safe(s.plateauScale, 1.4f, 0.5f, 4f);
        s.cliffWidth = Safe(s.cliffWidth, 0.10f, 0.04f, 0.2f);
        s.duneHeight = Safe(s.duneHeight, 0.12f, 0f, 0.3f);
        s.duneWavelength = Safe(s.duneWavelength, 200f, 96f, 512f);
        s.dryWashDepth = Safe(s.dryWashDepth, 0.18f, 0f, 0.5f);
        return s;
    }
}
