using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public enum FoliagePublicationKind { Flower, LilyPad, Cattail, Clover, Dandelion, Bush, Rock }

/// <summary>One cancellable publication. Builds final batches in slices and swaps ownership only when complete.</summary>
public sealed class FoliagePublication : IDisposable
{
    private const int SliceSize = 128, BatchSize = 1023;
    private static readonly ProfilerMarker[] KindMarkers =
    {
        new("FS.Streaming.Foliage.Publication.Flower"), new("FS.Streaming.Foliage.Publication.LilyPad"),
        new("FS.Streaming.Foliage.Publication.Cattail"), new("FS.Streaming.Foliage.Publication.Clover"),
        new("FS.Streaming.Foliage.Publication.Dandelion"), new("FS.Streaming.Foliage.Publication.Bush"),
        new("FS.Streaming.Foliage.Publication.Rock")
    };
    private static readonly ProfilerMarker UnknownKindMarker = new("FS.Streaming.Foliage.Publication.UnknownKind");
    private static readonly ProfilerMarker ValidateMarker = new("FS.Streaming.Foliage.Publication.ValidateInputs");
    private static readonly ProfilerMarker PrepareMarker = new("FS.Streaming.Foliage.Publication.PrepareBatches");
    private static readonly ProfilerMarker CountMarker = new("FS.Streaming.Foliage.Publication.CountGroups");
    private static readonly ProfilerMarker AllocateMarker = new("FS.Streaming.Foliage.Publication.AllocateBatch");
    private static readonly ProfilerMarker TransformMarker = new("FS.Streaming.Foliage.Publication.TransformInstances");
    private static readonly ProfilerMarker CommitMarker = new("FS.Streaming.Foliage.Publication.CommitBatches");
    private static readonly ProfilerMarker DisposeMarker = new("FS.Streaming.Foliage.Publication.Dispose");
    private static readonly ProfilerMarker CaptureMarker = new("FS.Streaming.Foliage.Publication.Setup.CaptureInputs");
    private static readonly ProfilerMarker TransformReadMarker = new("FS.Streaming.Foliage.Publication.Setup.ReadTransform");
    private static readonly ProfilerMarker RootReadMarker = new("FS.Streaming.Foliage.Publication.Setup.GetRootTransform");
    private static readonly ProfilerMarker MatrixReadMarker = new("FS.Streaming.Foliage.Publication.Setup.ReadTransformMatrix");
    private static readonly ProfilerMarker GroundIteratorMarker = new("FS.Streaming.Foliage.Publication.Setup.CreateGroundIterator");
    private static readonly ProfilerMarker BushIteratorMarker = new("FS.Streaming.Foliage.Publication.Setup.CreateBushIterator");
    private static readonly ProfilerMarker RockIteratorMarker = new("FS.Streaming.Foliage.Publication.Setup.CreateRockIterator");
    private readonly ProfilerMarker kindMarker;
    public readonly ChunkRecord Record;
    public readonly ChunkRuntime Runtime;
    public readonly FoliagePublicationKind Kind;
    private readonly ChunkFoliageRuntime target;
    private readonly ChunkFoliageData data;
    private readonly object source, heights, plan;
    private readonly int revision, count, publicationVersion, prefabCount;
    private readonly Matrix4x4 transform, meshLocal;
    private readonly IEnumerator<bool> steps;
    private bool disposed;
    public bool Published { get; private set; }
    public int LastSliceInstances { get; private set; }

    public FoliagePublication(ChunkRecord record, ChunkRuntime runtime, FoliagePublicationKind kind,
        int cloverPrefabCount, Matrix4x4 meshLocal)
    {
        using (CaptureMarker.Auto())
        {
            Record = record; Runtime = runtime; Kind = kind;
            kindMarker = (uint)kind < KindMarkers.Length ? KindMarkers[(int)kind] : UnknownKindMarker;
            target = runtime.FoliageRuntime; data = record.FoliageData;
            source = Source(data, kind); revision = Revision(data, kind); count = ((ICollection)source).Count;
            heights = record.HeightMap; plan = record.WorldFeaturePlan;
            publicationVersion = target.PublicationVersion;
            prefabCount = cloverPrefabCount; this.meshLocal = meshLocal;
        }
        using (TransformReadMarker.Auto())
        {
            Transform root;
            using (RootReadMarker.Auto()) root = runtime.RootTransform;
            using (MatrixReadMarker.Auto()) transform = root.localToWorldMatrix;
        }
        if (kind == FoliagePublicationKind.Bush)
        {
            using (BushIteratorMarker.Auto()) steps = target.BuildBushesIncrementally(data.bushInstances);
        }
        else if (kind == FoliagePublicationKind.Rock)
        {
            using (RockIteratorMarker.Auto()) steps = target.BuildRocksIncrementally(data.rockInstances);
        }
        else
        {
            using (GroundIteratorMarker.Auto()) steps = BuildBatches();
        }
    }

    public bool Matches(ChunkRecord record, ChunkRuntime runtime, int cloverPrefabCount)
    {
        using var scope = ValidateMarker.Auto();
        return !disposed && ReferenceEquals(record, Record) && ReferenceEquals(runtime, Runtime) &&
            ReferenceEquals(record.FoliageData, data) && ReferenceEquals(Source(data, Kind), source) &&
            Revision(data, Kind) == revision && Generated(data, Kind) && ((ICollection)source).Count == count &&
            ReferenceEquals(record.HeightMap, heights) && ReferenceEquals(record.WorldFeaturePlan, plan) &&
            ReferenceEquals(runtime.FoliageRuntime, target) && target.PublicationVersion == publicationVersion &&
            runtime.RootTransform != null && runtime.RootTransform.localToWorldMatrix == transform &&
            (Kind != FoliagePublicationKind.Clover || cloverPrefabCount == prefabCount);
    }

    // True means finished (including invalidation). One call never transforms more than 128 instances,
    // allocates more than one 1023-instance batch, or instantiates more than one object.
    public bool Advance()
    {
        using var scope = kindMarker.Auto();
        LastSliceInstances = 0;
        if (!Matches(Record, Runtime, prefabCount)) { Dispose(); return true; }
        if (steps.MoveNext()) return false;
        Published = true; Dispose(); return true;
    }

    private IEnumerator<bool> BuildBatches()
    {
        int groupCount = Kind == FoliagePublicationKind.Flower ? 3 :
            Kind == FoliagePublicationKind.Clover ? Mathf.Max(1, prefabCount) : 1;
        int[] counts;
        using (PrepareMarker.Auto()) counts = new int[groupCount];
        for (int i = 0; i < count;)
        {
            int end = Mathf.Min(count, i + SliceSize);
            using (CountMarker.Auto())
                for (; i < end; i++) counts[Group(i)]++;
            yield return true;
        }
        List<Matrix4x4[]>[] matrices;
        List<Vector4[]>[] values;
        List<FlowerRenderBatch> flowers;
        List<CloverRenderBatch> clover;
        List<GrassRenderBatch> dandelions;
        List<Matrix4x4[]> water;
        using (PrepareMarker.Auto())
        {
            matrices = new List<Matrix4x4[]>[groupCount]; values = new List<Vector4[]>[groupCount];
            flowers = new(); clover = new(); dandelions = new(); water = new();
        }
        for (int group = 0; group < groupCount; group++)
        {
            using (PrepareMarker.Auto()) { matrices[group] = new(); values[group] = new(); }
            for (int remaining = counts[group]; remaining > 0; remaining -= BatchSize)
            {
                using (AllocateMarker.Auto())
                {
                    var m = new Matrix4x4[Mathf.Min(BatchSize, remaining)];
                    Vector4[] v = Kind == FoliagePublicationKind.LilyPad || Kind == FoliagePublicationKind.Cattail ? null : new Vector4[m.Length];
                    matrices[group].Add(m); values[group].Add(v);
                    if (Kind == FoliagePublicationKind.Flower) flowers.Add(new FlowerRenderBatch(m, v, group == 1, group == 2));
                    else if (Kind == FoliagePublicationKind.Clover) clover.Add(new CloverRenderBatch(group, m, v));
                    else if (Kind == FoliagePublicationKind.Dandelion) dandelions.Add(new GrassRenderBatch(m, v));
                    else water.Add(m);
                }
                // End the stage sample before yielding; a slice may resume in another frame.
                yield return true;
            }
        }
        int[] offsets;
        using (PrepareMarker.Auto()) offsets = new int[groupCount];
        for (int i = 0; i < count;)
        {
            int end = Mathf.Min(count, i + SliceSize);
            using (TransformMarker.Auto())
            {
                for (; i < end; i++)
                {
                    int group = Group(i), offset = offsets[group]++, batch = offset / BatchSize, index = offset % BatchSize;
                    Sample(i, out Matrix4x4 m, out Vector4 v);
                    matrices[group][batch][index] = m;
                    if (values[group][batch] != null) values[group][batch][index] = v;
                    LastSliceInstances++;
                }
            }
            yield return true;
        }
        // Constant-time publication: no final list-to-array or whole-chunk batch copy.
        using (CommitMarker.Auto())
        {
            switch (Kind)
            {
                case FoliagePublicationKind.Flower: target.PublishFlowers(flowers); break;
                case FoliagePublicationKind.Clover: target.PublishClover(clover); break;
                case FoliagePublicationKind.Dandelion: target.PublishDandelions(dandelions); break;
                default: target.PublishWaterPlants(Kind == FoliagePublicationKind.Cattail, water); break;
            }
        }
    }

    private int Group(int i)
    {
        if (Kind == FoliagePublicationKind.Flower)
        { var f = data.flowerInstances[i]; return f.isDaisyWeed ? 2 : f.isTallFlower ? 1 : 0; }
        return Kind == FoliagePublicationKind.Clover ? Mathf.Clamp(data.cloverInstances[i].prefabIndex, 0, Mathf.Max(1, prefabCount) - 1) : 0;
    }
    private void Sample(int i, out Matrix4x4 matrix, out Vector4 value)
    {
        value = Vector4.zero;
        switch (Kind)
        {
            case FoliagePublicationKind.Flower:
                var f = data.flowerInstances[i]; matrix = transform * Matrix4x4.TRS(f.localPosition, f.localRotation, f.localScale);
                value = new Vector4(f.petalColor.r, f.petalColor.g, f.petalColor.b, f.petalColor.a) / 255f; break;
            case FoliagePublicationKind.Clover:
                var c = data.cloverInstances[i]; matrix = transform * Matrix4x4.TRS(c.localPosition, c.localRotation, c.localScale);
                value = Variation(c.selectionRank); break;
            case FoliagePublicationKind.Dandelion:
                var d = data.dandelionInstances[i]; matrix = transform * Matrix4x4.TRS(d.localPosition, d.localRotation, d.localScale);
                value = Variation(d.selectionRank); break;
            case FoliagePublicationKind.LilyPad:
                var p = data.lilyPadInstances[i]; matrix = transform * Matrix4x4.TRS(p.localPosition, p.localRotation, Vector3.one * p.uniformScale) * meshLocal; break;
            default:
                var t = data.cattailInstances[i]; matrix = transform * Matrix4x4.TRS(t.localPosition, t.localRotation, Vector3.one * t.uniformScale) * meshLocal; break;
        }
    }
    private static Vector4 Variation(uint rank)
    { float phase = (rank & 0xffffffu) / 16777216f; float color = phase * 37.618034f; return new Vector4(phase, color - Mathf.Floor(color), 0, 0); }
    private static object Source(ChunkFoliageData d, FoliagePublicationKind k) => k switch
    {
        FoliagePublicationKind.Flower => d.flowerInstances, FoliagePublicationKind.Clover => d.cloverInstances,
        FoliagePublicationKind.Dandelion => d.dandelionInstances, FoliagePublicationKind.LilyPad => d.lilyPadInstances,
        FoliagePublicationKind.Cattail => d.cattailInstances, FoliagePublicationKind.Bush => d.bushInstances, _ => d.rockInstances
    };
    private static int Revision(ChunkFoliageData d, FoliagePublicationKind k) => k switch
    {
        FoliagePublicationKind.Flower => d.FlowersRevision, FoliagePublicationKind.Clover => d.CloverRevision,
        FoliagePublicationKind.Dandelion => d.DandelionsRevision, FoliagePublicationKind.LilyPad => d.LilyPadsRevision,
        FoliagePublicationKind.Cattail => d.CattailsRevision, FoliagePublicationKind.Bush => d.BushesRevision, _ => d.RocksRevision
    };
    private static bool Generated(ChunkFoliageData d, FoliagePublicationKind k) => k switch
    {
        FoliagePublicationKind.Flower => d.flowersGenerated, FoliagePublicationKind.Clover => d.cloverGenerated,
        FoliagePublicationKind.Dandelion => d.dandelionsGenerated, FoliagePublicationKind.LilyPad => d.lilyPadsGenerated,
        FoliagePublicationKind.Cattail => d.cattailsGenerated, FoliagePublicationKind.Bush => d.bushesGenerated, _ => d.rocksGenerated
    };
    public static bool IsReady(ChunkFoliageData data, FoliagePublicationKind kind) =>
        data != null && Source(data, kind) != null && Generated(data, kind);
    public void Dispose()
    {
        if (disposed) return;
        using var scope = DisposeMarker.Auto();
        disposed = true; steps.Dispose();
    }
}
