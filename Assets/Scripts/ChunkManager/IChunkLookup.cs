/// <summary>The data/runtime lookup needed by foliage queues; does not expose streaming orchestration.</summary>
public interface IChunkLookup
{
    ChunkRecord GetChunkRecord(ChunkCoord coord);
    ChunkRuntime GetChunkRuntime(ChunkRecord record);
}
