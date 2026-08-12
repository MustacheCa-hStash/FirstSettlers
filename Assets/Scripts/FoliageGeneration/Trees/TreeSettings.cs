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

    [Tooltip("Grass exclusion radius around instantiated forest rocks.")]
    public float rockGrassExclusionRadius = 1.25f;

    [Header("Forest Rock Rendering")]
    [Tooltip("Chunk-ring radius for forest rock GameObjects.")]
    public int gameObjectRockChunkRingRadius = 3;

    [Header("Tree Rendering")]
    public bool castTreeShadows = true;
    public bool receiveTreeShadows = true;
}
