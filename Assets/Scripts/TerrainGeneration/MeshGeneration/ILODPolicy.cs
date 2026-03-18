public interface ILODPolicy
{
    int GetLOD(ChunkCoord viewerChunkCoord, ChunkCoord targetChunkCoord);
}
