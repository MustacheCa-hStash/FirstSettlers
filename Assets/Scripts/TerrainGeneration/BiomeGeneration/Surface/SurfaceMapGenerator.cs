public static class SurfaceMapGenerator
{
    public static SurfaceType[,] GenerateSurfaceTypeMap(float[,] heightMap, float[,] slopeMap, float[,] riverMaskMap, 
        BiomeType[,] biomeMap)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        SurfaceType[,] map = new SurfaceType[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                map[x, y] = SurfaceTypeClassifier.Classify(heightMap[x, y], slopeMap[x, y], riverMaskMap[x, y], 
                    biomeMap[x, y]);
            }
        }

        return map;
    }
}