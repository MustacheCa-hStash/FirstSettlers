public class TerrainDataRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int RequestVersion { get; }
    public float[,] HeightMap { get; }
    public float[,] GradientXMap { get; }
    public float[,] GradientZMap { get; }
    public float[,] SlopeMap { get; }
    public float[,] MoistureMap { get; }
    public float[,] TemperatureMap { get; }
    public BiomeType[,] BiomeMap { get; }
    public SurfaceType[,] SurfaceTypeMap { get; }
    public WaterState[,] WaterStateMap { get; }
    public GroundCoverType[,] GroundCoverMap { get; }
    public WorldFeaturePlan WorldFeaturePlan { get; }
    public float[,] RiverMaskMap { get; }
    public ControlMapPixelData ControlMapsRawData { get; }

    public TerrainDataRequestResult(ChunkCoord chunkCoord, int requestVersion, float[,] heightMap, float[,] gradientXMap,
        float[,] gradientZMap, float[,] slopeMap, float[,] moistureMap, float[,] temperatureMap, BiomeType[,] biomeMap, 
        SurfaceType[,] surfaceTypeMap, WaterState[,] waterStateMap, GroundCoverType[,] groundCoverMap,
        WorldFeaturePlan worldFeaturePlan, float[,] riverMaskMap, ControlMapPixelData controlMapsRawData)
    {
        ChunkCoord = chunkCoord;
        RequestVersion = requestVersion;
        HeightMap = heightMap;
        GradientXMap = gradientXMap;
        GradientZMap = gradientZMap;
        SlopeMap = slopeMap;
        MoistureMap = moistureMap;
        TemperatureMap = temperatureMap;
        BiomeMap = biomeMap;
        SurfaceTypeMap = surfaceTypeMap;
        WaterStateMap = waterStateMap;
        GroundCoverMap = groundCoverMap;
        WorldFeaturePlan = worldFeaturePlan;
        RiverMaskMap = riverMaskMap;
        ControlMapsRawData = controlMapsRawData;
    }
}
