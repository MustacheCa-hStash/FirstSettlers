using System;

// Bounded rolling window. Adding a frame allocates nothing; selection uses a reusable buffer.
public sealed class FrameTimeStatistics
{
    private readonly float[] durations, scratch;
    private readonly double[] ends;
    private readonly double windowSeconds;
    private int first;
    private double elapsed, sum;
    public int Count { get; private set; }
    public double SampleSeconds => sum;
    public FrameTimeStatistics(int capacity = 32768, double windowSeconds = 30d)
    {
        if (capacity < 1 || windowSeconds <= 0d) throw new ArgumentOutOfRangeException();
        durations = new float[capacity]; scratch = new float[capacity]; ends = new double[capacity]; this.windowSeconds = windowSeconds;
    }
    public void Reset() { first = 0; Count = 0; elapsed = 0d; sum = 0d; }
    public void Add(float seconds)
    {
        if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        elapsed += seconds;
        while (Count > 0 && (Count == durations.Length || ends[first] <= elapsed - windowSeconds))
        { sum -= durations[first]; first = (first + 1) % durations.Length; Count--; }
        int index = (first + Count) % durations.Length;
        durations[index] = seconds; ends[index] = elapsed; Count++; sum += seconds;
    }
    public void Calculate(out double averageFps, out double onePercentLowFps)
    {
        if (Count == 0) { averageFps = onePercentLowFps = 0d; return; }
        averageFps = Count / sum;
        for (int i = 0; i < Count; i++) scratch[i] = durations[(first + i) % durations.Length];
        int slowest = Math.Max(1, (int)Math.Ceiling(Count * 0.01d));
        // Partition descending around the slowest-1% boundary, without sorting every sample.
        int low = 0, high = Count - 1, target = slowest - 1;
        while (low < high)
        {
            float pivot = scratch[(low + high) / 2]; int left = low, right = high;
            while (left <= right)
            {
                while (scratch[left] > pivot) left++;
                while (scratch[right] < pivot) right--;
                if (left <= right) { (scratch[left], scratch[right]) = (scratch[right], scratch[left]); left++; right--; }
            }
            if (target <= right) high = right;
            else if (target >= left) low = left;
            else break;
        }
        double slowSeconds = 0d; for (int i = 0; i < slowest; i++) slowSeconds += scratch[i];
        onePercentLowFps = slowest / slowSeconds;
    }
}
