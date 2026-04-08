using System.Collections.Generic;

public class ChunkFoliageData
{
    public bool nearGrassGenerated;
    public int subChunksPerChunk;
    public List<FoliageInstanceData>[,] nearGrassInstancesBySubChunk;

    public bool billboardGenerated;
    public List<BillboardFoliageInstanceData> billboardGrassInstances = new List<BillboardFoliageInstanceData>();

    public void InitializeNearGrass(int subChunksPerChunk)
    {
        this.subChunksPerChunk = subChunksPerChunk;
        nearGrassInstancesBySubChunk = new List<FoliageInstanceData>[subChunksPerChunk, subChunksPerChunk];

        for (int x = 0; x < subChunksPerChunk; x++)
        {
            for (int z = 0; z < subChunksPerChunk; z++)
            {
                nearGrassInstancesBySubChunk[x, z] = new List<FoliageInstanceData>();
            }
        }
    }

    public void ClearNearGrass()
    {
        nearGrassGenerated = false;

        if (nearGrassInstancesBySubChunk == null)
            return;

        for (int x = 0; x < subChunksPerChunk; x++)
        {
            for (int z = 0; z < subChunksPerChunk; z++)
            {
                nearGrassInstancesBySubChunk[x, z].Clear();
            }
        }
    }

    public void ClearBillboards()
    {
        billboardGenerated = false;
        billboardGrassInstances.Clear();
    }

    public void ClearAll()
    {
        ClearNearGrass();
        ClearBillboards();
    }

    public int GetTotalNearGrassInstanceCount()
    {
        if (nearGrassInstancesBySubChunk == null)
            return 0;

        int total = 0;

        for (int x = 0; x < subChunksPerChunk; x++)
        {
            for (int z = 0; z < subChunksPerChunk; z++)
            {
                total += nearGrassInstancesBySubChunk[x, z].Count;
            }
        }

        return total;
    }

    public int GetTotalBillboardInstanceCount()
    {
        return billboardGrassInstances != null ? billboardGrassInstances.Count : 0;
    }
}