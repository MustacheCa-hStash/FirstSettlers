public class ColliderRequestResult
{
    public ChunkCoord ChunkCoord { get; }
    public int RequestVersion { get; }
    public MeshData ColliderMeshData { get; }

    public ColliderRequestResult(
        ChunkCoord chunkCoord,
        int requestVersion,
        MeshData colliderMeshData)
    {
        ChunkCoord = chunkCoord;
        RequestVersion = requestVersion;
        ColliderMeshData = colliderMeshData;
    }
}