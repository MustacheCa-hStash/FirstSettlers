using UnityEngine;

public static class BiomeMapGenerator
{
    public static BiomeType[,] GenerateBiomeMap(float[,] heightMap, float[,] moistureMap, float[,] temperatureMap, 
        float[,] slopeMap, float[,] mountainMaskMap, float[,] riverMaskMap)
    {
        int size = heightMap.GetLength(0);
        BiomeType[,] biomeMap = new BiomeType[size, size];

        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                biomeMap[x, z] = BiomeClassifier.Classify(heightMap[x, z], moistureMap[x, z], temperatureMap[x, z], 
                    slopeMap[x, z], mountainMaskMap[x, z], riverMaskMap[x, z]);
            }
        }

        return biomeMap;
    }
}
