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

public struct TreeBillboardInstanceBatch
{
    public Matrix4x4[] matrices;
    public Vector4[] leafTints;

    public TreeBillboardInstanceBatch(Matrix4x4[] matrices, Vector4[] leafTints)
    {
        this.matrices = matrices;
        this.leafTints = leafTints;
    }
}

public class ChunkFoliageRuntime
{
    private static readonly int TreeLeafTintPropertyId = Shader.PropertyToID("_TreeLeafTint");
    private static readonly int TreeBarkTintPropertyId = Shader.PropertyToID("_TreeBarkTint");

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

    public CloverRenderData[] cloverRenderData;
    public bool receiveCloverShadows;
    public int cloverInstanceDataPropertyId;

    public GameObject mapleTreePrefab;
    public GameObject sugarMapleTreePrefab;
    public GameObject birchAspenTreePrefab;
    public GameObject beechTreePrefab;
    public GameObject spruceTreePrefab;
    public GameObject whitePineTreePrefab;
    public GameObject oakTreePrefab;
    public GameObject fallbackTreePrefab;
    public GameObject grasslandMapleTreePrefab;
    public GameObject grasslandBirchAspenTreePrefab;
    public GameObject grasslandWhitePineTreePrefab;
    public GameObject grasslandOakTreePrefab;
    public GameObject grasslandWillowTreePrefab;
    public GameObject grasslandFallbackTreePrefab;
    public GameObject blueberryBushPrefab;
    public GameObject raspberryBushPrefab;
    public GameObject strawberryBushPrefab;
    public GameObject blackberryBushPrefab;
    public GameObject fallbackBushPrefab;
    public GameObject[] forestRockPrefabs;
    public GameObject forestRockFallbackPrefab;
    public GameObject[] grasslandRockPrefabs;
    public GameObject grasslandRockFallbackPrefab;
    public GameObject[] grasslandLargeRockPrefabs;
    public GameObject grasslandLargeRockFallbackPrefab;
    public TreeBillboardRenderData mapleTreeBillboard;
    public TreeBillboardRenderData sugarMapleTreeBillboard;
    public TreeBillboardRenderData birchAspenTreeBillboard;
    public TreeBillboardRenderData beechTreeBillboard;
    public TreeBillboardRenderData spruceTreeBillboard;
    public TreeBillboardRenderData whitePineTreeBillboard;
    public TreeBillboardRenderData oakTreeBillboard;
    public TreeBillboardRenderData fallbackTreeBillboard;
    public TreeBillboardRenderData grasslandMapleTreeBillboard;
    public TreeBillboardRenderData grasslandBirchAspenTreeBillboard;
    public TreeBillboardRenderData grasslandWhitePineTreeBillboard;
    public TreeBillboardRenderData grasslandOakTreeBillboard;
    public TreeBillboardRenderData grasslandWillowTreeBillboard;
    public TreeBillboardRenderData grasslandFallbackTreeBillboard;

    public bool isVisible;

    private readonly List<GrassRenderBatch> grassRenderBatches = new List<GrassRenderBatch>();
    private readonly List<GrassRenderBatch> billboardRenderBatches = new List<GrassRenderBatch>();
    private readonly MaterialPropertyBlock grassPropertyBlock = new MaterialPropertyBlock();
    private readonly List<FlowerRenderBatch> flowerRenderBatches = new List<FlowerRenderBatch>();
    private readonly MaterialPropertyBlock flowerPropertyBlock = new MaterialPropertyBlock();
    private readonly List<CloverRenderBatch> cloverRenderBatches = new List<CloverRenderBatch>();
    private readonly MaterialPropertyBlock cloverPropertyBlock = new MaterialPropertyBlock();

    private readonly List<TreeBillboardInstanceBatch> mapleTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> sugarMapleTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> birchAspenTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> beechTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> spruceTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> whitePineTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> oakTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> grasslandMapleTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> grasslandBirchAspenTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> grasslandWhitePineTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> grasslandOakTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();
    private readonly List<TreeBillboardInstanceBatch> grasslandWillowTreeBillboardMatrixBatches = new List<TreeBillboardInstanceBatch>();

    private GameObject treeGameObjectRoot;
    private readonly List<TreeGameObjectInstance> treeGameObjects = new List<TreeGameObjectInstance>();
    private readonly Dictionary<GameObject, Stack<TreeGameObjectInstance>> pooledTreeGameObjects =
        new Dictionary<GameObject, Stack<TreeGameObjectInstance>>();
    private GameObject bushGameObjectRoot;
    private readonly List<GameObject> bushGameObjects = new List<GameObject>();
    private GameObject rockGameObjectRoot;
    private readonly List<GameObject> rockGameObjects = new List<GameObject>();
    private readonly MaterialPropertyBlock treePropertyBlock = new MaterialPropertyBlock();
    private readonly MaterialPropertyBlock treeBillboardPropertyBlock = new MaterialPropertyBlock();

    private FoliageRepresentationMode currentTreeRepresentationMode;
    private bool hasCurrentTreeRepresentation;
    private bool hasCurrentBushRepresentation;
    private bool hasCurrentRockRepresentation;
    private bool hasBuiltGrassRenderData;
    private bool hasBuiltBillboardRenderData;
    private bool hasBuiltFlowerRenderData;
    private bool hasBuiltCloverRenderData;

    public bool IsCreated => root != null;
    public int GpuGrassInstanceCount =>
        CountGrassInstances(grassRenderBatches) +
        CountGrassInstances(billboardRenderBatches);
    public int GpuFlowerInstanceCount => CountFlowerInstances();
    public int GpuCloverInstanceCount => CountCloverInstances();
    public int GpuTreeInstanceCount =>
        CountMatrices(mapleTreeBillboardMatrixBatches) +
        CountMatrices(sugarMapleTreeBillboardMatrixBatches) +
        CountMatrices(birchAspenTreeBillboardMatrixBatches) +
        CountMatrices(beechTreeBillboardMatrixBatches) +
        CountMatrices(spruceTreeBillboardMatrixBatches) +
        CountMatrices(whitePineTreeBillboardMatrixBatches) +
        CountMatrices(oakTreeBillboardMatrixBatches) +
        CountMatrices(grasslandMapleTreeBillboardMatrixBatches) +
        CountMatrices(grasslandBirchAspenTreeBillboardMatrixBatches) +
        CountMatrices(grasslandWhitePineTreeBillboardMatrixBatches) +
        CountMatrices(grasslandOakTreeBillboardMatrixBatches) +
        CountMatrices(grasslandWillowTreeBillboardMatrixBatches);
    public int TreeGameObjectCount => treeGameObjects.Count;

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

    public void ClearTreeRepresentation()
    {
        ClearTreeRepresentation(false);
    }

    public void ClearTreeRepresentation(bool retainTreeGameObjectsForReuse)
    {
        if (!HasAnyTreeRepresentationData())
            return;

        if (retainTreeGameObjectsForReuse)
            ReleaseTreeGameObjectsToPool();
        else
            DestroyTreeGameObjectsAndPool();

        ClearTreeBillboardMatrices();
        ClearCurrentTreeRepresentation();
    }

    private bool HasAnyTreeRepresentationData()
    {
        return hasCurrentTreeRepresentation ||
               treeGameObjects.Count > 0 ||
               pooledTreeGameObjects.Count > 0 ||
               mapleTreeBillboardMatrixBatches.Count > 0 ||
               sugarMapleTreeBillboardMatrixBatches.Count > 0 ||
               birchAspenTreeBillboardMatrixBatches.Count > 0 ||
               beechTreeBillboardMatrixBatches.Count > 0 ||
               spruceTreeBillboardMatrixBatches.Count > 0 ||
               whitePineTreeBillboardMatrixBatches.Count > 0 ||
               oakTreeBillboardMatrixBatches.Count > 0 ||
               grasslandMapleTreeBillboardMatrixBatches.Count > 0 ||
               grasslandBirchAspenTreeBillboardMatrixBatches.Count > 0 ||
               grasslandWhitePineTreeBillboardMatrixBatches.Count > 0 ||
               grasslandOakTreeBillboardMatrixBatches.Count > 0 ||
               grasslandWillowTreeBillboardMatrixBatches.Count > 0;
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
        cloverRenderBatches.Clear();
        hasBuiltGrassRenderData = false;
        hasBuiltBillboardRenderData = false;
        hasBuiltFlowerRenderData = false;
        hasBuiltCloverRenderData = false;
        mapleTreeBillboardMatrixBatches.Clear();
        sugarMapleTreeBillboardMatrixBatches.Clear();
        birchAspenTreeBillboardMatrixBatches.Clear();
        beechTreeBillboardMatrixBatches.Clear();
        spruceTreeBillboardMatrixBatches.Clear();
        whitePineTreeBillboardMatrixBatches.Clear();
        oakTreeBillboardMatrixBatches.Clear();
        grasslandMapleTreeBillboardMatrixBatches.Clear();
        grasslandBirchAspenTreeBillboardMatrixBatches.Clear();
        grasslandWhitePineTreeBillboardMatrixBatches.Clear();
        grasslandOakTreeBillboardMatrixBatches.Clear();
        grasslandWillowTreeBillboardMatrixBatches.Clear();
        DestroyTreeGameObjectsAndPool();
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
               hasBuiltGrassRenderData;
    }

    public bool HasValidBillboardRenderData()
    {
        return billboardMesh != null &&
               billboardMaterial != null &&
               hasBuiltBillboardRenderData;
    }

    public bool HasValidFlowerRenderData()
    {
        return flowerMesh != null && flowerMaterial != null && hasBuiltFlowerRenderData;
    }

    public bool HasValidCloverRenderData()
    {
        return HasAnyValidCloverRenderAsset() && hasBuiltCloverRenderData;
    }

    public bool HasValidTreeBillboardRenderData()
    {
        return HasValidBillboardBatch(mapleTreeBillboard, mapleTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(sugarMapleTreeBillboard, sugarMapleTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(birchAspenTreeBillboard, birchAspenTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(beechTreeBillboard, beechTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(spruceTreeBillboard, spruceTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(whitePineTreeBillboard, whitePineTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(oakTreeBillboard, oakTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(grasslandMapleTreeBillboard, grasslandMapleTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(grasslandBirchAspenTreeBillboard, grasslandBirchAspenTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(grasslandWhitePineTreeBillboard, grasslandWhitePineTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(grasslandOakTreeBillboard, grasslandOakTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(grasslandWillowTreeBillboard, grasslandWillowTreeBillboardMatrixBatches);
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

    public void AccumulateCloverRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateCloverStats(ref stats.Clover);
    }

    public void AccumulateTreeBillboardRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateTreeBillboardStats(ref stats.TreeBillboards);
    }

    public void AccumulateTreeGameObjectRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        AccumulateTreeGameObjectStats(ref stats.TreeGameObjects);
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
        hasBuiltGrassRenderData = CacheGrassRenderBatches(worldMatrices, instanceData, grassRenderBatches);
    }

    public void CacheGrassMatrices(Matrix4x4[] worldMatrices, Vector4[] instanceData)
    {
        hasBuiltGrassRenderData = CacheGrassRenderBatches(worldMatrices, instanceData, grassRenderBatches);
    }

    public void CacheBillboardMatrices(List<Matrix4x4> worldMatrices, List<Vector4> instanceData)
    {
        hasBuiltBillboardRenderData = CacheGrassRenderBatches(worldMatrices, instanceData, billboardRenderBatches);
    }

    public void CacheBillboardMatrices(Matrix4x4[] worldMatrices, Vector4[] instanceData)
    {
        hasBuiltBillboardRenderData = CacheGrassRenderBatches(worldMatrices, instanceData, billboardRenderBatches);
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

    public void CacheFlowerBatches(List<Matrix4x4> worldMatrices, List<Vector4> petalColors)
    {
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

            flowerRenderBatches.Add(new FlowerRenderBatch(matrixBatch, petalColorBatch));
            startIndex += batchCount;
        }

        hasBuiltFlowerRenderData = true;
    }

    public void CacheFlowerBatches(Matrix4x4[] worldMatrices, Vector4[] petalColors)
    {
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

            flowerRenderBatches.Add(new FlowerRenderBatch(matrixBatch, petalColorBatch));
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

    public void CacheTreeBillboardMatrices(
        List<Matrix4x4> mapleWorldMatrices,
        List<Vector4> mapleLeafTints,
        List<Matrix4x4> sugarMapleWorldMatrices,
        List<Vector4> sugarMapleLeafTints,
        List<Matrix4x4> birchAspenWorldMatrices,
        List<Vector4> birchAspenLeafTints,
        List<Matrix4x4> beechWorldMatrices,
        List<Vector4> beechLeafTints,
        List<Matrix4x4> spruceWorldMatrices,
        List<Vector4> spruceLeafTints,
        List<Matrix4x4> whitePineWorldMatrices,
        List<Vector4> whitePineLeafTints,
        List<Matrix4x4> oakWorldMatrices,
        List<Vector4> oakLeafTints,
        List<Matrix4x4> grasslandMapleWorldMatrices,
        List<Vector4> grasslandMapleLeafTints,
        List<Matrix4x4> grasslandBirchAspenWorldMatrices,
        List<Vector4> grasslandBirchAspenLeafTints,
        List<Matrix4x4> grasslandWhitePineWorldMatrices,
        List<Vector4> grasslandWhitePineLeafTints,
        List<Matrix4x4> grasslandOakWorldMatrices,
        List<Vector4> grasslandOakLeafTints,
        List<Matrix4x4> grasslandWillowWorldMatrices,
        List<Vector4> grasslandWillowLeafTints)
    {
        CacheTreeBillboardBatches(mapleWorldMatrices, mapleLeafTints, mapleTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(sugarMapleWorldMatrices, sugarMapleLeafTints, sugarMapleTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(birchAspenWorldMatrices, birchAspenLeafTints, birchAspenTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(beechWorldMatrices, beechLeafTints, beechTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(spruceWorldMatrices, spruceLeafTints, spruceTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(whitePineWorldMatrices, whitePineLeafTints, whitePineTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(oakWorldMatrices, oakLeafTints, oakTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(grasslandMapleWorldMatrices, grasslandMapleLeafTints, grasslandMapleTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(grasslandBirchAspenWorldMatrices, grasslandBirchAspenLeafTints, grasslandBirchAspenTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(grasslandWhitePineWorldMatrices, grasslandWhitePineLeafTints, grasslandWhitePineTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(grasslandOakWorldMatrices, grasslandOakLeafTints, grasslandOakTreeBillboardMatrixBatches);
        CacheTreeBillboardBatches(grasslandWillowWorldMatrices, grasslandWillowLeafTints, grasslandWillowTreeBillboardMatrixBatches);
    }

    private void CacheTreeBillboardBatches(
        List<Matrix4x4> worldMatrices,
        List<Vector4> leafTints,
        List<TreeBillboardInstanceBatch> targetBatches)
    {
        targetBatches.Clear();

        if (worldMatrices == null || leafTints == null)
            return;

        if (worldMatrices.Count != leafTints.Count)
        {
            Debug.LogError("Tree billboard matrix and leaf tint counts must match.");
            return;
        }

        const int maxBatchSize = 1023;
        int totalCount = worldMatrices.Count;
        int startIndex = 0;

        while (startIndex < totalCount)
        {
            int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
            Matrix4x4[] matrixBatch = new Matrix4x4[batchCount];
            Vector4[] leafTintBatch = new Vector4[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                matrixBatch[i] = worldMatrices[startIndex + i];
                leafTintBatch[i] = leafTints[startIndex + i];
            }

            targetBatches.Add(new TreeBillboardInstanceBatch(matrixBatch, leafTintBatch));
            startIndex += batchCount;
        }
    }

    private int CountMatrices(List<TreeBillboardInstanceBatch> batches)
    {
        int count = 0;

        for (int i = 0; i < batches.Count; i++)
        {
            if (batches[i].matrices != null)
                count += batches[i].matrices.Length;
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

    private void AccumulateTreeBillboardStats(ref RenderGeometryStats stats)
    {
        AccumulateTreeBillboardBatchStats(mapleTreeBillboard, mapleTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(sugarMapleTreeBillboard, sugarMapleTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(birchAspenTreeBillboard, birchAspenTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(beechTreeBillboard, beechTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(spruceTreeBillboard, spruceTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(whitePineTreeBillboard, whitePineTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(oakTreeBillboard, oakTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(grasslandMapleTreeBillboard, grasslandMapleTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(grasslandBirchAspenTreeBillboard, grasslandBirchAspenTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(grasslandWhitePineTreeBillboard, grasslandWhitePineTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(grasslandOakTreeBillboard, grasslandOakTreeBillboardMatrixBatches, ref stats);
        AccumulateTreeBillboardBatchStats(grasslandWillowTreeBillboard, grasslandWillowTreeBillboardMatrixBatches, ref stats);
    }

    private void AccumulateTreeBillboardBatchStats(
        TreeBillboardRenderData renderData,
        List<TreeBillboardInstanceBatch> batches,
        ref RenderGeometryStats stats)
    {
        if (renderData.mesh == null)
            return;

        for (int i = 0; i < batches.Count; i++)
        {
            if (batches[i].matrices == null)
                continue;

            stats.AddMeshInstances(renderData.mesh, batches[i].matrices.Length);
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

    private void AccumulateTreeGameObjectStats(ref RenderGeometryStats stats)
    {
        for (int i = 0; i < treeGameObjects.Count; i++)
        {
            TreeGameObjectInstance treeObject = treeGameObjects[i];
            if (treeObject == null)
                continue;

            GameObject gameObject = treeObject.GameObject;
            if (gameObject == null || !gameObject.activeInHierarchy || treeObject.MeshFilters == null)
                continue;

            for (int meshIndex = 0; meshIndex < treeObject.MeshFilters.Length; meshIndex++)
            {
                MeshFilter meshFilter = treeObject.MeshFilters[meshIndex];
                if (meshFilter != null)
                    stats.AddMesh(meshFilter.sharedMesh);
            }
        }
    }

    public void RebuildTreeGameObjects(
        List<TreeInstanceData> instances,
        Transform chunkRoot)
    {
        ReleaseTreeGameObjectsToPool();

        if (instances == null || chunkRoot == null || root == null)
            return;

        EnsureTreeGameObjectRoot();

        for (int i = 0; i < instances.Count; i++)
        {
            TreeInstanceData instance = instances[i];
            GameObject prefab = GetTreePrefab(instance.variant);

            if (prefab == null)
                continue;

            TreeGameObjectInstance treeObject = GetTreeGameObject(prefab);
            Transform treeTransform = treeObject.GameObject.transform;
            treeTransform.SetParent(treeGameObjectRoot.transform, false);
            treeTransform.localPosition = instance.localPosition;
            treeTransform.localRotation = instance.localRotation;
            treeTransform.localScale = instance.localScale;

            ApplyTreeMaterialOverrides(treeObject, instance);
            treeObject.GameObject.SetActive(true);
            treeGameObjects.Add(treeObject);
        }
    }

    private void ApplyTreeMaterialOverrides(TreeGameObjectInstance treeObject, TreeInstanceData instance)
    {
        if (treeObject == null || treeObject.Renderers == null)
            return;

        for (int i = 0; i < treeObject.Renderers.Length; i++)
        {
            Renderer renderer = treeObject.Renderers[i];
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(treePropertyBlock);
            treePropertyBlock.SetColor(TreeLeafTintPropertyId, instance.leafTint);
            treePropertyBlock.SetColor(TreeBarkTintPropertyId, instance.barkTint);
            renderer.SetPropertyBlock(treePropertyBlock);
        }
    }

    private void EnsureTreeGameObjectRoot()
    {
        if (treeGameObjectRoot != null)
            return;

        treeGameObjectRoot = new GameObject("Tree_GameObjects");
        treeGameObjectRoot.transform.SetParent(root, false);
    }

    private TreeGameObjectInstance GetTreeGameObject(GameObject prefab)
    {
        if (pooledTreeGameObjects.TryGetValue(prefab, out Stack<TreeGameObjectInstance> pool))
        {
            while (pool.Count > 0)
            {
                TreeGameObjectInstance pooled = pool.Pop();
                if (pooled != null && pooled.GameObject != null)
                    return pooled;
            }
        }

        GameObject instance = Object.Instantiate(prefab, treeGameObjectRoot.transform);
        return new TreeGameObjectInstance(
            prefab,
            instance,
            instance.GetComponentsInChildren<Renderer>(true),
            instance.GetComponentsInChildren<MeshFilter>(true));
    }

    public void ReleaseTreeGameObjectsToPool()
    {
        if (treeGameObjects.Count == 0)
            return;

        EnsureTreeGameObjectRoot();

        for (int i = 0; i < treeGameObjects.Count; i++)
        {
            TreeGameObjectInstance treeObject = treeGameObjects[i];
            if (treeObject == null || treeObject.GameObject == null || treeObject.Prefab == null)
                continue;

            treeObject.GameObject.SetActive(false);
            treeObject.GameObject.transform.SetParent(treeGameObjectRoot.transform, false);

            if (!pooledTreeGameObjects.TryGetValue(treeObject.Prefab, out Stack<TreeGameObjectInstance> pool))
            {
                pool = new Stack<TreeGameObjectInstance>();
                pooledTreeGameObjects.Add(treeObject.Prefab, pool);
            }

            pool.Push(treeObject);
        }

        treeGameObjects.Clear();
    }

    public void RebuildBushGameObjects(
        List<BerryBushInstanceData> instances,
        Transform chunkRoot)
    {
        ClearBushGameObjects();

        if (instances == null || chunkRoot == null || root == null)
            return;

        bushGameObjectRoot = new GameObject("BerryBush_GameObjects");
        bushGameObjectRoot.transform.SetParent(root, false);

        for (int i = 0; i < instances.Count; i++)
        {
            BerryBushInstanceData instance = instances[i];
            GameObject prefab = GetBushPrefab(instance.variant);

            if (prefab == null)
                continue;

            GameObject bushObject = Object.Instantiate(prefab, bushGameObjectRoot.transform);
            bushObject.transform.localPosition = instance.localPosition;
            bushObject.transform.localRotation = instance.localRotation;
            bushObject.transform.localScale = instance.localScale;

            BerryBushRuntime berryBushRuntime = bushObject.GetComponent<BerryBushRuntime>();
            if (berryBushRuntime == null)
                berryBushRuntime = bushObject.AddComponent<BerryBushRuntime>();

            berryBushRuntime.Initialize(instance, BerryBushManager.Instance);

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
            GameObject prefab = GetRockPrefab(instance.variant, instance.prefabIndex);

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
        DestroyTreeGameObjectsAndPool();
    }

    private void DestroyTreeGameObjectsAndPool()
    {
        for (int i = 0; i < treeGameObjects.Count; i++)
        {
            TreeGameObjectInstance treeObject = treeGameObjects[i];
            if (treeObject != null && treeObject.GameObject != null)
            {
                Object.Destroy(treeObject.GameObject);
            }
        }

        treeGameObjects.Clear();

        foreach (KeyValuePair<GameObject, Stack<TreeGameObjectInstance>> poolPair in pooledTreeGameObjects)
        {
            Stack<TreeGameObjectInstance> pool = poolPair.Value;
            while (pool.Count > 0)
            {
                TreeGameObjectInstance treeObject = pool.Pop();
                if (treeObject != null && treeObject.GameObject != null)
                    Object.Destroy(treeObject.GameObject);
            }
        }

        pooledTreeGameObjects.Clear();

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
        hasBuiltFlowerRenderData = false;
    }

    public void ClearGrassBatches()
    {
        grassRenderBatches.Clear();
        hasBuiltGrassRenderData = false;
    }

    public void ClearCloverBatches()
    {
        cloverRenderBatches.Clear();
        hasBuiltCloverRenderData = false;
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
        grasslandMapleTreeBillboardMatrixBatches.Clear();
        grasslandBirchAspenTreeBillboardMatrixBatches.Clear();
        grasslandWhitePineTreeBillboardMatrixBatches.Clear();
        grasslandOakTreeBillboardMatrixBatches.Clear();
        grasslandWillowTreeBillboardMatrixBatches.Clear();
    }

    public void DrawGrass()
    {
        if (!isVisible || !HasValidGrassRenderData() || grassRenderBatches.Count == 0)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        for (int i = 0; i < grassRenderBatches.Count; i++)
        {
            DrawInstancedBatch(grassMesh, grassMaterial, grassRenderBatches[i]);
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageGrassDraw,
            stageStart);
    }

    public void DrawBillboards()
    {
        if (!isVisible || !HasValidBillboardRenderData() || billboardRenderBatches.Count == 0)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        for (int i = 0; i < billboardRenderBatches.Count; i++)
        {
            DrawInstancedBatch(billboardMesh, billboardMaterial, billboardRenderBatches[i]);
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageBillboardGrassDraw,
            stageStart);
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

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageFlowerDraw,
            stageStart);
    }

    public void DrawClover()
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

    public void DrawTreeBillboards(bool castShadows, bool receiveShadows)
    {
        if (!isVisible || !HasValidTreeBillboardRenderData())
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();

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
        DrawTreeBillboardBatches(grasslandMapleTreeBillboard, grasslandMapleTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(grasslandBirchAspenTreeBillboard, grasslandBirchAspenTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(grasslandWhitePineTreeBillboard, grasslandWhitePineTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(grasslandOakTreeBillboard, grasslandOakTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(grasslandWillowTreeBillboard, grasslandWillowTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageTreeBillboardDraw,
            stageStart);
    }

    private void DrawTreeBillboardBatches(
        TreeBillboardRenderData renderData,
        List<TreeBillboardInstanceBatch> batches,
        ShadowCastingMode shadowMode,
        bool receiveShadows)
    {
        if (renderData.mesh == null || renderData.material == null)
            return;

        for (int i = 0; i < batches.Count; i++)
        {
            TreeBillboardInstanceBatch batch = batches[i];

            if (batch.matrices == null || batch.leafTints == null)
                continue;

            treeBillboardPropertyBlock.Clear();
            treeBillboardPropertyBlock.SetVectorArray(TreeLeafTintPropertyId, batch.leafTints);

            Graphics.DrawMeshInstanced(
                renderData.mesh,
                0,
                renderData.material,
                batch.matrices,
                batch.matrices.Length,
                treeBillboardPropertyBlock,
                shadowMode,
                receiveShadows
            );
        }
    }

    private bool HasValidBillboardBatch(TreeBillboardRenderData renderData, List<TreeBillboardInstanceBatch> batches)
    {
        return renderData.mesh != null &&
               renderData.material != null &&
               batches.Count > 0;
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

        if (variant == WorldFeatureVariant.GrasslandMapleTree && grasslandMapleTreePrefab != null)
            return grasslandMapleTreePrefab;

        if (variant == WorldFeatureVariant.GrasslandBirchAspenTree && grasslandBirchAspenTreePrefab != null)
            return grasslandBirchAspenTreePrefab;

        if (variant == WorldFeatureVariant.GrasslandWhitePineTree && grasslandWhitePineTreePrefab != null)
            return grasslandWhitePineTreePrefab;

        if (variant == WorldFeatureVariant.GrasslandOakTree && grasslandOakTreePrefab != null)
            return grasslandOakTreePrefab;

        if (variant == WorldFeatureVariant.GrasslandWillowTree && grasslandWillowTreePrefab != null)
            return grasslandWillowTreePrefab;

        if (IsGrasslandTreeVariant(variant) && grasslandFallbackTreePrefab != null)
            return grasslandFallbackTreePrefab;

        if (IsGrasslandTreeVariant(variant))
            return null;

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

    private static bool IsGrasslandTreeVariant(WorldFeatureVariant variant)
    {
        return variant == WorldFeatureVariant.GrasslandMapleTree ||
               variant == WorldFeatureVariant.GrasslandBirchAspenTree ||
               variant == WorldFeatureVariant.GrasslandWhitePineTree ||
               variant == WorldFeatureVariant.GrasslandOakTree ||
               variant == WorldFeatureVariant.GrasslandWillowTree;
    }

    private sealed class TreeGameObjectInstance
    {
        public readonly GameObject Prefab;
        public readonly GameObject GameObject;
        public readonly Renderer[] Renderers;
        public readonly MeshFilter[] MeshFilters;

        public TreeGameObjectInstance(
            GameObject prefab,
            GameObject gameObject,
            Renderer[] renderers,
            MeshFilter[] meshFilters)
        {
            Prefab = prefab;
            GameObject = gameObject;
            Renderers = renderers;
            MeshFilters = meshFilters;
        }
    }
}
