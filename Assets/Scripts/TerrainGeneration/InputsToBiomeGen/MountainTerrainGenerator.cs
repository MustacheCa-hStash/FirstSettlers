using UnityEngine;

public static class MountainTerrainGenerator
{
    public static float Sample(
        float sampleX,
        float sampleZ,
        int seed,
        out float synthesisDx,
        out float synthesisDz)
    {
        synthesisDx = 0f;
        synthesisDz = 0f;

        float normalized = Mathf.PerlinNoise(sampleX, sampleZ);
        normalized = Mathf.Pow(normalized, 1.75f);

        return normalized;
    }
}