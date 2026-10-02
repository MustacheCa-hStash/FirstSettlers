using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

// Uses the same GPU instancing API as clover/dandelions. CPU culling selects
// distance-dependent batches; DrawMeshInstanced submits leaves without Renderer objects.
public sealed class LeafClusterSystem : IDisposable
{
    private static readonly ProfilerMarker GenerationMarker = new ProfilerMarker("FS.Streaming.LeafClusters.Generate");
    private static readonly ProfilerMarker DrawMarker = new ProfilerMarker("FS.Streaming.LeafClusters.Draw");
    private static readonly int TintId = Shader.PropertyToID("_LeafInstanceTint");
    private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
    private static readonly int FadeEndId = Shader.PropertyToID("_FadeEnd");
    private readonly LeafClusterSettings settings;
    private readonly GrassSettings grassSettings;
    private readonly int seed, size;
    private readonly float scale, heightMultiplier;
    private readonly Dictionary<ChunkCoord, LeafClusterGeneration> resident = new Dictionary<ChunkCoord, LeafClusterGeneration>();
    private readonly List<ChunkCoord> eviction = new List<ChunkCoord>();
    private readonly HashSet<ChunkCoord> wanted = new HashSet<ChunkCoord>();
    private readonly Matrix4x4[] matrices = new Matrix4x4[1023];
    private readonly Vector4[] tints = new Vector4[1023];
    private readonly Plane[] frustum = new Plane[6];
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private GameObject resolvedPrefab;
    private GameObject defaultPrefab;
    private GameObject resolvedFarPrefab, defaultFarPrefab;
    private Mesh mesh;
    private Material material;
    private Mesh farMesh;
    private Material farMaterial;
    private Matrix4x4 farLocal = Matrix4x4.identity;
    private Matrix4x4 meshLocal = Matrix4x4.identity;
    private float meshRadius = 0.4f;
    public int VisibleInstances { get; private set; }
    public int DrawCalls { get; private set; }

    public LeafClusterSystem(LeafClusterSettings settings, int seed, int size, float scale, float heightMultiplier, GrassSettings grassSettings = null)
    {
        this.settings = settings ?? new LeafClusterSettings(); this.seed = seed;
        this.size = size; this.scale = scale; this.heightMultiplier = heightMultiplier;
        this.grassSettings = grassSettings;
    }
    public float RenderDistance => settings.matchGrassRenderDistance && grassSettings != null
        ? GrassStreamingPolicy.RenderDistance(grassSettings,size,scale) : Mathf.Max(1,settings.renderDistance);
    public static int SelectLod(uint rank, float distance, float start, float end) =>
        LeafClusterGeneration.Unit(rank,193) < Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,Mathf.Max(start+.01f,end),distance)) ? 1 : 0;

    public void Update(ChunkManager manager, List<ChunkCoord> coords, Vector3 viewer, Camera camera)
    {
        VisibleInstances = DrawCalls = 0;
        if (!settings.enabled || !SystemInfo.supportsInstancing) { resident.Clear(); return; }
        ResolveAssets();
        if (mesh == null || material == null) return;
        float range = RenderDistance;
        float fade = settings.matchGrassRenderDistance && grassSettings != null
            ? GrassStreamingPolicy.EdgeWidth(grassSettings,size,scale) : Mathf.Max(.01f,settings.fadeWidth);
        float residentRange = range + Mathf.Max(0, settings.prewarmDistance);
        float half = size * scale * 0.5f;
        wanted.Clear();
        ChunkCoord next = default;
        LeafClusterGeneration work = null;
        float best = float.MaxValue;
        foreach (ChunkCoord coord in coords)
        {
            Vector2 center = new Vector2((coord.x * size + size * 0.5f) * scale, (coord.z * size + size * 0.5f) * scale);
            float distance = GrassStreamingPolicy.DistanceToSquare(new Vector2(viewer.x, viewer.z), center, half);
            if (distance > residentRange) continue;
            ChunkRecord record = manager.GetChunkRecord(coord);
            ChunkRuntime runtime = record?.ActiveRuntime;
            if (runtime == null || !runtime.IsVisible || !runtime.HasTerrainMesh || !record.HasTerrainData) continue;
            // A missing packed map means this chunk contains no forest-floor samples.
            if (record.WorldFeaturePlan.ForestStructure.FloorEcologyMap == null) continue;
            wanted.Add(coord);
            if (!resident.TryGetValue(coord, out var generation) || !generation.Matches(record, settings))
            {
                // Start at most one new chunk per frame, including blocker-index construction.
                resident.Remove(coord);
                generation = null;
            }
            if ((generation == null || !generation.Complete) && distance < best)
            {
                next = coord; work = generation; best = distance;
            }
        }
        eviction.Clear();
        foreach (var pair in resident) if (!wanted.Contains(pair.Key)) eviction.Add(pair.Key);
        foreach (ChunkCoord coord in eviction) resident.Remove(coord);
        if (best < float.MaxValue)
        {
            using (GenerationMarker.Auto())
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                if (work == null)
                {
                    work = new LeafClusterGeneration(manager.GetChunkRecord(next), settings, seed, size, scale, heightMultiplier, meshRadius);
                    resident[next] = work;
                }
                int budget = Mathf.Max(1, settings.candidateBudgetPerFrame);
                for (int visited = 0; visited < budget && !work.Complete; visited += 32)
                {
                    work.Step(Mathf.Min(32, budget - visited));
                    double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (elapsed >= Mathf.Max(0.01f, settings.generationBudgetMs)) break;
                }
            }
        }
        if (camera != null) GeometryUtility.CalculateFrustumPlanes(camera, frustum);
        using (DrawMarker.Auto())
        {
            properties.Clear();
            properties.SetFloat(FadeStartId, Mathf.Max(0, range - fade));
            properties.SetFloat(FadeEndId, range);
            properties.SetVector("_LeafViewer", new Vector4(viewer.x,viewer.y,viewer.z,1));
            for (int lod = 0; lod < (farMesh != null && farMaterial != null ? 2 : 1); lod++)
            {
                int count = 0;
                foreach (var pair in resident)
                {
                    var runtime = manager.GetChunkRecord(pair.Key)?.ActiveRuntime;
                    if (runtime == null || !runtime.IsVisible || !runtime.IsFoliageRenderVisible) continue;
                    foreach (LeafClusterInstance instance in pair.Value.Instances)
                    {
                        float radius = meshRadius * instance.scale;
                        float distance = Vector2.Distance(new Vector2(instance.position.x,instance.position.z),new Vector2(viewer.x,viewer.z));
                        if (distance > range + radius) continue;
                        if (farMesh != null && farMaterial != null && SelectLod(instance.rank,distance,settings.lodStart,settings.lodEnd) != lod) continue;
                        if (camera != null && !InsideFrustum(instance.position, radius, frustum)) continue;
                        matrices[count] = Matrix4x4.TRS(instance.position, instance.rotation, Vector3.one * instance.scale) * (lod == 0 ? meshLocal : farLocal);
                        tints[count++] = instance.tint;
                        if (count == matrices.Length) { Submit(count, camera, lod); count = 0; }
                    }
                }
                if (count > 0) Submit(count, camera, lod);
            }
        }
    }

    private void ResolveAssets()
    {
        GameObject prefab = settings.prefab != null ? settings.prefab : defaultPrefab != null
            ? defaultPrefab : defaultPrefab = Resources.Load<GameObject>("Foliage/LeafCluster");
        GameObject distant = settings.distantPrefab != null ? settings.distantPrefab : defaultFarPrefab != null
            ? defaultFarPrefab : defaultFarPrefab = Resources.Load<GameObject>("Foliage/LeafScatter_LOD1");
        if (prefab == resolvedPrefab && distant == resolvedFarPrefab && mesh != null && material != null) return;
        resident.Clear();
        resolvedPrefab = prefab; resolvedFarPrefab = distant; mesh = farMesh = null; material = farMaterial = null;
        if (prefab == null) return;
        MeshFilter filter = prefab.GetComponentInChildren<MeshFilter>();
        MeshRenderer renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
        if (filter == null || renderer == null || renderer.sharedMaterial == null || !renderer.sharedMaterial.enableInstancing) return;
        mesh = filter.sharedMesh; material = renderer.sharedMaterial;
        meshLocal = filter.transform.localToWorldMatrix;
        if (mesh != null) meshRadius = mesh.bounds.extents.magnitude * Mathf.Max(meshLocal.lossyScale.x, meshLocal.lossyScale.y, meshLocal.lossyScale.z) + meshLocal.MultiplyPoint3x4(mesh.bounds.center).magnitude;
        filter = distant != null ? distant.GetComponentInChildren<MeshFilter>() : null;
        renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
        if (filter != null && renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.enableInstancing)
        {
            farMesh = filter.sharedMesh; farMaterial = renderer.sharedMaterial; farLocal = filter.transform.localToWorldMatrix;
            if (farMesh != null) meshRadius = Mathf.Max(meshRadius,farMesh.bounds.extents.magnitude *
                Mathf.Max(farLocal.lossyScale.x,farLocal.lossyScale.y,farLocal.lossyScale.z) + farLocal.MultiplyPoint3x4(farMesh.bounds.center).magnitude);
        }
    }

    private void Submit(int count, Camera camera, int lod)
    {
        properties.SetVectorArray(TintId, tints);
        Graphics.DrawMeshInstanced(lod == 0 ? mesh : farMesh, 0, lod == 0 ? material : farMaterial, matrices, count, properties,
            ShadowCastingMode.Off, true, 0, camera, LightProbeUsage.Off);
        VisibleInstances += count; DrawCalls++;
    }
    public static bool InsideFrustum(Vector3 position, float radius, Plane[] planes)
    {
        foreach (Plane plane in planes) if (plane.GetDistanceToPoint(position) < -radius) return false;
        return true;
    }
    public void Dispose() { resident.Clear(); wanted.Clear(); eviction.Clear(); }
}
