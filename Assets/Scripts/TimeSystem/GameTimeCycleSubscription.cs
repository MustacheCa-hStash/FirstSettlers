using System;

public sealed class GameTimeCycleSubscription : IDisposable
{
    private readonly GameTimeManager manager;

    internal GameTimeCycleSubscription(
        GameTimeManager manager,
        string cycleId,
        long intervalGameMinutes,
        Action<GameTimeSnapshot, long> callback,
        bool invokeForEachMissedInterval,
        long nextGameMinute)
    {
        this.manager = manager;
        CycleId = cycleId;
        IntervalGameMinutes = intervalGameMinutes;
        Callback = callback;
        InvokeForEachMissedInterval = invokeForEachMissedInterval;
        NextGameMinute = nextGameMinute;
    }

    public string CycleId { get; }
    public long IntervalGameMinutes { get; }
    public bool InvokeForEachMissedInterval { get; }
    public long CompletedCycles { get; internal set; }

    internal Action<GameTimeSnapshot, long> Callback { get; }
    internal long NextGameMinute { get; set; }
    internal bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        manager.UnregisterCycle(this);
    }
}
