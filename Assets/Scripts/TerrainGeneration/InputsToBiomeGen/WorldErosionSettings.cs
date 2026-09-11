using System;
using Unity.Mathematics;
using UnityEngine;

public enum ErosionFadeTarget { LocalRelief, Altitude }

[Serializable]
public struct WorldErosionSettings
{
    [SerializeField, HideInInspector] private int version;
    [Header("Base landforms")]
    [Tooltip("Normalized height units. World Y = height * Mesh Height Multiplier * World Scale.")]
    public float baseElevation;
    [Min(0f)] public float lowlandRelief;
    [Min(0f)] public float mountainRelief;
    [Range(1, 4)] public int baseOctaves;
    [Range(0f, 0.7f)] public float baseRoughness;
    [Range(0.5f, 3f)] public float mountainShape;
    [Tooltip("Optional existing river-channel carving, applied AFTER world erosion.")]
    public bool carveRivers;
    [Tooltip("Width of broad, dry river valleys relative to the existing drainage field. Channel paths stay fixed.")]
    [Range(0.25f, 3f)] public float riverValleyWidth;
    [Tooltip("Blend toward a flat dry valley floor alongside river channels. 1 retains the original flat-floor behavior; 0 keeps eroded slopes.")]
    [Range(0f, 1f)] public float riverValleyFlattening;
    [Header("Playable landforms")]
    [Tooltip("Lowland height relative to water; 0.5 halves relief without moving shorelines. Fades out on mountains.")]
    [Range(0.1f, 1f)] public float lowlandHeightRatio;
    [Tooltip("Normalized height band around water where slopes soften.")]
    [Min(0.001f)] public float shoreHeightBand;
    [Range(0.05f, 1f)] public float shoreSlopeRatio;
    [Tooltip("XZ scale multiplier for mountain shapes and regions. Larger values make broader, less frequent mountains without reducing relief.")]
    [Range(1f, 6f)] public float mountainSpatialScale;
    [Tooltip("Raises the mountain region threshold, leaving more open lowlands. Does not lower the maximum mountain mask.")]
    [Range(0f, 0.3f)] public float mountainSparsity;
    [Tooltip("Erosion retained on gentle mountain slopes; steep faces retain full erosion.")]
    [Range(0f, 1f)] public float gentleMountainErosion;
    [Tooltip("Occasional extra lowland hill height relative to the usual ratio. Zero gives uniform relief.")]
    [Range(0f, 2f)] public float lowlandHillVariation;
    [Min(32f)] public float lowlandHillScale;
    [Header("World erosion")]
    public bool enabled;
    [Tooltip("Maximum accumulated displacement in normalized height units; zero bypasses erosion. Applies to lowlands, mountains and seabed.")]
    [Min(0f)] public float amplitude;
    [Tooltip("Largest gully wavelength in terrain XZ units, independent of Sample Scale. Multiply by World Scale for world units.")]
    [Min(1f)] public float wavelength;
    [Range(1, 8)] public int octaves;
    [Range(1.2f, 3f)] public float lacunarity;
    [Range(0f, 0.9f)] public float persistence;
    [Tooltip("Globally excludes erosion octaves finer than this terrain-space wavelength. Independent of chunk LOD.")]
    [Min(4f)] public float minimumWavelength;
    [Header("Spatial distribution")]
    [Tooltip("Stretch gullies along the two rotated domain axes. 1,1 is isotropic.")]
    public Vector2 stretch;
    [Range(-180f, 180f)] public float rotation;
    [Tooltip("Translation in terrain XZ units; deterministic across all chunks.")]
    public Vector2 offset;
    public int seedOffset;
    [Header("Gullies and ridges")]
    [Range(0f, 1f)] public float gullyWeight;
    [Range(0.1f, 3f)] public float branching;
    [Range(0.25f, 1f)] public float cellSize;
    [Range(0f, 0.95f)] public float normalization;
    [Range(0f, 1f)] public float ridgeRounding;
    [Range(0f, 1f)] public float valleyRounding;
    [Tooltip("Smooths the internal sign-like gully direction change at crests. Prevents tiny slope changes from flipping subsequent octaves abruptly.")]
    [Range(0.01f, 0.5f)] public float directionSmoothing;
    [Tooltip("Controls how quickly erosion appears as the input slope increases.")]
    [Range(0.1f, 8f)] public float slopeResponse;
    [Tooltip("Height units used to normalize slopes inside the filter; also controls branching relative to base relief.")]
    [Min(0.1f)] public float heightScale;
    [Header("Peak / valley shaping")]
    public ErosionFadeTarget fadeTarget;
    [Tooltip("Neighborhood radius in terrain units for the local-relief peak/valley target.")]
    [Min(1f)] public float reliefRadius;
    [Tooltip("Normalized height difference from neighborhood mean needed for a strong peak/valley target.")]
    [Min(0.001f)] public float reliefContrast;
    [Tooltip("Used only in Altitude mode, normalized terrain heights.")]
    public Vector2 valleyAndPeakHeights;
    [Header("Geometry fidelity")]
    [Tooltip("Maximum spacing of terrain vertices in terrain units. Smaller values improve erosion silhouettes but increase near/far mesh cost. Applied on regeneration.")]
    [Range(2, 32)] public int maxMeshSpacing;

    public static WorldErosionSettings Default => new WorldErosionSettings {
        version = 4, baseElevation = 0.45f, lowlandRelief = 0.8f, mountainRelief = 14f,
        lowlandHeightRatio = 0.5f, shoreHeightBand = 0.25f, shoreSlopeRatio = 0.2f,
        mountainSpatialScale = 2.4f, mountainSparsity = 0.12f, gentleMountainErosion = 0.45f,
        lowlandHillVariation = 0.8f, lowlandHillScale = 900f,
        baseOctaves = 2, baseRoughness = 0.3f, mountainShape = 1.5f, carveRivers = true,
        riverValleyWidth = 1.4f, riverValleyFlattening = 1f,
        enabled = true, amplitude = 0.6f, wavelength = 512f, octaves = 4, lacunarity = 2f,
        persistence = 0.5f, minimumWavelength = 32f, stretch = Vector2.one, seedOffset = 27183,
        gullyWeight = 0.7f, branching = 1.3f, cellSize = 0.7f, normalization = 0.5f,
        ridgeRounding = 0.2f, valleyRounding = 0.25f, directionSmoothing = 0.15f,
        slopeResponse = 2.8f, heightScale = 4f, fadeTarget = ErosionFadeTarget.LocalRelief,
        reliefRadius = 256f, reliefContrast = 0.15f, valleyAndPeakHeights = new Vector2(0f, 8f), maxMeshSpacing = 16
    };
    private static float Safe(float v, float fallback, float lo, float hi) => math.clamp(math.isfinite(v) ? v : fallback, lo, hi);
    public WorldErosionSettings Sanitized()
    {
        if (version == 0) return Default;
        var s = this;
        if (s.version < 2)
        {
            s.riverValleyWidth = 1.4f;
            s.riverValleyFlattening = 1f;
            s.version = 2;
        }
        if (s.version < 3)
        {
            var d = Default;
            s.lowlandHeightRatio = d.lowlandHeightRatio; s.shoreHeightBand = d.shoreHeightBand;
            s.shoreSlopeRatio = d.shoreSlopeRatio; s.version = 3;
        }
        s.lowlandHeightRatio = Safe(s.lowlandHeightRatio, 0.5f, 0.1f, 1f);
        s.shoreHeightBand = Safe(s.shoreHeightBand, 0.25f, 0.001f, 10f);
        s.shoreSlopeRatio = Safe(s.shoreSlopeRatio, 0.2f, 0.05f, 1f);
        if (s.version < 4)
        {
            var d = Default;
            s.mountainSpatialScale = d.mountainSpatialScale; s.mountainSparsity = d.mountainSparsity;
            s.gentleMountainErosion = d.gentleMountainErosion;
            s.lowlandHillVariation = d.lowlandHillVariation; s.lowlandHillScale = d.lowlandHillScale;
            s.version = 4;
        }
        s.mountainSpatialScale = Safe(s.mountainSpatialScale, 2.4f, 1f, 6f);
        s.mountainSparsity = Safe(s.mountainSparsity, 0.12f, 0f, 0.3f);
        s.gentleMountainErosion = Safe(s.gentleMountainErosion, 0.45f, 0f, 1f);
        s.lowlandHillVariation = Safe(s.lowlandHillVariation, 0.8f, 0f, 2f);
        s.lowlandHillScale = Safe(s.lowlandHillScale, 900f, 32f, 100000f);
        s.baseElevation = Safe(s.baseElevation, 0.45f, -100f, 100f);
        s.lowlandRelief = Safe(s.lowlandRelief, 0.8f, 0f, 100f);
        s.mountainRelief = Safe(s.mountainRelief, 14f, 0f, 100f);
        s.baseOctaves = math.clamp(s.baseOctaves, 1, 4); s.baseRoughness = Safe(s.baseRoughness, 0.3f, 0f, 0.7f);
        s.mountainShape = Safe(s.mountainShape, 1.5f, 0.5f, 3f);
        s.riverValleyWidth = Safe(s.riverValleyWidth, 1.4f, 0.25f, 3f);
        s.riverValleyFlattening = Safe(s.riverValleyFlattening, 1f, 0f, 1f);
        s.amplitude = Safe(s.amplitude, 0.6f, 0f, 20f); s.wavelength = Safe(s.wavelength, 512f, 4f, 100000f);
        s.octaves = math.clamp(s.octaves, 1, 8); s.lacunarity = Safe(s.lacunarity, 2f, 1.2f, 3f);
        s.persistence = Safe(s.persistence, 0.5f, 0f, 0.9f); s.minimumWavelength = Safe(s.minimumWavelength, 32f, 4f, 100000f);
        s.stretch.x = Safe(s.stretch.x, 1f, 0.1f, 10f); s.stretch.y = Safe(s.stretch.y, 1f, 0.1f, 10f);
        s.rotation = Safe(s.rotation, 0f, -180f, 180f); s.offset.x = Safe(s.offset.x, 0f, -1000000f, 1000000f); s.offset.y = Safe(s.offset.y, 0f, -1000000f, 1000000f);
        s.gullyWeight = Safe(s.gullyWeight, 0.7f, 0f, 1f); s.branching = Safe(s.branching, 1.3f, 0.1f, 3f);
        s.cellSize = Safe(s.cellSize, 0.7f, 0.25f, 1f); s.normalization = Safe(s.normalization, 0.5f, 0f, 0.95f);
        s.ridgeRounding = Safe(s.ridgeRounding, 0.2f, 0f, 1f); s.valleyRounding = Safe(s.valleyRounding, 0.25f, 0f, 1f);
        s.directionSmoothing = Safe(s.directionSmoothing, 0.15f, 0.01f, 0.5f); s.slopeResponse = Safe(s.slopeResponse, 2.8f, 0.1f, 8f);
        s.heightScale = Safe(s.heightScale, 4f, 0.1f, 100f); s.reliefRadius = Safe(s.reliefRadius, 256f, 1f, 100000f);
        s.reliefContrast = Safe(s.reliefContrast, 0.15f, 0.001f, 100f);
        s.valleyAndPeakHeights.x = Safe(s.valleyAndPeakHeights.x, 0f, -100f, 100f);
        s.valleyAndPeakHeights.y = Safe(s.valleyAndPeakHeights.y, 8f, s.valleyAndPeakHeights.x + 0.01f, 200f);
        s.maxMeshSpacing = math.clamp(s.maxMeshSpacing, 2, 32);
        return s;
    }
}
