using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct TreeRenderPart
{
    public Mesh mesh;
    public Material material;
    public Matrix4x4 childLocalMatrix;
}

public struct TreeBillboardRenderData
{
    public Mesh mesh;
    public Material material;

    public TreeBillboardRenderData(Mesh mesh, Material material)
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

public class ChunkFoliageRuntime
{
    public Transform root;

    public Mesh grassMesh;
    public Material grassMaterial;
    public bool receiveGrassShadows;
    public int grassInstanceDataPropertyId;
    public Color forestDarkGrassColor;
    public Color forestMidGrassColor;
    public Color forestLightGrassColor;

    public Mesh billboardMesh;
    public Material billboardMaterial;

    public Mesh flowerMesh;
    public Material flowerMaterial;
    public int flowerPetalColorPropertyId;

    public GameObject mapleTreePrefab;
    public GameObject sugarMapleTreePrefab;
    public GameObject birchAspenTreePrefab;
    public GameObject beechTreePrefab;
    public GameObject spruceTreePrefab;
    public GameObject whitePineTreePrefab;
    public GameObject oakTreePrefab;
    public GameObject fallbackTreePrefab;
    public GameObject blueberryBushPrefab;
    public GameObject raspberryBushPrefab;
    public GameObject strawberryBushPrefab;
    public GameObject blackberryBushPrefab;
    public GameObject fallbackBushPrefab;
    public GameObject[] forestRockPrefabs;
    public TreeBillboardRenderData mapleTreeBillboard;
    public TreeBillboardRenderData sugarMapleTreeBillboard;
    public TreeBillboardRenderData birchAspenTreeBillboard;
    public TreeBillboardRenderData beechTreeBillboard;
    public TreeBillboardRenderData spruceTreeBillboard;
    public TreeBillboardRenderData whitePineTreeBillboard;
    public TreeBillboardRenderData oakTreeBillboard;
    public TreeBillboardRenderData fallbackTreeBillboard;

    public bool isVisible;

    private readonly List<GrassRenderBatch> grassRenderBatches = new List<GrassRenderBatch>();
    private readonly List<GrassRenderBatch> billboardRenderBatches = new List<GrassRenderBatch>();
    private readonly MaterialPropertyBlock grassPropertyBlock = new MaterialPropertyBlock();
    private readonly List<FlowerRenderBatch> flowerRenderBatches = new List<FlowerRenderBatch>();
    private readonly MaterialPropertyBlock flowerPropertyBlock = new MaterialPropertyBlock();

    private readonly List<Matrix4x4[]> mapleTreeBillboardMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> sugarMapleTreeBillboardMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> birchAspenTreeBillboardMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> beechTreeBillboardMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> spruceTreeBillboardMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> whitePineTreeBillboardMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> oakTreeBillboardMatrixBatches = new List<Matrix4x4[]>();

    private GameObject treeGameObjectRoot;
    private readonly List<GameObject> treeGameObjects = new List<GameObject>();
    private GameObject bushGameObjectRoot;
    private readonly List<GameObject> bushGameObjects = new List<GameObject>();
    private GameObject rockGameObjectRoot;
    private readonly List<GameObject> rockGameObjects = new List<GameObject>();

    private FoliageRepresentationMode currentTreeRepresentationMode;
    private bool hasCurrentTreeRepresentation;
    private bool hasCurrentBushRepresentation;
    private bool hasCurrentRockRepresentation;

    public bool IsCreated => root != null;
    public int GpuGrassInstanceCount =>
        CountGrassInstances(grassRenderBatches) +
        CountGrassInstances(billboardRenderBatches);
    public int GpuFlowerInstanceCount => CountFlowerInstances();
    public int GpuTreeInstanceCount =>
        CountMatrices(mapleTreeBillboardMatrixBatches) +
        CountMatrices(sugarMapleTreeBillboardMatrixBatches) +
        CountMatrices(birchAspenTreeBillboardMatrixBatches) +
        CountMatrices(beechTreeBillboardMatrixBatches) +
        CountMatrices(spruceTreeBillboardMatrixBatches) +
        CountMatrices(whitePineTreeBillboardMatrixBatches) +
        CountMatrices(oakTreeBillboardMatrixBatches);

    public bool HasCurrentTreeRepresentation(FoliageRepresentationMode mode)
    {
        return hasCurrentTreeRepresentation &&
               currentTreeRepresentationMode == mode;
    }

    public void SetCurrentTreeRepresentation(FoliageRepresentationMode mode)
    {
        currentTreeRepresentationMode = mode;
        hasCurrentTreeRepresentation = true;
    }

    public void ClearCurrentTreeRepresentation()
    {
        hasCurrentTreeRepresentation = false;
    }

    public bool HasCurrentBushRepresentation()
    {
        return hasCurrentBushRepresentation;
    }

    public bool HasCurrentRockRepresentation()
    {
        return hasCurrentRockRepresentation;
    }

    public void ClearCachedBatches()
    {
        grassRenderBatches.Clear();
        billboardRenderBatches.Clear();
        flowerRenderBatches.Clear();
        mapleTreeBillboardMatrixBatches.Clear();
        sugarMapleTreeBillboardMatrixBatches.Clear();
        birchAspenTreeBillboardMatrixBatches.Clear();
        beechTreeBillboardMatrixBatches.Clear();
        spruceTreeBillboardMatrixBatches.Clear();
        whitePineTreeBillboardMatrixBatches.Clear();
        oakTreeBillboardMatrixBatches.Clear();
        ClearTreeGameObjects();
        ClearBushGameObjects();
        ClearRockGameObjects();
        ClearCurrentTreeRepresentation();
    }

    public void SetVisible(bool visible)
    {
        isVisible = visible;

        if (root != null)
        {
            root.gameObject.SetActive(visible);
        }
    }

    public bool HasValidGrassRenderData()
    {
        return grassMesh != null &&
               grassMaterial != null &&
               grassRenderBatches.Count > 0;
    }

    public bool HasValidBillboardRenderData()
    {
        return billboardMesh != null &&
               billboardMaterial != null &&
               billboardRenderBatches.Count > 0;
    }

    public bool HasValidFlowerRenderData()
    {
        return flowerMesh != null && flowerMaterial != null && flowerRenderBatches.Count > 0;
    }

    public bool HasValidTreeBillboardRenderData()
    {
        return HasValidBillboardBatch(mapleTreeBillboard, mapleTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(sugarMapleTreeBillboard, sugarMapleTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(birchAspenTreeBillboard, birchAspenTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(beechTreeBillboard, beechTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(spruceTreeBillboard, spruceTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(whitePineTreeBillboard, whitePineTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(oakTreeBillboard, oakTreeBillboardMatrixBatches);
    }

    public bool HasTreeGameObjects()
    {
        return treeGameObjects.Count > 0;
    }

    public bool HasBushGameObjects()
    {
        return bushGameObjects.Count > 0;
    }

    public bool HasRockGameObjects()
    {
        return rockGameObjects.Count > 0;
    }

    public void AccumulateGrassRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateGrassStats(grassMesh, grassRenderBatches, ref stats.Grass);
    }

    public void AccumulateBillboardGrassRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateGrassStats(billboardMesh, billboardRenderBatches, ref stats.BillboardGrass);
    }

    public void AccumulateFlowerRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateFlowerStats(ref stats.Flowers);
    }

    public void AccumulateTreeBillboardRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateTreeBillboardStats(ref stats.TreeBillboards);
    }

    public void AccumulateTreeGameObjectRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateGameObjectStats(treeGameObjects, ref stats.TreeGameObjects);
    }

    public void AccumulateBushGameObjectRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateGameObjectStats(bushGameObjects, ref stats.BushGameObjects);
    }

    public void AccumulateRockGameObjectRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateGameObjectStats(rockGameObjects, ref stats.RockGameObjects);
    }

    public void CacheGrassMatrices(List<Matrix4x4> worldMatrices, List<Vector4> instanceData)
    {
        CacheGrassRenderBatches(worldMatrices, instanceData, grassRenderBatches);
    }

    public void CacheBillboardMatrices(List<Matrix4x4> worldMatrices, List<Vector4> instanceData)
    {
        CacheGrassRenderBatches(worldMatrices, instanceData, billboardRenderBatches);
    }

    private void CacheGrassRenderBatches(
        List<Matrix4x4> worldMatrices,
        List<Vector4> instanceData,
        List<GrassRenderBatch> targetBatches)
    {
        targetBatches.Clear();

        if (worldMatrices == null || instanceData == null)
            return;

        if (worldMatrices.Count != instanceData.Count)
        {
            Debug.LogError("Grass matrix and instance data counts must match.");
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

            targetBatches.Add(new GrassRenderBatch(matrixBatch, instanceDataBatch));
            startIndex += batchCount;
        }
    }

    public void CacheFlowerBatches(List<Matrix4x4> worldMatrices, List<Vector4> petalColors)
    {
        flowerRenderBatches.Clear();

        if (worldMatrices == null || petalColors == null)
            return;

        if (worldMatrices.Count != petalColors.Count)
        {
            Debug.LogError("Flower matrix and petal color counts must match.");
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

            flowerRenderBatches.Add(new FlowerRenderBatch(matrixBatch, petalColorBatch));
            startIndex += batchCount;
        }
    }

    public void CacheTreeBillboardMatrices(
        List<Matrix4x4> mapleWorldMatrices,
        List<Matrix4x4> sugarMapleWorldMatrices,
        List<Matrix4x4> birchAspenWorldMatrices,
        List<Matrix4x4> beechWorldMatrices,
        List<Matrix4x4> spruceWorldMatrices,
        List<Matrix4x4> whitePineWorldMatrices,
        List<Matrix4x4> oakWorldMatrices)
    {
        CacheMatrices(mapleWorldMatrices, mapleTreeBillboardMatrixBatches);
        CacheMatrices(sugarMapleWorldMatrices, sugarMapleTreeBillboardMatrixBatches);
        CacheMatrices(birchAspenWorldMatrices, birchAspenTreeBillboardMatrixBatches);
        CacheMatrices(beechWorldMatrices, beechTreeBillboardMatrixBatches);
        CacheMatrices(spruceWorldMatrices, spruceTreeBillboardMatrixBatches);
        CacheMatrices(whitePineWorldMatrices, whitePineTreeBillboardMatrixBatches);
        CacheMatrices(oakWorldMatrices, oakTreeBillboardMatrixBatches);
    }

    private void CacheMatrices(List<Matrix4x4> worldMatrices, List<Matrix4x4[]> targetBatches)
    {
        targetBatches.Clear();

        const int maxBatchSize = 1023;
        int totalCount = worldMatrices.Count;
        int startIndex = 0;

        while (startIndex < totalCount)
        {
            int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
            Matrix4x4[] batch = new Matrix4x4[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                batch[i] = worldMatrices[startIndex + i];
            }

            targetBatches.Add(batch);
            startIndex += batchCount;
        }
    }

    private int CountMatrices(List<Matrix4x4[]> batches)
    {
        int count = 0;

        for (int i = 0; i < batches.Count; i++)
        {
            if (batches[i] != null)
                count += batches[i].Length;
        }

        return count;
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

    private void AccumulateGrassStats(
        Mesh mesh,
        List<GrassRenderBatch> batches,
        ref RenderGeometryStats stats)
    {
        if (mesh == null)
            return;

        for (int i = 0; i < batches.Count; i++)
        {
            if (batches[i].matrices == null)
                continue;

            stats.AddMeshInstances(mesh, batches[i].matrices.Length);
        }
    }

    private void AccumulateFlowerStats(ref RenderGeometryStats stats)
    {
        if (flowerMesh == null)
            return;

        for (int i = 0; i < flowerRenderBatches.Count; i++)
        {
            if (flowerRenderBatches[i].matrices == null)
                continue;

            stats.AddMeshInstances(flowerMesh, flowerRenderBatches[i].matrices.Length);
        }
    }

    private void AccumulateTreeBillboardStats(ref RenderGeometryStats stats)
    {
        AccumulateTreeBillboardBatchStats(mapleTreeBillboard, mapleTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(sugarMapleTreeBillboard, sugarMapleTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(birchAspenTreeBillboard, birchAspenTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(beechTreeBillboard, beechTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(spruceTreeBillboard, spruceTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(whitePineTreeBillboard, whitePineTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(oakTreeBillboard, oakTreeBillboardMatrixBatches, ref stats);
    }

    private void AccumulateTreeBillboardBatchStats(
        TreeBillboardRenderData renderData,
        List<Matrix4x4[]> batches,
        ref RenderGeometryStats stats)
    {
        if (renderData.mesh == null)
            return;

        for (int i = 0; i < batches.Count; i++)
        {
            if (batches[i] == null)
                continue;

            stats.AddMeshInstances(renderData.mesh, batches[i].Length);
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

    public void RebuildTreeGameObjects(
        List<TreeInstanceData> instances,
        Transform chunkRoot)
    {
        ClearTreeGameObjects();

        if (instances == null || chunkRoot == null || root == null)
            return;

        treeGameObjectRoot = new GameObject("Tree_GameObjects");
        treeGameObjectRoot.transform.SetParent(root, false);

        for (int i = 0; i < instances.Count; i++)
        {
            TreeInstanceData instance = instances[i];
            GameObject prefab = GetTreePrefab(instance.variant);

            if (prefab == null)
                continue;

            GameObject treeObject = Object.Instantiate(prefab, treeGameObjectRoot.transform);
            treeObject.transform.localPosition = instance.localPosition;
            treeObject.transform.localRotation = instance.localRotation;
            treeObject.transform.localScale = instance.localScale;

            treeGameObjects.Add(treeObject);
        }
    }

    public void RebuildBushGameObjects(
        List<TreeInstanceData> instances,
        Transform chunkRoot)
    {
        ClearBushGameObjects();

        if (instances == null || chunkRoot == null || root == null)
            return;

        bushGameObjectRoot = new GameObject("BerryBush_GameObjects");
        bushGameObjectRoot.transform.SetParent(root, false);

        for (int i = 0; i < instances.Count; i++)
        {
            TreeInstanceData instance = instances[i];
            GameObject prefab = GetBushPrefab(instance.variant);

            if (prefab == null)
                continue;

            GameObject bushObject = Object.Instantiate(prefab, bushGameObjectRoot.transform);
            bushObject.transform.localPosition = instance.localPosition;
            bushObject.transform.localRotation = instance.localRotation;
            bushObject.transform.localScale = instance.localScale;

            bushGameObjects.Add(bushObject);
        }

        hasCurrentBushRepresentation = true;
    }

    public void RebuildRockGameObjects(
        List<RockInstanceData> instances,
        Transform chunkRoot)
    {
        ClearRockGameObjects();

        if (instances == null || chunkRoot == null || root == null)
            return;

        rockGameObjectRoot = new GameObject("ForestRock_GameObjects");
        rockGameObjectRoot.transform.SetParent(root, false);

        for (int i = 0; i < instances.Count; i++)
        {
            RockInstanceData instance = instances[i];
            GameObject prefab = GetRockPrefab(instance.prefabIndex);

            if (prefab == null)
                continue;

            GameObject rockObject = Object.Instantiate(prefab, rockGameObjectRoot.transform);
            rockObject.transform.localPosition = instance.localPosition;
            rockObject.transform.localRotation = instance.localRotation;
            rockObject.transform.localScale = instance.localScale;

            rockGameObjects.Add(rockObject);
        }

        hasCurrentRockRepresentation = true;
    }

    public void ClearTreeGameObjects()
    {
        for (int i = 0; i < treeGameObjects.Count; i++)
        {
            if (treeGameObjects[i] != null)
            {
                Object.Destroy(treeGameObjects[i]);
            }
        }

        treeGameObjects.Clear();

        if (treeGameObjectRoot != null)
        {
            Object.Destroy(treeGameObjectRoot);
            treeGameObjectRoot = null;
        }
    }

    public void ClearBushGameObjects()
    {
        for (int i = 0; i < bushGameObjects.Count; i++)
        {
            if (bushGameObjects[i] != null)
            {
                Object.Destroy(bushGameObjects[i]);
            }
        }

        bushGameObjects.Clear();

        if (bushGameObjectRoot != null)
        {
            Object.Destroy(bushGameObjectRoot);
            bushGameObjectRoot = null;
        }

        hasCurrentBushRepresentation = false;
    }

    public void ClearRockGameObjects()
    {
        for (int i = 0; i < rockGameObjects.Count; i++)
        {
            if (rockGameObjects[i] != null)
            {
                Object.Destroy(rockGameObjects[i]);
            }
        }

        rockGameObjects.Clear();

        if (rockGameObjectRoot != null)
        {
            Object.Destroy(rockGameObjectRoot);
            rockGameObjectRoot = null;
        }

        hasCurrentRockRepresentation = false;
    }

    public void ClearFlowerBatches()
    {
        flowerRenderBatches.Clear();
    }

    public void ClearTreeBillboardMatrices()
    {
        mapleTreeBillboardMatrixBatches.Clear();
        sugarMapleTreeBillboardMatrixBatches.Clear();
        birchAspenTreeBillboardMatrixBatches.Clear();
        beechTreeBillboardMatrixBatches.Clear();
        spruceTreeBillboardMatrixBatches.Clear();
        whitePineTreeBillboardMatrixBatches.Clear();
        oakTreeBillboardMatrixBatches.Clear();
    }

    public void DrawGrass()
    {
        if (!isVisible || !HasValidGrassRenderData())
            return;

        for (int i = 0; i < grassRenderBatches.Count; i++)
        {
            DrawInstancedBatch(grassMesh, grassMaterial, grassRenderBatches[i]);
        }
    }

    public void DrawBillboards()
    {
        if (!isVisible || !HasValidBillboardRenderData())
            return;

        for (int i = 0; i < billboardRenderBatches.Count; i++)
        {
            DrawInstancedBatch(billboardMesh, billboardMaterial, billboardRenderBatches[i]);
        }
    }

    private void DrawInstancedBatch(
        Mesh mesh,
        Material material,
        GrassRenderBatch batch)
    {
        grassPropertyBlock.Clear();
        grassPropertyBlock.SetVectorArray(grassInstanceDataPropertyId, batch.instanceData);
        grassPropertyBlock.SetColor("_ForestDarkGrassColor", forestDarkGrassColor);
        grassPropertyBlock.SetColor("_ForestMidGrassColor", forestMidGrassColor);
        grassPropertyBlock.SetColor("_ForestLightGrassColor", forestLightGrassColor);

        Graphics.DrawMeshInstanced(
            mesh,
            0,
            material,
            batch.matrices,
            batch.matrices.Length,
            grassPropertyBlock,
            ShadowCastingMode.Off,
            receiveGrassShadows
        );
    }

    public void DrawFlowers()
    {
        if (!isVisible || !HasValidFlowerRenderData())
            return;

        for (int i = 0; i < flowerRenderBatches.Count; i++)
        {
            FlowerRenderBatch batch = flowerRenderBatches[i];

            if (batch.matrices == null || batch.petalColors == null)
                continue;

            flowerPropertyBlock.Clear();
            flowerPropertyBlock.SetVectorArray(flowerPetalColorPropertyId, batch.petalColors);

            Graphics.DrawMeshInstanced(
                flowerMesh,
                0,
                flowerMaterial,
                batch.matrices,
                batch.matrices.Length,
                flowerPropertyBlock,
                ShadowCastingMode.Off,
                true
            );
        }
    }

    public void DrawTreeBillboards(bool castShadows, bool receiveShadows)
    {
        if (!isVisible || !HasValidTreeBillboardRenderData())
            return;

        ShadowCastingMode shadowMode = castShadows
            ? ShadowCastingMode.On
            : ShadowCastingMode.Off;

        DrawTreeBillboardBatches(mapleTreeBillboard, mapleTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(sugarMapleTreeBillboard, sugarMapleTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(birchAspenTreeBillboard, birchAspenTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(beechTreeBillboard, beechTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(spruceTreeBillboard, spruceTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(whitePineTreeBillboard, whitePineTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(oakTreeBillboard, oakTreeBillboardMatrixBatches, shadowMode, receiveShadows);
    }

    private void DrawTreeBillboardBatches(
        TreeBillboardRenderData renderData,
        List<Matrix4x4[]> batches,
        ShadowCastingMode shadowMode,
        bool receiveShadows)
    {
        if (renderData.mesh == null || renderData.material == null)
            return;

        for (int i = 0; i < batches.Count; i++)
        {
            Graphics.DrawMeshInstanced(
                renderData.mesh,
                0,
                renderData.material,
                batches[i],
                batches[i].Length,
                null,
                shadowMode,
                receiveShadows
            );
        }
    }

    private bool HasValidBillboardBatch(TreeBillboardRenderData renderData, List<Matrix4x4[]> batches)
    {
        return renderData.mesh != null &&
               renderData.material != null &&
               batches.Count > 0;
    }

    private GameObject GetTreePrefab(WorldFeatureVariant variant)
    {
        if (variant == WorldFeatureVariant.MapleTree && mapleTreePrefab != null)
            return mapleTreePrefab;

        if (variant == WorldFeatureVariant.SugarMapleTree && sugarMapleTreePrefab != null)
            return sugarMapleTreePrefab;

        if (variant == WorldFeatureVariant.BirchAspenTree && birchAspenTreePrefab != null)
            return birchAspenTreePrefab;

        if (variant == WorldFeatureVariant.BeechTree && beechTreePrefab != null)
            return beechTreePrefab;

        if (variant == WorldFeatureVariant.SpruceTree && spruceTreePrefab != null)
            return spruceTreePrefab;

        if (variant == WorldFeatureVariant.WhitePineTree && whitePineTreePrefab != null)
            return whitePineTreePrefab;

        if (variant == WorldFeatureVariant.OakTree && oakTreePrefab != null)
            return oakTreePrefab;

        return fallbackTreePrefab;
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

    private GameObject GetRockPrefab(int prefabIndex)
    {
        if (forestRockPrefabs == null || forestRockPrefabs.Length == 0)
            return null;

        int clampedIndex = Mathf.Clamp(prefabIndex, 0, forestRockPrefabs.Length - 1);
        return forestRockPrefabs[clampedIndex];
    }
}
