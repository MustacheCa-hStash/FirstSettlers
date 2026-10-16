using System;
using System.Diagnostics;
using Unity.Profiling;

public enum WorldStreamingPhase { Results, Content, Foliage, Grass, Sculpt }

/// <summary>Accounts only measured main-thread streaming work; drawing and worker time are excluded.
/// Reservations protect later phases, then unused capacity returns to the shared allowance.</summary>
public sealed class WorldStreamingBudget
{
    private static readonly ProfilerMarker ResultsMarker = new("FS.Streaming.Budgeted.Results");
    private static readonly ProfilerMarker ContentMarker = new("FS.Streaming.Budgeted.Content");
    private static readonly ProfilerMarker FoliageMarker = new("FS.Streaming.Budgeted.Foliage");
    private static readonly ProfilerMarker GrassMarker = new("FS.Streaming.Budgeted.Grass");
    private static readonly ProfilerMarker SculptMarker = new("FS.Streaming.Budgeted.Sculpt");
    private readonly WorldStreamingConfiguration configuration;
    private readonly Func<long> timestamp;
    private readonly double millisecondsPerTick;
    private double spentMs;
    private long started;
    private int completedPhases;
    private WorldStreamingPhase? active;
    public double LastFrameMs { get; private set; }
    public double PeakFrameMs { get; private set; }
    public int OverrunFrames { get; private set; }
    public double TotalMs => configuration.TotalMs;
    public double RemainingMs
    {
        get
        {
            if (configuration.TotalMs <= 0) return double.PositiveInfinity;
            double reserved = 0;
            for (int i = 1; i <= 4; i++)
                if ((completedPhases & (1 << i)) == 0 && active != (WorldStreamingPhase)i)
                    reserved += Reservation((WorldStreamingPhase)i);
            return Math.Max(0, configuration.TotalMs - ElapsedMs - reserved);
        }
    }
    public bool HasRemaining => RemainingMs > 0;
    private double ElapsedMs => spentMs + (active.HasValue ? (timestamp() - started) * millisecondsPerTick : 0);

    public WorldStreamingBudget(WorldStreamingConfiguration configuration)
        : this(configuration, Stopwatch.GetTimestamp, 1000d / Stopwatch.Frequency) { }

    // Injectable clock keeps budget and reservation regressions deterministic.
    public WorldStreamingBudget(WorldStreamingConfiguration configuration, Func<long> timestamp, double millisecondsPerTick)
    {
        this.configuration = configuration.Sanitized();
        this.timestamp = timestamp ?? throw new ArgumentNullException(nameof(timestamp));
        this.millisecondsPerTick = millisecondsPerTick;
    }
    private double Reservation(WorldStreamingPhase phase) => phase switch
    {
        WorldStreamingPhase.Content => configuration.ContentReserveMs,
        WorldStreamingPhase.Foliage => configuration.FoliageReserveMs,
        WorldStreamingPhase.Grass => configuration.GrassReserveMs,
        WorldStreamingPhase.Sculpt => configuration.SculptReserveMs,
        _ => 0
    };
    public void BeginFrame()
    {
        if (active.HasValue) throw new InvalidOperationException("Streaming phase is still active.");
        spentMs = 0; completedPhases = 0;
    }
    public Scope Measure(WorldStreamingPhase phase)
    {
        if (active.HasValue) throw new InvalidOperationException("Streaming phases must not overlap.");
        var marker = phase switch
        {
            WorldStreamingPhase.Results => ResultsMarker,
            WorldStreamingPhase.Content => ContentMarker,
            WorldStreamingPhase.Foliage => FoliageMarker,
            WorldStreamingPhase.Grass => GrassMarker,
            WorldStreamingPhase.Sculpt => SculptMarker,
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
        var profilerScope = marker.Auto();
        active = phase; started = timestamp();
        return new Scope(this, profilerScope);
    }
    private void EndPhase()
    {
        spentMs = ElapsedMs;
        completedPhases |= 1 << (int)active.Value;
        active = null;
    }
    public void EndFrame()
    {
        if (active.HasValue) throw new InvalidOperationException("Streaming phase is still active.");
        LastFrameMs = spentMs; PeakFrameMs = Math.Max(PeakFrameMs, spentMs);
        if (configuration.TotalMs > 0 && spentMs > configuration.TotalMs) OverrunFrames++;
    }
    public readonly struct Scope : IDisposable
    {
        private readonly WorldStreamingBudget owner;
        private readonly ProfilerMarker.AutoScope profilerScope;
        internal Scope(WorldStreamingBudget owner, ProfilerMarker.AutoScope profilerScope)
        { this.owner = owner; this.profilerScope = profilerScope; }
        public void Dispose()
        {
            try { owner.EndPhase(); }
            finally { profilerScope.Dispose(); }
        }
    }
}
