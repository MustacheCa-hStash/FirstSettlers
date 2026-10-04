using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Main-thread registry consumer. A single focus drives physical trunk proxies for now.</summary>
public sealed class TreeGameplayManager : IDisposable
{
    private sealed class Template
    {
        public GameObject Prefab;
        public Collider[] Colliders;
        public readonly Stack<TreeGameplayProxy> Pool = new();
    }
    private struct ActiveProxy { public Template Template; public TreeGameplayProxy Proxy; }
    private struct Candidate : IComparable<Candidate>
    {
        public TreeId Id;
        public float DistanceSquared;
        public int CompareTo(Candidate other) => DistanceSquared.CompareTo(other.DistanceSquared);
    }
    private static readonly ProfilerMarker UpdateMarker = new("FS.TreeGameplay.Update");
    private static readonly ProfilerMarker ScanMarker = new("FS.TreeGameplay.ScanRegistry");
    private static readonly ProfilerMarker ActivationMarker = new("FS.TreeGameplay.Activate");
    private readonly TreeRegistry registry;
    private readonly TreeSettings treeSettings;
    private readonly TreeGameplaySettings settings;
    private readonly float chunkWorldSize;
    private readonly Dictionary<GameObject, Template> templates = new();
    private readonly Dictionary<TreeId, ActiveProxy> active = new();
    private readonly List<TreeRecord> nearby = new();
    private readonly List<TreeId> releases = new();
    private readonly List<Candidate> candidates = new();
    private readonly GameObject root;
    private Vector3 lastScanPosition;
    private double nextScan;
    private bool scanDirty = true, disposed;
    private int nextCandidate;

    public Transform Root => root != null ? root.transform : null;
    public int ActiveCount => active.Count;
    public int PooledCount { get; private set; }
    public int LastActivationCount { get; private set; }
    public bool SyncedPhysicsThisUpdate { get; private set; }
    public float ActivationRadius => Mathf.Max(.05f, settings.activationRadiusChunks) * chunkWorldSize;
    public float ReleaseRadius => Mathf.Max(ActivationRadius + Mathf.Max(1f, chunkWorldSize * .05f),
        settings.releaseRadiusChunks * chunkWorldSize);

    public TreeGameplayManager(TreeRegistry registry, TreeSettings treeSettings, float chunkWorldSize)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.treeSettings = treeSettings ?? throw new ArgumentNullException(nameof(treeSettings));
        if (chunkWorldSize <= 0 || float.IsNaN(chunkWorldSize) || float.IsInfinity(chunkWorldSize))
            throw new ArgumentOutOfRangeException(nameof(chunkWorldSize));
        this.chunkWorldSize = chunkWorldSize;
        settings = treeSettings.gameplay ??= new TreeGameplaySettings();
        // World-space root, independent of chunk transforms, culling and renderer lifetime.
        root = new GameObject("Tree Gameplay (Pooled)");
        registry.ChunkChanged += OnChunkChanged;
    }

    public bool TryGetProxy(TreeId id, out TreeGameplayProxy proxy)
    {
        if (active.TryGetValue(id, out var entry)) { proxy = entry.Proxy; return true; }
        proxy = null; return false;
    }
    public void Update(Vector3 focus) => Update(focus, Time.realtimeSinceStartupAsDouble);

    // Explicit time also makes scheduling testable without depending on frame rate.
    public void Update(Vector3 focus, double now)
    {
        if (disposed) return;
        using var updateSample = UpdateMarker.Auto();
        LastActivationCount = 0; SyncedPhysicsThisUpdate = false;
        bool physicsChanged = false;
        releases.Clear();
        float releaseSquared = ReleaseRadius * ReleaseRadius;
        foreach (var pair in active)
        {
            if (!settings.enabled || !registry.TryGet(pair.Key, out var record) || record.State != TreeState.Standing ||
                DistanceSquared(record.WorldPosition, focus) > releaseSquared ||
                treeSettings.GetNearPrefab(record.Placement.variant) != pair.Value.Template.Prefab)
                releases.Add(pair.Key);
            else physicsChanged |= pair.Value.Proxy.Bind(record);
        }
        foreach (var id in releases) { Release(id); physicsChanged = true; }
        TrimPool();
        if (!settings.enabled)
        {
            candidates.Clear(); nextCandidate = 0; scanDirty = true;
        }
        else
        {
            if (scanDirty || now >= nextScan || DistanceSquared(lastScanPosition, focus) >= 1f)
                Scan(focus, now);
            using var activationSample = ActivationMarker.Auto();
            double started = Time.realtimeSinceStartupAsDouble;
            int processed = 0;
            float activationSquared = ActivationRadius * ActivationRadius;
            while (nextCandidate < candidates.Count && LastActivationCount < Mathf.Max(1, settings.maxActivationsPerFrame))
            {
                if (processed > 0 && settings.activationBudgetMs > 0 &&
                    (Time.realtimeSinceStartupAsDouble - started) * 1000 >= settings.activationBudgetMs) break;
                processed++;
                var candidate = candidates[nextCandidate++];
                if (active.ContainsKey(candidate.Id) || !registry.TryGet(candidate.Id, out var record) ||
                    record.State != TreeState.Standing || DistanceSquared(record.WorldPosition, focus) > activationSquared) continue;
                var template = GetTemplate(treeSettings.GetNearPrefab(record.Placement.variant));
                if (template == null || template.Colliders.Length == 0) continue;
                TreeGameplayProxy proxy;
                if (template.Pool.Count > 0) { proxy = template.Pool.Pop(); PooledCount--; }
                else proxy = CreateProxy(template);
                proxy.Bind(record);
                proxy.gameObject.name = "Tree " + record.Placement.variant + " [" + record.Id + "]";
                proxy.gameObject.SetActive(true);
                active.Add(record.Id, new ActiveProxy { Template = template, Proxy = proxy });
                LastActivationCount++; physicsChanged = true;
            }
        }
        // Auto Sync Transforms is disabled in this project. Flush only changed batches,
        // so same-frame CharacterController movement sees newly positioned colliders.
        if (physicsChanged) { Physics.SyncTransforms(); SyncedPhysicsThisUpdate = true; }
    }

    private void Scan(Vector3 focus, double now)
    {
        using var scanSample = ScanMarker.Auto();
        nearby.Clear(); candidates.Clear(); nextCandidate = 0;
        registry.CollectOriginsInRadiusXZ(focus, ActivationRadius, nearby);
        foreach (var record in nearby)
            if (record.State == TreeState.Standing && !active.ContainsKey(record.Id))
                candidates.Add(new Candidate { Id = record.Id, DistanceSquared = DistanceSquared(record.WorldPosition, focus) });
        candidates.Sort();
        lastScanPosition = focus;
        nextScan = now + Mathf.Max(.01f, settings.scanIntervalSeconds);
        scanDirty = false;
    }
    private void OnChunkChanged(ChunkCoord coord)
    {
        // Distant manifest arrivals shouldn't force nearby scans every frame.
        float x = Mathf.Clamp(lastScanPosition.x, coord.x * chunkWorldSize, (coord.x + 1f) * chunkWorldSize);
        float z = Mathf.Clamp(lastScanPosition.z, coord.z * chunkWorldSize, (coord.z + 1f) * chunkWorldSize);
        if (DistanceSquared(new Vector3(x, 0, z), lastScanPosition) <= ActivationRadius * ActivationRadius)
            scanDirty = true;
    }
    private static float DistanceSquared(Vector3 a, Vector3 b)
    { float x = a.x - b.x, z = a.z - b.z; return x * x + z * z; }

    private Template GetTemplate(GameObject prefab)
    {
        if (prefab == null) return null;
        if (templates.TryGetValue(prefab, out var template)) return template;
        var authoring = prefab.GetComponent<TreeGameplayAuthoring>();
        var valid = new List<Collider>();
        if (authoring != null)
        {
            foreach (var collider in authoring.PhysicalTrunkColliders)
            {
                if (collider == null || valid.Contains(collider)) continue;
                if (!collider.transform.IsChildOf(prefab.transform) || collider.isTrigger ||
                    (collider is not CapsuleCollider && collider is not BoxCollider && collider is not SphereCollider))
                {
                    Debug.LogWarning("Tree gameplay: '" + prefab.name + "' has an invalid physical trunk collider. Use a non-trigger Box, Capsule or Sphere on this prefab or its children.", prefab);
                    continue;
                }
                valid.Add(collider);
            }
        }
        template = new Template { Prefab = prefab, Colliders = valid.ToArray() };
        templates.Add(prefab, template);
        if (template.Colliders.Length == 0)
            Debug.LogWarning("Tree gameplay: '" + prefab.name + "' needs TreeGameplayAuthoring on its root with Physical Trunk Colliders assigned. This tree will render but will not block movement.", prefab);
        return template;
    }

    private TreeGameplayProxy CreateProxy(Template template)
    {
        var body = new GameObject("Tree proxy"); body.SetActive(false);
        body.transform.SetParent(root.transform, false);
        body.layer = template.Prefab.layer;
        var proxy = body.AddComponent<TreeGameplayProxy>();
        var transforms = new Dictionary<Transform, Transform> { { template.Prefab.transform, body.transform } };
        foreach (var source in template.Colliders)
        {
            Transform destination = CopyPath(source.transform, transforms);
            Collider copy;
            if (source is CapsuleCollider capsule)
            {
                var c = destination.gameObject.AddComponent<CapsuleCollider>();
                c.center = capsule.center; c.radius = capsule.radius; c.height = capsule.height; c.direction = capsule.direction; copy = c;
            }
            else if (source is BoxCollider box)
            {
                var c = destination.gameObject.AddComponent<BoxCollider>(); c.center = box.center; c.size = box.size; copy = c;
            }
            else
            {
                var sphere = (SphereCollider)source;
                var c = destination.gameObject.AddComponent<SphereCollider>(); c.center = sphere.center; c.radius = sphere.radius; copy = c;
            }
            copy.sharedMaterial = source.sharedMaterial;
            copy.contactOffset = source.contactOffset;
            copy.includeLayers = source.includeLayers; copy.excludeLayers = source.excludeLayers;
            copy.layerOverridePriority = source.layerOverridePriority;
            copy.isTrigger = false; copy.enabled = true;
        }
        return proxy;
    }
    private static Transform CopyPath(Transform source, Dictionary<Transform, Transform> transforms)
    {
        if (transforms.TryGetValue(source, out var copy)) return copy;
        Transform parent = CopyPath(source.parent, transforms);
        copy = new GameObject(source.name).transform; copy.gameObject.layer = source.gameObject.layer;
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
        transforms.Add(source, copy); return copy;
    }
    private void Release(TreeId id)
    {
        var entry = active[id]; active.Remove(id);
        entry.Proxy.gameObject.SetActive(false); entry.Proxy.Unbind();
        if (PooledCount < Mathf.Max(0, settings.maxPooledProxies))
        { entry.Template.Pool.Push(entry.Proxy); PooledCount++; }
        else DestroyOwned(entry.Proxy.gameObject);
    }
    private void TrimPool()
    {
        if (PooledCount <= Mathf.Max(0, settings.maxPooledProxies)) return;
        foreach (var template in templates.Values)
            while (template.Pool.Count > 0 && PooledCount > Mathf.Max(0, settings.maxPooledProxies))
            { DestroyOwned(template.Pool.Pop().gameObject); PooledCount--; }
    }
    private static void DestroyOwned(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; registry.ChunkChanged -= OnChunkChanged;
        root.SetActive(false); DestroyOwned(root);
        active.Clear(); templates.Clear(); nearby.Clear(); candidates.Clear(); releases.Clear(); PooledCount = 0;
    }
}
