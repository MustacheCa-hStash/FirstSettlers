using UnityEngine;

[System.Serializable]
public class FlowerSettings
{
    public bool enableFlowers = true;
    public GameObject flowerPrefab;

    [Header("Render Range")]
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

    public float maxSlope = 0.08f;
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
