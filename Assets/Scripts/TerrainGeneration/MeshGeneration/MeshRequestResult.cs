public class MeshRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int LOD { get; }
    public int RequestVersion { get; }
    public MeshData TerrainMeshData { get; }
    public WaterMeshData LakeMeshData { get; }
    public WaterMeshData RiverMeshData { get; }

    public MeshRequestResult(ChunkCoord chunkCoord, int lod, int requestVersion, MeshData terrainMeshData, 
        WaterMeshData lakeMeshData, WaterMeshData riverMeshData)
    {
        ChunkCoord = chunkCoord;
        LOD = lod;
        RequestVersion = requestVersion;
        TerrainMeshData = terrainMeshData;
        LakeMeshData = lakeMeshData;
        RiverMeshData = riverMeshData;
    }
}