using UnityEngine;

[System.Serializable]
public struct FlowerBiomePetalPalette
{
    public BiomeType biome;
    public Color[] petalColors;

    public FlowerBiomePetalPalette(BiomeType biome, Color[] petalColors)
    {
        this.biome = biome;
        this.petalColors = petalColors;
    }
}
