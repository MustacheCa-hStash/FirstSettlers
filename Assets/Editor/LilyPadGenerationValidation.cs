using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class LilyPadGenerationValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Terrain/Validate Lily Pad Generation")]
    public static void Run()
    {
        var settings = new LilyPadSettings
        {
            minDistanceFromShore = 1f, maxDistanceFromShore = 6f,
            candidateSpacing = 0.75f, spawnChance = 1f,
            colonyThreshold = 0f, waterSurfaceOffset = 0.08f
        };
        ChunkRecord first = CreateShoreRecord();
        Drain(LilyPadGenerator.GenerateIncrementally(first, settings, 9123, 16, 1f, 0.24f, 2.4f));
        Check(first.FoliageData.lilyPadsGenerated && first.FoliageData.lilyPadInstances.Count > 0,
            "Expected lily pads on the water side of the shore.");
        foreach (LilyPadInstanceData pad in first.FoliageData.lilyPadInstances)
        {
            Check(Mathf.Abs(pad.localPosition.y - 2.48f) < 0.0001f, "Water offset was not applied.");
            Check(pad.localPosition.x > -5.5f && pad.localPosition.x < 3f,
                "Pad escaped the shore distance band or landed on dry ground.");
        }

        ChunkRecord second = CreateShoreRecord();
        Drain(LilyPadGenerator.GenerateIncrementally(second, settings, 9123, 16, 1f, 0.24f, 2.4f));
        List<LilyPadInstanceData> a = first.FoliageData.lilyPadInstances;
        List<LilyPadInstanceData> b = second.FoliageData.lilyPadInstances;
        Check(a.Count == b.Count, "Lily pad count changed between equal chunks.");
        for (int i = 0; i < a.Count; i++)
            Check(a[i].localPosition == b[i].localPosition && a[i].localRotation == b[i].localRotation &&
                  Mathf.Approximately(a[i].uniformScale, b[i].uniformScale), "Lily pad placement was not deterministic.");

        ChunkRecord cancelled = CreateShoreRecord();
        using (IEnumerator<bool> steps = LilyPadGenerator.GenerateIncrementally(cancelled, settings, 9123, 16, 1f, 0.24f, 2.4f))
        {
            Check(steps.MoveNext(), "Expected incremental discovery.");
            cancelled.FoliageData.ClearLilyPads();
            while (steps.MoveNext()) { }
        }
        Check(!cancelled.FoliageData.lilyPadsGenerated && cancelled.FoliageData.lilyPadInstances.Count == 0,
            "Cancelled generation published stale pads.");
        Debug.Log("LILY PAD PASS: shoreline band, water offset, deterministic placement, and cancellation.");
    }

    private static ChunkRecord CreateShoreRecord()
    {
        var record = new ChunkRecord(new ChunkCoord(0, 0));
        var heights = new float[19, 19];
        var water = new WaterState[19, 19];
        for (int x = 0; x < 19; x++)
            for (int z = 0; z < 19; z++)
            {
                heights[x, z] = x < 3 ? 0.3f : 0.2f;
                water[x, z] = x < 3 ? WaterState.Dry : WaterState.Deep;
            }
        typeof(ChunkRecord).GetField("heightMap", PrivateInstance).SetValue(record, heights);
        typeof(ChunkRecord).GetField("waterStateMap", PrivateInstance).SetValue(record, water);
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
