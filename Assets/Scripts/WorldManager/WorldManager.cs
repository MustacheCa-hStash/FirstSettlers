using UnityEngine;
using Unity.Profiling;

public class WorldManager : MonoBehaviour
{
    private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("FS.Streaming.WorldManager.Update");

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
    [SerializeField] CloverSettings cloverSettings = new CloverSettings();
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
    [SerializeField] int maxActiveFarTerrainJobs = 1;
    [SerializeField] int maxActiveMeshJobs = 4;
    [SerializeField] int maxActiveColliderJobs = 2;
    [SerializeField] int maxTerrainDataResultsAppliedPerFrame = 2;
    [SerializeField] int maxFarTerrainResultsAppliedPerFrame = 1;
    [SerializeField] int maxLODMeshResultsAppliedPerFrame = 12;
    [SerializeField] int maxColliderResultsAppliedPerFrame = 2;
    [SerializeField] int urgentVisibleChunkRingRadius = 1;
    [SerializeField] int maxVisibleChunkContentUpdatesPerFrame = 32;
    [SerializeField] int maxRenderVisibilityChecksPerFrame = 160;
    [SerializeField] float visibleChunkContentBudgetMsPerFrame = 1.5f;
    [SerializeField] int maxFarTerrainTileContentUpdatesPerFrame = 4;
    [SerializeField] int maxFarTerrainTileVisibilityChecksPerFrame = 24;
    [SerializeField] float farTerrainTileContentBudgetMsPerFrame = 0.35f;
    [SerializeField] float completedRequestApplyBudgetMsPerFrame = 3f;
    [SerializeField] float terrainDataApplyBudgetMsPerFrame = 0.75f;
    [SerializeField] float farTerrainApplyBudgetMsPerFrame = 0.25f;
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
            chunkParent, foliageParent, grassSettings, flowerSettings, cloverSettings, treeSettings, sampleScale, worldScale, octaves, persistence,
            lacunarity, erosionStrength, meshHeightMultiplier, terrainMaterial, waterMaterial,
            maxActiveTerrainDataJobs, maxActiveFarTerrainJobs, maxActiveMeshJobs,
            maxActiveColliderJobs, maxTerrainDataResultsAppliedPerFrame,
            maxFarTerrainResultsAppliedPerFrame, maxLODMeshResultsAppliedPerFrame,
            maxColliderResultsAppliedPerFrame, urgentVisibleChunkRingRadius,
            maxVisibleChunkContentUpdatesPerFrame, maxRenderVisibilityChecksPerFrame,
            visibleChunkContentBudgetMsPerFrame, maxFarTerrainTileContentUpdatesPerFrame,
            maxFarTerrainTileVisibilityChecksPerFrame, farTerrainTileContentBudgetMsPerFrame,
            completedRequestApplyBudgetMsPerFrame,
            terrainDataApplyBudgetMsPerFrame, farTerrainApplyBudgetMsPerFrame,
            lodMeshApplyBudgetMsPerFrame, colliderApplyBudgetMsPerFrame);
    }

    void Start()
    {
        chunkManager.UpdateActiveChunks();
    }

    void Update()
    {
        using (UpdateMarker.Auto())
        {
        chunkManager.UpdateActiveChunks();
        TerrainGenerationProfiler.LogSummaryIfDue(
            Time.unscaledTime,
            terrainGenerationProfileLogInterval,
            resetTerrainGenerationProfileAfterLog);
        }
    }

    void OnDestroy()
    {
        chunkManager?.Dispose();
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
