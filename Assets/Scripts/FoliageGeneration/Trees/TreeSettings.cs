using UnityEngine;

[System.Serializable]
public class TreeSettings
{
    public GameObject treeCubePrefab;

    public float treeCellSize = 12f;

    [Range(0f, 1f)]
    public float treeSpawnChance = 0.3f;

    public float treeMinDistance = 9f;
    public Vector2 treeUniformScaleRange = new Vector2(2f, 2f);

    public float grassExclusionRadius = 1.5f;

    public int seedOffset = 12000;
}

