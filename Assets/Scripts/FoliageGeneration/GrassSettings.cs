using UnityEngine;

[System.Serializable]
public class GrassSettings
{
    public GameObject grassPrefab;

    public int cellsPerAxis = 125;
    [Range(0f, 1f)] public float cellJitter = 1.0f;
    public int activeRingRadius = 1;
    public int subChunksPerChunk = 10;
    public int activeSubChunkRadius = 0;
    public int maxSubChunkGenerationsPerFrame = 8;
    public float subChunkGenerationBudgetMsPerFrame = 1.0f;

    [Header("Foliage Work Budgets")]
    public int maxRenderBatchRebuildsPerFrame = 1;
    public float renderBatchRebuildBudgetMsPerFrame = 0.35f;

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
    public Color forestDarkGrassColor = new Color(0.20f, 0.48f, 0.18f, 1f);
    public Color forestMidGrassColor = new Color(0.29f, 0.62f, 0.24f, 1f);
    public Color forestLightGrassColor = new Color(0.44f, 0.78f, 0.30f, 1f);

    public GameObject billboardGrassPrefab;

    public int billboardRingRadius = 2;
    public int billboardCellsPerAxis = 50;
    [Range(0f, 1f)] public float billboardSpawnChance = 0.4f;

    public Vector2 billboardUniformScaleRange = new Vector2(1.5f, 2.5f);
    public bool randomizeBillboardYaw = true;
    public int billboardSeedOffset = 9000;
}
