using System;

/// <summary>Worker scratch only: fixed capacity and one padded map shape per request manager.</summary>
public sealed class TerrainControlMapScratchPool : IDisposable
{
    private readonly object gate = new();
    private readonly ControlMapPixelData[] idle;
    private int idleCount, allocatedCount, width, height;
    private bool disposed;

    public TerrainControlMapScratchPool(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        idle = new ControlMapPixelData[capacity];
    }

    // The first request fixes the retained shape. Other sizes and excess borrowers still make
    // progress, but their scratch is not retained. Allocation and clearing happen outside the lock.
    internal ControlMapPixelData Rent(int requestedWidth, int requestedHeight, out bool poolOwned)
    {
        poolOwned = false;
        lock (gate)
        {
            if (!disposed)
            {
                if (allocatedCount == 0 && width == 0)
                {
                    width = requestedWidth;
                    height = requestedHeight;
                }
                if (requestedWidth == width && requestedHeight == height)
                {
                    if (idleCount > 0)
                    {
                        poolOwned = true;
                        var result = idle[--idleCount];
                        idle[idleCount] = null;
                        return result;
                    }
                    if (allocatedCount < idle.Length)
                    {
                        allocatedCount++;
                        poolOwned = true;
                    }
                }
            }
        }

        try { return new ControlMapPixelData(requestedWidth, requestedHeight, 3); }
        catch
        {
            if (poolOwned)
                lock (gate) allocatedCount--;
            throw;
        }
    }

    // Only the builder returns pool-owned scratch, once, after its final read.
    internal void Return(ControlMapPixelData scratch)
    {
        lock (gate)
        {
            if (disposed) allocatedCount--;
            else idle[idleCount++] = scratch;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Array.Clear(idle, 0, idle.Length);
            allocatedCount -= idleCount;
            idleCount = 0;
        }
        // Borrowed scratch remains valid; late returns release their references without retention.
    }
}
