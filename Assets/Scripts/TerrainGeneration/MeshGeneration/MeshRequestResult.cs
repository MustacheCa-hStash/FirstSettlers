public class MeshRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int LOD { get; }
    public int RequestVersion { get; }
    public MeshData MeshData { get; }

    public MeshRequestResult(ChunkCoord chunkCoord, int lod, int requestVersion, MeshData meshData)
    {
        ChunkCoord = chunkCoord;
        LOD = lod;
        RequestVersion = requestVersion;
        MeshData = meshData;
    }
}