using System;
using Unity.Mathematics;

public struct TerrainSculptConfiguration
{
    public bool Enabled;
    public long DensityBytes, DerivedBytes;
    public float PublicationMs;
    public TerrainSculptSurfaceSettings SurfaceAppearance;
    public TerrainSkyAccessSettings SkyAccess;
    public static TerrainSculptConfiguration Default => new TerrainSculptConfiguration
    { DensityBytes = 64L * 1024 * 1024, DerivedBytes = 128L * 1024 * 1024, PublicationMs = .5f };
}

/// <summary>Immutable coverage identity. Only rendering uses the distance blend.</summary>
public readonly struct TerrainSculptCoveragePolicy
{
    public const double FullRadius = 192, OuterRadius = 256, PrepareRadius = 288, ReleaseRadius = 320;
    public const double FullVertical = 64, OuterVertical = 96, PrepareVertical = 112, ReleaseVertical = 128;
    public const double CollisionRadius = 32, CollisionPrepare = 48, CollisionRelease = 64;
    // Used only by the one-shot successor-memory recovery; authored normal distances stay unchanged.
    public const double PressureRenderRadius = 16;
    public readonly double3 Anchor;
    public TerrainSculptCoveragePolicy(double3 anchor) { Anchor = anchor; }
    private static double Fade(double x, double start, double end)
    { double t = math.clamp((x - start) / (end - start), 0, 1); return 1 - t * t * (3 - 2 * t); }
    public double Weight(double3 p) => Fade(math.length((p - Anchor).xz), FullRadius, OuterRadius) *
        Fade(Math.Abs(p.y - Anchor.y), FullVertical, OuterVertical);
    public static double3 AnchorFor(double3 focus, TerrainDensityProfile profile) =>
        math.round(focus / (profile.Spacing * 16)) * (profile.Spacing * 16);
    public static bool MoveAnchor(double3 focus, double3 old, TerrainDensityProfile profile) =>
        math.cmax(math.abs(focus - old)) > profile.Spacing * 16 * .75;
    public double HorizontalDistance(double3 min, double3 max) => math.length(math.max(math.max(min.xz - Anchor.xz, Anchor.xz - max.xz), 0));
    public double VerticalDistance(double3 min, double3 max) => Math.Max(0, Math.Max(min.y - Anchor.y, Anchor.y - max.y));
    public bool Prepare(double3 min, double3 max) => HorizontalDistance(min,max) <= PrepareRadius && VerticalDistance(min,max) <= PrepareVertical;
    public bool Retain(double3 min, double3 max) => HorizontalDistance(min,max) <= ReleaseRadius && VerticalDistance(min,max) <= ReleaseVertical;
    public bool Visible(double3 min, double3 max) => HorizontalDistance(min,max) < OuterRadius && VerticalDistance(min,max) < OuterVertical;
    public static double Distance(double3 p, double3 min, double3 max) => math.length(math.max(math.max(min - p, p - max), 0));
}

/// <summary>Read-only sample source; snapshots and visual views share the same extraction path.</summary>
public interface ITerrainDensityField
{
    TerrainDensityProfile Profile { get; }
    short ReadEncoded(int3 sample);
    void FillMeshHalo(int3 first,short[] destination);
    bool HasOverrides(int3 minimum, int3 maximum);
    bool IsSurfaceFree(TerrainDensityCellBounds bounds);
    IDisposable ReserveScratch(long bytes);
    void RequireAlive();
}

/// <summary>Does not own or mutate its snapshot. Authoritative queries/collision never use this view.</summary>
public sealed class TerrainSculptVisualField : ITerrainDensityField
{
    private readonly TerrainDensitySnapshot source;
    private readonly TerrainSculptCoveragePolicy policy;
    public TerrainDensityProfile Profile => source.Profile;
    internal double3 CacheAnchor => policy.Anchor;
    public TerrainSculptVisualField(TerrainDensitySnapshot source, TerrainSculptCoveragePolicy policy)
    { this.source = source ?? throw new ArgumentNullException(nameof(source)); this.policy = policy; }
    public short ReadEncoded(int3 sample)
    {
        double3 p = TerrainDensityCoordinates.ToWorld(sample, Profile);
        short original = Profile.Encode(source.Generator.SampleDensity(p));
        double w = policy.Weight(p);
        if (w <= 0) return original;
        short edited = source.ReadEncoded(sample);
        // Blend encoded clamped fields, then quantize once using the profile's tie rule.
        return (short)Math.Round(math.lerp((double)original, edited, w), MidpointRounding.AwayFromZero);
    }
    public bool HasOverrides(int3 minimum, int3 maximum) => source.HasOverrides(minimum,maximum);
    public void FillMeshHalo(int3 first,short[] destination) => source.FillMeshHalo(first,destination,policy);
    public bool IsSurfaceFree(TerrainDensityCellBounds cells)
    {
        // A blend of two entirely negative (or two nonnegative) fields cannot cross zero.
        if (!source.IsSurfaceFree(cells) || !(source.Generator is ITerrainDensityBounds bounds) ||
            !bounds.TryGetDensityBounds(TerrainDensityCoordinates.ToWorld(cells.Min,Profile),
                TerrainDensityCoordinates.ToWorld(cells.Max,Profile),out double low,out double high)) return false;
        if (low > high) throw new InvalidOperationException("Inverted generator bounds.");
        if (Profile.Encode(high) < 0) return source.ReadEncoded(cells.Min) < 0;
        if (Profile.Encode(low) >= 0) return source.ReadEncoded(cells.Min) >= 0;
        return false;
    }
    public void RequireAlive() => source.RequireAlive();
    public IDisposable ReserveScratch(long bytes) => source.ReserveScratch(bytes);
}

/// <summary>Conservative build admission, including growable collections and publication copies.
/// A batch reserves its capacity before dispatch; charges happen before growing collections.</summary>
public sealed class TerrainSculptBuildBudget
{
    private readonly System.Threading.CancellationToken cancellation;
    public long Capacity { get; }
    public long ChargedBytes { get; private set; }
    public long PeakBytes { get; private set; }
    public long PersistentBytes { get; private set; }
    public TerrainSculptBuildBudget(long capacity, System.Threading.CancellationToken cancellation = default)
    { if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity)); Capacity = capacity; this.cancellation = cancellation; }
    public void Check() => cancellation.ThrowIfCancellationRequested();
    public void Charge(long bytes)
    {
        Check();
        if (bytes < 0 || bytes > Capacity - ChargedBytes)
            throw new TerrainDensityCapacityException($"Derived terrain build budget exceeded ({ChargedBytes} + {bytes} > {Capacity} bytes).");
        ChargedBytes += bytes;
        PeakBytes=Math.Max(PeakBytes,ChargedBytes);
    }
    // Extraction scratch becomes unreachable at a builder's return. Retain a conservative
    // charge for its output arrays; this is live ownership accounting, not process/GC memory.
    public void ChargePersistent(long bytes) { Charge(bytes);PersistentBytes+=bytes; }
    internal void RetainCharged(long bytes)
    {
        if(bytes<0||bytes>ChargedBytes-PersistentBytes)throw new InvalidOperationException("Uncharged cache retention.");
        PersistentBytes+=bytes;
    }
    public void CompleteScope(long start,long retained,long persistentAtStart)
    {
        long delta=PersistentBytes-persistentAtStart;
        if(start<0||start>ChargedBytes||retained<0||delta<0||retained+delta>ChargedBytes-start)
            throw new InvalidOperationException("Invalid sculpt build accounting scope.");
        ChargedBytes=start+retained+delta;
    }
}

public interface ITerrainMovementGuard
{
    bool CanMove(double3 from, double3 to, double radius, double height);
}

/// <summary>Lighting needs published render geometry, independently of player collision residency.</summary>
public interface ITerrainLightGuard
{
    bool CanIlluminate(double3 center,double radius);
}
