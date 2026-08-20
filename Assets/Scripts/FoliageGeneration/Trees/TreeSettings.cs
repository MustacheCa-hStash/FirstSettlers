using UnityEngine;

[System.Serializable]
public class TreeSettings
{
    [Header("Tree Prefabs")]
    [Tooltip("Generic maple / red maple near tree prefab.")]
    public GameObject mapleTreePrefab;
    public GameObject sugarMapleTreePrefab;
    public GameObject birchAspenTreePrefab;
    public GameObject beechTreePrefab;
    public GameObject spruceTreePrefab;
    public GameObject whitePineTreePrefab;
    public GameObject oakTreePrefab;

    [Tooltip("Fallback near tree prefab used when a species prefab is not assigned.")]
    public GameObject treeLOD0GameObjectPrefab;

    [Header("Grassland Tree Prefabs")]
    public GameObject grasslandMapleTreePrefab;
    public GameObject grasslandBirchAspenTreePrefab;
    public GameObject grasslandWhitePineTreePrefab;
    public GameObject grasslandOakTreePrefab;

    [Tooltip("Fallback grassland tree prefab used when a grassland species prefab is not assigned.")]
    public GameObject grasslandTreeFallbackPrefab;

    [Header("Berry Bush Prefabs")]
    public GameObject blueberryBushPrefab;
    public GameObject raspberryBushPrefab;
    public GameObject strawberryBushPrefab;
    public GameObject blackberryBushPrefab;

    [Tooltip("Fallback bush prefab used when a berry-specific prefab is not assigned.")]
    public GameObject fallbackBushPrefab;

    [Header("Forest Rock Prefabs")]
    [Tooltip("Forest rock and boulder prefabs sampled deterministically from the world seed and rock location.")]
    public GameObject[] forestRockPrefabs;

    [Tooltip("Fallback forest rock prefab used when the forest rock list is empty or a sampled slot is unassigned.")]
    public GameObject forestRockFallbackPrefab;

    [Header("Grassland Rock Prefabs")]
    [Tooltip("Grassland rock and boulder prefabs sampled deterministically from the world seed and rock location.")]
    public GameObject[] grasslandRockPrefabs;

    [Tooltip("Fallback grassland rock prefab used when the grassland rock list is empty or a sampled slot is unassigned.")]
    public GameObject grasslandRockFallbackPrefab;

    [Header("Tree Billboard Prefabs")]
    [Tooltip("Optional merged billboard prefab for generic maple / red maple trees.")]
    public GameObject mapleTreeBillboardPrefab;
    public GameObject sugarMapleTreeBillboardPrefab;
    public GameObject birchAspenTreeBillboardPrefab;
    public GameObject beechTreeBillboardPrefab;
    public GameObject spruceTreeBillboardPrefab;
    public GameObject whitePineTreeBillboardPrefab;
    public GameObject oakTreeBillboardPrefab;

    [Tooltip("Fallback merged billboard tree prefab used when a species billboard is not assigned.")]
    public GameObject treeBillboardPrefab;

    [Header("Grassland Tree Billboard Prefabs")]
    public GameObject grasslandMapleTreeBillboardPrefab;
    public GameObject grasslandBirchAspenTreeBillboardPrefab;
    public GameObject grasslandWhitePineTreeBillboardPrefab;
    public GameObject grasslandOakTreeBillboardPrefab;

    [Tooltip("Fallback merged billboard tree prefab used when a grassland species billboard is not assigned.")]
    public GameObject grasslandTreeBillboardFallbackPrefab;

    [Header("Tree Placement")]
    public float treeCellSize = 12f;

    [Range(0f, 1f)]
    public float treeSpawnChance = 0.3f;

    public float treeMinDistance = 9f;
    public Vector2 treeUniformScaleRange = new Vector2(2f, 2f);

    public float grassExclusionRadius = 1.5f;
    public float bushGrassExclusionRadius = 0.45f;

    public int seedOffset = 12000;

    [Header("Tree Representation Rings")]
    [Tooltip("Chunk-ring radius for real GameObject trees. 1 means a 3x3 block around the player.")]
    public int gameObjectTreeChunkRingRadius = 1;

    [Tooltip("Maximum chunk-ring radius for billboard trees.")]
    public int billboardTreeChunkRingRadius = 8;

    [Header("Berry Bush Rendering")]
    [Tooltip("Chunk-ring radius for berry bush GameObjects. Bushes do not currently use billboards.")]
    public int gameObjectBushChunkRingRadius = 3;

    [Header("Forest Rock Placement")]
    [Tooltip("Maximum planned rock or boulder placements in a forest chunk.")]
    public int maxForestRocksPerChunk = 2;

    public Vector2 forestRockUniformScaleRange = new Vector2(0.75f, 1.45f);
    public Vector2 forestRockPitchRange = new Vector2(-15f, 15f);

    [Header("Grassland Tree Placement")]
    [Tooltip("Maximum planned trees in a grassland chunk. Many grassland chunks will still place fewer or none.")]
    public int maxGrasslandTreesPerChunk = 8;

    [Header("Grassland Rock Placement")]
    [Tooltip("Maximum planned rock or boulder placements in a grassland chunk.")]
    public int maxGrasslandRocksPerChunk = 7;

    public Vector2 grasslandRockUniformScaleRange = new Vector2(0.55f, 1.35f);
    public Vector2 grasslandRockPitchRange = new Vector2(-12f, 12f);

    [Tooltip("Grass exclusion radius around instantiated forest rocks.")]
    public float rockGrassExclusionRadius = 1.25f;

    [Header("Forest Rock Rendering")]
    [Tooltip("Chunk-ring radius for forest rock GameObjects.")]
    public int gameObjectRockChunkRingRadius = 3;

    [Header("Tree Rendering")]
    public bool castTreeShadows = true;
    public bool receiveTreeShadows = true;
}
