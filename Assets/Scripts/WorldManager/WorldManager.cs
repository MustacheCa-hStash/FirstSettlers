using UnityEngine;

public class WorldManager : MonoBehaviour
{
    [SerializeField] int worldSeed = 12345;
    [SerializeField] int viewDistance = 4;
    [SerializeField] int colliderDistance = 3;
    [SerializeField] bool enableFarTerrain = true;
    [SerializeField] int farTerrainStartRing = 8;
    [SerializeField] int farTerrainMacroTileSize = 4;
    [SerializeField] int farTerrainHeightGridResolution = 9;
    [SerializeField] int farTerrainControlMapResolution = 16;
    [SerializeField] float farTerrainSkirtDepth = 6f;
    [SerializeField] int chunkSize = 128;
    [SerializeField] Transform viewer;
    [SerializeField] Camera viewerCamera;
    [SerializeField] Transform chunkParent;
    [SerializeField] Transform foliageParent;
    [SerializeField] GrassSettings grassSettings;
    [SerializeField] FlowerSettings flowerSettings = new FlowerSettings();
    [SerializeField] TreeSettings treeSettings;
    [SerializeField] float sampleScale = 10f;
    [SerializeField] float worldScale = 1.0f;
    [SerializeField] int octaves = 3;
    [SerializeField] float persistence = 0.5f;
    [SerializeField] float lacunarity = 2f;
    [SerializeField] float erosionStrength = 1.0f;
    [SerializeField] float meshHeightMultiplier = 10f;
    [SerializeField] Material terrainMaterial;
    [SerializeField] Material waterMaterial;
    [Header("Terrain Generation Profiling")]
    [SerializeField] bool logTerrainGenerationProfile = true;
    [SerializeField] float terrainGenerationProfileLogInterval = 5f;
    [SerializeField] bool resetTerrainGenerationProfileAfterLog = true;
    [Header("Terrain Streaming Budgets")]
    [SerializeField] int maxActiveTerrainDataJobs = 3;
    [SerializeField] int maxActiveFarTerrainJobs = 3;
    [SerializeField] int maxActiveMeshJobs = 4;
    [SerializeField] int maxActiveColliderJobs = 2;
    [SerializeField] int maxTerrainDataResultsAppliedPerFrame = 2;
    [SerializeField] int maxFarTerrainResultsAppliedPerFrame = 96;
    [SerializeField] int maxLODMeshResultsAppliedPerFrame = 12;
    [SerializeField] int maxColliderResultsAppliedPerFrame = 2;
    [SerializeField] float completedRequestApplyBudgetMsPerFrame = 3f;
    [SerializeField] float terrainDataApplyBudgetMsPerFrame = 0.75f;
    [SerializeField] float farTerrainApplyBudgetMsPerFrame = 1.5f;
    [SerializeField] float lodMeshApplyBudgetMsPerFrame = 0.75f;
    [SerializeField] float colliderApplyBudgetMsPerFrame = 0.25f;

    private ChunkManager chunkManager;
    public Transform Viewer => viewer;

    void Awake()
    {
        TerrainGenerationProfiler.SetEnabled(logTerrainGenerationProfile);

        chunkManager = new ChunkManager(viewDistance, colliderDistance, enableFarTerrain, farTerrainStartRing,
            farTerrainMacroTileSize, farTerrainHeightGridResolution, farTerrainControlMapResolution, farTerrainSkirtDepth,
            chunkSize, worldSeed, viewer, viewerCamera,
            chunkParent, foliageParent, grassSettings, flowerSettings, treeSettings, sampleScale, worldScale, octaves, persistence, 
            lacunarity, erosionStrength, meshHeightMultiplier, terrainMaterial, waterMaterial,
            maxActiveTerrainDataJobs, maxActiveFarTerrainJobs, maxActiveMeshJobs,
            maxActiveColliderJobs, maxTerrainDataResultsAppliedPerFrame,
            maxFarTerrainResultsAppliedPerFrame, maxLODMeshResultsAppliedPerFrame,
            maxColliderResultsAppliedPerFrame, completedRequestApplyBudgetMsPerFrame,
            terrainDataApplyBudgetMsPerFrame, farTerrainApplyBudgetMsPerFrame,
            lodMeshApplyBudgetMsPerFrame, colliderApplyBudgetMsPerFrame);
    }

    void Start()
    {
        chunkManager.UpdateActiveChunks();
    }

    void Update()
    {
        chunkManager.UpdateActiveChunks();
        TerrainGenerationProfiler.LogSummaryIfDue(
            Time.unscaledTime,
            terrainGenerationProfileLogInterval,
            resetTerrainGenerationProfileAfterLog);
    }

    public WorldDebugInfo GetDebugInfoAtWorldPosition(Vector3 worldPosition)
    {
        return chunkManager.GetDebugInfoAtWorldPosition(worldPosition);
    }

    public WorldRenderStatsDebugInfo GetVisibleRenderStatsDebugInfo()
    {
        return chunkManager.GetVisibleRenderStatsDebugInfo();
    }
}
