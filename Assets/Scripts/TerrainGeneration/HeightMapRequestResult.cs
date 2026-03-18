public class HeightMapRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int RequestVersion { get; }
    public float[,] HeightMap { get; }

    public HeightMapRequestResult(ChunkCoord chunkCoord, int requestVersion, float[,] heightMap)
    {
        ChunkCoord = chunkCoord;
        RequestVersion = requestVersion;
        HeightMap = heightMap;
    }
}