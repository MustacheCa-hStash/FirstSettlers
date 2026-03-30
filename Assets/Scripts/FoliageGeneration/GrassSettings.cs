using UnityEngine;

[System.Serializable]
public class GrassSettings
{
    public GameObject grassPrefab;
    public int cellsPerAxis = 8;
    public int activeRingRadius = 1;
    public Vector2 uniformScaleRange = new Vector2(0.9f, 1.1f);
    public bool randomizeYaw = true;
    public int seedOffset = 5000;
}