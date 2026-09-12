using UnityEngine;

[System.Serializable]
public class GrassSettings
{
    public GameObject grassPrefab;

    [Header("Grass GPU Rendering")]
    [Tooltip("Render the existing rank-selected grass clumps with resident buffers and compute visibility culling. Keeps all generation/density rules; falls back to CPU instancing when unavailable.")]
    public bool gpuIndirectRendering = true;
    public ComputeShader grassCompactShader;

    [Header("Subchunk Streaming")]
    [Range(0f, 1f), Tooltip("Far grass coverage selected from the same candidates as detailed grass. Independent of the retired billboard cell grid.")]
    public float billboardCoverage = 0.2f;
    [Min(0.05f)] public float transitionWidthChunks = 0.35f;
    [Min(0.01f)] public float representationTransitionSeconds = 0.4f;
    [Range(0f, 0.2f)] public float representationHysteresis = 0.03f;
    [Min(1)] public int maxConcurrentGrassJobs = 8;
    [Min(0.05f)] public float grassCompletionBudgetMs = 0.35f;
    [Min(1)] public int maxGrassUploadsPerFrame = 8;
    [Min(0.05f)] public float grassUploadBudgetMs = 0.35f;
    public int cellsPerAxis = 125;
    [Range(0f, 1f)] public float cellJitter = 1.0f;
    [Tooltip("Distance from the actual player in chunk widths where detailed grass transitions to billboards; diagonal subchunks are included.")]
    public int activeRingRadius = 1;
    [Min(0)] public int nearGrassPrecomputeChunkPadding = 0;
    public int subChunksPerChunk = 10;
    [Tooltip("Circular generation radius in subchunks. Zero derives the radius from chunk coverage and precompute padding.")]
    [HideInInspector] public int activeSubChunkRadius = 0;
    public int maxSubChunkGenerationsPerFrame = 8;
    public float subChunkGenerationBudgetMsPerFrame = 1.0f;

    [Header("Foliage Work Budgets")]
    public float foregroundFoliageWorkBudgetMsPerFrame = 1.35f;
    public int maxFoliageManagementChunksPerFrame = 24;
    public float foliageManagementBudgetMsPerFrame = 0.75f;
    public int maxQueuedFoliageManagementWork = 2048;
    [Tooltip("Maximum ground discovery requests started per frame. One job is kept in flight; unfinished requests continue across frames.")]
    public int maxGroundFoliageGenerationsPerFrame = 2;
    [Tooltip("Main-thread ground discovery setup/result collection budget. Results are collected in 256-candidate slices; one step always progresses. Does not limit worker execution time or render-batch building.")]
    public float groundFoliageGenerationBudgetMsPerFrame = 0.75f;
    public int maxRenderBatchRebuildsPerFrame = 1;
    public float renderBatchRebuildBudgetMsPerFrame = 0.35f;
    [HideInInspector] public int maxQueuedGrassSubChunkWork = 128;
    public int maxQueuedGroundFoliageGenerationWork = 48;
    public int maxQueuedRenderBatchWork = 96;

    [Range(0f, 1f)] public float densityRadius3 = 1.0f;
    [Range(0f, 1f)] public float densityRadius6 = 0.7f;
    [Range(0f, 1f)] public float densityRadius10 = 0.4f;
    [Range(0f, 1f)] public float densityBeyond10 = 0.3f;

    public Vector2 uniformScaleRange = new Vector2(0.9f, 1.1f);
    public bool randomizeYaw = true;
    public bool receiveGrassShadows = false;
    public int seedOffset = 5000;

    [Header("Forest Grass Instance Tint")]
    public string grassInstanceDataPropertyName = "_GrassInstanceData";
    public Color forestDarkGrassColor = new Color(0.10f, 0.28f, 0.09f, 1f);
    public Color forestMidGrassColor = new Color(0.16f, 0.40f, 0.13f, 1f);
    public Color forestLightGrassColor = new Color(0.25f, 0.52f, 0.19f, 1f);

    public GameObject billboardGrassPrefab;

    [Tooltip("Outer grass distance in chunk widths from the actual player. Density tapers at the boundary.")]
    public int billboardRingRadius = 2;
    [HideInInspector] public int billboardCellsPerAxis = 50;
    [HideInInspector] public float billboardSpawnChance = 0.4f;

    [HideInInspector] public Vector2 billboardUniformScaleRange = new Vector2(1.5f, 2.5f);
    [HideInInspector] public bool randomizeBillboardYaw = true;
    [HideInInspector] public int billboardSeedOffset = 9000;

    [Header("Billboard Render Fade")]
    public bool enableBillboardRenderFade = true;
    public float billboardRenderFadeDuration = 0.65f;
    public float billboardFadeDitherPixelSize = 1f;
}
