using UnityEngine;

/// <summary>Composition inputs. Value groups are copied at construction; authored foliage assets retain their existing live settings.</summary>
public sealed class WorldConfiguration
{
    public WorldGenerationConfiguration Generation = WorldGenerationConfiguration.Default;
    public TerrainCoverageConfiguration Coverage = TerrainCoverageConfiguration.Default;
    public TerrainWorkerLimits Workers = TerrainWorkerLimits.Default;
    public TerrainPublicationBudget Publication = TerrainPublicationBudget.Default;
    public TerrainContentBudget Content = TerrainContentBudget.Default;
    public WorldRenderingConfiguration Rendering;
    public WorldSceneReferences Scene;
    public WorldFoliageConfiguration Foliage = WorldFoliageConfiguration.Default;
}

public struct WorldGenerationConfiguration
{
    public int Seed, ChunkSize, Octaves;
    public float SampleScale, WorldScale, Persistence, Lacunarity, MeshHeightMultiplier;
    public float MountainHorizontalScale, MountainSnowRenderCoverageGamma;
    public WorldErosionSettings Erosion;
    public TerrainWaterSettings Water;
    public static WorldGenerationConfiguration Default => new()
    {
        Seed = 12345, ChunkSize = 128, Octaves = 3, SampleScale = 10, WorldScale = 1,
        Persistence = .5f, Lacunarity = 2, MeshHeightMultiplier = 10, MountainHorizontalScale = 1,
        MountainSnowRenderCoverageGamma = MountainSnow.DefaultRenderCoverageGamma,
        Erosion = WorldErosionSettings.Default, Water = new TerrainWaterSettings(2.4f, 10, 1)
    };
}
public struct TerrainCoverageConfiguration
{
    public int ViewDistance, ColliderDistance, FarTerrainStartRing, FarTerrainMacroTileSize;
    public int FarTerrainHeightGridResolution, FarTerrainControlMapResolution;
    public bool EnableFarTerrain;
    public float FarTerrainSkirtDepth;
    public static TerrainCoverageConfiguration Default => new()
    { ViewDistance = 4, ColliderDistance = 3, EnableFarTerrain = true, FarTerrainStartRing = 8,
      FarTerrainMacroTileSize = 4, FarTerrainHeightGridResolution = 9, FarTerrainControlMapResolution = 16, FarTerrainSkirtDepth = 6 };
}
public struct TerrainWorkerLimits
{
    public int TerrainData, FarTerrain, LodMesh, Collider;
    public static TerrainWorkerLimits Default => new() { TerrainData = 3, FarTerrain = 1, LodMesh = 4, Collider = 2 };
}
public struct TerrainPublicationBudget
{
    public int TerrainDataCount, FarTerrainCount, LodMeshCount, ColliderCount;
    public float TotalMs, TerrainDataMs, FarTerrainMs, LodMeshMs, ColliderMs;
    public TerrainPublicationBudget Sanitized() => new()
    {
        TerrainDataCount = Mathf.Max(1, TerrainDataCount), FarTerrainCount = Mathf.Max(1, FarTerrainCount),
        LodMeshCount = Mathf.Max(1, LodMeshCount), ColliderCount = Mathf.Max(1, ColliderCount),
        TotalMs = Mathf.Max(0, TotalMs), TerrainDataMs = Mathf.Max(0, TerrainDataMs), FarTerrainMs = Mathf.Max(0, FarTerrainMs),
        LodMeshMs = Mathf.Max(0, LodMeshMs), ColliderMs = Mathf.Max(0, ColliderMs)
    };
    public static TerrainPublicationBudget Default => new()
    { TerrainDataCount = 2, FarTerrainCount = 1, LodMeshCount = 12, ColliderCount = 2,
      TotalMs = 3, TerrainDataMs = .75f, FarTerrainMs = .25f, LodMeshMs = .75f, ColliderMs = .25f };
}
public struct TerrainContentBudget
{
    public int UrgentVisibleChunkRingRadius, MaxVisibleChunkUpdates, MaxRenderVisibilityChecks, MaxFarTileUpdates;
    public float FoliageFrustumPaddingChunks, VisibleChunkMs, FarTileMs;
    public static TerrainContentBudget Default => new()
    { UrgentVisibleChunkRingRadius = 1, MaxVisibleChunkUpdates = 32, MaxRenderVisibilityChecks = 160,
      MaxFarTileUpdates = 4, FoliageFrustumPaddingChunks = 1, VisibleChunkMs = 1.5f, FarTileMs = .35f };
}
public struct WorldRenderingConfiguration
{
    public Material TerrainMaterial, WaterMaterial;
    public bool TerrainReceiveShadows;
    public TerrainHorizonShadowSettings HorizonShadows;
}
public struct WorldSceneReferences
{
    public Transform Viewer, ChunkParent;
    public Camera ViewerCamera;
}
public struct WorldFoliageConfiguration
{
    public GrassSettings Grass;
    public FlowerSettings Flowers;
    public LilyPadSettings LilyPads;
    public CattailSettings Cattails;
    public CloverSettings Clover;
    public DandelionSettings Dandelions;
    public TreeSettings Trees;
    public ButterflySettings Butterflies;
    public BeeSettings Bees;
    public LeafClusterSettings Leaves;
    public FernSettings Ferns;
    public static WorldFoliageConfiguration Default => new()
    { Grass = new GrassSettings(), Flowers = new FlowerSettings(), LilyPads = new LilyPadSettings(),
      Cattails = new CattailSettings(), Clover = new CloverSettings(), Dandelions = new DandelionSettings(),
      Trees = new TreeSettings(), Butterflies = new ButterflySettings(), Bees = new BeeSettings(),
      Leaves = new LeafClusterSettings(), Ferns = new FernSettings() };
}
