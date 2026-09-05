using UnityEngine;

[System.Serializable]
public class GrassSettings
{
    public GameObject grassPrefab;

    public int cellsPerAxis = 125;
    [Range(0f, 1f)] public float cellJitter = 1.0f;
    public int activeRingRadius = 1;
    [Min(0)] public int nearGrassPrecomputeChunkPadding = 0;
    public int subChunksPerChunk = 10;
    public int activeSubChunkRadius = 0;
    public int maxSubChunkGenerationsPerFrame = 8;
    public float subChunkGenerationBudgetMsPerFrame = 1.0f;

    [Header("Foliage Work Budgets")]
    public float foregroundFoliageWorkBudgetMsPerFrame = 1.35f;
    public int maxFoliageManagementChunksPerFrame = 24;
    public float foliageManagementBudgetMsPerFrame = 0.75f;
    public int maxQueuedFoliageManagementWork = 2048;
    public int maxGroundFoliageGenerationsPerFrame = 2;
    public float groundFoliageGenerationBudgetMsPerFrame = 0.75f;
    public int maxRenderBatchRebuildsPerFrame = 1;
    public float renderBatchRebuildBudgetMsPerFrame = 0.35f;
    public int maxQueuedGrassSubChunkWork = 128;
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

    public int billboardRingRadius = 2;
    public int billboardCellsPerAxis = 50;
    [Range(0f, 1f)] public float billboardSpawnChance = 0.4f;

    [HideInInspector] public Vector2 billboardUniformScaleRange = new Vector2(1.5f, 2.5f);
    [HideInInspector] public bool randomizeBillboardYaw = true;
    [HideInInspector] public int billboardSeedOffset = 9000;

    [Header("Billboard Render Fade")]
    public bool enableBillboardRenderFade = true;
    public float billboardRenderFadeDuration = 0.65f;
    public float billboardFadeDitherPixelSize = 1f;
}
