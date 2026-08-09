using UnityEngine;

[System.Serializable]
public class TreeSettings
{
    [Header("Tree Prefabs")]
    public GameObject mapleTreePrefab;
    public GameObject spruceTreePrefab;

    [Tooltip("Fallback near tree prefab used when a species prefab is not assigned.")]
    public GameObject treeLOD0GameObjectPrefab;

    [Tooltip("Optional merged billboard prefab for maple trees.")]
    public GameObject mapleTreeBillboardPrefab;

    [Tooltip("Optional merged billboard prefab for spruce trees.")]
    public GameObject spruceTreeBillboardPrefab;

    [Tooltip("Fallback merged billboard tree prefab used when a species billboard is not assigned.")]
    public GameObject treeBillboardPrefab;

    [Header("Tree Placement")]
    public float treeCellSize = 12f;

    [Range(0f, 1f)]
    public float treeSpawnChance = 0.3f;

    public float treeMinDistance = 9f;
    public Vector2 treeUniformScaleRange = new Vector2(2f, 2f);

    public float grassExclusionRadius = 1.5f;

    public int seedOffset = 12000;

    [Header("Tree Representation Rings")]
    [Tooltip("Chunk-ring radius for real GameObject trees. 1 means a 3x3 block around the player.")]
    public int gameObjectTreeChunkRingRadius = 1;

    [Tooltip("Maximum chunk-ring radius for billboard trees.")]
    public int billboardTreeChunkRingRadius = 8;

    [Header("Tree Rendering")]
    public bool castTreeShadows = true;
    public bool receiveTreeShadows = true;
}
