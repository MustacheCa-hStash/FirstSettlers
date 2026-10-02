using UnityEngine;

[System.Serializable]
public sealed class TerrainHorizonShadowSettings
{
    public bool enabled = true;
    [Tooltip("World-space obstruction search range, independent of URP shadow distance.")]
    [Min(100f)] public float searchDistance = 6000f;
    [Range(0f, 1f)] public float strength = 0.8f;
    [Tooltip("Color multiplier inside procedural terrain shadows, including their ambient illumination.")]
    [ColorUsage(false)] public Color shadowTint = new Color(0.45f, 0.52f, 0.65f, 1f);
    [Tooltip("Additional shadow darkening/tint. 0 preserves the original ambient lighting.")]
    [Range(0f, 1f)] public float tintStrength = 0.5f;
    [Tooltip("Angular transition width. Coarse mountain shadows benefit from a soft edge.")]
    [Range(0.5f, 10f)] public float softnessDegrees = 3f;
    [Tooltip("Raise receivers slightly to suppress small-scale self-shadowing.")]
    [Min(0f)] public float heightBias = 1.5f;
    [Range(16, 48)] public int searchSteps = 32;
    [Tooltip("Shared CPU height cache; missing heights are sampled once on the shadow worker.")]
    [Range(8192, 262144)] public int cachedHeightSamples = 65536;
    [Tooltip("Retain this many unbound shadow tiles for revisits, in addition to currently bound tiles.")]
    [Range(0, 512)] public int cachedInactiveTiles = 128;
}
