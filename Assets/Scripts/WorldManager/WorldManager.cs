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
    [SerializeField] DandelionSettings dandelionSettings = new DandelionSettings();
    [SerializeField] TreeSettings treeSettings;
    [Header("Broad Terrain")]
    [Tooltip("Scale of the base landforms. Erosion Wavelength has its own independent terrain-space scale.")]
    [SerializeField] float sampleScale = 10f;
    [Tooltip("Broadens the smooth mountain mask before global erosion. Higher values create more mountainous land; no duplicated or stretched mountain surfaces. Regenerate after changing.")]
    [UnityEngine.Serialization.FormerlySerializedAs("mountainHorizontalScale")]
    [UnityEngine.Serialization.FormerlySerializedAs("mountainCoverage")]
    [SerializeField, Range(1f, 3f), InspectorName("Mountain Coverage")] float mountainWidth = 1f;
    [Tooltip("Render-only gamma for mountain snow coverage. 1 disables the boost; lower values make blended mountain snow brighter without changing other surface transitions. Restart Play Mode after changing.")]
    [SerializeField, Range(0.35f, 1.25f)] float mountainSnowBlendGamma = MountainSnow.DefaultRenderCoverageGamma;
    [SerializeField] float worldScale = 1.0f;
    [Header("Climate Noise (not terrain erosion)")]
    [SerializeField] int octaves = 3;
    [SerializeField] float persistence = 0.5f;
    [SerializeField] float lacunarity = 2f;
    [Header("Heightfield overhaul")]
    [SerializeField] WorldErosionSettings erosion = WorldErosionSettings.Default;
    [SerializeField] float meshHeightMultiplier = 10f;
    [SerializeField] Material terrainMaterial;
    [SerializeField] Material waterMaterial;
    [Header("Water")]
    [Tooltip("Shared world-space surface Y for rivers and lakes. Applied when the world starts; restart Play Mode after changing. Raising this also expands lakes and moves shorelines.")]
    [SerializeField] float globalWaterY = TerrainWaterSettings.DefaultWaterLevel * 10f;
    [Header("Terrain Lighting")]
    [SerializeField] bool terrainReceiveShadows = true;
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
    [Tooltip("Extra chunk-width margin used only for foliage render visibility, so off-camera trees can still cast shadows into view.")]
    [SerializeField] float foliageFrustumPaddingChunks = 1f;
    [SerializeField] float visibleChunkContentBudgetMsPerFrame = 1.5f;
    [SerializeField] int maxFarTerrainTileContentUpdatesPerFrame = 4;
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
            chunkParent, foliageParent, grassSettings, flowerSettings, cloverSettings, dandelionSettings, treeSettings, sampleScale, worldScale, octaves, persistence,
            lacunarity, meshHeightMultiplier, terrainMaterial, waterMaterial,
            terrainReceiveShadows, new TerrainWaterSettings(globalWaterY, meshHeightMultiplier, worldScale),
            maxActiveTerrainDataJobs, maxActiveFarTerrainJobs, maxActiveMeshJobs,
            maxActiveColliderJobs, maxTerrainDataResultsAppliedPerFrame,
            maxFarTerrainResultsAppliedPerFrame, maxLODMeshResultsAppliedPerFrame,
            maxColliderResultsAppliedPerFrame, urgentVisibleChunkRingRadius,
            maxVisibleChunkContentUpdatesPerFrame, maxRenderVisibilityChecksPerFrame,
            foliageFrustumPaddingChunks,
            visibleChunkContentBudgetMsPerFrame, maxFarTerrainTileContentUpdatesPerFrame,
            farTerrainTileContentBudgetMsPerFrame,
            completedRequestApplyBudgetMsPerFrame,
            terrainDataApplyBudgetMsPerFrame, farTerrainApplyBudgetMsPerFrame,
            lodMeshApplyBudgetMsPerFrame, colliderApplyBudgetMsPerFrame, mountainWidth, mountainSnowBlendGamma, erosion.Sanitized());
    }

    void OnValidate() { erosion = erosion.Sanitized(); }

    [ContextMenu("Regenerate Terrain")]
    public void RegenerateTerrain()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        chunkManager?.Dispose();
        chunkManager = null;
        Awake();
        chunkManager.UpdateActiveChunks();
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
