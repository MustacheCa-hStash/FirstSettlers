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
    [Header("Cold valleys and glaciers")]
    public bool glaciers;
    [Range(0f, 1f)] public float glacialStrength;
    [Tooltip("Small cooling of already cool regions; dry desert cores are excluded.")]
    [Range(0f, 0.08f)] public float coldExpansion;
    [Tooltip("Local rock-wall rise above cold valley floors, in height units (60m per unit at scene scale).")]
    [Range(0.3f, 2.5f)] public float glacialRelief;
    [Tooltip("Solid glacier-front height in normalized height units; independent of floor smoothing.")]
    [Range(0.15f, 1.2f)] public float iceFrontHeight;
    [Tooltip("Rock face transition width in terrain units. Ice faces use 45% of this width; both resolve over several near-mesh cells.")]
    [Range(20f, 96f)] public float glacialWallWidth;

    public static WorldLandformSettings Default => new WorldLandformSettings
    {
        version = 4, enabled = true, dryDeserts = true, desertRegionScale = 18f,
        plateauRelief = 1.8f, plateauScale = 1.4f, cliffWidth = 0.10f,
        duneHeight = 0.12f, duneWavelength = 200f, dryWashDepth = 0.18f,
        glaciers = true, glacialStrength = 1f, coldExpansion = 0.025f,
        glacialRelief = 1.2f, iceFrontHeight = 0.65f, glacialWallWidth = 44f
    };

    private static float Safe(float v, float fallback, float low, float high) =>
        math.clamp(math.isfinite(v) ? v : fallback, low, high);

    public WorldLandformSettings Sanitized()
    {
        if (version == 0) return Default;
        var s = this;
        if (s.version < 2) { s.dryDeserts = true; s.version = 2; }
        if (s.version < 3) { s.glaciers = true; s.glacialStrength = 1f; s.coldExpansion = 0.025f; s.version = 3; }
        if (s.version < 4) { s.glacialRelief=1.2f; s.iceFrontHeight=.65f; s.glacialWallWidth=44f; s.version=4; }
        s.glacialRelief = Safe(s.glacialRelief,1.2f,.3f,2.5f);
        s.iceFrontHeight = Safe(s.iceFrontHeight,.65f,.15f,1.2f);
        s.glacialWallWidth = Safe(s.glacialWallWidth,44f,20f,96f);
        s.glacialStrength = Safe(s.glacialStrength, 1f, 0f, 1f);
        s.coldExpansion = Safe(s.coldExpansion, 0.025f, 0f, 0.08f);
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
