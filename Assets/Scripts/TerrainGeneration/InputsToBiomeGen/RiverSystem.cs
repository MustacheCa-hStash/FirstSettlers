using UnityEngine;

public static class RiverGenerator
{
    private const float SiteCellSize = 1.8f;
    private const float SiteJitter = 0.85f;

    private const float RiverHalfWidth = 0.005f;
    private const float BankFalloffWidth = 0.01f;

    public static float Sample(float sampleX, float sampleZ)
    {
        int baseCellX = Mathf.FloorToInt(sampleX / SiteCellSize);
        int baseCellZ = Mathf.FloorToInt(sampleZ / SiteCellSize);

        float nearestDistSq = float.MaxValue;
        float secondDistSq = float.MaxValue;

        Vector2 nearestSite = Vector2.zero;
        Vector2 secondSite = Vector2.zero;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = baseCellX + dx;
                int cz = baseCellZ + dz;

                Vector2 site = GetSite(cx, cz);

                float dxp = sampleX - site.x;
                float dzp = sampleZ - site.y;
                float distSq = dxp * dxp + dzp * dzp;

                if (distSq < nearestDistSq)
                {
                    secondDistSq = nearestDistSq;
                    secondSite = nearestSite;

                    nearestDistSq = distSq;
                    nearestSite = site;
                }
                else if (distSq < secondDistSq)
                {
                    secondDistSq = distSq;
                    secondSite = site;
                }
            }
        }

        float siteDeltaX = secondSite.x - nearestSite.x;
        float siteDeltaZ = secondSite.y - nearestSite.y;
        float siteSeparation = Mathf.Sqrt(siteDeltaX * siteDeltaX + siteDeltaZ * siteDeltaZ);

        if (siteSeparation < 0.0001f)
            return 0f;

        float borderDistance = Mathf.Abs(secondDistSq - nearestDistSq) / (2f * siteSeparation);

        float riverMask = 1f - Mathf.InverseLerp(
            RiverHalfWidth,
            RiverHalfWidth + BankFalloffWidth,
            borderDistance);

        return Mathf.Clamp01(riverMask);
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