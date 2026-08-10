public class FarTerrainRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int RequestVersion { get; }
    public MeshData TerrainMeshData { get; }
    public ControlMapPixelData ControlMapsRawData { get; }

    public FarTerrainRequestResult(
        ChunkCoord chunkCoord,
        int requestVersion,
        MeshData terrainMeshData,
        ControlMapPixelData controlMapsRawData)
    {
        ChunkCoord = chunkCoord;
        RequestVersion = requestVersion;
        TerrainMeshData = terrainMeshData;
        ControlMapsRawData = controlMapsRawData;
    }
}
