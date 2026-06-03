using UnityEngine;

[System.Serializable]
public class TreeSettings
{
    [Header("Tree Prefabs")]
    public GameObject treeLOD0GameObjectPrefab;

    [Tooltip("Merged single-mesh, single-material prefabs for GPU-instanced tree LODs. Element 0 is GPU LOD1.")]
    public GameObject[] treeGPUInstancedLODPrefabs;

    [Tooltip("Optional merged billboard tree prefab. Used when the tree representation mode becomes GPUInstancedBillboard.")]
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

    [Tooltip("Maximum chunk-ring radius for GPU-instanced mesh trees before switching to billboard trees.")]
    public int gpuInstancedTreeChunkRingRadius = 4;

    [Tooltip("Maximum chunk-ring radius for billboard trees.")]
    public int billboardTreeChunkRingRadius = 8;

    [Header("Tree Rendering")]
    public bool castTreeShadows = true;
    public bool receiveTreeShadows = true;
}