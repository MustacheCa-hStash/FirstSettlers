using UnityEngine;

[System.Serializable]
public class GrassSettings
{
    public GameObject grassPrefab;

    public int cellsPerAxis = 125;
    public int activeRingRadius = 1;
    public int subChunksPerChunk = 10;

    [Range(0f, 1f)] public float densityRadius3 = 1.0f;
    [Range(0f, 1f)] public float densityRadius6 = 0.7f;
    [Range(0f, 1f)] public float densityRadius10 = 0.4f;
    [Range(0f, 1f)] public float densityBeyond10 = 0.3f;

    public Vector2 uniformScaleRange = new Vector2(0.9f, 1.1f);
    public bool randomizeYaw = true;
    public int seedOffset = 5000;

    public GameObject billboardGrassPrefab;

    public int billboardRingRadius = 2;
    public int billboardCellsPerAxis = 50;
    [Range(0f, 1f)] public float billboardSpawnChance = 0.4f;

    public Vector2 billboardUniformScaleRange = new Vector2(1.5f, 2.5f);
    public bool randomizeBillboardYaw = true;
    public int billboardSeedOffset = 9000;
}