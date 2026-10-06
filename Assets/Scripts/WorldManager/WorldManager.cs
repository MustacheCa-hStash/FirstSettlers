using UnityEngine;
using Unity.Profiling;

public class WorldManager : MonoBehaviour
{
    private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("FS.Streaming.WorldManager.Update");

    [SerializeField] int worldSeed = 12345;
    [SerializeField] int viewDistance = 4;
    [SerializeField] int colliderDistance = 3;
    [SerializeField] bool enableFarTerrain = true;
    [Tooltip("Normal chunks are retained through this ring. World-aligned far quadtree leaves begin beyond it; 8 aligns cleanly with the 4/8/16/32 patch hierarchy.")]
    [SerializeField] int farTerrainStartRing = 8;
    [Tooltip("Smallest far quadtree leaf size in chunks. Keep this at 4 for the 4/8/16/32 hierarchy.")]
    [SerializeField] int farTerrainMacroTileSize = 4;
    [SerializeField] int farTerrainHeightGridResolution = 9;
    [SerializeField] int farTerrainControlMapResolution = 16;
    [SerializeField] float farTerrainSkirtDepth = 6f;
    [SerializeField] int chunkSize = 128;
    [SerializeField] Transform viewer;
    [SerializeField] Camera viewerCamera;
    [SerializeField] Transform chunkParent;
    [SerializeField] GrassSettings grassSettings;
    [SerializeField] FlowerSettings flowerSettings = new FlowerSettings();
    [SerializeField] ButterflySettings butterflySettings = new ButterflySettings();
    [SerializeField] BeeSettings beeSettings = new BeeSettings();
    [SerializeField] LilyPadSettings lilyPadSettings = new LilyPadSettings();
    [SerializeField] CattailSettings cattailSettings = new CattailSettings();
    [SerializeField] CloverSettings cloverSettings = new CloverSettings();
    [SerializeField] DandelionSettings dandelionSettings = new DandelionSettings();
    [SerializeField] LeafClusterSettings leafClusterSettings = new LeafClusterSettings();
    [SerializeField] FernSettings fernSettings = new FernSettings();
    [SerializeField] TreeSettings treeSettings;
    [Tooltip("Draw each registered standing tree's applied exclusion radius in Play Mode. Enable Gizmos in the Scene or Game view. Uses the generation snapshot, including the habitat-specific range; no regeneration is needed to toggle this display.")]
    [SerializeField] bool showTreeExclusionRadiusGizmos;
    [Tooltip("Live preview of summer-to-autumn colors on season-capable tree leaf shaders. Turn off to restore each material's authored season. Does not regenerate the world.")]
    [SerializeField] bool simulateTreeSeason;
    [Tooltip("0 is summer, 1 is autumn. Updates existing leaf materials, including cached instanced copies, immediately.")]
    [SerializeField, Range(0f, 1f), InspectorName("Summer to Autumn")] float treeSeasonAutumnAmount;
    [Tooltip("Scale of the base landforms. Erosion Wavelength has its own independent terrain-space scale.")]
    [SerializeField] float sampleScale = 10f;
    [Tooltip("Broadens the smooth mountain mask before global erosion. Higher values create more mountainous land; no duplicated or stretched mountain surfaces. Regenerate after changing.")]
    [UnityEngine.Serialization.FormerlySerializedAs("mountainHorizontalScale")]
    [UnityEngine.Serialization.FormerlySerializedAs("mountainCoverage")]
    [SerializeField, Range(1f, 3f), InspectorName("Mountain Coverage")] float mountainWidth = 1f;
    [Tooltip("Render-only gamma for mountain snow coverage. 1 disables the boost; lower values make blended mountain snow brighter without changing other surface transitions. Restart Play Mode after changing.")]
    [SerializeField, Range(0.35f, 1.25f)] float mountainSnowBlendGamma = MountainSnow.DefaultRenderCoverageGamma;
    [SerializeField] float worldScale = 1.0f;
    [SerializeField] int octaves = 3;
    [SerializeField] float persistence = 0.5f;
    [SerializeField] float lacunarity = 2f;
    [SerializeField] WorldErosionSettings erosion = WorldErosionSettings.Default;
    [SerializeField] float meshHeightMultiplier = 10f;
    [SerializeField] Material terrainMaterial;
    [SerializeField] Material waterMaterial;
    [Tooltip("Shared world-space surface Y for rivers and lakes. Applied when the world starts; restart Play Mode after changing. Raising this also expands lakes and moves shorelines.")]
    [SerializeField] float globalWaterY = TerrainWaterSettings.DefaultWaterLevel * 10f;
    [SerializeField, Range(0.25f, 1f)] float waterReflectionResolution = 0.7f;
    [SerializeField, Range(1f, 60f)] float waterReflectionUpdatesPerSecond = 30f;
    [SerializeField, Range(30f, 240f)] float waterReflectionMovingUpdatesPerSecond = 120f;
    [SerializeField, Min(20f)] float waterReflectionDistance = 300f;
    [SerializeField] bool terrainReceiveShadows = true;
    [SerializeField] TerrainHorizonShadowSettings terrainHorizonShadows = new TerrainHorizonShadowSettings();
    [SerializeField] bool logTerrainGenerationProfile = true;
    [SerializeField] float terrainGenerationProfileLogInterval = 5f;
    [SerializeField] bool resetTerrainGenerationProfileAfterLog = true;
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
    private PlanarWaterReflection planarWaterReflection;
    private static readonly int TreeSeasonEnabledId = Shader.PropertyToID("_TreeSeasonSimulationEnabled");
    private static readonly int TreeSeasonAutumnId = Shader.PropertyToID("_TreeSeasonSimulationAutumnAmount");
    private static WorldManager treeSeasonOwner;
    private float appliedTreeAutumnAmount = -1f;
    public Transform Viewer => viewer;
    public TreeRegistry Trees => chunkManager?.Trees;
    public bool ShowTreeExclusionRadiusGizmos => showTreeExclusionRadiusGizmos;
    public TreeGameplayManager TreeGameplay => chunkManager?.TreeGameplay;
    public int TerrainGenerationRevision { get; private set; }
    public bool SimulateTreeSeason => simulateTreeSeason;
    public float TreeSeasonAutumnAmount => treeSeasonAutumnAmount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetTreeSeasonSimulation()
    {
        treeSeasonOwner = null;
        Shader.SetGlobalFloat(TreeSeasonEnabledId, 0f);
        Shader.SetGlobalFloat(TreeSeasonAutumnId, 0f);
    }

    public static void RefreshTreeSeasonSimulation()
    {
        // Normal MonoBehaviours do not run destruction/disable callbacks in every
        // Edit-mode path. The editor polls the owner without executing generation.
        if (object.ReferenceEquals(treeSeasonOwner, null)) return;
        if (treeSeasonOwner == null)
        {
            ResetTreeSeasonSimulation();
            return;
        }
        treeSeasonOwner.ApplyTreeSeasonSimulation();
    }

    public void SetTreeSeasonSimulation(bool enabled, float autumnAmount)
    {
        simulateTreeSeason = enabled;
        treeSeasonAutumnAmount = Mathf.Clamp01(autumnAmount);
        ApplyTreeSeasonSimulation();
    }

    public void ApplyTreeSeasonSimulation()
    {
        if (!simulateTreeSeason || !isActiveAndEnabled)
        {
            ReleaseTreeSeasonSimulation();
            return;
        }
        float amount = Mathf.Clamp01(treeSeasonAutumnAmount);
        if (treeSeasonOwner == this && appliedTreeAutumnAmount == amount) return;
        treeSeasonOwner = this;
        appliedTreeAutumnAmount = amount;
        Shader.SetGlobalFloat(TreeSeasonAutumnId, amount);
        Shader.SetGlobalFloat(TreeSeasonEnabledId, 1f);
    }

    private void ReleaseTreeSeasonSimulation()
    {
        appliedTreeAutumnAmount = -1f;
        if (treeSeasonOwner != this) return;
        treeSeasonOwner = null;
        Shader.SetGlobalFloat(TreeSeasonEnabledId, 0f);
        Shader.SetGlobalFloat(TreeSeasonAutumnId, 0f);
    }

    void OnEnable() { ApplyTreeSeasonSimulation(); }
    void OnDisable() { ReleaseTreeSeasonSimulation(); }

    void Awake()
    {
        TerrainGenerationRevision++;
        TerrainGenerationProfiler.SetEnabled(logTerrainGenerationProfile);
        butterflySettings ??= new ButterflySettings();
        beeSettings ??= new BeeSettings();
        beeSettings.prefab ??= butterflySettings.prefab;

        chunkManager = new ChunkManager(BuildConfiguration());

        if (waterMaterial != null && waterMaterial.shader != null &&
            waterMaterial.shader.name == "FirstSettlers/Murky Planar Water")
        {
            planarWaterReflection = GetComponent<PlanarWaterReflection>();
            if (planarWaterReflection == null)
                planarWaterReflection = gameObject.AddComponent<PlanarWaterReflection>();
            planarWaterReflection.Configure(viewerCamera, globalWaterY, waterReflectionResolution,
                waterReflectionUpdatesPerSecond, waterReflectionMovingUpdatesPerSecond,
                waterReflectionDistance);
            planarWaterReflection.SetWaterVisibilityProvider((planes, mask) =>
                chunkManager != null && chunkManager.HasVisibleWater(planes, mask));
        }
    }

    private WorldConfiguration BuildConfiguration() => new WorldConfiguration
    {
        Generation = new WorldGenerationConfiguration
        {
            Seed = worldSeed,
            ChunkSize = chunkSize,
            SampleScale = sampleScale,
            WorldScale = worldScale,
            Octaves = octaves,
            Persistence = persistence,
            Lacunarity = lacunarity,
            MeshHeightMultiplier = meshHeightMultiplier,
            MountainHorizontalScale = mountainWidth,
            MountainSnowRenderCoverageGamma = mountainSnowBlendGamma,
            Erosion = erosion.Sanitized(),
            Water = new TerrainWaterSettings(globalWaterY, meshHeightMultiplier, worldScale)
        },
        Coverage = new TerrainCoverageConfiguration
        {
            ViewDistance = viewDistance,
            ColliderDistance = colliderDistance,
            EnableFarTerrain = enableFarTerrain,
            FarTerrainStartRing = farTerrainStartRing,
            FarTerrainMacroTileSize = farTerrainMacroTileSize,
            FarTerrainHeightGridResolution = farTerrainHeightGridResolution,
            FarTerrainControlMapResolution = farTerrainControlMapResolution,
            FarTerrainSkirtDepth = farTerrainSkirtDepth
        },
        Workers = new TerrainWorkerLimits
        {
            TerrainData = maxActiveTerrainDataJobs,
            FarTerrain = maxActiveFarTerrainJobs,
            LodMesh = maxActiveMeshJobs,
            Collider = maxActiveColliderJobs
        },
        Publication = new TerrainPublicationBudget
        {
            TerrainDataCount = maxTerrainDataResultsAppliedPerFrame,
            FarTerrainCount = maxFarTerrainResultsAppliedPerFrame,
            LodMeshCount = maxLODMeshResultsAppliedPerFrame,
            ColliderCount = maxColliderResultsAppliedPerFrame,
            TotalMs = completedRequestApplyBudgetMsPerFrame,
            TerrainDataMs = terrainDataApplyBudgetMsPerFrame,
            FarTerrainMs = farTerrainApplyBudgetMsPerFrame,
            LodMeshMs = lodMeshApplyBudgetMsPerFrame,
            ColliderMs = colliderApplyBudgetMsPerFrame
        },
        Content = new TerrainContentBudget
        {
            UrgentVisibleChunkRingRadius = urgentVisibleChunkRingRadius,
            MaxVisibleChunkUpdates = maxVisibleChunkContentUpdatesPerFrame,
            MaxRenderVisibilityChecks = maxRenderVisibilityChecksPerFrame,
            FoliageFrustumPaddingChunks = foliageFrustumPaddingChunks,
            VisibleChunkMs = visibleChunkContentBudgetMsPerFrame,
            MaxFarTileUpdates = maxFarTerrainTileContentUpdatesPerFrame,
            FarTileMs = farTerrainTileContentBudgetMsPerFrame
        },
        Rendering = new WorldRenderingConfiguration
        {
            TerrainMaterial = terrainMaterial,
            WaterMaterial = waterMaterial,
            TerrainReceiveShadows = terrainReceiveShadows,
            HorizonShadows = terrainHorizonShadows
        },
        Scene = new WorldSceneReferences
        {
            Viewer = viewer,
            ViewerCamera = viewerCamera,
            ChunkParent = chunkParent
        },
        Foliage = new WorldFoliageConfiguration
        {
            Grass = grassSettings,
            Flowers = flowerSettings,
            LilyPads = lilyPadSettings,
            Cattails = cattailSettings,
            Clover = cloverSettings,
            Dandelions = dandelionSettings,
            Trees = treeSettings,
            Butterflies = butterflySettings,
            Bees = beeSettings,
            Leaves = leafClusterSettings,
            Ferns = fernSettings
        },
    };

    void OnValidate()
    {
        erosion = erosion.Sanitized();
        treeSeasonAutumnAmount = Mathf.Clamp01(treeSeasonAutumnAmount);
        // Shader globals are applied by Update or the custom Inspector on the
        // main thread, not by this potentially import-thread validation callback.
    }

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
        ApplyTreeSeasonSimulation();
        chunkManager.UpdateActiveChunks();
        TerrainGenerationProfiler.LogSummaryIfDue(
            Time.unscaledTime,
            terrainGenerationProfileLogInterval,
            resetTerrainGenerationProfileAfterLog);
        }
    }

    void OnDestroy()
    {
        ReleaseTreeSeasonSimulation();
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
