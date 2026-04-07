using UnityEngine;

[System.Serializable]
public class GrassSettings
{
    public GameObject grassPrefab;

    public int cellsPerAxis = 250;
    public int activeRingRadius = 1;

    public int subChunksPerChunk = 10;

    [Range(0f, 1f)] public float densityRadius3 = 1.0f;
    [Range(0f, 1f)] public float densityRadius6 = 0.7f;
    [Range(0f, 1f)] public float densityRadius10 = 0.4f;
    [Range(0f, 1f)] public float densityBeyond10 = 0.3f;

    public Vector2 uniformScaleRange = new Vector2(0.9f, 1.1f);
    public bool randomizeYaw = true;
    public int seedOffset = 5000;
}