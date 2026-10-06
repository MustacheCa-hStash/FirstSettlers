using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum FoliagePublicationKind { Flower, LilyPad, Cattail, Clover, Dandelion, Bush, Rock }

/// <summary>One cancellable publication. Builds final batches in slices and swaps ownership only when complete.</summary>
public sealed class FoliagePublication : IDisposable
{
    private const int SliceSize = 128, BatchSize = 1023;
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
        Record = record; Runtime = runtime; Kind = kind;
        target = runtime.FoliageRuntime; data = record.FoliageData;
        source = Source(data, kind); revision = Revision(data, kind); count = ((ICollection)source).Count;
        heights = record.HeightMap; plan = record.WorldFeaturePlan;
        publicationVersion = target.PublicationVersion; transform = runtime.RootTransform.localToWorldMatrix;
        prefabCount = cloverPrefabCount; this.meshLocal = meshLocal;
        steps = kind == FoliagePublicationKind.Bush ? target.BuildBushesIncrementally(data.bushInstances) :
            kind == FoliagePublicationKind.Rock ? target.BuildRocksIncrementally(data.rockInstances) : BuildBatches();
    }

    public bool Matches(ChunkRecord record, ChunkRuntime runtime, int cloverPrefabCount)
    {
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
        LastSliceInstances = 0;
        if (!Matches(Record, Runtime, prefabCount)) { Dispose(); return true; }
        if (steps.MoveNext()) return false;
        Published = true; Dispose(); return true;
    }

    private IEnumerator<bool> BuildBatches()
    {
        int groupCount = Kind == FoliagePublicationKind.Flower ? 3 :
            Kind == FoliagePublicationKind.Clover ? Mathf.Max(1, prefabCount) : 1;
        var counts = new int[groupCount];
        for (int i = 0; i < count;)
        {
            int end = Mathf.Min(count, i + SliceSize);
            for (; i < end; i++) counts[Group(i)]++;
            yield return true;
        }
        var matrices = new List<Matrix4x4[]>[groupCount];
        var values = new List<Vector4[]>[groupCount];
        var flowers = new List<FlowerRenderBatch>(); var clover = new List<CloverRenderBatch>();
        var dandelions = new List<GrassRenderBatch>(); var water = new List<Matrix4x4[]>();
        for (int group = 0; group < groupCount; group++)
        {
            matrices[group] = new(); values[group] = new();
            for (int remaining = counts[group]; remaining > 0; remaining -= BatchSize)
            {
                var m = new Matrix4x4[Mathf.Min(BatchSize, remaining)];
                Vector4[] v = Kind == FoliagePublicationKind.LilyPad || Kind == FoliagePublicationKind.Cattail ? null : new Vector4[m.Length];
                matrices[group].Add(m); values[group].Add(v);
                if (Kind == FoliagePublicationKind.Flower) flowers.Add(new FlowerRenderBatch(m, v, group == 1, group == 2));
                else if (Kind == FoliagePublicationKind.Clover) clover.Add(new CloverRenderBatch(group, m, v));
                else if (Kind == FoliagePublicationKind.Dandelion) dandelions.Add(new GrassRenderBatch(m, v));
                else water.Add(m);
                yield return true;
            }
        }
        var offsets = new int[groupCount];
        for (int i = 0; i < count;)
        {
            int end = Mathf.Min(count, i + SliceSize);
            for (; i < end; i++)
            {
                int group = Group(i), offset = offsets[group]++, batch = offset / BatchSize, index = offset % BatchSize;
                Sample(i, out Matrix4x4 m, out Vector4 v);
                matrices[group][batch][index] = m;
                if (values[group][batch] != null) values[group][batch][index] = v;
                LastSliceInstances++;
            }
            yield return true;
        }
        // Constant-time publication: no final list-to-array or whole-chunk batch copy.
        switch (Kind)
        {
            case FoliagePublicationKind.Flower: target.PublishFlowers(flowers); break;
            case FoliagePublicationKind.Clover: target.PublishClover(clover); break;
            case FoliagePublicationKind.Dandelion: target.PublishDandelions(dandelions); break;
            default: target.PublishWaterPlants(Kind == FoliagePublicationKind.Cattail, water); break;
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
    public void Dispose() { if (disposed) return; disposed = true; steps.Dispose(); }
}
