using UnityEngine;

[System.Serializable]
public class TreeSettings
{
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
