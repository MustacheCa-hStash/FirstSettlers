using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class CattailGenerationValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Terrain/Validate Cattail Generation")]
    public static void Run()
    {
        var settings = new CattailSettings
        {
            landwardDistance = 3f, waterwardDistance = 3f,
            candidateSpacing = 0.6f, spawnChance = 1f, colonyThreshold = 0f
        };
        ChunkRecord first = CreateShoreRecord();
        Drain(CattailGenerator.GenerateIncrementally(first, settings, 9123, 16, 1f, 10f, 0.24f));
        Check(first.FoliageData.cattailsGenerated, "Generation did not complete.");
        bool foundBank = false;
        bool foundWater = false;
        foreach (CattailInstanceData cattail in first.FoliageData.cattailInstances)
        {
            float localX = cattail.localPosition.x + 8f;
            float expectedHeight = localX <= 7f ? 2.5f :
                localX >= 8f ? 2f : Mathf.Lerp(2.5f, 2f, localX - 7f);
            Check(Mathf.Abs(cattail.localPosition.y - expectedHeight) < 0.001f,
                "Root is not on the sampled terrain surface.");
            foundBank |= cattail.localPosition.y > 2.4f;
            foundWater |= cattail.localPosition.y < 2.1f;
        }
        Check(foundBank && foundWater, "Expected cattails on both the wet bank and shallow bed.");

        var waterOnly = new CattailSettings
        {
            landwardDistance = 0f, minWaterwardDistance = 1.25f, waterwardDistance = 3f,
            candidateSpacing = 0.6f, spawnChance = 1f, colonyThreshold = 0f
        };
        ChunkRecord offshore = CreateShoreRecord();
        Drain(CattailGenerator.GenerateIncrementally(offshore, waterOnly, 9123, 16, 1f, 10f, 0.24f));
        Check(offshore.FoliageData.cattailInstances.Count > 0, "Expected an offshore cattail stand.");
        foreach (CattailInstanceData cattail in offshore.FoliageData.cattailInstances)
        {
            Check(cattail.localPosition.y <= 2.4f, "Water-only setting placed a cattail on land.");
            Check(cattail.localPosition.x + 8f >= 8.5f,
                "Minimum waterward distance did not clear the shoreline.");
        }

        ChunkRecord second = CreateShoreRecord();
        Drain(CattailGenerator.GenerateIncrementally(second, settings, 9123, 16, 1f, 10f, 0.24f));
        List<CattailInstanceData> a = first.FoliageData.cattailInstances;
        List<CattailInstanceData> b = second.FoliageData.cattailInstances;
        Check(a.Count == b.Count, "Count changed for identical terrain and seed.");
        for (int i = 0; i < a.Count; i++)
            Check(a[i].localPosition == b[i].localPosition && a[i].localRotation == b[i].localRotation &&
                  Mathf.Approximately(a[i].uniformScale, b[i].uniformScale), "Placement is not deterministic.");

        ChunkRecord steep = CreateShoreRecord(45f);
        Drain(CattailGenerator.GenerateIncrementally(steep, settings, 9123, 16, 1f, 10f, 0.24f));
        Check(steep.FoliageData.cattailInstances.Count == 0, "Steep shore was populated.");

        ChunkRecord cancelled = CreateShoreRecord();
        using (IEnumerator<bool> steps = CattailGenerator.GenerateIncrementally(cancelled, settings, 9123, 16, 1f, 10f, 0.24f))
        {
            Check(steps.MoveNext(), "Expected incremental discovery.");
            cancelled.FoliageData.ClearCattails();
            while (steps.MoveNext()) { }
        }
        Check(!cancelled.FoliageData.cattailsGenerated && cancelled.FoliageData.cattailInstances.Count == 0,
            "Cancelled generation published stale cattails.");
        Debug.Log("CATTAIL PASS: bank and bed rooting, offshore minimum, slope exclusion, deterministic placement, cancellation.");
    }

    private static ChunkRecord CreateShoreRecord(float slope = 0f)
    {
        var record = new ChunkRecord(new ChunkCoord(0, 0));
        var heights = new float[19, 19];
        var slopes = new float[19, 19];
        var water = new WaterState[19, 19];
        var surfaces = new SurfaceType[19, 19];
        for (int x = 0; x < 19; x++)
            for (int z = 0; z < 19; z++)
            {
                bool bank = x < 9;
                heights[x, z] = bank ? 0.25f : 0.2f;
                slopes[x, z] = slope;
                water[x, z] = bank ? WaterState.Wet : WaterState.Shallow;
                surfaces[x, z] = bank ? SurfaceType.Sand : SurfaceType.Riverbed;
            }
        typeof(ChunkRecord).GetField("heightMap", PrivateInstance).SetValue(record, heights);
        typeof(ChunkRecord).GetField("slopeMap", PrivateInstance).SetValue(record, slopes);
        typeof(ChunkRecord).GetField("waterStateMap", PrivateInstance).SetValue(record, water);
        typeof(ChunkRecord).GetField("surfaceTypeMap", PrivateInstance).SetValue(record, surfaces);
        return record;
    }

    private static void Drain(IEnumerator<bool> steps)
    {
        using (steps)
            while (steps.MoveNext()) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
