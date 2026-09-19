public class FarTerrainRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int RequestVersion { get; }
    public bool IsMacroTile { get; }
    public int PatchSizeInChunks { get; }
    public MeshData TerrainMeshData { get; }
    public WaterMeshData WaterMeshData { get; }
    public float[,] HeightGrid { get; }
    public ControlMapPixelData ControlMapsRawData { get; }

    public FarTerrainRequestResult(
        ChunkCoord chunkCoord,
        int requestVersion,
        bool isMacroTile,
        int patchSizeInChunks,
        MeshData terrainMeshData,
        ControlMapPixelData controlMapsRawData, WaterMeshData waterMeshData = null, float[,] heightGrid = null)
    {
        ChunkCoord = chunkCoord;
        RequestVersion = requestVersion;
        IsMacroTile = isMacroTile;
        PatchSizeInChunks = patchSizeInChunks;
        TerrainMeshData = terrainMeshData;
        WaterMeshData = waterMeshData;
        HeightGrid = heightGrid;
        ControlMapsRawData = controlMapsRawData;
    }
}
