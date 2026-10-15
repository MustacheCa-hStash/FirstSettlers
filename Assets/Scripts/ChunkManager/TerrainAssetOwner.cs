using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Profiling;
using UnityEngine;

/// <summary>Owns generated terrain assets. Slot references prevent destroying an aliased asset twice.</summary>
internal sealed class TerrainAssetOwner : IDisposable
{
    private readonly Dictionary<UnityEngine.Object, int> references = new();
    private static readonly ProfilerMarker ReleaseMarker = new("FS.Streaming.Ownership.ReleaseGeneratedAssets");

    public void Replace(UnityEngine.Object previous, UnityEngine.Object next)
    {
        if (ReferenceEquals(previous, next)) return;
        Retain(next); Release(previous);
    }

    public void Replace(Texture2D[] previous, Texture2D[] next)
    {
        if (ReferenceEquals(previous, next)) return;
        if (next != null) foreach (var asset in next) Retain(asset);
        if (previous != null) foreach (var asset in previous) Release(asset);
    }

    public void Replace(Mesh previousTerrain, Mesh previousWater, Mesh nextTerrain, Mesh nextWater)
    {
        Retain(nextTerrain); Retain(nextWater);
        Release(previousTerrain); Release(previousWater);
    }

    private void Retain(UnityEngine.Object asset)
    {
        if (ReferenceEquals(asset, null)) return;
        references.TryGetValue(asset, out int count);
        references[asset] = count + 1;
    }

    private void Release(UnityEngine.Object asset)
    {
        if (ReferenceEquals(asset, null) || !references.TryGetValue(asset, out int count)) return;
        if (count > 1) references[asset] = count - 1;
        else { references.Remove(asset); Destroy(asset); }
    }

    public void Dispose()
    {
        using var scope = ReleaseMarker.Auto();
        foreach (var asset in references.Keys) Destroy(asset);
        references.Clear();
    }

    // Only generated assets transferred into a record or staged for publication enter this owner.
    public static void Destroy(UnityEngine.Object asset)
    {
        if (asset == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
        else UnityEngine.Object.DestroyImmediate(asset);
    }
}

/// <summary>Session-wide tokens survive removal/recreation of records at the same coordinate.</summary>
internal static class TerrainRequestIdentity
{
    private static int next;
    public static int Next()
    {
        int result = Interlocked.Increment(ref next);
        return result == 0 ? Interlocked.Increment(ref next) : result;
    }
}

internal enum TerrainRequestKind { Data, Far, Lod, Collider }
internal readonly struct TerrainRequestFailure
{
    public readonly TerrainRequestKind Kind;
    public readonly ChunkCoord Coord;
    public readonly int Version, Lod, PatchSize;
    public readonly bool Macro;
    public TerrainRequestFailure(TerrainRequestKind kind, ChunkCoord coord, int version, int lod, int patchSize, bool macro)
    { Kind = kind; Coord = coord; Version = version; Lod = lod; PatchSize = patchSize; Macro = macro; }
}
