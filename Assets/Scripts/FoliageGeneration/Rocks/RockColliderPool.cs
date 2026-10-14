using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>World-owned collision/query pool, independent of chunk renderer culling and hierarchy visibility.</summary>
public sealed class RockColliderPool : IDisposable
{
    private sealed class Template
    {
        public GameObject Prefab;
        public readonly Stack<RockQueryTarget> Free = new();
    }
    private struct Active { public Template Template; public RockQueryTarget Body; }
    private struct Candidate : IComparable<Candidate>
    {
        public RockRenderSet.Entry Entry; public float Distance;
        public int CompareTo(Candidate other) => Distance.CompareTo(other.Distance);
    }
    private readonly HashSet<RockRenderSet> sets = new();
    // Coatings share physical geometry, so moving between biomes reuses the same bodies.
    private readonly Dictionary<(Mesh Hull, GameObject LegacyPrefab), Template> templates = new();
    private readonly Dictionary<RockRenderSet.Entry, Active> active = new();
    private readonly List<RockRenderSet.Entry> releases = new();
    private readonly List<Candidate> candidates = new();
    private readonly TreeSettings settings;
    private GameObject root;
    private double nextScan;
    private Vector3 lastFocus;
    private bool dirty = true, disposed;
    private int nextCandidate;
    private static readonly ProfilerMarker UpdateMarker = new("FS.Rocks.CollisionPool");
    private static readonly ProfilerMarker ScanMarker = new("FS.Rocks.CollisionScan"),ActivationMarker = new("FS.Rocks.CollisionActivate");
    private float observedActivation, observedRelease;
    public int LastScanEntries { get; private set; }
    public int ScanCount { get; private set; }
    public int ActiveCount => active.Count;
    public int PooledCount { get; private set; }
    public int CreatedCount { get; private set; }
    public float ActivationRadius => Mathf.Max(1, settings != null ? settings.rockColliderActivationRadius : 28);
    public float ReleaseRadius => Mathf.Max(ActivationRadius + 2, settings != null ? settings.rockColliderReleaseRadius : 36);
    public RockColliderPool(TreeSettings settings) { this.settings = settings; }
    public void Register(RockRenderSet set) { if (!disposed) { sets.Add(set); dirty = true; } }
    public void Unregister(RockRenderSet set)
    {
        if (set == null) return;
        sets.Remove(set);
        foreach (var entry in set.Entries) Release(entry);
        dirty = true;
    }
    private static float Distance(Vector3 a, Vector3 b)
    { float x = a.x-b.x, z = a.z-b.z; return x*x+z*z; }
    public void Update(Vector3 focus, double now)
    {
        if (disposed) return;
        using var sample = UpdateMarker.Auto();
        bool enabled = settings == null || settings.enableRockColliders;
        if (observedActivation != ActivationRadius || observedRelease != ReleaseRadius)
        { dirty=true;observedActivation=ActivationRadius;observedRelease=ReleaseRadius; }
        releases.Clear();
        foreach (var pair in active)
            if (!enabled || Distance(pair.Key.Position, focus) > ReleaseRadius * ReleaseRadius) releases.Add(pair.Key);
        bool changed = releases.Count > 0;
        foreach (var entry in releases) Release(entry);
        if (!enabled) { candidates.Clear();nextCandidate=0;dirty=true;if(changed)Physics.SyncTransforms();return; }
        float moved = Distance(lastFocus, focus);
        if (dirty || moved >= 1 || (now >= nextScan && moved > .0001f))
        {
            using var scan=ScanMarker.Auto();LastScanEntries=0;ScanCount++;
            candidates.Clear(); nextCandidate = 0;
            foreach (var set in sets)
            {
                var bound = set.CollisionBounds;var flatFocus=focus;flatFocus.y=bound.center.y;
                if(bound.SqrDistance(flatFocus)>ActivationRadius*ActivationRadius)continue;
                foreach (var entry in set.Entries)
                {
                    LastScanEntries++;
                    float distance = Distance(entry.Position, focus);
                    if (!active.ContainsKey(entry) && distance <= ActivationRadius * ActivationRadius)
                        candidates.Add(new Candidate { Entry = entry, Distance = distance });
                }
            }
            candidates.Sort(); dirty = false; nextScan = now + .1; lastFocus = focus;
        }
        int activated = 0;
        double started = Time.realtimeSinceStartupAsDouble;
        int maxActive = Mathf.Max(1, settings != null ? settings.maxActiveRockColliders : 256);
        int maxActivations = Mathf.Max(1, settings != null ? settings.rockColliderActivationsPerFrame : 8);
        while (nextCandidate < candidates.Count && activated < maxActivations && active.Count < maxActive)
        {
            using var activation=ActivationMarker.Auto();
            if (activated > 0 && (Time.realtimeSinceStartupAsDouble-started)*1000 >= .5) break;
            var entry = candidates[nextCandidate++].Entry;
            if (active.ContainsKey(entry) || Distance(entry.Position, focus) > ActivationRadius*ActivationRadius) continue;
            // An unregister invalidates the candidate snapshot before its next consumption.
            var template = GetTemplate(entry.Prefab);
            RockQueryTarget body;
            if (template.Free.Count > 0) { body = template.Free.Pop(); PooledCount--; }
            else { body = CreateBody(template); CreatedCount++; }
            body.transform.SetPositionAndRotation(entry.Position, entry.Rotation);
            body.transform.localScale = entry.Scale;
            body.CopyAuthoring(entry.Prefab.GetComponent<RockQueryTarget>());
            body.Initialize(entry.Placement); body.gameObject.SetActive(true);
            active.Add(entry, new Active { Template = template, Body = body }); activated++; changed = true;
        }
        if (changed) Physics.SyncTransforms();
    }
    private Template GetTemplate(GameObject prefab)
    {
        var hull = prefab.GetComponent<RockPreset>()?.collisionHull;
        var key = (hull, hull != null ? null : prefab);
        if (!templates.TryGetValue(key, out var value))
            templates.Add(key, value = new Template { Prefab = prefab });
        return value;
    }
    private RockQueryTarget CreateBody(Template template)
    {
        if (root == null) root = new GameObject("Rock Collision and Queries (Pooled)");
        var body = new GameObject(template.Prefab.name + " collision") { layer = GameplayLayers.WorldSolid };
        body.SetActive(false); body.transform.SetParent(root.transform, false);
        var query = body.AddComponent<RockQueryTarget>();
        query.CopyAuthoring(template.Prefab.GetComponent<RockQueryTarget>());
        var preset = template.Prefab.GetComponent<RockPreset>();
        if (preset != null && preset.collisionHull != null)
        {
            var collider = body.AddComponent<MeshCollider>(); collider.convex = true; collider.sharedMesh = preset.collisionHull;
        }
        else
        {
            // Compatibility with authored legacy prefabs: copy collider shapes only, never renderer/scripts.
            foreach (var source in template.Prefab.GetComponentsInChildren<Collider>(true))
            {
                if (source.isTrigger || !source.enabled) continue;
                var child = new GameObject("Solid") { layer = GameplayLayers.WorldSolid };
                child.transform.SetParent(body.transform, false);
                var matrix = template.Prefab.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
                child.transform.localPosition = matrix.GetColumn(3); child.transform.localRotation = matrix.rotation;
                child.transform.localScale = matrix.lossyScale;
                Collider copy = null;
                if (source is BoxCollider box) { var c = child.AddComponent<BoxCollider>(); c.center=box.center;c.size=box.size;copy=c; }
                else if (source is SphereCollider sphere) { var c=child.AddComponent<SphereCollider>();c.center=sphere.center;c.radius=sphere.radius;copy=c; }
                else if (source is CapsuleCollider capsule) { var c=child.AddComponent<CapsuleCollider>();c.center=capsule.center;c.radius=capsule.radius;c.height=capsule.height;c.direction=capsule.direction;copy=c; }
                else if (source is MeshCollider mesh) { var c=child.AddComponent<MeshCollider>();c.sharedMesh=mesh.sharedMesh;c.convex=mesh.convex;copy=c; }
                if (copy != null) copy.sharedMaterial=source.sharedMaterial;
            }
        }
        return query;
    }
    private void Release(RockRenderSet.Entry entry)
    {
        if (!active.Remove(entry, out var value)) return;
        value.Body.Release(); value.Body.gameObject.SetActive(false);
        int maxPool = Mathf.Max(0, settings != null ? settings.maxPooledRockColliders : 64);
        if (PooledCount < maxPool) { value.Template.Free.Push(value.Body); PooledCount++; }
        else Destroy(value.Body.gameObject);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed=true; sets.Clear(); active.Clear(); templates.Clear(); candidates.Clear();
        PooledCount=0; if (root != null) Destroy(root); root=null;
    }
    private static void Destroy(GameObject obj)
    { obj.SetActive(false); if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
}
