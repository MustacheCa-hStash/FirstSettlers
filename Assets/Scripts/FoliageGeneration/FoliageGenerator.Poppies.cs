using System.Collections.Generic;
using UnityEngine;

public static partial class FoliageGenerator
{
    // World-cell hashes and half-open chunk ownership let drifts cross positive/negative chunk seams.
    // One cell or 128 candidates per slice keeps this optional flower within discovery's shared budget.
    private static IEnumerator<bool> GenerateMeadowFlowerPatchesIncrementally(ChunkRecord record, MeadowFlowerPatchSettings settings,
        int worldSeed, int chunkSize, float worldScale, float meshHeightMultiplier,
        PlannedPlantClearanceIndex blockers, PlantClearancePolicy clearance, bool isCornflower)
    {
        float cellSize = Mathf.Max(1f, settings.patchCellSize);
        float minRadius = Mathf.Max(0f, Mathf.Min(settings.patchRadiusRange.x, settings.patchRadiusRange.y));
        float maxRadius = Mathf.Max(minRadius, Mathf.Max(settings.patchRadiusRange.x, settings.patchRadiusRange.y));
        float irregularity = Mathf.Clamp(settings.edgeIrregularity, 0f, 0.5f);
        float reach = maxRadius * (1f + irregularity);
        float minX = record.ChunkCoord.x * chunkSize, minZ = record.ChunkCoord.z * chunkSize;
        float maxX = minX + chunkSize, maxZ = minZ + chunkSize;
        int minCellX = Mathf.FloorToInt((minX - reach) / cellSize), maxCellX = Mathf.FloorToInt((maxX + reach) / cellSize);
        int minCellZ = Mathf.FloorToInt((minZ - reach) / cellSize), maxCellZ = Mathf.FloorToInt((maxZ + reach) / cellSize);
        int minCount = Mathf.Max(1, settings.minPlantsPerPatch), maxCount = Mathf.Max(minCount, settings.maxPlantsPerPatch);
        float scaleMin = Mathf.Max(.01f, Mathf.Min(settings.uniformScaleRange.x, settings.uniformScaleRange.y));
        float scaleMax = Mathf.Max(scaleMin, Mathf.Max(settings.uniformScaleRange.x, settings.uniformScaleRange.y));
        float noiseScale = Mathf.Max(.0001f, settings.patchNoiseScale), threshold = Mathf.Clamp01(settings.patchNoiseThreshold);
        float spawnChance = Mathf.Clamp01(settings.patchSpawnChance);
        if (spawnChance <= 0f || maxRadius <= 0f) yield break;

        for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
        for (int cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            yield return true;
            int patchHash = Hash(worldSeed, settings.seedOffset, cellX, cellZ, 1877);
            float centerX = (cellX + Hash01(patchHash + 31)) * cellSize;
            float centerZ = (cellZ + Hash01(patchHash + 67)) * cellSize;
            float noise = Mathf.PerlinNoise(centerX * noiseScale + settings.seedOffset * .001f,
                centerZ * noiseScale - settings.seedOffset * .0017f);
            if (noise < threshold) continue;
            float strength = threshold >= .999f ? 1f : Mathf.InverseLerp(threshold, 1f, noise);
            if (Hash01(patchHash + 105) >= spawnChance * Mathf.Lerp(.55f, 1f, strength)) continue;
            float radius = Mathf.Lerp(minRadius, maxRadius, Hash01(patchHash + 139));
            float patchReach = radius * (1f + irregularity);
            if (centerX + patchReach < minX || centerX - patchReach >= maxX ||
                centerZ + patchReach < minZ || centerZ - patchReach >= maxZ) continue;

            int count = GetDeterministicCount(minCount, maxCount, patchHash + 173);
            for (int plant = 0; plant < count; plant++)
            {
                if (plant > 0 && (plant & 127) == 0) yield return true;
                int hash = Hash(worldSeed, settings.seedOffset, cellX, cellZ, plant, 1993);
                float angle = Hash01(hash + 19) * Mathf.PI * 2f;
                float fraction = Mathf.Sqrt(Hash01(hash + 41));
                float distance = fraction * GetTallFlowerEdgeRadius(radius, angle, patchHash, irregularity);
                float x = centerX + Mathf.Cos(angle) * distance, z = centerZ + Mathf.Sin(angle) * distance;
                if (x < minX || x >= maxX || z < minZ || z >= maxZ) continue;
                float edgeDrop = Mathf.Clamp01((fraction - .65f) / .35f) * .45f;
                if (Hash01(hash + 677) < edgeDrop) continue;
                float gapNoise = Mathf.PerlinNoise(x * .19f + settings.seedOffset * .007f,
                    z * .19f - settings.seedOffset * .009f);
                if (gapNoise < .48f && Hash01(hash + 613) < Mathf.Clamp01(settings.gapStrength)) continue;

                float sampleX = x - minX, sampleZ = z - minZ;
                int mapX = Mathf.Clamp(Mathf.RoundToInt(sampleX), 0, chunkSize) + 1;
                int mapZ = Mathf.Clamp(Mathf.RoundToInt(sampleZ), 0, chunkSize) + 1;
                if (record.SurfaceTypeMap[mapX, mapZ] != SurfaceType.Grass || !AllowsMeadowPlant(record, mapX, mapZ, hash) ||
                    (record.SlopeMap != null && record.SlopeMap[mapX, mapZ] > settings.maxSlope) ||
                    blockers.IsExcluded(sampleX, sampleZ, clearance)) continue;

                float height = SampleHeightBilinear(record.HeightMap, sampleX, sampleZ, chunkSize);
                float scale = Mathf.Lerp(scaleMin, scaleMax, Hash01(hash + 127));
                Color tint = Color.Lerp(settings.darkTint, settings.lightTint, Hash01(hash + 419)); tint.a = 1f;
                record.FoliageData.flowerInstances.Add(new FlowerInstanceData(
                    new Vector3((sampleX - chunkSize * .5f) * worldScale, height * meshHeightMultiplier * worldScale,
                        (sampleZ - chunkSize * .5f) * worldScale),
                    Quaternion.Euler(0f, Hash01(hash + 83) * 360f, 0f), Vector3.one * scale,
                    (Color32)tint, isPoppy: !isCornflower, isCornflower: isCornflower));
            }
        }
    }
}
