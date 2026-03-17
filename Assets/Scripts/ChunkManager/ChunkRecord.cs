public class ChunkRecord
{
    private ChunkCoord chunkCoord;
    private ChunkRuntime activeRuntime;
    private float[,] heightMap;

    public ChunkCoord ChunkCoord => chunkCoord;
    public ChunkRuntime ActiveRuntime => activeRuntime;
    public bool IsLoaded => activeRuntime != null;
    public bool HasHeightMap => heightMap != null;
    public float[,] HeightMap => heightMap;

    public ChunkRecord(ChunkCoord chunkCoord)
    {
        this.chunkCoord = chunkCoord;
    }

    public void SetActiveRuntime(ChunkRuntime activeRuntime)
    {
        this.activeRuntime = activeRuntime;
    }

    public void ClearActiveRuntime(ChunkRuntime runtime)
    {
        if (activeRuntime == runtime) { activeRuntime = null; }
    }

    public void SetHeightMap(float[,] heightMap)
    {
        this.heightMap = heightMap; 
    }
}
