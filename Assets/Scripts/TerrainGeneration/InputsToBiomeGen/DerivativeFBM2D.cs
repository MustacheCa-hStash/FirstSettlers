using UnityEngine;

public static class DerivativeFbm2D
{
    public static NoiseSample2D Sample(
        float x,
        float z,
        int seed,
        int octaves,
        float persistence,
        float lacunarity,
        float erosionStrength,
        Vector2[] octaveOffsets)
    {
        float amplitude = 1f;
        float frequency = 1f;

        float totalValue = 0f;
        float totalDx = 0f;
        float totalDz = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float octaveX = (x + octaveOffsets[o].x) * frequency;
            float octaveZ = (z + octaveOffsets[o].y) * frequency;

            NoiseSample2D octaveSample = AnalyticValueNoise2D.Sample(octaveX, octaveZ, seed + o * 1619);

            float octaveDx = octaveSample.Dx * frequency;
            float octaveDz = octaveSample.Dz * frequency;

            float nextDx = totalDx + amplitude * octaveDx;
            float nextDz = totalDz + amplitude * octaveDz;

            float gradMagnitudeSq = nextDx * nextDx + nextDz * nextDz;
            float erosionWeight = 1f / (1f + erosionStrength * gradMagnitudeSq);

            totalValue += amplitude * octaveSample.Value * erosionWeight;
            totalDx = nextDx;
            totalDz = nextDz;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return new NoiseSample2D(totalValue, totalDx, totalDz);
    }
}