using UnityEngine;

public static class AnalyticValueNoise2D
{
    public static NoiseSample2D Sample(float x, float z, int seed)
    {
        int ix = Mathf.FloorToInt(x);
        int iz = Mathf.FloorToInt(z);

        float fx = x - ix;
        float fz = z - iz;

        float u = Quintic(fx);
        float v = Quintic(fz);

        float du = QuinticDerivative(fx);
        float dv = QuinticDerivative(fz);

        float a = HashToSignedValue(ix, iz, seed);
        float b = HashToSignedValue(ix + 1, iz, seed);
        float c = HashToSignedValue(ix, iz + 1, seed);
        float d = HashToSignedValue(ix + 1, iz + 1, seed);

        float k0 = a;
        float k1 = b - a;
        float k2 = c - a;
        float k3 = a - b - c + d;

        float value = k0 + k1 * u + k2 * v + k3 * u * v;
        float dx = (k1 + k3 * v) * du;
        float dz = (k2 + k3 * u) * dv;

        return new NoiseSample2D(value, dx, dz);
    }

    private static float Quintic(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private static float QuinticDerivative(float t)
    {
        float oneMinusT = t - 1f;
        return 30f * t * t * oneMinusT * oneMinusT;
    }

    private static float HashToSignedValue(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= 374761393u * (uint)x;
            h ^= 668265263u * (uint)z;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;

            float value01 = (h & 0x00FFFFFFu) / 16777215f;
            return value01 * 2f - 1f;
        }
    }
}