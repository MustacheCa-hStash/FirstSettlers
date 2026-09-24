using UnityEngine;

[System.Serializable]
public class FlowerSettings
{
    public bool enableFlowers = true;
    public GameObject flowerPrefab;

    [Header("Grassland Tall Flower")]
    [Tooltip("Optional second flower mesh/material for the grassland mini-patches.")]
    public GameObject tallFlowerPrefab;
    [Range(0.5f, 2.5f), Tooltip("Uniform scale multiplier for every tall flower, applied on top of the existing flower scale range.")]
    public float tallFlowerUniformScale = 1f;

    [Header("Grassland Daisy Weed Patches")]
    [Tooltip("Optional daisy-weed model. When assigned, it spawns in independent grassland patches.")]
    public GameObject daisyWeedPrefab;
    [Min(1f)] public float daisyWeedPatchCellSize = 36f;
    [Range(0f, 1f)] public float daisyWeedPatchSpawnChance = 0.28f;
    public float daisyWeedPatchNoiseScale = 0.026f;
    [Range(0f, 1f)] public float daisyWeedPatchNoiseThreshold = 0.54f;
    [Min(1)] public int minDaisyWeedsPerPatch = 8;
    [Min(1)] public int maxDaisyWeedsPerPatch = 16;
    public Vector2 daisyWeedPatchRadiusRange = new Vector2(3.5f, 7f);
    public int daisyWeedSeedOffset = 56000;
    [Range(0.5f, 2.5f)] public float daisyWeedUniformScale = 1f;
    [Range(0f, 90f)] public float daisyWeedMaxSlope = 40f;
    public float daisyWeedTreeExclusionRadius = 1f;

    [Header("Lupine Small Patches")]
    [Min(1f)] public float tallFlowerPatchCellSize = 54f;
    [Range(0f, 1f)] public float tallFlowerPatchSpawnChance = 0.18f;
    public float tallFlowerPatchNoiseScale = 0.026f;
    [Range(0f, 1f)] public float tallFlowerPatchNoiseThreshold = 0.52f;
    [Min(1)] public int minTallFlowersPerPatch = 10;
    [Min(1)] public int maxTallFlowersPerPatch = 18;
    public Vector2 tallFlowerPatchRadiusRange = new Vector2(4.5f, 7f);
    [Range(0f, 0.5f), Tooltip("Breaks up the circular outline of each lupine patch.")]
    public float tallFlowerEdgeIrregularity = 0.25f;
    [Range(0f, 1f), Tooltip("Chance to leave grassy gaps within a lupine patch.")]
    public float tallFlowerGapStrength = 0.35f;
    [Range(0f, 1f), Tooltip("Chance for a patch to grow a few small groups beyond its main drift.")]
    public float tallFlowerSatelliteChance = 0.55f;
    public int tallFlowerSeedOffset = 48000;

    [Header("Lupine Meadows")]
    [Min(1f), Tooltip("Spacing between candidates for rare, large Lupine drifts.")]
    public float tallFlowerMeadowCellSize = 80f;
    [Range(0f, 1f)] public float tallFlowerMeadowSpawnChance = 0.25f;
    public float tallFlowerMeadowNoiseScale = 0.018f;
    [Range(0f, 1f)] public float tallFlowerMeadowNoiseThreshold = 0.52f;
    [Min(1)] public int minTallFlowersPerMeadow = 150;
    [Min(1)] public int maxTallFlowersPerMeadow = 250;
    public Vector2 tallFlowerMeadowRadiusRange = new Vector2(18f, 28f);
    public int tallFlowerMeadowSeedOffset = 64000;

    [Tooltip("Subtle per-instance tint variation. The mesh red vertex channel, or red mask texture when vertex colors are absent, controls the petal shade.")]
    public Color tallFlowerDarkVariant = new Color(0.88f, 0.84f, 0.98f, 1f);
    public Color tallFlowerLightVariant = new Color(1.0f, 0.90f, 0.96f, 1f);

    [Header("Render Range")]
    [Tooltip("Circular radius in whole chunks around the player chunk; diagonal chunks outside the radius are excluded.")]
    public int activeRingRadius = 2;

    [Header("Patch Placement")]
    public float patchCellSize = 16f;
    [Min(1)] public int maxPatchCentersPerCell = 2;
    [Range(0f, 1f)] public float patchSpawnChance = 0.6f;
    public float patchNoiseScale = 0.035f;
    [Range(0f, 1f)] public float patchNoiseThreshold = 0.52f;
    [Min(1)] public int minFlowersPerPatch = 6;
    [Min(1)] public int maxFlowersPerPatch = 18;
    public Vector2 patchRadiusRange = new Vector2(1.5f, 4f);

    [Header("Per Flower Variation")]
    public Vector2 uniformScaleRange = new Vector2(0.85f, 1.15f);
    public bool randomizeYaw = true;
    public int seedOffset = 18000;

    [Header("Surface Filters")]
    public BiomeType[] allowedBiomes = new[]
    {
        BiomeType.Grassland,
        BiomeType.Forest,
        BiomeType.Tundra,
        BiomeType.Taiga
    };

    [Range(0f, 90f), Tooltip("Maximum terrain slope in degrees.")]
    public float maxSlope = 40f;
    public float treeExclusionRadius = 1.0f;

    [Header("Petal Color")]
    public string flowerPetalColorPropertyName = "_FlowerPetalColor";
    public float petalColorVariation = 0.08f;
    public Color fallbackPetalColor = new Color(1.0f, 0.78f, 0.92f, 1.0f);
    public FlowerBiomePetalPalette[] biomePetalPalettes = new[]
    {
        new FlowerBiomePetalPalette(
            BiomeType.Grassland,
            new[]
            {
                new Color(1.0f, 0.72f, 0.88f, 1.0f),
                new Color(1.0f, 0.92f, 0.45f, 1.0f),
                new Color(0.75f, 0.84f, 1.0f, 1.0f)
            }),
        new FlowerBiomePetalPalette(
            BiomeType.Forest,
            new[]
            {
                new Color(0.88f, 0.72f, 1.0f, 1.0f),
                new Color(1.0f, 0.82f, 0.55f, 1.0f)
            }),
        new FlowerBiomePetalPalette(
            BiomeType.Tundra,
            new[]
            {
                new Color(0.82f, 0.88f, 1.0f, 1.0f),
                new Color(0.95f, 0.95f, 1.0f, 1.0f)
            }),
        new FlowerBiomePetalPalette(
            BiomeType.Taiga,
            new[]
            {
                new Color(0.72f, 0.82f, 1.0f, 1.0f),
                new Color(0.9f, 0.72f, 1.0f, 1.0f)
            })
    };

    public bool AllowsBiome(BiomeType biome)
    {
        if (allowedBiomes == null || allowedBiomes.Length == 0)
            return true;

        for (int i = 0; i < allowedBiomes.Length; i++)
        {
            if (allowedBiomes[i] == biome)
                return true;
        }

        return false;
    }

    public Color GetBasePetalColor(BiomeType biome, float selector)
    {
        Color[] colors = null;

        if (biomePetalPalettes != null)
        {
            for (int i = 0; i < biomePetalPalettes.Length; i++)
            {
                if (biomePetalPalettes[i].biome == biome)
                {
                    colors = biomePetalPalettes[i].petalColors;
                    break;
                }
            }
        }

        if (colors == null || colors.Length == 0)
            return fallbackPetalColor;

        int index = Mathf.Clamp(
            Mathf.FloorToInt(Mathf.Clamp01(selector) * colors.Length),
            0,
            colors.Length - 1);

        return colors[index];
    }
}
