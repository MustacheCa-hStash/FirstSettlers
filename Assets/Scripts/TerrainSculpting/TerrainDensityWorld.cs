using System;
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>Immutable published payload. Uniform overrides are real data, even when all air.</summary>
internal sealed class TerrainDensityBrick
{
    // Process-local immutable identity for derived caches; never serialized in terrain saves.
    private static long nextIdentity;
    internal readonly long Identity=System.Threading.Interlocked.Increment(ref nextIdentity);
    private readonly short[] values;
    private readonly short uniform;
    internal long PayloadBytes => values == null ? sizeof(short) : TerrainDensityProfile.DetailedBrickBytes;
    internal bool IsUniform => values == null;
    internal short Read(int index) => values == null ? uniform : values[index];

    internal TerrainDensityBrick(short[] ownedValues)
    {
        short first = ownedValues[0];
        for (int i = 1; i < ownedValues.Length; i++)
            if (ownedValues[i] != first) { values = ownedValues; return; }
        uniform = first;
    }

    internal TerrainDensityBrick(short value)
    {
        if (value == short.MinValue) throw new ArgumentException("Reserved density code.");
        uniform = value;
    }
}

/// <summary>Admission failure leaves the committed field unchanged.</summary>
public sealed class TerrainDensityCapacityException : InvalidOperationException
{
    public TerrainDensityCapacityException() : base("Density payload/snapshot budget cannot admit this operation.") { }
    internal TerrainDensityCapacityException(string message):base(message) { }
}

/// <summary>
/// Baseline plus immutable sparse overrides. Stage on a producer, then commit only when the
/// corresponding mesh/collision transaction is ready. No scene objects or disk IO are created here.
/// </summary>
public sealed class TerrainDensityWorld : IDisposable
{
    public const long DefaultBudgetBytes = 64L * 1024 * 1024;
    private readonly object gate = new object();
    private Dictionary<int3, TerrainDensityBrick> bricks = new Dictionary<int3, TerrainDensityBrick>();
    private readonly ITerrainDensityGenerator generator;
    private readonly long budgetBytes;
    private readonly double minimumExcavationY;
    private long residentBytes, reservedBytes, revision;
    private long residentLimit;
    private bool disposed, editing;
    public TerrainDensityProfile Profile { get; }
    public double MinimumRemovalY => minimumExcavationY;
    public long Revision { get { lock (gate) return revision; } }
    public long ResidentPayloadBytes { get { lock (gate) return residentBytes; } }
    public long ReservedPayloadBytes { get { lock (gate) return reservedBytes; } }
    public int ModifiedBrickCount { get { lock (gate) return bricks.Count; } }

    public TerrainDensityWorld(TerrainDensityProfile profile, ITerrainDensityGenerator baseline,
        long memoryBudgetBytes = DefaultBudgetBytes, double minimumRemovalY = double.NegativeInfinity)
    {
        profile.Validate();
        generator = baseline ?? throw new ArgumentNullException(nameof(baseline));
        if (memoryBudgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(memoryBudgetBytes));
        if (double.IsNaN(minimumRemovalY) || double.IsPositiveInfinity(minimumRemovalY))
            throw new ArgumentOutOfRangeException(nameof(minimumRemovalY));
        Profile = profile; budgetBytes = memoryBudgetBytes; residentLimit = memoryBudgetBytes; minimumExcavationY = minimumRemovalY;
    }

    public short ReadEncoded(int3 sample)
    {
        TerrainDensityBrick brick;
        lock (gate) { ThrowIfDisposed(); bricks.TryGetValue(TerrainDensityCoordinates.BrickOf(sample), out brick); }
        return Read(brick, sample, Profile, generator);
    }

    internal static short Read(TerrainDensityBrick brick, int3 sample, TerrainDensityProfile profile, ITerrainDensityGenerator baseline) =>
        brick == null ? profile.Encode(baseline.SampleDensity(TerrainDensityCoordinates.ToWorld(sample, profile))) :
        brick.Read(TerrainDensityCoordinates.Index(TerrainDensityCoordinates.LocalOf(sample)));

    public TerrainDensityEdit StageExcavation(TerrainDensityBrush brush)
    {
        brush.Validate();
        if (brush.Minimum.y < minimumExcavationY)
            throw new ArgumentException("Removal bounds extend below the dry-excavation limit.", nameof(brush));
        // Outward inclusive bounds are conservative; exact max() below decides whether a sample changes.
        int3 min = TerrainDensityCoordinates.FloorSample(brush.Minimum - Profile.Band, Profile);
        int3 max = TerrainDensityCoordinates.FloorSample(brush.Maximum + Profile.Band, Profile);
        // Leave room for a canonical brick's complete payload and its future gradient halo.
        if (math.any(min < int.MinValue + 32) || math.any(max > int.MaxValue - 32))
            throw new ArgumentOutOfRangeException(nameof(brush));
        int3 first = TerrainDensityCoordinates.BrickOf(min), last = TerrainDensityCoordinates.BrickOf(max);
        long count = checked(checked(((long)last.x - first.x + 1) * ((long)last.y - first.y + 1)) * ((long)last.z - first.z + 1));
        long reservation = checked(count * TerrainDensityProfile.DetailedBrickBytes);
        var originals = new Dictionary<int3, TerrainDensityBrick>();
        long sourceRevision;
        lock (gate)
        {
            ThrowIfDisposed();
            if (editing) throw new InvalidOperationException("Only one density edit may be staged at once.");
            Admit(reservation);
            // Capture only relevant immutable owners; no lock remains held during generation.
            for (int z = first.z; z <= last.z; z++) for (int y = first.y; y <= last.y; y++) for (int x = first.x; x <= last.x; x++)
            {
                var key = new int3(x, y, z);
                if (bricks.TryGetValue(key, out var value)) originals.Add(key, value);
            }
            reservedBytes += reservation; editing = true; sourceRevision = revision;
        }
        try
        {
            var replacements = new Dictionary<int3, TerrainDensityBrick>();
            long changedSamples = 0;
            for (int z = first.z; z <= last.z; z++) for (int y = first.y; y <= last.y; y++) for (int x = first.x; x <= last.x; x++)
            {
                var key = new int3(x, y, z);
                originals.TryGetValue(key, out var previous);
                int3 origin = TerrainDensityCoordinates.Origin(key);
                int3 lo = math.max(min, origin), hi = math.min(max, origin + 15);
                short[] staged = null;
                for (int sz = lo.z; sz <= hi.z; sz++) for (int sy = lo.y; sy <= hi.y; sy++) for (int sx = lo.x; sx <= hi.x; sx++)
                {
                    var sample = new int3(sx, sy, sz);
                    double cutDistance = -brush.SignedDistance(TerrainDensityCoordinates.ToWorld(sample, Profile));
                    // Beyond the negative clamp, max(old, -B) is guaranteed to have no effect.
                    if (cutDistance <= -Profile.Band) continue;
                    short cut = Profile.Encode(cutDistance), old = Read(previous, sample, Profile, generator);
                    if (cut <= old) continue;
                    if (staged == null)
                    {
                        staged = new short[TerrainDensityProfile.SamplesPerBrick];
                        for (int i = 0; i < staged.Length; i++)
                            staged[i] = Read(previous, origin + TerrainDensityCoordinates.LocalFromIndex(i), Profile, generator);
                    }
                    staged[TerrainDensityCoordinates.Index(sample - origin)] = cut;
                    changedSamples++;
                }
                if (staged != null) replacements.Add(key, new TerrainDensityBrick(staged));
            }
            lock(gate)
            {
                long nextBytes=residentBytes;
                foreach(var pair in replacements){if(bricks.TryGetValue(pair.Key,out var old))nextBytes-=old.PayloadBytes;nextBytes+=pair.Value.PayloadBytes;}
                if(nextBytes>residentLimit)throw new TerrainDensityCapacityException();
            }
            return new TerrainDensityEdit(this, sourceRevision, reservation, replacements, changedSamples, brush);
        }
        catch
        {
            lock (gate) { reservedBytes -= reservation; editing = false; }
            throw;
        }
    }

    public bool TryCommit(TerrainDensityEdit edit)
    {
        if (edit == null) throw new ArgumentNullException(nameof(edit));
        lock (gate)
        {
            ThrowIfDisposed();
            if (!ReferenceEquals(edit.Owner, this)) throw new InvalidOperationException("Edit is consumed or belongs to another density world.");
            if (revision != edit.BaseRevision) { FinishEdit(edit); return false; }
            long nextRevision = edit.ChangedSamples == 0 ? revision : checked(revision + 1);
            // Allocate and populate a new root before publishing anything. A failed dictionary
            // allocation cannot leave half of a cross-brick edit visible in the old root.
            var next = edit.ChangedSamples == 0 ? bricks : new Dictionary<int3, TerrainDensityBrick>(bricks);
            long nextBytes = residentBytes;
            foreach (var pair in edit.Replacements)
            {
                if (next.TryGetValue(pair.Key, out var old)) nextBytes -= old.PayloadBytes;
                next[pair.Key] = pair.Value; nextBytes += pair.Value.PayloadBytes;
            }
            bricks = next; residentBytes = nextBytes;
            revision = nextRevision;
            FinishEdit(edit);
            return true;
        }
    }

    internal void Cancel(TerrainDensityEdit edit)
    {
        lock (gate) if (ReferenceEquals(edit.Owner, this)) FinishEdit(edit);
    }

    private void FinishEdit(TerrainDensityEdit edit)
    {
        reservedBytes -= edit.ReservedBytes; editing = false; edit.Consume();
    }

    public TerrainDensitySnapshot CaptureSnapshot() => CaptureSnapshot(null, null);
    /// <summary>Stable staged overlay for replacement geometry; never use it as a committed save.</summary>
    public TerrainDensitySnapshot CaptureMeshingSnapshot(TerrainDensityEdit stagedEdit) => CaptureSnapshot(null, null, stagedEdit);

    private TerrainDensitySnapshot CaptureSnapshot(int3? minBrick, int3? maxBrick, TerrainDensityEdit stagedEdit = null)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (stagedEdit != null && (!ReferenceEquals(stagedEdit.Owner, this) || stagedEdit.BaseRevision != revision))
                throw new InvalidOperationException("Mesh staging requires this world's current pending edit.");
            var captured = new Dictionary<int3, TerrainDensityBrick>();
            foreach (var pair in bricks)
            {
                if (minBrick.HasValue && (math.any(pair.Key < minBrick.Value) || math.any(pair.Key > maxBrick.Value))) continue;
                captured.Add(pair.Key, pair.Value);
            }
            if (stagedEdit != null) foreach (var pair in stagedEdit.Replacements)
            {
                if (minBrick.HasValue && (math.any(pair.Key < minBrick.Value) || math.any(pair.Key > maxBrick.Value))) continue;
                captured[pair.Key] = pair.Value;
            }
            long bytes = 0;
            foreach (var value in captured.Values) bytes += value.PayloadBytes;
            // Conservative accounting: reserve referenced payload even when it is still shared.
            Admit(bytes); reservedBytes += bytes;
            return new TerrainDensitySnapshot(this, Profile, generator, revision, captured, bytes, minimumExcavationY, stagedEdit != null);
        }
    }

    public TerrainDensityHalo CaptureMeshInput(int3 meshBrick, TerrainDensityEdit stagedEdit = null)
    {
        int3 origin = TerrainDensityCoordinates.Origin(meshBrick);
        if (math.any(origin < int.MinValue + 1) || math.any(origin > int.MaxValue - 17))
            throw new ArgumentOutOfRangeException(nameof(meshBrick));
        int3 start = origin - 1, end = origin + 17;
        const int width = 19, bytes = width * width * width * sizeof(short);
        lock (gate) { ThrowIfDisposed(); Admit(bytes); reservedBytes += bytes; }
        try
        {
            using var snapshot = CaptureSnapshot(TerrainDensityCoordinates.BrickOf(start), TerrainDensityCoordinates.BrickOf(end), stagedEdit);
            var values = new short[width * width * width];
            for (int z = 0; z < width; z++) for (int y = 0; y < width; y++) for (int x = 0; x < width; x++)
                values[x + width * (y + width * z)] = snapshot.ReadEncoded(start + new int3(x, y, z));
            return new TerrainDensityHalo(this, start, snapshot.Revision, values, stagedEdit != null);
        }
        catch { ReleaseReservation(bytes); throw; }
    }

    public static TerrainDensityWorld FromSnapshot(TerrainDensitySnapshot snapshot, long memoryBudgetBytes = DefaultBudgetBytes)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        snapshot.RequireAlive();
        var world = new TerrainDensityWorld(snapshot.Profile, snapshot.Generator, memoryBudgetBytes, snapshot.MinimumRemovalY);
        foreach (var pair in snapshot.Bricks)
        {
            world.Admit(pair.Value.PayloadBytes);
            world.bricks.Add(pair.Key, pair.Value); world.residentBytes += pair.Value.PayloadBytes;
        }
        world.revision = snapshot.Revision;
        return world;
    }

    // Startup-only root publication: IO must finish before this single assignment.
    internal void RestoreCommitted(Dictionary<int3, TerrainDensityBrick> restored, long savedRevision)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (editing || revision != 0 || bricks.Count != 0 || reservedBytes != 0 || savedRevision < 0)
                throw new InvalidOperationException("Terrain restore requires a fresh, unleased world.");
            long bytes = 0;
            foreach (var brick in restored.Values) bytes = checked(bytes + brick.PayloadBytes);
            if(bytes>residentLimit)throw new TerrainDensityCapacityException();
            Admit(bytes);
            var root = new Dictionary<int3, TerrainDensityBrick>(restored);
            bricks = root; residentBytes = bytes; revision = savedRevision;
        }
    }

    // The conservative snapshot accounting charges shared payload twice. Persistent hosts
    // reserve room for a full save lease plus bounded codec scratch before accepting edits.
    internal void RequirePersistenceHeadroom()
    {
        lock(gate)
        {
            ThrowIfDisposed();long limit=(budgetBytes-64*1024)/2;
            if(limit<0||residentBytes>limit)throw new TerrainDensityCapacityException();
            residentLimit=limit;
        }
    }

    private void Admit(long bytes)
    {
        if (bytes < 0 || bytes > budgetBytes - residentBytes - reservedBytes) throw new TerrainDensityCapacityException();
    }

    internal void ReleaseReservation(long bytes) { lock (gate) reservedBytes -= bytes; }
    internal IDisposable ReserveScratch(long bytes)
    {
        lock(gate){ThrowIfDisposed();Admit(bytes);reservedBytes+=bytes;}
        return new ScratchLease(this,bytes);
    }
    private sealed class ScratchLease : IDisposable
    {
        private TerrainDensityWorld owner;private readonly long bytes;
        internal ScratchLease(TerrainDensityWorld owner,long bytes){this.owner=owner;this.bytes=bytes;}
        public void Dispose()=>System.Threading.Interlocked.Exchange(ref owner,null)?.ReleaseReservation(bytes);
    }
    private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(TerrainDensityWorld)); }
    public void Dispose() { lock (gate) { disposed = true; bricks.Clear(); residentBytes = 0; } }
}

/// <summary>Staged copy-on-write edit. Disposal without commit is cancellation.</summary>
public sealed class TerrainDensityEdit : IDisposable
{
    internal TerrainDensityWorld Owner;
    internal readonly long ReservedBytes;
    internal Dictionary<int3, TerrainDensityBrick> Replacements { get; private set; }
    public long BaseRevision { get; }
    public long ChangedSamples { get; }
    public IReadOnlyList<int3> ChangedBricks { get; }
    public int ChangedBrickCount => ChangedBricks.Count;
    public TerrainDensityBrush Brush { get; }

    internal TerrainDensityEdit(TerrainDensityWorld owner, long revision, long bytes,
        Dictionary<int3, TerrainDensityBrick> replacements, long changed, TerrainDensityBrush brush)
    {
        Owner = owner; BaseRevision = revision; ReservedBytes = bytes; Replacements = replacements;
        ChangedSamples = changed; Brush = brush;
        var coordinates = new List<int3>(replacements.Keys);
        coordinates.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
        ChangedBricks = coordinates.AsReadOnly();
    }

    internal void Consume() { Owner = null; Replacements = null; }
    public void Dispose() => Owner?.Cancel(this);
}

/// <summary>Stable leased snapshot. Pending mesh overlays are explicitly ineligible for saving.</summary>
public sealed class TerrainDensitySnapshot : IDisposable, ITerrainDensityField
{
    private TerrainDensityWorld owner;
    private readonly long reservedBytes;
    internal readonly ITerrainDensityGenerator Generator;
    internal readonly double MinimumRemovalY;
    internal Dictionary<int3, TerrainDensityBrick> Bricks { get; private set; }
    public TerrainDensityProfile Profile { get; }
    public long Revision { get; }
    public bool IncludesPendingEdit { get; }
    public int ModifiedBrickCount { get { RequireAlive(); return Bricks.Count; } }
    public int UniformBrickCount
    {
        get { RequireAlive(); int count = 0; foreach (var brick in Bricks.Values) if (brick.IsUniform) count++; return count; }
    }

    internal TerrainDensitySnapshot(TerrainDensityWorld owner, TerrainDensityProfile profile, ITerrainDensityGenerator generator,
        long revision, Dictionary<int3, TerrainDensityBrick> bricks, long bytes, double minimumRemovalY, bool includesPendingEdit = false)
    {
        this.owner = owner; Profile = profile; Generator = generator; Revision = revision;
        Bricks = bricks; reservedBytes = bytes; MinimumRemovalY = minimumRemovalY;
        IncludesPendingEdit = includesPendingEdit;
    }

    public short ReadEncoded(int3 sample)
    {
        RequireAlive(); Bricks.TryGetValue(TerrainDensityCoordinates.BrickOf(sample), out var brick);
        return TerrainDensityWorld.Read(brick, sample, Profile, Generator);
    }
    public IDisposable ReserveScratch(long bytes) { RequireAlive();return owner.ReserveScratch(bytes); }
    public void FillMeshHalo(int3 first,short[] destination) => FillMeshHalo(first,destination,null);
    internal void FillMeshHalo(int3 first,short[] destination,TerrainSculptCoveragePolicy? visual)
    {
        RequireAlive();const int width=19;
        if(destination==null||destination.Length!=width*width*width||math.any(first<int.MinValue+32)||math.any(first>int.MaxValue-32))
            throw new ArgumentException("A mesh halo needs exactly 19 cubed samples and valid addresses.");
        var height=Generator as ITerrainHeightDensityGenerator;
        double spacing=Profile.Spacing;
        for(int z=0;z<width;z++)for(int x=0;x<width;x++)
        {
            int sx=first.x+x,sz=first.z+z;
            double wx=sx*spacing,wz=sz*spacing;
            double column=height!=null?height.SampleHeight(wx,wz):0;
            int bx=TerrainDensityCoordinates.FloorDiv(sx,16),bz=TerrainDensityCoordinates.FloorDiv(sz,16),lastY=int.MinValue;
            TerrainDensityBrick brick=null;
            for(int y=0;y<width;y++)
            {
                int sy=first.y+y,by=TerrainDensityCoordinates.FloorDiv(sy,16);
                if(by!=lastY){Bricks.TryGetValue(new int3(bx,by,bz),out brick);lastY=by;}
                double3 p=new double3(wx,sy*spacing,wz);
                short original=0;
                if(brick==null||visual.HasValue)original=Profile.Encode(height!=null?p.y-column:Generator.SampleDensity(p));
                short value=brick==null?original:brick.Read(TerrainDensityCoordinates.Index(new int3(sx-bx*16,sy-by*16,sz-bz*16)));
                if(visual.HasValue)value=(short)Math.Round(math.lerp((double)original,value,visual.Value.Weight(p)),MidpointRounding.AwayFromZero);
                destination[x+width*(y+width*z)]=value;
            }
        }
    }

    public short[] CopyBrickSamples(int3 coord)
    {
        RequireAlive(); int3 origin = TerrainDensityCoordinates.Origin(coord);
        var result = new short[TerrainDensityProfile.SamplesPerBrick];
        for (int i = 0; i < result.Length; i++) result[i] = ReadEncoded(origin + TerrainDensityCoordinates.LocalFromIndex(i));
        return result;
    }

    public bool HasOverrides(int3 minimum, int3 maximum)
    {
        RequireAlive();int3 first=TerrainDensityCoordinates.BrickOf(minimum),last=TerrainDensityCoordinates.BrickOf(maximum);
        if(math.any(last<first))return false;
        double count=((double)last.x-first.x+1)*((double)last.y-first.y+1)*((double)last.z-first.z+1);
        if(count<=Bricks.Count)
        {
            for(int z=first.z;z<=last.z;z++)for(int y=first.y;y<=last.y;y++)for(int x=first.x;x<=last.x;x++)
                if(Bricks.ContainsKey(new int3(x,y,z)))return true;
            return false;
        }
        foreach(var key in Bricks.Keys)if(math.all(key>=first)&&math.all(key<=last))return true;
        return false;
    }

    /// <summary>Prove a cell box has one sign, accounting for every intersecting override owner.</summary>
    public bool IsSurfaceFree(TerrainDensityCellBounds cells)
    {
        RequireAlive();int3 first=TerrainDensityCoordinates.BrickOf(cells.Min),last=TerrainDensityCoordinates.BrickOf(cells.Max);
        int low=32767,high=-32767;bool baselineNeeded=false;
        for(int z=first.z;z<=last.z;z++)for(int y=first.y;y<=last.y;y++)for(int x=first.x;x<=last.x;x++)
        {
            if(!Bricks.TryGetValue(new int3(x,y,z),out var brick)){baselineNeeded=true;continue;}
            if(!brick.IsUniform)return false;
            int value=brick.Read(0);low=Math.Min(low,value);high=Math.Max(high,value);
        }
        if(baselineNeeded)
        {
            if(!(Generator is ITerrainDensityBounds bounds)||!bounds.TryGetDensityBounds(
                TerrainDensityCoordinates.ToWorld(cells.Min,Profile),TerrainDensityCoordinates.ToWorld(cells.Max,Profile),out double a,out double b))return false;
            if(a>b)throw new InvalidOperationException("Generator returned inverted conservative bounds.");
            low=Math.Min(low,Profile.Encode(a));high=Math.Max(high,Profile.Encode(b));
        }
        return high<0||low>=0;
    }

    public void RequireAlive() { if (Bricks == null) throw new ObjectDisposedException(nameof(TerrainDensitySnapshot)); }
    public void Dispose()
    {
        var previous = System.Threading.Interlocked.Exchange(ref owner, null);
        if (previous == null) return;
        Bricks = null; previous.ReleaseReservation(reservedBytes);
    }
}

/// <summary>19^3 immutable mesh inputs, including the shared corner and central-difference halo.</summary>
public sealed class TerrainDensityHalo : IDisposable
{
    public const int Width = 19;
    private TerrainDensityWorld owner;
    private short[] values;
    public int3 FirstSample { get; }
    public long Revision { get; }
    public bool IncludesPendingEdit { get; }
    internal TerrainDensityHalo(TerrainDensityWorld owner, int3 first, long revision, short[] samples, bool includesPendingEdit)
    { this.owner = owner; FirstSample = first; Revision = revision; values = samples; IncludesPendingEdit = includesPendingEdit; }
    public short Read(int x, int y, int z)
    {
        if (values == null) throw new ObjectDisposedException(nameof(TerrainDensityHalo));
        if ((uint)x >= Width || (uint)y >= Width || (uint)z >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        return values[x + Width * (y + Width * z)];
    }
    public void Dispose()
    {
        var previous = System.Threading.Interlocked.Exchange(ref owner, null);
        if (previous == null) return;
        values = null; previous.ReleaseReservation(Width * Width * Width * sizeof(short));
    }
}
