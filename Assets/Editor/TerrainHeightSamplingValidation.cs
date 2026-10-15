using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class TerrainHeightSamplingValidation
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    [MenuItem("Tools/Terrain/Validate Height Sampling Concurrency")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before running the isolated height sampling fixture.");
        Check(TerrainHeightSamplingScheduler.MaxConcurrentBatches == 6, "Height sampling cap changed from six.");
        ValidateEdges(); ValidateConcurrentProducers(); ValidatePartialSchedulingFailure(); ValidateProductionSampling();
        Debug.Log("HEIGHT SAMPLING CAP PASS: six shared batches, exact coverage/tails, concurrent producers, partial-schedule failure recovery, production near/far overlap and retained native outputs.");
    }

    private struct ProbeJob : ITerrainHeightSamplingBatchJob
    {
        public int SampleOffset { get; set; }
        public int SampleCount { get; set; }
        [NativeDisableParallelForRestriction] public NativeArray<int> Values;
        [NativeDisableParallelForRestriction] public NativeArray<long> Times;
        public void Execute(int batchIndex)
        {
            int start = SampleOffset + batchIndex * TerrainHeightSamplingScheduler.SamplesPerBatch;
            int end = Math.Min(SampleCount, start + TerrainHeightSamplingScheduler.SamplesPerBatch);
            int timeIndex = start / TerrainHeightSamplingScheduler.SamplesPerBatch * 2;
            Times[timeIndex] = Stopwatch.GetTimestamp();
            for (int i = start; i < end; i++) Values[i] += i + 1;
            // Managed validation job only: make overlapping work observable without blocking on a lock.
            Thread.SpinWait(40000);
            Times[timeIndex + 1] = Stopwatch.GetTimestamp();
        }
    }

    private struct FailingScheduleJob : ITerrainHeightSamplingBatchJob
    {
        private int offset;
        public int SampleOffset
        {
            get => offset;
            set
            {
                if (value >= 6 * 64) throw new InvalidOperationException("injected scheduling failure");
                offset = value;
            }
        }
        public int SampleCount { get; set; }
        [NativeDisableParallelForRestriction] public NativeArray<int> Values;
        public void Execute(int batchIndex)
        {
            int start = SampleOffset + batchIndex * TerrainHeightSamplingScheduler.SamplesPerBatch;
            for (int i = start; i < Math.Min(SampleCount, start + TerrainHeightSamplingScheduler.SamplesPerBatch); i++) Values[i]++;
        }
    }

    private static void ValidateEdges()
    {
        foreach (int count in new[] { 0, 1, 63, 64, 65, 383, 384, 385, 1025 })
        {
            using var values = new NativeArray<int>(count, Allocator.Persistent);
            using var times = new NativeArray<long>(2 * ((count + 63) / 64), Allocator.Persistent);
            TerrainHeightSamplingScheduler.Run(new ProbeJob { Values = values, Times = times }, count);
            for (int i = 0; i < count; i++) Check(values[i] == i + 1, "Skipped or repeated sample at batch/wave boundary.");
        }
        try { TerrainHeightSamplingScheduler.Run(default(ProbeJob), -1); throw new Exception("Negative sample count accepted."); }
        catch (ArgumentOutOfRangeException) { }
    }

    private static void ValidateConcurrentProducers()
    {
        const int producerCount = 4, samples = 6 * 64 * 8 + 17;
        var values = new NativeArray<int>[producerCount]; var times = new NativeArray<long>[producerCount];
        var tasks = new Task[producerCount];
        using var start = new ManualResetEventSlim(false);
        try
        {
            for (int p = 0; p < producerCount; p++)
            {
                values[p] = new NativeArray<int>(samples, Allocator.Persistent);
                times[p] = new NativeArray<long>(2 * ((samples + 63) / 64), Allocator.Persistent);
                var job = new ProbeJob { Values = values[p], Times = times[p] };
                tasks[p] = Task.Run(() => { start.Wait(); TerrainHeightSamplingScheduler.Run(job, samples); });
            }
            start.Set(); Task.WaitAll(tasks);
            var events = new List<(long Time, int Delta)>();
            for (int p = 0; p < producerCount; p++)
            {
                for (int i = 0; i < samples; i++) Check(values[p][i] == i + 1, "Concurrent producer output changed.");
                for (int i = 0; i < times[p].Length; i += 2)
                {
                    Check(times[p][i] > 0 && times[p][i + 1] >= times[p][i], "Missing batch interval.");
                    events.Add((times[p][i], 1)); events.Add((times[p][i + 1], -1));
                }
            }
            events.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Delta.CompareTo(b.Delta));
            int active = 0, peak = 0;
            foreach (var entry in events) { active += entry.Delta; peak = Math.Max(peak, active); }
            Check(active == 0 && peak >= 1 && peak <= 6, "Concurrent requests multiplied the six-batch allowance.");
            Debug.Log("HEIGHT CONCURRENCY OBSERVATION: four producers, " + producerCount * ((samples + 63) / 64) + " batches, observed peak=" + peak + ", limit=6.");
        }
        finally
        {
            start.Set();
            // Ensure a failing assertion never disposes native outputs while a producer/job still uses them.
            for (int p = 0; p < producerCount; p++)
            {
                // Preserve the original failure and still release every producer's arrays.
                try { tasks[p]?.Wait(); } catch (AggregateException) { }
                if (values[p].IsCreated) values[p].Dispose();
                if (times[p].IsCreated) times[p].Dispose();
            }
        }
    }

    private static void ValidatePartialSchedulingFailure()
    {
        using var values = new NativeArray<int>(385, Allocator.Persistent);
        try
        {
            TerrainHeightSamplingScheduler.Run(new FailingScheduleJob { Values = values }, values.Length);
            throw new Exception("Scheduling fixture did not fail.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "injected scheduling failure") { }
        // Access is legal only if the submitted prefix was completed before Run rethrew.
        for (int i = 0; i < 384; i++) Check(values[i] == 1, "Submitted prefix was not completed on failure.");
        Check(values[384] == 0, "Unsubmitted tail was executed.");
        ValidateEdges(); // Subsequent requests must still be schedulable after failure and output disposal.
    }

    private static void ValidateProductionSampling()
    {
        var jobs = new List<Task>();
        for (int p = 0; p < 4; p++)
        {
            int index = p;
            jobs.Add(Task.Run(() =>
            {
                var coord = new ChunkCoord(index - 2, 3 - index);
                if ((index & 1) == 0)
                {
                    var field = HeightMapGenerator.GenerateTerrainHeightFieldForRequest(32, 12345, 600, coord, .24f,
                        1.3f, 200, WorldErosionSettings.Default, out var heights, out var slopes, out var rivers);
                    try
                    {
                        for (int x = 0; x < 35; x++) for (int z = 0; z < 35; z++)
                            Check(heights[x * 35 + z] == field.HeightMap[x, z] && slopes[x * 35 + z] == field.SlopeMap[x, z] &&
                                rivers[x * 35 + z] == field.RiverMaskMap[x, z], "Retained native output changed under shared admission.");
                    }
                    finally { heights.Dispose(); slopes.Dispose(); rivers.Dispose(); }
                }
                else
                {
                    var result = FarTerrainGenerator.Generate(coord, 1, 32, 12345, 600, 200, .3f, 9, 9, 0, .24f,
                        isMacroTile: true, patchSizeInChunks: 4, mountainHorizontalScale: 1.3f, erosion: WorldErosionSettings.Default);
                    Check(result.HeightGrid.GetLength(0) == 9 && result.ControlMapsRawData.Maps.Length == 3,
                        "Concurrent far height/control generation lost output.");
                }
            }));
        }
        Task.WaitAll(jobs.ToArray());
    }
}
