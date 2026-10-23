using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

public struct CloverRenderData
{
    public Mesh mesh;
    public Material material;

    public CloverRenderData(Mesh mesh, Material material)
    {
        this.mesh = mesh;
        this.material = material;
    }
}

public struct GrassRenderBatch
{
    public Matrix4x4[] matrices;
    public Vector4[] instanceData;

    public GrassRenderBatch(Matrix4x4[] matrices, Vector4[] instanceData)
    {
        this.matrices = matrices;
        this.instanceData = instanceData;
    }
}

public struct CloverRenderBatch
{
    public int prefabIndex;
    public Matrix4x4[] matrices;
    public Vector4[] instanceData;

    public CloverRenderBatch(int prefabIndex, Matrix4x4[] matrices, Vector4[] instanceData)
    {
        this.prefabIndex = prefabIndex;
        this.matrices = matrices;
        this.instanceData = instanceData;
    }
}

public class ChunkFoliageRuntime
{
    private static readonly ProfilerMarker BushStagingMarker = new("FS.Streaming.Foliage.Publication.Bush.CreateStaging");
    private static readonly ProfilerMarker BushInstantiateMarker = new("FS.Streaming.Foliage.Publication.Bush.Instantiate");
    private static readonly ProfilerMarker BushSetupMarker = new("FS.Streaming.Foliage.Publication.Bush.SetupInstance");
    private static readonly ProfilerMarker BushClearMarker = new("FS.Streaming.Foliage.Publication.Bush.ClearPrevious");
    private static readonly ProfilerMarker BushActivateMarker = new("FS.Streaming.Foliage.Publication.Bush.Activate");
    private static readonly ProfilerMarker BushDiscardMarker = new("FS.Streaming.Foliage.Publication.Bush.DiscardStaging");
    private static readonly ProfilerMarker RockStagingMarker = new("FS.Streaming.Foliage.Publication.Rock.CreateStaging");
    private static readonly ProfilerMarker RockAddMarker = new("FS.Streaming.Foliage.Publication.Rock.AddInstance");
    private static readonly ProfilerMarker RockClearMarker = new("FS.Streaming.Foliage.Publication.Rock.ClearPrevious");
    private static readonly ProfilerMarker RockColliderMarker = new("FS.Streaming.Foliage.Publication.Rock.RegisterColliders");
    private static readonly ProfilerMarker RockRendererMarker = new("FS.Streaming.Foliage.Publication.Rock.RegisterRenderer");
    private static readonly ProfilerMarker SuppressMarker=new("FS.Streaming.Sculpt.SmallFoliageSuppress");
    public Transform root;
    private bool rangeVisible, renderVisible = true, shadowCasterVisible = true;
    public bool isVisible;
    public bool IsCreated => root != null;
    public bool TreePlacementReady;
    public int PublicationVersion { get; private set; }
    public Mesh flowerMesh, tallFlowerMesh, daisyWeedMesh, poppyMesh, cornflowerMesh, lilyPadMesh, cattailMesh, dandelionMesh;
    public Material flowerMaterial, tallFlowerMaterial, daisyWeedMaterial, poppyMaterial, cornflowerMaterial, lilyPadMaterial, cattailMaterial, dandelionMaterial;
    public int flowerPetalColorPropertyId, cloverInstanceDataPropertyId, dandelionInstanceDataPropertyId;
    public CloverRenderData[] cloverRenderData;
    public bool receiveCloverShadows, receiveDandelionShadows;
    public GameObject blueberryBushPrefab, raspberryBushPrefab, strawberryBushPrefab, blackberryBushPrefab, fallbackBushPrefab;
    public GameObject[] forestRockPrefabs, grasslandRockPrefabs, grasslandLargeRockPrefabs;
    public GameObject forestRockFallbackPrefab, grasslandRockFallbackPrefab, grasslandLargeRockFallbackPrefab;
    public GameObject[] snowRockPrefabs, snowLargeRockPrefabs;
    public RockColliderPool RockPool;
    public RockWorldRenderer RockRenderer;
    private RockRenderSet rockRenderSet;
    public int RockInstanceCount => rockRenderSet?.InstanceCount ?? 0;
    private List<FlowerRenderBatch> flowerRenderBatches = new();
    private List<Matrix4x4[]> lilyPadRenderBatches = new(), cattailRenderBatches = new();
    private List<CloverRenderBatch> cloverRenderBatches = new();
    private List<GrassRenderBatch> dandelionRenderBatches = new();
    private readonly MaterialPropertyBlock flowerPropertyBlock = new(), cloverPropertyBlock = new(), dandelionPropertyBlock = new();
    private GameObject bushGameObjectRoot;
    private readonly Dictionary<Matrix4x4[],BuildingFoliageBatch> buildingBatches=new();
    private readonly List<BuildFoliageExclusion.Filter> buildingBushes=new();
    private BuildingFoliageBatch BuildingBatch(Matrix4x4[] matrices,Vector4[] values,Mesh mesh)
    {
        if(!buildingBatches.TryGetValue(matrices,out var batch))
        {
            batch=new BuildingFoliageBatch(matrices,values,mesh!=null?Mathf.Max(0,mesh.bounds.max.y):.2f);
            buildingBatches.Add(matrices,batch);
        }
        return batch;
    }
    private void RetireBuildingBatch(Matrix4x4[] matrices)
    {
        if(matrices!=null && buildingBatches.TryGetValue(matrices,out var batch)) { batch.Dispose();buildingBatches.Remove(matrices); }
    }
    private void ClearBuildingBatches() { foreach(var batch in buildingBatches.Values)batch.Dispose();buildingBatches.Clear(); }
    private TerrainSculptSuppression suppression;
    private bool suppressionDirty;
    public void ApplySuppression(TerrainSculptSuppression mask,float chunkWidth)
    {
        if(ReferenceEquals(mask,suppression)||mask==null)return;
        var previous=suppression;suppression=mask;
        Vector3 center=root!=null?root.position:Vector3.zero;float half=chunkWidth*.5f+1;
        if(!mask.ChangedIn(previous,center.x-half,center.z-half,center.x+half,center.z+half))return;
        suppressionDirty=true;FilterSmallBatches();SuppressBushes(bushGameObjects);
    }
    private bool Hidden(Vector3 p)=>suppression?.Contains(p.x,p.z)==true;
    private void SuppressBushes(List<GameObject> objects)
    {
        for(int i=objects.Count-1;i>=0;i--)
        {
            var body=objects[i];if(body==null){objects.RemoveAt(i);continue;}
            if(!Hidden(body.transform.position))continue;
            body.SetActive(false);if(Application.isPlaying)Object.Destroy(body);else Object.DestroyImmediate(body);objects.RemoveAt(i);
        }
    }
    private void Filter(ref Matrix4x4[] matrices,ref Vector4[] data)
    {
        if(matrices==null)return;
        int count=0;foreach(var matrix in matrices)if(!suppression.Contains(matrix.m03,matrix.m23))count++;
        if(count==matrices.Length)return;
        RetireBuildingBatch(matrices);
        var filtered=new Matrix4x4[count];var attributes=data!=null?new Vector4[count]:null;int next=0;
        for(int i=0;i<matrices.Length;i++)
        {
            if(suppression.Contains(matrices[i].m03,matrices[i].m23))continue;
            filtered[next]=matrices[i];if(attributes!=null)attributes[next]=data[i];next++;
        }
        matrices=filtered;data=attributes;
    }
    private void FilterSmallBatches()
    {
        if(!suppressionDirty||suppression==null)return;
        using var marker=SuppressMarker.Auto();
        suppressionDirty=false;
        for(int i=flowerRenderBatches.Count-1;i>=0;i--)
        {
            var batch=flowerRenderBatches[i];Filter(ref batch.matrices,ref batch.petalColors);
            if(batch.matrices?.Length==0)flowerRenderBatches.RemoveAt(i);else flowerRenderBatches[i]=batch;
        }
        for(int i=cloverRenderBatches.Count-1;i>=0;i--)
        {
            var batch=cloverRenderBatches[i];Filter(ref batch.matrices,ref batch.instanceData);
            if(batch.matrices?.Length==0)cloverRenderBatches.RemoveAt(i);else cloverRenderBatches[i]=batch;
        }
        for(int i=dandelionRenderBatches.Count-1;i>=0;i--)
        {
            var batch=dandelionRenderBatches[i];Filter(ref batch.matrices,ref batch.instanceData);
            if(batch.matrices?.Length==0)dandelionRenderBatches.RemoveAt(i);else dandelionRenderBatches[i]=batch;
        }
        void Water(List<Matrix4x4[]> batches)
        {
            for(int i=batches.Count-1;i>=0;i--)
            {
                var matrices=batches[i];Vector4[] unused=null;Filter(ref matrices,ref unused);
                if(matrices?.Length==0)batches.RemoveAt(i);else batches[i]=matrices;
            }
        }
        Water(lilyPadRenderBatches);Water(cattailRenderBatches);
    }
    private void SmallBatchesChanged(){suppressionDirty=true;FilterSmallBatches();}
    private List<GameObject> bushGameObjects = new();
    private bool hasCurrentBushRepresentation, hasCurrentRockRepresentation;
    private bool hasBuiltFlowerRenderData, hasBuiltLilyPadRenderData, hasBuiltCattailRenderData, hasBuiltCloverRenderData, hasBuiltDandelionRenderData;
    public int GpuFlowerInstanceCount => CountFlowerInstances();
    public int GpuCloverInstanceCount => CountCloverInstances();
    public int GpuDandelionInstanceCount => CountGrassInstances(dandelionRenderBatches);
    public int GpuLilyPadInstanceCount { get { int n=0; foreach(var b in lilyPadRenderBatches)n+=b.Length; return n; } }
    public int GpuCattailInstanceCount { get { int n=0; foreach(var b in cattailRenderBatches)n+=b.Length; return n; } }
    public void ClearCachedBatches()
    {
        PublicationVersion++;
        ClearBuildingBatches();
        flowerRenderBatches = new(); lilyPadRenderBatches = new(); cattailRenderBatches = new();
        cloverRenderBatches = new(); dandelionRenderBatches = new();
        hasBuiltFlowerRenderData = hasBuiltLilyPadRenderData = hasBuiltCattailRenderData = false;
        hasBuiltCloverRenderData = hasBuiltDandelionRenderData = false;
        ClearBushGameObjects(); ClearRockGameObjects(); TreePlacementReady = false;
    }
    internal void PublishFlowers(List<FlowerRenderBatch> batches) { foreach(var old in flowerRenderBatches)RetireBuildingBatch(old.matrices);flowerRenderBatches = batches; hasBuiltFlowerRenderData = true;SmallBatchesChanged(); }
    internal void PublishClover(List<CloverRenderBatch> batches) { foreach(var old in cloverRenderBatches)RetireBuildingBatch(old.matrices);cloverRenderBatches = batches; hasBuiltCloverRenderData = true;SmallBatchesChanged(); }
    internal void PublishDandelions(List<GrassRenderBatch> batches) { foreach(var old in dandelionRenderBatches)RetireBuildingBatch(old.matrices);dandelionRenderBatches = batches; hasBuiltDandelionRenderData = true;SmallBatchesChanged(); }
    internal void PublishWaterPlants(bool cattails, List<Matrix4x4[]> batches)
    {
        if (cattails) { foreach(var old in cattailRenderBatches)RetireBuildingBatch(old);cattailRenderBatches = batches; hasBuiltCattailRenderData = true; }
        else { foreach(var old in lilyPadRenderBatches)RetireBuildingBatch(old);lilyPadRenderBatches = batches; hasBuiltLilyPadRenderData = true; }
        SmallBatchesChanged();
    }
    internal IEnumerator<bool> BuildBushesIncrementally(List<BerryBushInstanceData> instances)
    {
        GameObject staging;
        List<GameObject> objects;
        using (BushStagingMarker.Auto())
        {
            staging = new GameObject("BerryBush_Staging");
            staging.SetActive(false); staging.transform.SetParent(root, false);
            objects = new();
        }
        bool published = false;
        var stagedFilters=new List<BuildFoliageExclusion.Filter>();
        try
        {
            yield return true;
            foreach (var instance in instances)
            {
                if(Hidden(root.TransformPoint(instance.localPosition))){yield return true;continue;}
                GameObject prefab = GetBushPrefab(instance.variant);
                if (prefab != null)
                {
                    GameObject body;
                    using (BushInstantiateMarker.Auto()) body = Object.Instantiate(prefab, staging.transform);
                    objects.Add(body); // Cancellation owns it even if setup throws.
                    using (BushSetupMarker.Auto())
                    {
                        GameplayLayers.AssignPhysicalColliders(body, GameplayLayers.WorldSolid);
                        body.transform.localPosition = instance.localPosition;
                        body.transform.localRotation = instance.localRotation;
                        body.transform.localScale = instance.localScale;
                        var bush = body.GetComponent<BerryBushRuntime>() ?? body.AddComponent<BerryBushRuntime>();
                        bush.Initialize(instance, BerryBushManager.Instance);
                        ConfigureSpawnedRendererCulling(body);
                        var position=body.transform.position;var plantHeight=2*Mathf.Abs(body.transform.lossyScale.y);
                        var exclusion=BuildFoliageExclusion.Active?.Register(1,_=>new BuildFoliageExclusion.Probe(position,plantHeight,.15f),
                            (_,hidden,__)=>{if(body!=null)body.SetActive(!hidden[0]);});
                        if(exclusion!=null)stagedFilters.Add(exclusion);
                    }
                }
                yield return true;
            }
            SuppressBushes(objects);
            using (BushClearMarker.Auto()) ClearBushGameObjects();
            bushGameObjectRoot = staging; bushGameObjects = objects;buildingBushes.AddRange(stagedFilters);
            using (BushActivateMarker.Auto()) { staging.name = "BerryBush_GameObjects"; staging.SetActive(true); }
            hasCurrentBushRepresentation = true; published = true;
        }
        finally
        {
            if(!published)foreach(var filter in stagedFilters)filter.Dispose();
            if (!published && staging != null)
                using (BushDiscardMarker.Auto()) DestroyObjectRoot(staging);
        }
    }
    internal IEnumerator<bool> BuildRocksIncrementally(List<RockInstanceData> instances)
    {
        RockRenderSet staging;
        using (RockStagingMarker.Auto()) staging = new();
        foreach (var instance in instances)
        {
            using (RockAddMarker.Auto()) staging.Add(GetRockPrefab(instance.variant, instance.prefabIndex), instance, root);
            yield return true;
        }
        if (RockRenderer == null)
        {
            using var batches = staging.FinishIncrementally();
            while (batches.MoveNext()) yield return true;
        }
        using (RockClearMarker.Auto()) ClearRockGameObjects();
        rockRenderSet = staging;
        using (RockColliderMarker.Auto()) RockPool?.Register(staging);
        staging.Enabled = rangeVisible;
        using (RockRendererMarker.Auto()) RockRenderer?.Register(staging);
        hasCurrentRockRepresentation = true;
    }
    private static void DestroyObjectRoot(GameObject value)
    {
        value.SetActive(false);
        if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
    }
#if UNITY_EDITOR
    // Authoring fixtures drain the production iterator; there is no alternate object builder.
    public void RebuildRockGameObjects(List<RockInstanceData> instances, Transform chunkRoot)
    {
        if (root == null || chunkRoot == null) return;
        using var work = BuildRocksIncrementally(instances);
        while (work.MoveNext()) { }
    }
#endif
    public bool HasCurrentBushRepresentation()
    {
        return hasCurrentBushRepresentation;
    }

    public bool HasCurrentRockRepresentation()
    {
        return hasCurrentRockRepresentation;
    }

    public void SetVisible(bool visible)
    {
        rangeVisible = visible;
        if (rockRenderSet != null) rockRenderSet.Enabled = visible;
        ApplyVisibility();
    }

    public void SetRenderVisible(bool visible)
    {
        renderVisible = visible;
        ApplyVisibility();
    }

    public void SetShadowCasterVisible(bool visible)
    {
        shadowCasterVisible = visible;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        isVisible = rangeVisible && renderVisible;
        bool rootVisible = rangeVisible && (renderVisible || shadowCasterVisible);

        if (root != null)
        {
            root.gameObject.SetActive(rootVisible);
        }
    }

    public bool HasValidFlowerRenderData()
    {
        return ((flowerMesh != null && flowerMaterial != null) ||
                (tallFlowerMesh != null && tallFlowerMaterial != null) ||
                (daisyWeedMesh != null && daisyWeedMaterial != null) ||
                (poppyMesh != null && poppyMaterial != null) ||
                (cornflowerMesh != null && cornflowerMaterial != null)) && hasBuiltFlowerRenderData;
    }

    public bool HasValidLilyPadRenderData()
    {
        return lilyPadMesh != null && lilyPadMaterial != null && hasBuiltLilyPadRenderData;
    }

    public bool HasValidCattailRenderData()
    {
        return cattailMesh != null && cattailMaterial != null && hasBuiltCattailRenderData;
    }

    public bool HasValidCloverRenderData()
    {
        return HasAnyValidCloverRenderAsset() && hasBuiltCloverRenderData;
    }

    public bool HasValidDandelionRenderData()
    {
        return dandelionMesh != null && dandelionMaterial != null && hasBuiltDandelionRenderData;
    }

    public bool HasBushGameObjects()
    {
        return bushGameObjects.Count > 0;
    }

    public bool HasRockGameObjects()
    {
        // Kept for callers of the legacy API: generated rendering now owns instance data only.
        return RockInstanceCount > 0;
    }

    public void DrawRocks() => rockRenderSet?.Draw(isVisible, rangeVisible && shadowCasterVisible);

    public void AccumulateFlowerRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateFlowerStats(ref stats.Flowers);
    }

    public void AccumulateLilyPadRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        if (lilyPadMesh == null)
            return;

        for (int i = 0; i < lilyPadRenderBatches.Count; i++)
            stats.LilyPads.AddMeshInstances(lilyPadMesh, lilyPadRenderBatches[i].Length);
    }

    public void AccumulateCattailRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        if (cattailMesh == null)
            return;

        for (int i = 0; i < cattailRenderBatches.Count; i++)
            stats.Cattails.AddMeshInstances(cattailMesh, cattailRenderBatches[i].Length);
    }

    public void AccumulateCloverRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateCloverStats(ref stats.Clover);
    }

    public void AccumulateDandelionRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        if (dandelionMesh == null) return;
        foreach (var batch in dandelionRenderBatches)
            stats.Dandelions.AddMeshInstances(dandelionMesh, batch.matrices.Length);
    }

    public void AccumulateBushGameObjectRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateGameObjectStats(bushGameObjects, ref stats.BushGameObjects);
    }

    public void AccumulateRockGameObjectRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        // Preserve the debug bucket while reporting the actual instanced geometry.
        rockRenderSet?.Accumulate(ref stats.RockGameObjects);
    }

    private bool CacheGrassRenderBatches(
        List<Matrix4x4> worldMatrices,
        List<Vector4> instanceData,
        List<GrassRenderBatch> targetBatches)
    {
        foreach(var old in targetBatches)RetireBuildingBatch(old.matrices);
        targetBatches.Clear();

        if (worldMatrices == null || instanceData == null)
            return true;

        if (worldMatrices.Count != instanceData.Count)
        {
            Debug.LogError("Grass matrix and instance data counts must match.");
            return false;
        }

        const int maxBatchSize = 1023;
        int totalCount = worldMatrices.Count;
        int startIndex = 0;

        while (startIndex < totalCount)
        {
            int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
            Matrix4x4[] matrixBatch = new Matrix4x4[batchCount];
            Vector4[] instanceDataBatch = new Vector4[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                matrixBatch[i] = worldMatrices[startIndex + i];
                instanceDataBatch[i] = instanceData[startIndex + i];
            }

            targetBatches.Add(new GrassRenderBatch(matrixBatch, instanceDataBatch));
            startIndex += batchCount;
        }

        return true;
    }

    private bool CacheGrassRenderBatches(
        Matrix4x4[] worldMatrices,
        Vector4[] instanceData,
        List<GrassRenderBatch> targetBatches)
    {
        foreach(var old in targetBatches)RetireBuildingBatch(old.matrices);
        targetBatches.Clear();

        if (worldMatrices == null || instanceData == null)
            return true;

        if (worldMatrices.Length != instanceData.Length)
        {
            Debug.LogError("Grass matrix and instance data counts must match.");
            return false;
        }

        const int maxBatchSize = 1023;
        int totalCount = worldMatrices.Length;
        int startIndex = 0;

        while (startIndex < totalCount)
        {
            int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
            Matrix4x4[] matrixBatch = new Matrix4x4[batchCount];
            Vector4[] instanceDataBatch = new Vector4[batchCount];

            System.Array.Copy(worldMatrices, startIndex, matrixBatch, 0, batchCount);
            System.Array.Copy(instanceData, startIndex, instanceDataBatch, 0, batchCount);

            targetBatches.Add(new GrassRenderBatch(matrixBatch, instanceDataBatch));
            startIndex += batchCount;
        }

        return true;
    }

    public void CacheFlowerBatches(List<Matrix4x4> worldMatrices, List<Vector4> petalColors, bool isTallFlower = false, bool isDaisyWeed = false, bool isPoppy = false, bool isCornflower = false)
    {
        suppressionDirty=true;
        if (!isTallFlower && !isDaisyWeed && !isPoppy && !isCornflower)
            foreach(var old in flowerRenderBatches)RetireBuildingBatch(old.matrices);
        flowerRenderBatches.Clear();

        if (worldMatrices == null || petalColors == null)
        {
            hasBuiltFlowerRenderData = true;
            return;
        }

        if (worldMatrices.Count != petalColors.Count)
        {
            Debug.LogError("Flower matrix and petal color counts must match.");
            hasBuiltFlowerRenderData = false;
            return;
        }

        const int maxBatchSize = 1023;
        int totalCount = worldMatrices.Count;
        int startIndex = 0;

        while (startIndex < totalCount)
        {
            int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
            Matrix4x4[] matrixBatch = new Matrix4x4[batchCount];
            Vector4[] petalColorBatch = new Vector4[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                matrixBatch[i] = worldMatrices[startIndex + i];
                petalColorBatch[i] = petalColors[startIndex + i];
            }

            flowerRenderBatches.Add(new FlowerRenderBatch(matrixBatch, petalColorBatch, isTallFlower, isDaisyWeed, isPoppy, isCornflower));
            startIndex += batchCount;
        }

        hasBuiltFlowerRenderData = true;
    }

    public void CacheFlowerBatches(Matrix4x4[] worldMatrices, Vector4[] petalColors, bool isTallFlower = false, bool isDaisyWeed = false, bool isPoppy = false, bool isCornflower = false)
    {
        suppressionDirty=true;
        if (!isTallFlower && !isDaisyWeed && !isPoppy && !isCornflower)
            foreach(var old in flowerRenderBatches)RetireBuildingBatch(old.matrices);
        flowerRenderBatches.Clear();

        if (worldMatrices == null || petalColors == null)
        {
            hasBuiltFlowerRenderData = true;
            return;
        }

        if (worldMatrices.Length != petalColors.Length)
        {
            Debug.LogError("Flower matrix and petal color counts must match.");
            hasBuiltFlowerRenderData = false;
            return;
        }

        const int maxBatchSize = 1023;
        int totalCount = worldMatrices.Length;
        int startIndex = 0;

        while (startIndex < totalCount)
        {
            int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
            Matrix4x4[] matrixBatch = new Matrix4x4[batchCount];
            Vector4[] petalColorBatch = new Vector4[batchCount];

            System.Array.Copy(worldMatrices, startIndex, matrixBatch, 0, batchCount);
            System.Array.Copy(petalColors, startIndex, petalColorBatch, 0, batchCount);

            flowerRenderBatches.Add(new FlowerRenderBatch(matrixBatch, petalColorBatch, isTallFlower, isDaisyWeed, isPoppy, isCornflower));
            startIndex += batchCount;
        }

        hasBuiltFlowerRenderData = true;
    }

    public void CacheCloverBatches(List<Matrix4x4>[] worldMatricesByPrefab, List<Vector4>[] instanceDataByPrefab)
    {
        suppressionDirty=true;
        foreach(var old in cloverRenderBatches)RetireBuildingBatch(old.matrices);
        cloverRenderBatches.Clear();

        if (worldMatricesByPrefab == null || instanceDataByPrefab == null)
        {
            hasBuiltCloverRenderData = true;
            return;
        }

        int prefabCount = Mathf.Min(worldMatricesByPrefab.Length, instanceDataByPrefab.Length);
        for (int prefabIndex = 0; prefabIndex < prefabCount; prefabIndex++)
        {
            List<Matrix4x4> worldMatrices = worldMatricesByPrefab[prefabIndex];
            List<Vector4> instanceData = instanceDataByPrefab[prefabIndex];

            if (worldMatrices == null || instanceData == null)
                continue;

            if (worldMatrices.Count != instanceData.Count)
            {
                Debug.LogError("Clover matrix and instance data counts must match.");
                hasBuiltCloverRenderData = false;
                return;
            }

            const int maxBatchSize = 1023;
            int totalCount = worldMatrices.Count;
            int startIndex = 0;

            while (startIndex < totalCount)
            {
                int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
                Matrix4x4[] matrixBatch = new Matrix4x4[batchCount];
                Vector4[] instanceDataBatch = new Vector4[batchCount];

                for (int i = 0; i < batchCount; i++)
                {
                    matrixBatch[i] = worldMatrices[startIndex + i];
                    instanceDataBatch[i] = instanceData[startIndex + i];
                }

                cloverRenderBatches.Add(new CloverRenderBatch(prefabIndex, matrixBatch, instanceDataBatch));
                startIndex += batchCount;
            }
        }

        hasBuiltCloverRenderData = true;
    }

    public void CacheDandelionBatches(List<Matrix4x4> worldMatrices, List<Vector4> instanceData)
    {
        suppressionDirty=true;
        hasBuiltDandelionRenderData = CacheGrassRenderBatches(worldMatrices, instanceData, dandelionRenderBatches);
    }

    public void CacheDandelionBatches(Matrix4x4[] worldMatrices, Vector4[] instanceData)
    {
        suppressionDirty=true;
        hasBuiltDandelionRenderData = CacheGrassRenderBatches(worldMatrices, instanceData, dandelionRenderBatches);
    }

    private int CountGrassInstances(List<GrassRenderBatch> batches)
    {
        int count = 0;

        for (int i = 0; i < batches.Count; i++)
        {
            if (batches[i].matrices != null)
                count += batches[i].matrices.Length;
        }

        return count;
    }

    private int CountFlowerInstances()
    {
        int count = 0;

        for (int i = 0; i < flowerRenderBatches.Count; i++)
        {
            if (flowerRenderBatches[i].matrices != null)
                count += flowerRenderBatches[i].matrices.Length;
        }

        return count;
    }

    private int CountCloverInstances()
    {
        int count = 0;

        for (int i = 0; i < cloverRenderBatches.Count; i++)
        {
            if (cloverRenderBatches[i].matrices != null)
                count += cloverRenderBatches[i].matrices.Length;
        }

        return count;
    }

    private void AccumulateFlowerStats(ref RenderGeometryStats stats)
    {
        for (int i = 0; i < flowerRenderBatches.Count; i++)
        {
            if (flowerRenderBatches[i].matrices == null)
                continue;

            FlowerRenderBatch batch = flowerRenderBatches[i];
            Mesh mesh = batch.isCornflower ? cornflowerMesh : batch.isPoppy ? poppyMesh : batch.isDaisyWeed ? daisyWeedMesh : batch.isTallFlower ? tallFlowerMesh : flowerMesh;
            if (mesh != null)
                stats.AddMeshInstances(mesh, flowerRenderBatches[i].matrices.Length);
        }
    }

    private void AccumulateCloverStats(ref RenderGeometryStats stats)
    {
        if (cloverRenderData == null)
            return;

        for (int i = 0; i < cloverRenderBatches.Count; i++)
        {
            CloverRenderBatch batch = cloverRenderBatches[i];
            if (batch.matrices == null)
                continue;

            if ((uint)batch.prefabIndex >= cloverRenderData.Length)
                continue;

            Mesh mesh = cloverRenderData[batch.prefabIndex].mesh;
            if (mesh != null)
                stats.AddMeshInstances(mesh, batch.matrices.Length);
        }
    }

    private void AccumulateGameObjectStats(List<GameObject> gameObjects, ref RenderGeometryStats stats)
    {
        for (int i = 0; i < gameObjects.Count; i++)
        {
            GameObject gameObject = gameObjects[i];
            if (gameObject == null || !gameObject.activeInHierarchy)
                continue;

            MeshFilter[] meshFilters = gameObject.GetComponentsInChildren<MeshFilter>();
            for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
            {
                stats.AddMesh(meshFilters[meshIndex].sharedMesh);
            }
        }
    }

    public void ClearBushGameObjects()
    {
        foreach(var filter in buildingBushes)filter.Dispose();buildingBushes.Clear();
        if (bushGameObjectRoot != null) DestroyObjectRoot(bushGameObjectRoot);
        bushGameObjectRoot = null; bushGameObjects = new(); hasCurrentBushRepresentation = false;
    }

    public void ClearRockGameObjects()
    {
        RockPool?.Unregister(rockRenderSet);
        RockRenderer?.Unregister(rockRenderSet);
        rockRenderSet = null; hasCurrentRockRepresentation = false;
    }

    public void ClearFlowerBatches()
    {
        foreach(var old in flowerRenderBatches)RetireBuildingBatch(old.matrices);
        flowerRenderBatches.Clear();
        hasBuiltFlowerRenderData = false;
    }

    public void CacheLilyPadBatches(List<Matrix4x4> matrices)
    {
        suppressionDirty=true;
        foreach(var old in lilyPadRenderBatches)RetireBuildingBatch(old);
        lilyPadRenderBatches.Clear();
        const int maxBatchSize = 1023;
        for (int start = 0; start < matrices.Count; start += maxBatchSize)
        {
            int count = Mathf.Min(maxBatchSize, matrices.Count - start);
            Matrix4x4[] batch = new Matrix4x4[count];
            matrices.CopyTo(start, batch, 0, count);
            lilyPadRenderBatches.Add(batch);
        }
        hasBuiltLilyPadRenderData = true;
    }

    public void ClearLilyPadBatches()
    {
        foreach(var old in lilyPadRenderBatches)RetireBuildingBatch(old);
        lilyPadRenderBatches.Clear();
        hasBuiltLilyPadRenderData = false;
    }

    public void CacheCattailBatches(List<Matrix4x4> matrices)
    {
        suppressionDirty=true;
        foreach(var old in cattailRenderBatches)RetireBuildingBatch(old);
        cattailRenderBatches.Clear();
        const int maxBatchSize = 1023;
        for (int start = 0; start < matrices.Count; start += maxBatchSize)
        {
            int count = Mathf.Min(maxBatchSize, matrices.Count - start);
            Matrix4x4[] batch = new Matrix4x4[count];
            matrices.CopyTo(start, batch, 0, count);
            cattailRenderBatches.Add(batch);
        }
        hasBuiltCattailRenderData = true;
    }

    public void ClearCattailBatches()
    {
        foreach(var old in cattailRenderBatches)RetireBuildingBatch(old);
        cattailRenderBatches.Clear();
        hasBuiltCattailRenderData = false;
    }

    private static void ConfigureSpawnedRendererCulling(GameObject rootObject)
    {
        if (rootObject == null)
            return;

        ConfigureSpawnedRendererCulling(rootObject.GetComponentsInChildren<Renderer>(true));
    }

    private static void ConfigureSpawnedRendererCulling(Renderer[] renderers)
    {
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.forceRenderingOff = false;
            renderer.allowOcclusionWhenDynamic = true;
        }
    }

    public void ClearCloverBatches()
    {
        foreach(var old in cloverRenderBatches)RetireBuildingBatch(old.matrices);
        cloverRenderBatches.Clear();
        hasBuiltCloverRenderData = false;
    }

    public void ClearDandelionBatches()
    {
        foreach(var old in dandelionRenderBatches)RetireBuildingBatch(old.matrices);
        dandelionRenderBatches.Clear();
        hasBuiltDandelionRenderData = false;
    }

    public void DrawFlowers()
    {
        FilterSmallBatches();
        if (!isVisible || !HasValidFlowerRenderData() || flowerRenderBatches.Count == 0)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        for (int i = 0; i < flowerRenderBatches.Count; i++)
        {
            FlowerRenderBatch batch = flowerRenderBatches[i];

            if (batch.matrices == null || batch.petalColors == null)
                continue;


            Mesh mesh = batch.isCornflower ? cornflowerMesh : batch.isPoppy ? poppyMesh : batch.isDaisyWeed ? daisyWeedMesh : batch.isTallFlower ? tallFlowerMesh : flowerMesh;
            Material material = batch.isCornflower ? cornflowerMaterial : batch.isPoppy ? poppyMaterial : batch.isDaisyWeed ? daisyWeedMaterial : batch.isTallFlower ? tallFlowerMaterial : flowerMaterial;
            if (mesh == null || material == null)
                continue;
            var clearing=BuildingBatch(batch.matrices,batch.petalColors,mesh);
            if(clearing.Count==0)continue;
            flowerPropertyBlock.Clear();flowerPropertyBlock.SetVectorArray(flowerPetalColorPropertyId,clearing.Values);

            Graphics.DrawMeshInstanced(
                mesh,
                0,
                material,
                clearing.Matrices,
                clearing.Count,
                flowerPropertyBlock,
                batch.isCornflower || batch.isPoppy || batch.isTallFlower || batch.isDaisyWeed ? ShadowCastingMode.On : ShadowCastingMode.Off,
                true
            );
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageFlowerDraw,
            stageStart);
    }

    public void DrawLilyPads()
    {
        FilterSmallBatches();
        if (!isVisible || !HasValidLilyPadRenderData())
            return;

        for (int i = 0; i < lilyPadRenderBatches.Count; i++)
        {
            Matrix4x4[] batch = lilyPadRenderBatches[i];
            var clearing=BuildingBatch(batch,null,lilyPadMesh);
            if(clearing.Count==0)continue;
            Graphics.DrawMeshInstanced(lilyPadMesh, 0, lilyPadMaterial, clearing.Matrices, clearing.Count,
                null, ShadowCastingMode.Off, false);
        }
    }

    public void DrawCattails()
    {
        FilterSmallBatches();
        if (!isVisible || !HasValidCattailRenderData())
            return;

        for (int i = 0; i < cattailRenderBatches.Count; i++)
        {
            Matrix4x4[] batch = cattailRenderBatches[i];
            var clearing=BuildingBatch(batch,null,cattailMesh);
            if(clearing.Count==0)continue;
            Graphics.DrawMeshInstanced(cattailMesh, 0, cattailMaterial, clearing.Matrices, clearing.Count,
                null, ShadowCastingMode.Off, false);
        }
    }

    public void DrawClover(Vector3? viewer=null,float renderDistance=0,float fadeWidth=0)
    {
        FilterSmallBatches();
        if (!isVisible || !HasValidCloverRenderData() || cloverRenderBatches.Count == 0)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        for (int i = 0; i < cloverRenderBatches.Count; i++)
        {
            CloverRenderBatch batch = cloverRenderBatches[i];
            if (batch.matrices == null || batch.instanceData == null)
                continue;

            if ((uint)batch.prefabIndex >= cloverRenderData.Length)
                continue;

            CloverRenderData renderData = cloverRenderData[batch.prefabIndex];
            if (renderData.mesh == null || renderData.material == null)
                continue;

            var clearing=BuildingBatch(batch.matrices,batch.instanceData,renderData.mesh);
            if(clearing.Count==0)continue;
            cloverPropertyBlock.Clear();
            cloverPropertyBlock.SetVectorArray(cloverInstanceDataPropertyId, clearing.Values);
            if(viewer.HasValue && renderDistance>0)
            {
                var player=viewer.Value;
                cloverPropertyBlock.SetVector("_CloverViewer",new Vector4(player.x,player.y,player.z,1));
                cloverPropertyBlock.SetFloat("_FadeStartDistance",Mathf.Max(0,renderDistance-fadeWidth));
                cloverPropertyBlock.SetFloat("_FadeEndDistance",renderDistance);
            }

            Graphics.DrawMeshInstanced(
                renderData.mesh,
                0,
                renderData.material,
                clearing.Matrices,
                clearing.Count,
                cloverPropertyBlock,
                ShadowCastingMode.Off,
                receiveCloverShadows
            );
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageCloverDraw,
            stageStart);
    }

    public void DrawDandelions()
    {
        FilterSmallBatches();
        if (!isVisible || !HasValidDandelionRenderData() || dandelionRenderBatches.Count == 0)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        for (int i = 0; i < dandelionRenderBatches.Count; i++)
        {
            GrassRenderBatch batch = dandelionRenderBatches[i];
            if (batch.matrices == null || batch.instanceData == null)
                continue;

            var clearing=BuildingBatch(batch.matrices,batch.instanceData,dandelionMesh);
            if(clearing.Count==0)continue;
            dandelionPropertyBlock.Clear();
            dandelionPropertyBlock.SetVectorArray(dandelionInstanceDataPropertyId, clearing.Values);

            Graphics.DrawMeshInstanced(
                dandelionMesh,
                0,
                dandelionMaterial,
                clearing.Matrices,
                clearing.Count,
                dandelionPropertyBlock,
                ShadowCastingMode.Off,
                receiveDandelionShadows
            );
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageDandelionDraw,
            stageStart);
    }

    private bool HasAnyValidCloverRenderAsset()
    {
        if (cloverRenderData == null)
            return false;

        for (int i = 0; i < cloverRenderData.Length; i++)
        {
            if (cloverRenderData[i].mesh != null && cloverRenderData[i].material != null)
                return true;
        }

        return false;
    }

    private GameObject GetBushPrefab(WorldFeatureVariant variant)
    {
        if (variant == WorldFeatureVariant.BlueberryBush && blueberryBushPrefab != null)
            return blueberryBushPrefab;

        if (variant == WorldFeatureVariant.RaspberryBush && raspberryBushPrefab != null)
            return raspberryBushPrefab;

        if (variant == WorldFeatureVariant.StrawberryBush && strawberryBushPrefab != null)
            return strawberryBushPrefab;

        if (variant == WorldFeatureVariant.BlackberryBush && blackberryBushPrefab != null)
            return blackberryBushPrefab;

        return fallbackBushPrefab;
    }

    private GameObject GetRockPrefab(WorldFeatureVariant variant, int prefabIndex)
    {
        GameObject[] prefabs;
        GameObject fallbackPrefab;

        if (variant == WorldFeatureVariant.SnowBoulder || variant == WorldFeatureVariant.SnowLargeBoulder)
        {
            prefabs = variant == WorldFeatureVariant.SnowBoulder ? snowRockPrefabs : snowLargeRockPrefabs;
            fallbackPrefab = null;
        }
        else if (variant == WorldFeatureVariant.GrasslandLargeBoulder)
        {
            prefabs = grasslandLargeRockPrefabs;
            fallbackPrefab = grasslandLargeRockFallbackPrefab;
        }
        else if (variant == WorldFeatureVariant.GrasslandBoulder)
        {
            prefabs = grasslandRockPrefabs;
            fallbackPrefab = grasslandRockFallbackPrefab;
        }
        else
        {
            prefabs = forestRockPrefabs;
            fallbackPrefab = forestRockFallbackPrefab;
        }

        if (prefabs == null || prefabs.Length == 0)
            return fallbackPrefab;

        int clampedIndex = Mathf.Clamp(prefabIndex, 0, prefabs.Length - 1);
        return prefabs[clampedIndex] != null ? prefabs[clampedIndex] : fallbackPrefab;
    }

}
