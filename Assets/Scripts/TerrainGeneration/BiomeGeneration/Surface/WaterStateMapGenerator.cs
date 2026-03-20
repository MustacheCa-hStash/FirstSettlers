public static class WaterStateMapGenerator
{
    public static WaterState[,] GenerateWaterStateMap(float[,] heightMap, float[,] riverMaskMap)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        WaterState[,] map = new WaterState[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                map[x, y] = WaterStateClassifier.Classify(heightMap[x, y], riverMaskMap[x, y]);
            }
        }

        return map;
    }
}