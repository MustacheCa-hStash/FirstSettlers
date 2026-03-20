public class MeshRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int LOD { get; }
    public int RequestVersion { get; }
    public MeshData TerrainMeshData { get; }
    public WaterMeshData WaterMeshData { get; }

    public MeshRequestResult(ChunkCoord chunkCoord, int lod, int requestVersion, MeshData terrainMeshData, 
        WaterMeshData waterMeshData)
    {
        ChunkCoord = chunkCoord;
        LOD = lod;
        RequestVersion = requestVersion;
        TerrainMeshData = terrainMeshData;
        WaterMeshData = waterMeshData;
    }
}