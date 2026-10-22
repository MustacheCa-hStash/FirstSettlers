using UnityEngine;

[System.Serializable]
public class TreeSettings
{
    [Header("Nearby Tree Gameplay")]
    public TreeGameplaySettings gameplay = new TreeGameplaySettings();
    [Header("Layered Tree Distribution")]
    [Tooltip("World-space stands, candidate intensity and finite neighbour competition. Defaults to Resources/Foliage/TreeDistribution. Restart Play Mode or regenerate after editing.")]
    public TreeDistributionProfile distributionProfile;
    [Header("Tree Datacards")]
    [Tooltip("Species datacards shared by tree scale, placement and query identity. Forest and grassland variants use the same card. Scale and placement changes require world regeneration.")]
    public WorldObjectDefinition mapleTreeDefinition;
    public WorldObjectDefinition sugarMapleTreeDefinition;
    public WorldObjectDefinition birchAspenTreeDefinition;
    public WorldObjectDefinition beechTreeDefinition;
    public WorldObjectDefinition spruceTreeDefinition;
    public WorldObjectDefinition whitePineTreeDefinition;
    public WorldObjectDefinition oakTreeDefinition;
    public WorldObjectDefinition willowTreeDefinition;
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

    [Header("Tree Scale and Ground Clearance")]
    [InspectorName("Fallback Tree Uniform Scale Range"), Tooltip("Uniform instance scale used only for tree species without a datacard. Configured trees use Tree Scale on their World Object Definition. Regenerate the world or restart Play Mode after changing scale ranges.")]
    public Vector2 treeUniformScaleRange = new Vector2(2f, 2f);

    public float grassExclusionRadius = 1.5f;
    public float bushGrassExclusionRadius = 0.45f;

    [InspectorName("Tree Color Seed Offset"), Tooltip("Varies tree leaf and bark colors. Tree locations come from the world seed and feature plan, not this offset.")]
    public int seedOffset = 12000;

    [Header("Instanced Tree Detail Distance")]
    [InspectorName("Mesh Detail Radius (chunk widths)"), Tooltip("Retained serialized setting: meshes remain fully visible through radius + 1 chunk widths and fade to billboards across the following chunk width. Distance is measured per tree in XZ.")]
    public int gameObjectTreeChunkRingRadius = 1;

    [Range(1, 8), Tooltip("World-aligned chunk widths grouped into each standing-tree draw region. 1 keeps per-chunk draws; larger regions reduce submissions with broader camera/shadow culling bounds. Does not change tree detail or coverage.")]
    public int standingTreeBatchSizeChunks = 4;

    [HideInInspector, Tooltip("Legacy billboard ring start; retained for serialized compatibility.")]
    public int billboardTreeChunkStartRingRadius = 2;

    [Tooltip("Maximum chunk-ring radius for billboard trees.")]
    public int billboardTreeChunkRingRadius = 8;

    [Header("Distant Tree Coverage (restart Play Mode after enabling/disabling)")]
    [Tooltip("Extend instanced tree coverage to distant terrain. Nearby trees always use instanced meshes; billboards handle the outer range.")]
    public bool enableDistantTrees = true;
    [Min(1f), Tooltip("Maximum tree distance in chunk widths, capped by loaded terrain. Clamped upward to surround the circular 3D tree region plus its fade band.")]
    public float distantTreeDistanceChunks = 18f;
    [Min(0.1f), Tooltip("Outer fade width in chunk widths. Cards use alpha-test dithering, not transparency blending.")]
    public float distantTreeFadeWidthChunks = 2f;
    [Min(0f), Tooltip("Distance in chunk widths where stable thinning starts. The near handoff always retains full density.")]
    public float distantTreeThinningStartChunks = 8f;
    [Range(0.05f, 1f), Tooltip("Outer-distance retention target. With density awareness, applies to dense interiors; sparse stands retain more. Not multiplied by a second thinning factor.")]
    public float distantTreeDensity = 0.45f;
    [Tooltip("Use cached local crown coverage to preserve sparse stands and thin dense interiors. Disabling restores uniform distance thinning.")]
    public bool distantTreeDensityAware = true;
    [Min(0.01f), Tooltip("Summed approximate crown area / ground area where thinning begins. Full dense-stand thinning at twice this value. Lower values thin more aggressively. Neighborhoods span three quarter-chunk cells.")]
    public float distantTreeCrowdingThreshold = 0.35f;
    [Range(0f, 1f), Tooltip("Reduce thinning near empty neighboring cells. 0 ignores forest edges; 1 gives strongest edge protection. Isolated groups of up to three trees in the sampled neighborhood are always protected.")]
    public float distantTreeEdgeProtection = 0.8f;
    [Min(0), Tooltip("Preserve this many well-spaced representatives per logical chunk when thinning. Sparse chunks retain all their trees.")]
    public int distantTreeProtectedCount = 2;
    [Min(0.05f), Tooltip("Seconds for 3D/billboard and initial-load dither transitions. Replacement must be ready before outgoing trees fade.")]
    public float distantTreeTransitionSeconds = 0.65f;
    [Range(0f, 1f), Tooltip("How strongly far billboards follow the displayed coarse terrain height. Blends away as detailed terrain arrives.")]
    public float distantTreeTerrainConform = 1f;
    [Min(0f), Tooltip("Vertical seating adjustment speed in world units per second. Zero snaps to the displayed ground immediately.")]
    public float distantTreeHeightBlendSpeed = 20f;
    [Header("Distant Tree Draw Ordering")]
    [Tooltip("Order visible far billboards approximately front-to-back per mesh/material using reusable camera-depth bands. Updates with camera movement; does not change density or near tree LODs.")]
    public bool distantTreeDepthOrdering = true;
    [Range(4, 64), Tooltip("Number of camera-depth bands per mesh/material. CPU batches use stable counting sort; compute uses stable GPU compaction into depth bands. Default 32.")]
    public int distantTreeDepthBands = 32;
    [Header("Distant Tree GPU Compaction")]
    [Tooltip("Use resident tree buffers with GPU culling, thinning, fading and depth ordering for compatible distant billboard materials. Falls back to CPU instanced batches when unsupported or unassigned.")]
    public bool distantTreeGpuCompaction = true;
    [Tooltip("Compute shader used to compact visible distant tree billboard instances before indirect rendering.")]
    public ComputeShader distantTreeCompactShader;
    [Header("Distant Tree Streaming")]
    [Range(1, 4), Tooltip("Maximum concurrent background placement jobs. Each job samples a single logical chunk; no meshes or GameObjects are built on workers.")]
    public int distantTreeWorkerCount = 1;
    [Range(1, 8), Tooltip("Maximum completed manifests installed per frame.")]
    public int distantTreeResultsPerFrame = 1;
    [Min(64), Tooltip("Soft cache limit. Active chunks are retained; least recently used inactive manifests are evicted first.")]
    public int distantTreeCacheChunks = 2048;

    [Header("Tree Streaming Budgets")]
    [Tooltip("Maximum detailed tree placement chunks prepared per frame for the registry and instanced renderer.")]
    public int maxTreeRepresentationRebuildsPerFrame = 1;

    [Tooltip("Approximate per-frame time budget for tree representation rebuilds. Set to 0 to use only the per-frame count cap.")]
    public float treeRepresentationRebuildBudgetMsPerFrame = 0.75f;

    [HideInInspector, Tooltip("Legacy visual GameObject pooling setting; retained for serialized compatibility.")]
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
    [Tooltip("Grassland count guard. With layered distribution, the profile controls population; keep this guard above expected grove counts. The legacy planner also treats this as its maximum.")]
    public int maxGrasslandTreesPerChunk = 24;

    [Header("Grassland Rock Placement")]
    [Tooltip("Maximum planned rock or boulder placements in a grassland chunk.")]
    public int maxGrasslandRocksPerChunk = 7;

    public Vector2 grasslandRockUniformScaleRange = new Vector2(60f, 95f);
    public Vector2 grasslandLargeRockUniformScaleRange = new Vector2(95f, 135f);
    public Vector2 grasslandRockPitchRange = new Vector2(-12f, 12f);

    [Tooltip("Fallback grass exclusion radius for rock prefabs without a Foliage Exclusion Zone profile. Configured rocks use their baked ground-contact hull. Restart Play Mode or regenerate after editing profiles.")]
    public float rockGrassExclusionRadius = 1.25f;

    [Header("Rock Rendering and Pooled Physics")]
    [Tooltip("Circular chunk radius for instanced rock visuals. Independent of physical collider range.")]
    public int gameObjectRockChunkRingRadius = 3;
    public bool enableRockRendering = true;
    public bool enableRockColliders = true;
    [Min(0)] public float rockShadowDistance = 140;

    [Min(1f)] public float rockColliderActivationRadius = 28f;
    [Min(1f)] public float rockColliderReleaseRadius = 36f;
    [Min(1)] public int rockColliderActivationsPerFrame = 8;
    [Min(1)] public int maxActiveRockColliders = 256;
    [Min(0)] public int maxPooledRockColliders = 64;
    [Header("Snow Rocks")]
    public GameObject[] snowRockPrefabs;
    public GameObject[] snowLargeRockPrefabs;
    [Min(0)] public int maxSnowRocksPerChunk = 6;
    public Vector2 snowRockUniformScaleRange = new Vector2(.8f, 1.4f);
    public Vector2 snowLargeRockUniformScaleRange = new Vector2(.85f, 1.3f);

    [Header("Tree Rendering")]
    public bool castTreeShadows = true;
    public bool receiveTreeShadows = true;

    public WorldObjectDefinition GetDefinition(WorldFeatureVariant variant) =>
        TreeSpeciesCatalog.TryGet(variant, out var species) ? species.Definition(this) : null;

    public GameObject GetNearPrefab(WorldFeatureVariant variant) =>
        TreeSpeciesCatalog.TryGet(variant, out var species) ? species.NearPrefab(this) : treeLOD0GameObjectPrefab;

    public GameObject GetRockPrefab(WorldFeatureVariant variant, int prefabIndex)
    {
        GameObject[] prefabs;
        GameObject fallback;
        switch (variant)
        {
            case WorldFeatureVariant.SnowBoulder: prefabs = snowRockPrefabs; fallback = null; break;
            case WorldFeatureVariant.SnowLargeBoulder: prefabs = snowLargeRockPrefabs; fallback = null; break;
            case WorldFeatureVariant.GrasslandLargeBoulder: prefabs = grasslandLargeRockPrefabs; fallback = grasslandLargeRockFallbackPrefab; break;
            case WorldFeatureVariant.GrasslandBoulder: prefabs = grasslandRockPrefabs; fallback = grasslandRockFallbackPrefab; break;
            default: prefabs = forestRockPrefabs; fallback = forestRockFallbackPrefab; break;
        }
        if (prefabs == null || prefabs.Length == 0) return fallback;
        var selected = prefabs[Mathf.Clamp(prefabIndex, 0, prefabs.Length - 1)];
        return selected != null ? selected : fallback;
    }
}
