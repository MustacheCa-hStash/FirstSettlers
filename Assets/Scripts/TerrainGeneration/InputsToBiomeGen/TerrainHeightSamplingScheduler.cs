using System;
using System.Threading;
using Unity.Jobs;
using Unity.Profiling;

/// <summary>A batch index maps to at most SamplesPerBatch disjoint output samples.</summary>
public interface ITerrainHeightSamplingBatchJob : IJobParallelFor
{
    int SampleOffset { get; set; }
    int SampleCount { get; set; }
}

/// <summary>Process-wide admission for expensive terrain height evaluation, shared by all requests.</summary>
public static class TerrainHeightSamplingScheduler
{
    public const int MaxConcurrentBatches = 6;
    public const int SamplesPerBatch = 64;
    private static readonly object Gate = new();
    private static long nextTicket;
    private static long servingTicket;
    private static readonly ProfilerMarker RunMarker = new("FS.Streaming.Worker.HeightSampling.RunCapped");
    private static readonly ProfilerMarker AdmissionMarker = new("FS.Streaming.Worker.HeightSampling.WaitForSlot");
    private static readonly ProfilerMarker ScheduleMarker = new("FS.Streaming.Worker.HeightSampling.ScheduleCapped");
    private static readonly ProfilerMarker CompleteMarker = new("FS.Streaming.Worker.HeightSampling.CompleteCapped");

    // Called by request producers, never from a Unity job's Execute method. Admission waits
    // here, outside the Unity worker pool, so lighting and other jobs can use the remaining workers.
    public static void Run<T>(T job, int sampleCount) where T : struct, ITerrainHeightSamplingBatchJob
    {
        if (sampleCount < 0) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (sampleCount == 0) return;
        using var scope = RunMarker.Auto();
        job.SampleCount = sampleCount;
        for (int offset = 0; offset < sampleCount;)
        {
            int remaining = sampleCount - offset;
            int batches = Math.Min(MaxConcurrentBatches, 1 + (remaining - 1) / SamplesPerBatch);
            AcquireWave();
            try
            {
                job.SampleOffset = offset;
                JobHandle handle;
                using (ScheduleMarker.Auto()) handle = job.Schedule(batches, 1);
                // Unity handles remain on the thread that scheduled them. Finish this wave
                // before releasing admission or allowing callers to dispose their native data.
                using (CompleteMarker.Auto()) handle.Complete();
            }
            finally
            {
                lock (Gate)
                {
                    servingTicket++;
                    Monitor.PulseAll(Gate);
                }
            }
            offset += Math.Min(remaining, batches * SamplesPerBatch);
        }
    }

    private static void AcquireWave()
    {
        using var scope = AdmissionMarker.Auto();
        lock (Gate)
        {
            long ticket = nextTicket++;
            // FIFO admission between short waves lets overlapping requests make progress.
            // Only one wave of at most six sampling tasks may execute process-wide.
            while (ticket != servingTicket) Monitor.Wait(Gate);
        }
    }
}
