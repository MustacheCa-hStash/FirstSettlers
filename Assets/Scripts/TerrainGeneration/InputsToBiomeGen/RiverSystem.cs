using UnityEngine;

public static class RiverGenerator
{
    private const float SiteCellSize = 1.8f;
    private const float SiteJitter = 0.6f;

    private const float RiverHalfWidth = 0.014f;
    private const float BankFalloffWidth = 0.025f;

    private const float WarpScale = 0.45f;
    private const float WarpStrength = 0.30f;

    private const float PairAdjacencyFadeWidth = 0.06f;

    public static float Sample(float sampleX, float sampleZ)
    {
        GetWarpedSample(sampleX, sampleZ, out float warpedX, out float warpedZ);

        int baseCellX = Mathf.FloorToInt(warpedX / SiteCellSize);
        int baseCellZ = Mathf.FloorToInt(warpedZ / SiteCellSize);

        Vector2[] sites = new Vector2[9];
        float[] distSq = new float[9];

        int siteCount = 0;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = baseCellX + dx;
                int cz = baseCellZ + dz;

                Vector2 site = GetSite(cx, cz);

                sites[siteCount] = site;

                float dxp = warpedX - site.x;
                float dzp = warpedZ - site.y;
                distSq[siteCount] = dxp * dxp + dzp * dzp;

                siteCount++;
            }
        }

        float riverMask = 0f;

        for (int i = 0; i < siteCount; i++)
        {
            for (int j = i + 1; j < siteCount; j++)
            {
                Vector2 a = sites[i];
                Vector2 b = sites[j];

                float siteDeltaX = b.x - a.x;
                float siteDeltaZ = b.y - a.y;
                float siteSeparation = Mathf.Sqrt(siteDeltaX * siteDeltaX + siteDeltaZ * siteDeltaZ);

                if (siteSeparation < 0.0001f)
                    continue;

                float pairNearness = Mathf.Max(distSq[i], distSq[j]);

                float closestThirdGap = float.MaxValue;

                for (int k = 0; k < siteCount; k++)
                {
                    if (k == i || k == j)
                        continue;

                    float thirdGap = distSq[k] - pairNearness;

                    if (thirdGap < closestThirdGap)
                        closestThirdGap = thirdGap;
                }

                float adjacencyGate = Mathf.InverseLerp(-PairAdjacencyFadeWidth, 0f, closestThirdGap);

                adjacencyGate = Mathf.Clamp01(adjacencyGate);
                adjacencyGate = adjacencyGate * adjacencyGate * (3f - 2f * adjacencyGate);
                adjacencyGate = adjacencyGate * adjacencyGate * (3f - 2f * adjacencyGate);

                if (adjacencyGate <= 0f)
                    continue;

                float borderDistance = Mathf.Abs(distSq[j] - distSq[i]) / (2f * siteSeparation);

                float edgeMask = 1f - Mathf.InverseLerp(
                    RiverHalfWidth,
                    RiverHalfWidth + BankFalloffWidth,
                    borderDistance);

                edgeMask = Mathf.Clamp01(edgeMask);
                edgeMask *= adjacencyGate;

                riverMask = Mathf.Max(riverMask, edgeMask);
            }
        }

        return Mathf.Clamp01(riverMask);
    }

    private static void GetWarpedSample(float sampleX, float sampleZ, out float warpedX, out float warpedZ)
    {
        float warpSampleX1 = sampleX * WarpScale + 17.13f;
        float warpSampleZ1 = sampleZ * WarpScale + 41.27f;

        float warpSampleX2 = sampleX * WarpScale + 73.91f;
        float warpSampleZ2 = sampleZ * WarpScale + 12.58f;

        float offsetX = (Mathf.PerlinNoise(warpSampleX1, warpSampleZ1) * 2f - 1f) * WarpStrength;
        float offsetZ = (Mathf.PerlinNoise(warpSampleX2, warpSampleZ2) * 2f - 1f) * WarpStrength;

        warpedX = sampleX + offsetX;
        warpedZ = sampleZ + offsetZ;
    }

    private static Vector2 GetSite(int cellX, int cellZ)
    {
        float jitterRange = SiteCellSize * 0.5f * SiteJitter;

        float ox = Hash01(cellX, cellZ, 0) * 2f - 1f;
        float oz = Hash01(cellX, cellZ, 1) * 2f - 1f;

        float sx = (cellX + 0.5f) * SiteCellSize + ox * jitterRange;
        float sz = (cellZ + 0.5f) * SiteCellSize + oz * jitterRange;

        return new Vector2(sx, sz);
    }

    private static float Hash01(int x, int z, int channel)
    {
        uint h = (uint)(x * 374761393 + z * 668265263 + channel * 2246822519);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return h / (float)uint.MaxValue;
    }
}