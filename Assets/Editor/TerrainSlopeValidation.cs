using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
public static class TerrainSlopeValidation
{
#if UNITY_EDITOR
    [MenuItem("Tools/Validation/Terrain Slope Policy")]
#endif
    public static void Validate()
    {
        Check(Math.Abs(TerrainSlopePolicy.FromGradient(0.005f, 200f) - 45f) < 0.001f, "45 degree conversion");
        Check(Math.Abs(TerrainSlopePolicy.FromGradient(0.01f, 200f) - 63.43495f) < 0.001f, "Mountain grass reference");
        Check(Math.Abs(TerrainSlopePolicy.FromGradient(0.01f, 100f) - 45f) < 0.001f, "Height multiplier propagation");
        Check(TerrainSlopePolicy.FromGradient(0f, 200f) == 0f, "Flat terrain");
        Check(B(20f, 0f, 0.8f) == BiomeType.Forest, "Gentle wet forest");
        Check(B(45f, 0f, 0.8f) == BiomeType.Grassland, "Forest cutoff");
        Check(B(63f, 0.8f) == BiomeType.Grassland, "Mountain meadow limit");
        Check(B(63.1f, 0.8f) == BiomeType.Rock, "Mountain meadow exclusion");
        Check(B(20f, 0.8f, 0.5f, 0.2f) == BiomeType.Snow, "Cold mountain protection");
        Check(B(20f, 0.8f, 0.5f, 0.5f, 4f) == BiomeType.Rock, "Alpine protection");
        Check(B(70f, 0f, 0.8f, 0.2f) == BiomeType.Rock, "Taiga cannot bypass cliffs");
        Check(B(20f, 0.8f, 0.5f, 0.5f, 0.1f) == BiomeType.Water, "Water protection");
#if UNITY_EDITOR
        UnityEngine.Debug.Log("Terrain slope policy: 12 checks passed.");
#endif
    }
    private static BiomeType B(float slope, float mountain, float moisture = 0.5f, float temperature = 0.5f, float height = 1f)
        => TerrainSlopePolicy.ClassifyBiome(height, moisture, temperature, slope, mountain, 0f, 0.24f);
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
