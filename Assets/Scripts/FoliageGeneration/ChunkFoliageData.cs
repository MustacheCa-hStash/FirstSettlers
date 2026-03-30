using System.Collections.Generic;

public class ChunkFoliageData
{
    public bool grassGenerated;
    public readonly List<FoliageInstanceData> grassInstances = new List<FoliageInstanceData>();

    public void Clear()
    {
        grassGenerated = false;
        grassInstances.Clear();
    }
}