using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PerformanceDebugValidation
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Validation/Validate Performance Debug Statistics")]
    public static void Run()
    {
        checks = 0; ValidateStatistics();
        Debug.Log($"PERFORMANCE DEBUG PASS: {checks} checks; rolling average, slowest 1%, expiry, reset and bounded storage.");
    }
    public static void RunBatch()
    {
        try { Run(); DistantTreeGroundingValidation.Run(); TerrainErrorMeasurementValidation.Run(); WorldManagerInspectorValidation.Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void ValidateStatistics()
    {
        var stats = new FrameTimeStatistics();
        for (int i = 0; i < 1000; i++) stats.Add(i < 10 ? 0.05f : 0.005f);
        stats.Calculate(out double average, out double low);
        Check(Math.Abs(average - 1000d / 5.45d) < 0.001d && Math.Abs(low - 20d) < 0.001d,
            "Average is frame-count / elapsed time; 1% low averages slowest frame times");
        stats.Reset(); stats.Calculate(out average, out low); Check(stats.Count == 0 && average == 0d && low == 0d, "Reset excludes prior mode");
        var random = new System.Random(145678);
        for (int count = 1; count <= 2000; count += 17)
        {
            stats.Reset(); var samples = new List<float>();
            for (int i = 0; i < count; i++) { float value = i % 7 == 0 ? 0.02f : (float)(0.001d + random.NextDouble() * 0.008d); samples.Add(value); stats.Add(value); }
            stats.Calculate(out average, out low); samples.Sort((a, b) => b.CompareTo(a)); int slowest = Mathf.CeilToInt(count * 0.01f);
            Check(Math.Abs(low - slowest / samples.Take(slowest).Sum(v => (double)v)) < 0.00001d, "Partition agrees with sorted reference including duplicate durations");
        }
        for (int i = 0; i < 10000; i++) stats.Add(0.005f);
        stats.Calculate(out average, out low); Check(stats.Count >= 5998 && stats.Count <= 6002 && Math.Abs(average - 200d) < 0.001d && Math.Abs(low - 200d) < 0.001d,
            "Rolling window expires old stutters");
        var bounded = new FrameTimeStatistics(4, 30d); for (int i = 0; i < 100; i++) bounded.Add(0.01f);
        bounded.Add(float.NaN); bounded.Add(float.PositiveInfinity); bounded.Add(0f);
        bounded.Calculate(out average, out low); Check(bounded.Count == 4 && Math.Abs(average - 100d) < 0.001d, "Capacity and invalid samples remain bounded");
    }
}
