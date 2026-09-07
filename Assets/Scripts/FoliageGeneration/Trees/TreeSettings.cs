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
    public GameObject grasslandWillowTreePrefab;

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

    [Tooltip("Larger grassland boulder prefabs used as occasional anchors for small rock clusters.")]
    public GameObject[] grasslandLargeRockPrefabs;

    [Tooltip("Fallback larger grassland boulder prefab used when the larger boulder list is empty or a sampled slot is unassigned.")]
    public GameObject grasslandLargeRockFallbackPrefab;

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
    public GameObject grasslandWillowTreeBillboardPrefab;

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
    [Tooltip("Circular chunk radius for real GameObject trees. Radius 1 includes the player chunk and its four cardinal neighbors.")]
    public int gameObjectTreeChunkRingRadius = 1;

    [Tooltip("First chunk ring where billboard trees are allowed. Values inside the GameObject tree ring are clamped to the next ring.")]
    public int billboardTreeChunkStartRingRadius = 2;

    [Tooltip("Maximum chunk-ring radius for billboard trees.")]
    public int billboardTreeChunkRingRadius = 8;

    [Header("Distant Tree Coverage (restart Play Mode after enabling/disabling)")]
    [Tooltip("Use cached, deterministic tree billboards on near and far terrain. Replaces the legacy billboard rings; keeps existing 3D tree ring.")]
    public bool enableDistantTrees = true;
    [Min(1f), Tooltip("Maximum tree distance in chunk widths, capped by loaded terrain. Clamped upward to surround the circular 3D tree region plus its fade band.")]
    public float distantTreeDistanceChunks = 18f;
    [Min(0.1f), Tooltip("Outer fade width in chunk widths. Cards use alpha-test dithering, not transparency blending.")]
    public float distantTreeFadeWidthChunks = 2f;
    [Min(0f), Tooltip("Distance in chunk widths where stable thinning starts. The near handoff always retains full density.")]
    public float distantTreeThinningStartChunks = 8f;
    [Range(0.05f, 1f), Tooltip("Fraction of non-protected trees retained at the outer distance. Selection is stable across camera movement.")]
    public float distantTreeDensity = 0.45f;
    [Min(0), Tooltip("Preserve this many well-spaced representatives per logical chunk when thinning. Sparse chunks retain all their trees.")]
    public int distantTreeProtectedCount = 2;
    [Min(0.05f), Tooltip("Seconds for 3D/billboard and initial-load dither transitions. Replacement must be ready before outgoing trees fade.")]
    public float distantTreeTransitionSeconds = 0.65f;
    [Range(0f, 1f), Tooltip("How strongly far billboards follow the displayed coarse terrain height. Blends away as detailed terrain arrives.")]
    public float distantTreeTerrainConform = 1f;
    [Min(0f), Tooltip("Vertical seating adjustment speed in world units per second. Zero snaps to the displayed ground immediately.")]
    public float distantTreeHeightBlendSpeed = 20f;
    [Header("Distant Tree Streaming")]
    [Range(1, 4), Tooltip("Maximum concurrent background placement jobs. Each job samples a single logical chunk; no meshes or GameObjects are built on workers.")]
    public int distantTreeWorkerCount = 1;
    [Range(1, 8), Tooltip("Maximum completed manifests installed per frame.")]
    public int distantTreeResultsPerFrame = 1;
    [Min(64), Tooltip("Soft cache limit. Active chunks are retained; least recently used inactive manifests are evicted first.")]
    public int distantTreeCacheChunks = 2048;

    [Header("Tree Streaming Budgets")]
    [Tooltip("Maximum tree representation rebuilds applied per frame. This includes near GameObject trees and far tree billboard batches.")]
    public int maxTreeRepresentationRebuildsPerFrame = 1;

    [Tooltip("Approximate per-frame time budget for tree representation rebuilds. Set to 0 to use only the per-frame count cap.")]
    public float treeRepresentationRebuildBudgetMsPerFrame = 0.75f;

    [Tooltip("Extra rings beyond the GameObject tree ring where inactive tree GameObjects are retained for reuse instead of destroyed.")]
    public int treeGameObjectWarmRetainExtraRings = 1;

    [Header("Berry Bush Rendering")]
    [Tooltip("Circular chunk radius for berry bush GameObjects. Bushes do not currently use billboards.")]
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

    public Vector2 grasslandRockUniformScaleRange = new Vector2(60f, 95f);
    public Vector2 grasslandLargeRockUniformScaleRange = new Vector2(95f, 135f);
    public Vector2 grasslandRockPitchRange = new Vector2(-12f, 12f);

    [Tooltip("Grass exclusion radius around instantiated forest rocks.")]
    public float rockGrassExclusionRadius = 1.25f;

    [Header("Forest Rock Rendering")]
    [Tooltip("Circular chunk radius for forest rock GameObjects.")]
    public int gameObjectRockChunkRingRadius = 3;

    [Header("Tree Rendering")]
    public bool castTreeShadows = true;
    public bool receiveTreeShadows = true;
}
