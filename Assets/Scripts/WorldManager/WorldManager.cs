using UnityEngine;

public class WorldManager : MonoBehaviour
{
    [SerializeField] int worldSeed = 12345;
    [SerializeField] int viewDistance = 4;
    [SerializeField] int chunkSize = 128;
    [SerializeField] Transform viewer;
    [SerializeField] Transform chunkParent;
    [SerializeField] float sampleScale = 10f;
    [SerializeField] int octaves = 3;
    [SerializeField] float persistence = 0.5f;
    [SerializeField] float lacunarity = 2f;
    [SerializeField] float meshHeightMultiplier = 10f;
    [SerializeField] Material baseMaterial;

    private ChunkManager chunkManager;

    void Awake()
    {
        chunkManager = new ChunkManager(viewDistance, chunkSize, worldSeed, viewer, chunkParent, sampleScale,
        octaves, persistence, lacunarity, meshHeightMultiplier, baseMaterial);
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
