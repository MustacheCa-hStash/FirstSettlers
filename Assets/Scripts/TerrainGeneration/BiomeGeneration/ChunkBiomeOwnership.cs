using System;

// One cached 25-read vote over the owned vertex grid, excluding its one-sample halo.
public static class ChunkBiomeOwnership
{
    public static BiomeType? Classify(BiomeType[,] map)
    {
        if (map == null || map.GetLength(0) < 3 || map.GetLength(1) < 3) return null;
        Span<int> votes = stackalloc int[9];
        votes.Clear();
        for (int x=0;x<5;x++) for (int z=0;z<5;z++)
        {
            int ix=1+(map.GetLength(0)-3)*x/4, iz=1+(map.GetLength(1)-3)*z/4;
            int biome=(int)map[ix,iz];
            if ((uint)biome < votes.Length) votes[biome]++;
        }
        int winner=0;
        for (int i=1;i<votes.Length;i++) if (votes[i]>votes[winner]) winner=i;
        // No majority means a mixed chunk with no owner, rather than a fabricated biome.
        return votes[winner]>12 ? (BiomeType)winner : null;
    }
}
