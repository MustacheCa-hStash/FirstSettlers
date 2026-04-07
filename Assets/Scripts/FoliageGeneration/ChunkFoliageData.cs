using System.Collections.Generic;

public class ChunkFoliageData
{
    public bool grassGenerated;
    public int subChunksPerChunk;

    public List<FoliageInstanceData>[,] grassInstancesBySubChunk;

    public void Initialize(int subChunksPerChunk)
    {
        this.subChunksPerChunk = subChunksPerChunk;
        grassInstancesBySubChunk = new List<FoliageInstanceData>[subChunksPerChunk, subChunksPerChunk];

        for (int x = 0; x < subChunksPerChunk; x++)
        {
            for (int z = 0; z < subChunksPerChunk; z++)
            {
                grassInstancesBySubChunk[x, z] = new List<FoliageInstanceData>();
            }
        }
    }

    public void Clear()
    {
        grassGenerated = false;

        if (grassInstancesBySubChunk == null)
            return;

        for (int x = 0; x < subChunksPerChunk; x++)
        {
            for (int z = 0; z < subChunksPerChunk; z++)
            {
                grassInstancesBySubChunk[x, z].Clear();
            }
        }
    }

    public int GetTotalInstanceCount()
    {
        if (grassInstancesBySubChunk == null)
            return 0;

        int total = 0;

        for (int x = 0; x < subChunksPerChunk; x++)
        {
            for (int z = 0; z < subChunksPerChunk; z++)
            {
                total += grassInstancesBySubChunk[x, z].Count;
            }
        }

        return total;
    }
}