public class FarTerrainRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int RequestVersion { get; }
    public bool IsMacroTile { get; }
    public MeshData TerrainMeshData { get; }
    public WaterMeshData WaterMeshData { get; }
    public ControlMapPixelData ControlMapsRawData { get; }

    public FarTerrainRequestResult(
        ChunkCoord chunkCoord,
        int requestVersion,
        bool isMacroTile,
        MeshData terrainMeshData,
        ControlMapPixelData controlMapsRawData, WaterMeshData waterMeshData = null)
    {
        ChunkCoord = chunkCoord;
        RequestVersion = requestVersion;
        IsMacroTile = isMacroTile;
        TerrainMeshData = terrainMeshData;
        WaterMeshData = waterMeshData;
        ControlMapsRawData = controlMapsRawData;
    }
}
