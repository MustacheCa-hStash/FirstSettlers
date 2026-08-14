using System.Collections.Generic;

public class ChunkFoliageData
{
    public bool nearGrassGenerated;
    public int subChunksPerChunk;
    public List<FoliageInstanceData>[,] nearGrassInstancesBySubChunk;
    public bool[,] nearGrassSubChunkGenerated;

    public bool billboardGenerated;
    public List<BillboardFoliageInstanceData> billboardGrassInstances = new List<BillboardFoliageInstanceData>();

    public bool flowersGenerated;
    public List<FlowerInstanceData> flowerInstances = new List<FlowerInstanceData>();

    public bool treeCubesGenerated;
    public List<TreeInstanceData> treeCubeInstances = new List<TreeInstanceData>();

    public bool bushesGenerated;
    public List<TreeInstanceData> bushInstances = new List<TreeInstanceData>();

    public bool rocksGenerated;
    public List<RockInstanceData> rockInstances = new List<RockInstanceData>();

    public void InitializeNearGrass(int subChunksPerChunk)
    {
        this.subChunksPerChunk = subChunksPerChunk;
        nearGrassInstancesBySubChunk = new List<FoliageInstanceData>[subChunksPerChunk, subChunksPerChunk];
        nearGrassSubChunkGenerated = new bool[subChunksPerChunk, subChunksPerChunk];

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
                if (nearGrassSubChunkGenerated != null)
                    nearGrassSubChunkGenerated[x, z] = false;
            }
        }
    }

    public bool IsNearGrassSubChunkGenerated(int localSubChunkX, int localSubChunkZ)
    {
        if (!HasValidNearGrassSubChunk(localSubChunkX, localSubChunkZ))
            return false;

        return nearGrassSubChunkGenerated[localSubChunkX, localSubChunkZ];
    }

    public void ClearNearGrassSubChunk(int localSubChunkX, int localSubChunkZ)
    {
        if (!HasValidNearGrassSubChunk(localSubChunkX, localSubChunkZ))
            return;

        nearGrassInstancesBySubChunk[localSubChunkX, localSubChunkZ].Clear();
        nearGrassSubChunkGenerated[localSubChunkX, localSubChunkZ] = false;
        nearGrassGenerated = false;
    }

    public void MarkNearGrassSubChunkGenerated(int localSubChunkX, int localSubChunkZ)
    {
        if (!HasValidNearGrassSubChunk(localSubChunkX, localSubChunkZ))
            return;

        nearGrassSubChunkGenerated[localSubChunkX, localSubChunkZ] = true;
        nearGrassGenerated = AreAllNearGrassSubChunksGenerated();
    }

    private bool HasValidNearGrassSubChunk(int localSubChunkX, int localSubChunkZ)
    {
        return nearGrassInstancesBySubChunk != null &&
               nearGrassSubChunkGenerated != null &&
               localSubChunkX >= 0 &&
               localSubChunkZ >= 0 &&
               localSubChunkX < subChunksPerChunk &&
               localSubChunkZ < subChunksPerChunk;
    }

    private bool AreAllNearGrassSubChunksGenerated()
    {
        if (nearGrassSubChunkGenerated == null)
            return false;

        for (int x = 0; x < subChunksPerChunk; x++)
        {
            for (int z = 0; z < subChunksPerChunk; z++)
            {
                if (!nearGrassSubChunkGenerated[x, z])
                    return false;
            }
        }

        return true;
    }

    public void ClearBillboards()
    {
        billboardGenerated = false;
        billboardGrassInstances.Clear();
    }

    public void ClearFlowers()
    {
        flowersGenerated = false;
        flowerInstances.Clear();
    }

    public void ClearTreeCubes()
    {
        treeCubesGenerated = false;
        treeCubeInstances.Clear();
    }

    public void ClearBushes()
    {
        bushesGenerated = false;
        bushInstances.Clear();
    }

    public void ClearRocks()
    {
        rocksGenerated = false;
        rockInstances.Clear();
    }

    public void ClearAll()
    {
        ClearNearGrass();
        ClearBillboards();
        ClearFlowers();
        ClearTreeCubes();
        ClearBushes();
        ClearRocks();
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

    public int GetTotalFlowerInstanceCount()
    {
        return flowerInstances != null ? flowerInstances.Count : 0;
    }

    public int GetTotalTreeCubeInstanceCount()
    {
        return treeCubeInstances != null ? treeCubeInstances.Count : 0;
    }

    public int GetTotalBushInstanceCount()
    {
        return bushInstances != null ? bushInstances.Count : 0;
    }

    public int GetTotalRockInstanceCount()
    {
        return rockInstances != null ? rockInstances.Count : 0;
    }
}
