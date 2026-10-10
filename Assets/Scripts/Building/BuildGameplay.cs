using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using Object = UnityEngine.Object;

public sealed class BuildGameplay : IDisposable
{
    private static readonly ProfilerMarker UpdateMarker = new("FS.Building.CollisionStreaming");
    private readonly BuildSession session;
    private readonly GameObject root = new("Building Collision (Pooled)");
    private readonly Dictionary<ulong, BuildGameplayProxy> active = new();
    private readonly Stack<BuildGameplayProxy> pool = new();
    private readonly List<BuildPieceRecord> candidates = new();
    private readonly List<ulong> release = new();
    private Vector3 lastFocus = Vector3.positiveInfinity;
    private float nextScan;
    private int revision = -1;
    public int ActiveCount => active.Count;
    public bool TryGetProxy(ulong id,out BuildGameplayProxy proxy) => active.TryGetValue(id,out proxy);
    public BuildGameplay(BuildSession session) { this.session = session; }
    public void Remove(ulong id)
    {
        if (!active.TryGetValue(id, out var proxy)) return;
        active.Remove(id); proxy.Unbind();
        if (pool.Count < 128) pool.Push(proxy); else BuildLifetime.Destroy(proxy.gameObject);
        Physics.SyncTransforms();
    }
    public void Update(Vector3 focus, float radius, float releaseRadius, int activationLimit)
    {
        using var scope = UpdateMarker.Auto();
        release.Clear();
        foreach (var pair in active)
            if (!session.TryGet(pair.Key, out var piece) || BuildGeometry.Distance(piece.Definition.LocalBounds, piece.Origin, piece.WorldYawStep, focus) > releaseRadius) release.Add(pair.Key);
        bool changed = release.Count > 0;
        foreach(var pair in active)if(session.TryGet(pair.Key,out var current) && pair.Value.BoundRevision!=current.ResolvedRevision)
        {pair.Value.Bind(current);changed=true;}
        foreach (ulong id in release) { var proxy = active[id]; active.Remove(id); proxy.Unbind(); if (pool.Count < 128) pool.Push(proxy); else BuildLifetime.Destroy(proxy.gameObject); }
        if (revision != session.Revision || Time.unscaledTime >= nextScan || (focus - lastFocus).sqrMagnitude > 1)
        {
            session.Query(new Bounds(focus, Vector3.one * (radius * 2)), candidates);
            candidates.Sort((a, b) => a.WorldBounds.SqrDistance(focus).CompareTo(b.WorldBounds.SqrDistance(focus)));
            lastFocus = focus; nextScan = Time.unscaledTime + .1f; revision = session.Revision;
        }
        int activated = 0;
        foreach (var piece in candidates)
        {
            if (activated >= activationLimit) break;
            if (!session.TryGet(piece.Id, out _) || active.ContainsKey(piece.Id) || BuildGeometry.Distance(piece.Definition.LocalBounds, piece.Origin, piece.WorldYawStep, focus) > radius) continue;
            var proxy = pool.Count > 0 ? pool.Pop() : Create();
            proxy.Bind(piece); active.Add(piece.Id, proxy); activated++; changed = true;
        }
        if (changed) Physics.SyncTransforms();
    }
    private BuildGameplayProxy Create()
    {
        var obj = new GameObject("Build collision"); obj.transform.SetParent(root.transform, false);
        return obj.AddComponent<BuildGameplayProxy>();
    }
    public void Dispose()
    {
        // Destroy is deferred in Play mode; retired pools must stop colliding immediately.
        foreach(var proxy in active.Values)proxy.Unbind();
        if(root!=null){root.SetActive(false);BuildLifetime.Destroy(root);}
        active.Clear();pool.Clear();
    }
}

internal static class BuildLifetime
{
    public static void Destroy(Object obj)
    { if (obj == null) return; if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
}
