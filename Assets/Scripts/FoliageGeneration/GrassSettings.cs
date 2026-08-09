using UnityEngine;

[System.Serializable]
public class GrassSettings
{
    public GameObject grassPrefab;

    public int cellsPerAxis = 125;
    [Range(0f, 1f)] public float cellJitter = 1.0f;
    public int activeRingRadius = 1;
    public int subChunksPerChunk = 10;

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
    public Color forestDarkGrassColor = new Color(0.045f, 0.16f, 0.05f, 1f);
    public Color forestMidGrassColor = new Color(0.085f, 0.25f, 0.085f, 1f);
    public Color forestLightGrassColor = new Color(0.17f, 0.38f, 0.14f, 1f);

    public GameObject billboardGrassPrefab;

    public int billboardRingRadius = 2;
    public int billboardCellsPerAxis = 50;
    [Range(0f, 1f)] public float billboardSpawnChance = 0.4f;

    public Vector2 billboardUniformScaleRange = new Vector2(1.5f, 2.5f);
    public bool randomizeBillboardYaw = true;
    public int billboardSeedOffset = 9000;
}
