using UnityEngine;

public class WorldManager : MonoBehaviour
{
    [SerializeField] int worldSeed = 12345;
    [SerializeField] int viewDistance = 4;
    [SerializeField] int chunkSize = 128;
    [SerializeField] Transform viewer;
    [SerializeField] Transform chunkParent;

    private ChunkManager chunkManager;

    void Awake()
    {
        chunkManager = new ChunkManager(viewDistance, chunkSize, viewer, chunkParent);
    }

    void Start()
    {
        chunkManager.UpdateVisibleChunks();
    }

    void Update()
    {
        chunkManager.UpdateVisibleChunks();
    }
}
