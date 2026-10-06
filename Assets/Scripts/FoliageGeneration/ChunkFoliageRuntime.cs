using System.Collections.Generic;
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
    public Transform root;
    private bool rangeVisible, renderVisible = true, shadowCasterVisible = true;
    public bool isVisible;
    public bool IsCreated => root != null;
    public bool TreePlacementReady;
    public int PublicationVersion { get; private set; }
    public Mesh flowerMesh, tallFlowerMesh, daisyWeedMesh, lilyPadMesh, cattailMesh, dandelionMesh;
    public Material flowerMaterial, tallFlowerMaterial, daisyWeedMaterial, lilyPadMaterial, cattailMaterial, dandelionMaterial;
    public int flowerPetalColorPropertyId, cloverInstanceDataPropertyId, dandelionInstanceDataPropertyId;
    public CloverRenderData[] cloverRenderData;
    public bool receiveCloverShadows, receiveDandelionShadows;
    public GameObject blueberryBushPrefab, raspberryBushPrefab, strawberryBushPrefab, blackberryBushPrefab, fallbackBushPrefab;
    public GameObject[] forestRockPrefabs, grasslandRockPrefabs, grasslandLargeRockPrefabs;
    public GameObject forestRockFallbackPrefab, grasslandRockFallbackPrefab, grasslandLargeRockFallbackPrefab;
    private List<FlowerRenderBatch> flowerRenderBatches = new();
    private List<Matrix4x4[]> lilyPadRenderBatches = new(), cattailRenderBatches = new();
    private List<CloverRenderBatch> cloverRenderBatches = new();
    private List<GrassRenderBatch> dandelionRenderBatches = new();
    private readonly MaterialPropertyBlock flowerPropertyBlock = new(), cloverPropertyBlock = new(), dandelionPropertyBlock = new();
    private GameObject bushGameObjectRoot, rockGameObjectRoot;
    private List<GameObject> bushGameObjects = new(), rockGameObjects = new();
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
        flowerRenderBatches = new(); lilyPadRenderBatches = new(); cattailRenderBatches = new();
        cloverRenderBatches = new(); dandelionRenderBatches = new();
        hasBuiltFlowerRenderData = hasBuiltLilyPadRenderData = hasBuiltCattailRenderData = false;
        hasBuiltCloverRenderData = hasBuiltDandelionRenderData = false;
        ClearBushGameObjects(); ClearRockGameObjects(); TreePlacementReady = false;
    }
    internal void PublishFlowers(List<FlowerRenderBatch> batches) { flowerRenderBatches = batches; hasBuiltFlowerRenderData = true; }
    internal void PublishClover(List<CloverRenderBatch> batches) { cloverRenderBatches = batches; hasBuiltCloverRenderData = true; }
    internal void PublishDandelions(List<GrassRenderBatch> batches) { dandelionRenderBatches = batches; hasBuiltDandelionRenderData = true; }
    internal void PublishWaterPlants(bool cattails, List<Matrix4x4[]> batches)
    {
        if (cattails) { cattailRenderBatches = batches; hasBuiltCattailRenderData = true; }
        else { lilyPadRenderBatches = batches; hasBuiltLilyPadRenderData = true; }
    }
    internal IEnumerator<bool> BuildBushesIncrementally(List<BerryBushInstanceData> instances)
    {
        GameObject staging = new GameObject("BerryBush_Staging");
        staging.SetActive(false); staging.transform.SetParent(root, false);
        var objects = new List<GameObject>();
        bool published = false;
        try
        {
            yield return true;
            foreach (var instance in instances)
            {
                GameObject prefab = GetBushPrefab(instance.variant);
                if (prefab != null)
                {
                    GameObject body = Object.Instantiate(prefab, staging.transform);
                    objects.Add(body); // Cancellation owns it even if setup throws.
                    GameplayLayers.AssignPhysicalColliders(body, GameplayLayers.WorldSolid);
                    body.transform.localPosition = instance.localPosition;
                    body.transform.localRotation = instance.localRotation;
                    body.transform.localScale = instance.localScale;
                    var bush = body.GetComponent<BerryBushRuntime>() ?? body.AddComponent<BerryBushRuntime>();
                    bush.Initialize(instance, BerryBushManager.Instance);
                    ConfigureSpawnedRendererCulling(body);
                }
                yield return true;
            }
            ClearBushGameObjects();
            bushGameObjectRoot = staging; bushGameObjects = objects;
            staging.name = "BerryBush_GameObjects"; staging.SetActive(true);
            hasCurrentBushRepresentation = true; published = true;
        }
        finally { if (!published && staging != null) DestroyObjectRoot(staging); }
    }
    internal IEnumerator<bool> BuildRocksIncrementally(List<RockInstanceData> instances)
    {
        GameObject staging = new GameObject("Rocks_Staging");
        staging.SetActive(false); staging.transform.SetParent(root, false);
        var objects = new List<GameObject>();
        bool published = false;
        try
        {
            yield return true;
            foreach (var instance in instances)
            {
                GameObject prefab = GetRockPrefab(instance.variant, instance.prefabIndex);
                if (prefab != null)
                {
                    GameObject body = Object.Instantiate(prefab, staging.transform);
                    objects.Add(body);
                    body.layer = GameplayLayers.WorldSolid;
                    GameplayLayers.AssignPhysicalColliders(body, GameplayLayers.WorldSolid);
                    body.transform.localPosition = instance.localPosition;
                    body.transform.localRotation = instance.localRotation;
                    body.transform.localScale = instance.localScale;
                    var query = body.GetComponent<RockQueryTarget>() ?? body.AddComponent<RockQueryTarget>();
                    query.Initialize(instance); ConfigureSpawnedRendererCulling(body);
                }
                yield return true;
            }
            ClearRockGameObjects();
            rockGameObjectRoot = staging; rockGameObjects = objects;
            staging.name = "ForestRock_GameObjects"; staging.SetActive(true);
            hasCurrentRockRepresentation = true; published = true;
        }
        finally { if (!published && staging != null) DestroyObjectRoot(staging); }
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
                (daisyWeedMesh != null && daisyWeedMaterial != null)) && hasBuiltFlowerRenderData;
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
        return rockGameObjects.Count > 0;
    }

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
        AccumulateGameObjectStats(rockGameObjects, ref stats.RockGameObjects);
    }

    private bool CacheGrassRenderBatches(
        List<Matrix4x4> worldMatrices,
        List<Vector4> instanceData,
        List<GrassRenderBatch> targetBatches)
    {
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

    public void CacheFlowerBatches(List<Matrix4x4> worldMatrices, List<Vector4> petalColors, bool isTallFlower = false, bool isDaisyWeed = false)
    {
        if (!isTallFlower && !isDaisyWeed)
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

            flowerRenderBatches.Add(new FlowerRenderBatch(matrixBatch, petalColorBatch, isTallFlower, isDaisyWeed));
            startIndex += batchCount;
        }

        hasBuiltFlowerRenderData = true;
    }

    public void CacheFlowerBatches(Matrix4x4[] worldMatrices, Vector4[] petalColors, bool isTallFlower = false, bool isDaisyWeed = false)
    {
        if (!isTallFlower && !isDaisyWeed)
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

            flowerRenderBatches.Add(new FlowerRenderBatch(matrixBatch, petalColorBatch, isTallFlower, isDaisyWeed));
            startIndex += batchCount;
        }

        hasBuiltFlowerRenderData = true;
    }

    public void CacheCloverBatches(List<Matrix4x4>[] worldMatricesByPrefab, List<Vector4>[] instanceDataByPrefab)
    {
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
        hasBuiltDandelionRenderData = CacheGrassRenderBatches(worldMatrices, instanceData, dandelionRenderBatches);
    }

    public void CacheDandelionBatches(Matrix4x4[] worldMatrices, Vector4[] instanceData)
    {
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
            Mesh mesh = batch.isDaisyWeed ? daisyWeedMesh : batch.isTallFlower ? tallFlowerMesh : flowerMesh;
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
        if (bushGameObjectRoot != null) DestroyObjectRoot(bushGameObjectRoot);
        bushGameObjectRoot = null; bushGameObjects = new(); hasCurrentBushRepresentation = false;
    }

    public void ClearRockGameObjects()
    {
        if (rockGameObjectRoot != null) DestroyObjectRoot(rockGameObjectRoot);
        rockGameObjectRoot = null; rockGameObjects = new(); hasCurrentRockRepresentation = false;
    }

    public void ClearFlowerBatches()
    {
        flowerRenderBatches.Clear();
        hasBuiltFlowerRenderData = false;
    }

    public void CacheLilyPadBatches(List<Matrix4x4> matrices)
    {
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
        lilyPadRenderBatches.Clear();
        hasBuiltLilyPadRenderData = false;
    }

    public void CacheCattailBatches(List<Matrix4x4> matrices)
    {
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
        cloverRenderBatches.Clear();
        hasBuiltCloverRenderData = false;
    }

    public void ClearDandelionBatches()
    {
        dandelionRenderBatches.Clear();
        hasBuiltDandelionRenderData = false;
    }

    public void DrawFlowers()
    {
        if (!isVisible || !HasValidFlowerRenderData() || flowerRenderBatches.Count == 0)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        for (int i = 0; i < flowerRenderBatches.Count; i++)
        {
            FlowerRenderBatch batch = flowerRenderBatches[i];

            if (batch.matrices == null || batch.petalColors == null)
                continue;

            flowerPropertyBlock.Clear();
            flowerPropertyBlock.SetVectorArray(flowerPetalColorPropertyId, batch.petalColors);

            Mesh mesh = batch.isDaisyWeed ? daisyWeedMesh : batch.isTallFlower ? tallFlowerMesh : flowerMesh;
            Material material = batch.isDaisyWeed ? daisyWeedMaterial : batch.isTallFlower ? tallFlowerMaterial : flowerMaterial;
            if (mesh == null || material == null)
                continue;

            Graphics.DrawMeshInstanced(
                mesh,
                0,
                material,
                batch.matrices,
                batch.matrices.Length,
                flowerPropertyBlock,
                batch.isTallFlower || batch.isDaisyWeed ? ShadowCastingMode.On : ShadowCastingMode.Off,
                true
            );
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageFlowerDraw,
            stageStart);
    }

    public void DrawLilyPads()
    {
        if (!isVisible || !HasValidLilyPadRenderData())
            return;

        for (int i = 0; i < lilyPadRenderBatches.Count; i++)
        {
            Matrix4x4[] batch = lilyPadRenderBatches[i];
            Graphics.DrawMeshInstanced(lilyPadMesh, 0, lilyPadMaterial, batch, batch.Length,
                null, ShadowCastingMode.Off, false);
        }
    }

    public void DrawCattails()
    {
        if (!isVisible || !HasValidCattailRenderData())
            return;

        for (int i = 0; i < cattailRenderBatches.Count; i++)
        {
            Matrix4x4[] batch = cattailRenderBatches[i];
            Graphics.DrawMeshInstanced(cattailMesh, 0, cattailMaterial, batch, batch.Length,
                null, ShadowCastingMode.Off, false);
        }
    }

    public void DrawClover(Vector3? viewer=null,float renderDistance=0,float fadeWidth=0)
    {
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

            cloverPropertyBlock.Clear();
            cloverPropertyBlock.SetVectorArray(cloverInstanceDataPropertyId, batch.instanceData);
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
                batch.matrices,
                batch.matrices.Length,
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
        if (!isVisible || !HasValidDandelionRenderData() || dandelionRenderBatches.Count == 0)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        for (int i = 0; i < dandelionRenderBatches.Count; i++)
        {
            GrassRenderBatch batch = dandelionRenderBatches[i];
            if (batch.matrices == null || batch.instanceData == null)
                continue;

            dandelionPropertyBlock.Clear();
            dandelionPropertyBlock.SetVectorArray(dandelionInstanceDataPropertyId, batch.instanceData);

            Graphics.DrawMeshInstanced(
                dandelionMesh,
                0,
                dandelionMaterial,
                batch.matrices,
                batch.matrices.Length,
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

        if (variant == WorldFeatureVariant.GrasslandLargeBoulder)
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
