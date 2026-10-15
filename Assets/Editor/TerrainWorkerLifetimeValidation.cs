using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>Runs real terrain requests while Editor frames advance, then holds outputs across frames.</summary>
public static class TerrainWorkerLifetimeValidation
{
    private static TerrainRequestManager requests;
    private static readonly List<TerrainDataRequestResult> results = new();
    private static readonly ConcurrentQueue<string> failures = new();
    private static NativeLeakDetectionMode previousLeakMode;
    private static double deadline;
    private static int updates, holdUntil;
    private static bool batch;

    [MenuItem("Tools/Terrain/Validate Terrain Worker Allocation Lifetime")]
    public static void Run() => Start(false);
    public static void RunBatch() => Start(true);
    private static void Start(bool exitWhenDone)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before running the isolated worker fixture.");
        if (requests != null) throw new InvalidOperationException("Lifetime validation is already running.");
        batch = exitWhenDone; updates = holdUntil = 0; results.Clear();
        while (failures.TryDequeue(out _)) { }
        previousLeakMode = NativeLeakDetection.Mode;
        NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        Application.logMessageReceivedThreaded += ObserveLog;
        requests = new TerrainRequestManager(2, 1, 1, 1, new TerrainWaterSettings(.24f, 200, 1),
            erosion: WorldErosionSettings.Default);
        try
        {
            foreach (var coord in new[] { new ChunkCoord(0, 0), new ChunkCoord(-1, 1) })
                if (!requests.RequestTerrainData(coord, 1, 128, 12345, 50, 3, .5f, 2,
                    WorldFeatureGenerationSettings.Default, 200))
                    throw new Exception("Fixture could not dispatch terrain data.");
            deadline = EditorApplication.timeSinceStartup + 60;
            EditorApplication.update += Tick;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception ex) { Finish(ex); }
    }
    private static void ObserveLog(string message, string stack, LogType type)
    {
        if (message.Contains("older than its permitted lifetime") || message.Contains("JobTempAlloc") ||
            message.Contains("TerrainData request failed") || type == LogType.Exception)
            failures.Enqueue(message + "\n" + stack);
    }
    private static void Tick()
    {
        try
        {
            updates++;
            EditorApplication.QueuePlayerLoopUpdate();
            if (failures.TryDequeue(out string failure)) throw new Exception(failure);
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Terrain worker fixture timed out.");
            while (requests.TryDequeueTerrainDataResult(out var result)) results.Add(result);
            if (results.Count != 2) return;
            if (holdUntil == 0) { holdUntil = updates + 8; return; }
            if (updates < holdUntil) return;
            foreach (var result in results)
            {
                var data = result.NativeData;
                if (data == null || !data.HeightMap.IsCreated || result.HeightMap.GetLength(0) != 131 ||
                    data.HeightMap.Length != 131 * 131 || result.WorldFeaturePlan == null || result.ControlMapsRawData == null)
                    throw new Exception("Deferred result lost its retained data.");
                for (int x = 0; x < 131; x += 13)
                    for (int z = 0; z < 131; z += 13)
                        if (Mathf.Abs(data.HeightMap[x * 131 + z] - result.HeightMap[x, z]) > .00001f)
                            throw new Exception("Retained native/managed terrain values diverged.");
            }
            Finish(null);
        }
        catch (Exception ex) { Finish(ex); }
    }
    private static void Finish(Exception failure)
    {
        EditorApplication.update -= Tick;
        try
        {
            foreach (var result in results) result.Dispose();
            results.Clear();
            requests?.WaitForActiveRequestsToFinish();
            requests?.Dispose();
            requests = null;
            if (failure == null && failures.TryDequeue(out string message)) failure = new Exception(message);
        }
        catch (Exception cleanup) { failure ??= cleanup; }
        finally
        {
            Application.logMessageReceivedThreaded -= ObserveLog;
            NativeLeakDetection.Mode = previousLeakMode;
        }
        if (failure != null) Debug.LogException(failure);
        else Debug.Log($"TERRAIN WORKER LIFETIME PASS: two production 128-sample requests, {updates} Editor updates, eight-update output retention, native/managed agreement and disposal without TempJob lifetime diagnostics.");
        if (batch) EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
