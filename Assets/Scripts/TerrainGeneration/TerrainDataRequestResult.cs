public class TerrainDataRequestResult : System.IDisposable
{
    public ChunkCoord ChunkCoord { get; }
    public int RequestVersion { get; }
    public float[,] HeightMap { get; }
    // Smoothed terrain inclination in degrees, calculated from the wide stencil.
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
    public ChunkRecord.NativeTerrainData NativeData { get; private set; }

    public TerrainDataRequestResult(ChunkCoord chunkCoord, int requestVersion, float[,] heightMap,
        float[,] slopeMap, float[,] moistureMap, float[,] temperatureMap, BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap, WaterState[,] waterStateMap, GroundCoverType[,] groundCoverMap,
        WorldFeaturePlan worldFeaturePlan, float[,] riverMaskMap, ControlMapPixelData controlMapsRawData,
        ChunkRecord.NativeTerrainData nativeData = null)
    {
        ChunkCoord = chunkCoord;
        RequestVersion = requestVersion;
        HeightMap = heightMap;
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
        NativeData = nativeData;
    }

    public void TransferNativeOwnership() => NativeData = null;

    public void Dispose()
    {
        NativeData?.Dispose();
        NativeData = null;
    }
}
