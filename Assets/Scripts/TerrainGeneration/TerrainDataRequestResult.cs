public class TerrainDataRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int RequestVersion { get; }
    public float[,] HeightMap { get; }
    public float[,] GradientXMap { get; }
    public float[,] GradientZMap { get; }
    public float[,] MountainMaskMap { get; }
    public float[,] MoistureMap { get; }
    public float[,] TemperatureMap { get; }
    public BiomeType[,] BiomeMap { get; }

    public TerrainDataRequestResult(ChunkCoord chunkCoord, int requestVersion, float[,] heightMap, float[,] gradientXMap,
        float[,] gradientZMap, float[,] mountainMaskMap, float[,] moistureMap, float[,] temperatureMap, BiomeType[,] biomeMap)
    {
        ChunkCoord = chunkCoord;
        RequestVersion = requestVersion;
        HeightMap = heightMap;
        GradientXMap = gradientXMap;
        GradientZMap = gradientZMap;
        MountainMaskMap = mountainMaskMap;
        MoistureMap = moistureMap;
        TemperatureMap = temperatureMap;
        BiomeMap = biomeMap;
    }
}