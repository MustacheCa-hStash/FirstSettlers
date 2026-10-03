using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

// Bounded CPU discovery with resident GPU culling/rendering and an instanced fallback.
public sealed class LeafClusterSystem : IDisposable
{
    private readonly ProfilerMarker GenerationMarker;
    private readonly ProfilerMarker DrawMarker;
    private static readonly int TintId = Shader.PropertyToID("_LeafInstanceTint");
    private static readonly int ScatterId = Shader.PropertyToID("_LeafScatterParams");
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
    private readonly Vector4[] scatters = new Vector4[1023];
    private readonly Plane[] frustum = new Plane[6];
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private GameObject resolvedPrefab;
    private GameObject defaultPrefab;
    private GameObject resolvedFarPrefab, defaultFarPrefab;
    private GameObject resolvedCoarsePrefab,defaultCoarsePrefab;
    private Mesh mesh;
    private Material material;
    private Mesh farMesh;
    private Material farMaterial;
    private Mesh coarseMesh;
    private Material coarseMaterial;
    private Matrix4x4 coarseLocal=Matrix4x4.identity;
    private Matrix4x4 farLocal = Matrix4x4.identity;
    private Matrix4x4 meshLocal = Matrix4x4.identity;
    private float meshRadius = 0.4f;
    private ForestScatterGpuRenderer gpu;
    private ComputeShader defaultCompute, activeCompute;
    private readonly Dictionary<ChunkCoord, CpuTile> cpuTiles = new Dictionary<ChunkCoord, CpuTile>();
    sealed class CpuTile
    {
        public LeafClusterGeneration Generation;
        public readonly List<CachedInstance> Instances = new List<CachedInstance>();
    }
    struct CachedInstance
    {
        public LeafClusterInstance Instance;
        public Matrix4x4 Near, Far, Coarse;
        public Vector4 Scatter;
        public float LodRank,DensityRank,CoarseRank;
    }
    public int VisibleInstances { get; private set; }
    public int DrawCalls { get; private set; }
    public bool UsesGpu => gpu != null;
    public int ResidentInstances => gpu != null ? gpu.ResidentInstances : CountResident();
    int CountResident() { int count = 0; foreach (var value in resident.Values) count += value.Instances.Count; return count; }

    public LeafClusterSystem(LeafClusterSettings settings, int seed, int size, float scale, float heightMultiplier, GrassSettings grassSettings = null)
    {
        this.settings = settings ?? new LeafClusterSettings(); this.seed = seed;
        string kind=this.settings.IsFern?"Ferns":"LeafClusters";
        GenerationMarker=new ProfilerMarker("FS.Streaming."+kind+".Generate");
        DrawMarker=new ProfilerMarker("FS.Streaming."+kind+".Draw");
        this.size = size; this.scale = scale; this.heightMultiplier = heightMultiplier;
        this.grassSettings = grassSettings;
    }
    public float RenderDistance => settings.matchGrassRenderDistance && grassSettings != null
        ? GrassStreamingPolicy.RenderDistance(grassSettings,size,scale)*Mathf.Clamp(settings.grassRenderDistanceMultiplier,.1f,1f) : Mathf.Max(1,settings.renderDistance);
    public float DensityRingSize => ForestScatterPolicy.RingSize(grassSettings,size,scale);
    public Vector4 LodDistances => ForestScatterPolicy.LodDistances(settings,grassSettings,size,scale);
    public static int SelectLod(uint rank, float distance, float start, float end) =>
        LeafClusterGeneration.Unit(rank,193) < Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,Mathf.Max(start+.01f,end),distance)) ? 1 : 0;
    public static Vector4 ScatterParams(uint rank) => new Vector4(rank%8192+1,
        Mathf.Lerp(.28f,1f,LeafClusterGeneration.Unit(rank,229)),
        Mathf.Lerp(.65f,1.35f,LeafClusterGeneration.Unit(rank,251)),1);

    public void Update(ChunkManager manager, List<ChunkCoord> coords, Vector3 viewer, Camera camera)
    {
        VisibleInstances = DrawCalls = 0;
        if (!settings.enabled || !SystemInfo.supportsInstancing) { resident.Clear(); cpuTiles.Clear(); gpu?.Dispose(); gpu = null; return; }
        ResolveAssets();
        if (mesh == null || material == null) return;
        ConfigureGpu();
        float range = RenderDistance;
        float fade = settings.matchGrassRenderDistance && grassSettings != null
            ? GrassStreamingPolicy.EdgeWidth(grassSettings,size,scale)*Mathf.Clamp(settings.grassRenderDistanceMultiplier,.1f,1f) : Mathf.Max(.01f,settings.fadeWidth);
        Vector4 lodDistances=LodDistances,densityRings=ForestScatterPolicy.Densities(settings);
        float densityRingSize=DensityRingSize;
        bool hasFar=farMesh!=null && farMaterial!=null,hasCoarse=hasFar && coarseMesh!=null && coarseMaterial!=null;
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
                gpu?.Remove(coord); cpuTiles.Remove(coord);
                generation = null;
            }
            if ((generation == null || !generation.Complete) && distance < best)
            {
                next = coord; work = generation; best = distance;
            }
        }
        eviction.Clear();
        foreach (var pair in resident) if (!wanted.Contains(pair.Key)) eviction.Add(pair.Key);
        foreach (ChunkCoord coord in eviction) { resident.Remove(coord); gpu?.Remove(coord); cpuTiles.Remove(coord); }
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
            if (gpu != null)
            {
                foreach (var pair in resident)
                {
                    var runtime = manager.GetChunkRecord(pair.Key)?.ActiveRuntime;
                    gpu.Sync(pair.Key, pair.Value, runtime != null && runtime.IsVisible && runtime.IsFoliageRenderVisible,
                        meshRadius, settings.IsFern);
                }
                gpu.Draw(mesh, material, meshLocal, farMesh, farMaterial, farLocal, camera, frustum, viewer,
                    range, fade, lodDistances.x,lodDistances.y,coarseMesh,coarseMaterial,coarseLocal,
                    lodDistances.z,lodDistances.w,densityRingSize,densityRings);
                DrawCalls = gpu.DrawCalls;
                // Exact GPU visibility would require a synchronous readback. Use
                // ResidentInstances for residency; CPU fallback retains its exact count.
                VisibleInstances = -1;
                return;
            }
            foreach (var pair in resident) PrepareCpu(pair.Key, pair.Value);
            properties.Clear();
            properties.SetFloat(FadeStartId, Mathf.Max(0, range - fade));
            properties.SetFloat(FadeEndId, range);
            properties.SetVector("_LeafViewer", new Vector4(viewer.x,viewer.y,viewer.z,1));
            for (int lod = 0; lod < (hasCoarse?3:hasFar?2:1); lod++)
            {
                int count = 0;
                foreach (var pair in resident)
                {
                    var runtime = manager.GetChunkRecord(pair.Key)?.ActiveRuntime;
                    if (runtime == null || !runtime.IsVisible || !runtime.IsFoliageRenderVisible) continue;
                    foreach (CachedInstance cached in cpuTiles[pair.Key].Instances)
                    {
                        LeafClusterInstance instance = cached.Instance;
                        float radius = meshRadius * instance.scale;
                        float distance = Vector2.Distance(new Vector2(instance.position.x,instance.position.z),new Vector2(viewer.x,viewer.z));
                        if (distance > range + radius) continue;
                        if(cached.DensityRank>=GrassStreamingPolicy.Density(distance/densityRingSize,densityRings))continue;
                        if(ForestScatterPolicy.SelectLod(cached.LodRank,cached.CoarseRank,distance,lodDistances,hasFar,hasCoarse)!=lod)continue;
                        if (camera != null && !InsideFrustum(instance.position, radius, frustum)) continue;
                        matrices[count] = lod == 0 ? cached.Near : lod==1?cached.Far:cached.Coarse;
                        scatters[count]=cached.Scatter;
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
            ? defaultPrefab : defaultPrefab = Resources.Load<GameObject>(settings.DefaultPrefabPath);
        GameObject distant = settings.distantPrefab != null ? settings.distantPrefab : defaultFarPrefab != null
            ? defaultFarPrefab : defaultFarPrefab = Resources.Load<GameObject>(settings.DefaultDistantPrefabPath);
        GameObject coarse=settings is FernSettings fern?fern.coarsePrefab!=null?fern.coarsePrefab:
            defaultCoarsePrefab!=null?defaultCoarsePrefab:defaultCoarsePrefab=Resources.Load<GameObject>("Foliage/ForestFern_LOD2"):null;
        if (prefab == resolvedPrefab && distant == resolvedFarPrefab && coarse==resolvedCoarsePrefab && mesh != null && material != null) return;
        resident.Clear(); cpuTiles.Clear(); gpu?.Dispose(); gpu = null;
        resolvedPrefab = prefab; resolvedFarPrefab = distant; resolvedCoarsePrefab=coarse;
        mesh = farMesh = coarseMesh = null; material = farMaterial = coarseMaterial = null;
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
        filter=coarse!=null?coarse.GetComponentInChildren<MeshFilter>():null;
        renderer=filter!=null?filter.GetComponent<MeshRenderer>():null;
        if(filter!=null && renderer!=null && renderer.sharedMaterial!=null && renderer.sharedMaterial.enableInstancing)
        {
            coarseMesh=filter.sharedMesh;coarseMaterial=renderer.sharedMaterial;coarseLocal=filter.transform.localToWorldMatrix;
            if(coarseMesh!=null)meshRadius=Mathf.Max(meshRadius,coarseMesh.bounds.extents.magnitude *
                Mathf.Max(coarseLocal.lossyScale.x,coarseLocal.lossyScale.y,coarseLocal.lossyScale.z)+coarseLocal.MultiplyPoint3x4(coarseMesh.bounds.center).magnitude);
        }
        // Bound per-leaf spread, heading/size variation and local offsets in the shader.
        if(!settings.IsFern) meshRadius=meshRadius*1.5f+.1f;
    }

    private void Submit(int count, Camera camera, int lod)
    {
        properties.SetVectorArray(TintId, tints);
        if(!settings.IsFern) properties.SetVectorArray(ScatterId,scatters);
        Graphics.DrawMeshInstanced(lod == 0 ? mesh : lod==1?farMesh:coarseMesh, 0, lod == 0 ? material : lod==1?farMaterial:coarseMaterial, matrices, count, properties,
            ShadowCastingMode.Off, true, 0, camera, LightProbeUsage.Off);
        VisibleInstances += count; DrawCalls++;
    }
    private void ConfigureGpu()
    {
        var compute = settings.scatterCompactShader != null ? settings.scatterCompactShader :
            defaultCompute != null ? defaultCompute : defaultCompute = Resources.Load<ComputeShader>("Foliage/ForestScatterCompact");
        bool supported = settings.gpuIndirectRendering && ForestScatterGpuRenderer.Supports(compute, material, farMaterial,coarseMaterial);
        if (supported == (gpu != null) && compute == activeCompute) return;
        gpu?.Dispose(); gpu = supported ? new ForestScatterGpuRenderer(compute,settings.IsFern?3:2) : null;
        activeCompute = compute; cpuTiles.Clear();
    }
    private void PrepareCpu(ChunkCoord coord, LeafClusterGeneration generation)
    {
        if (!cpuTiles.TryGetValue(coord, out var tile) || !ReferenceEquals(tile.Generation, generation))
            cpuTiles[coord] = tile = new CpuTile { Generation = generation };
        for (int i = tile.Instances.Count; i < generation.Instances.Count; i++)
        {
            var instance = generation.Instances[i];
            var transform = Matrix4x4.TRS(instance.position, instance.rotation, Vector3.one * instance.scale);
            tile.Instances.Add(new CachedInstance { Instance = instance, Near = transform * meshLocal, Far = transform * farLocal,Coarse=transform*coarseLocal,
                Scatter = settings.IsFern ? Vector4.zero : ScatterParams(instance.rank), LodRank = LeafClusterGeneration.Unit(instance.rank, 193),
                DensityRank=LeafClusterGeneration.Unit(instance.rank,277),CoarseRank=LeafClusterGeneration.Unit(instance.rank,307) });
        }
    }
    public static bool InsideFrustum(Vector3 position, float radius, Plane[] planes)
    {
        foreach (Plane plane in planes) if (plane.GetDistanceToPoint(position) < -radius) return false;
        return true;
    }
    public void Dispose() { gpu?.Dispose(); gpu = null; cpuTiles.Clear(); resident.Clear(); wanted.Clear(); eviction.Clear(); }
}
