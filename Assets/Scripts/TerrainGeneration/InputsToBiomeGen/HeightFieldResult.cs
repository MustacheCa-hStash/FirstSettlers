public readonly struct HeightFieldResult
{
    public readonly float[,] HeightMap;
    // Smoothed terrain inclination in degrees, calculated from the wide stencil.
    public readonly float[,] SlopeMap;
    public readonly float[,] MountainMaskMap;
    public readonly float[,] RiverMaskMap;

    public HeightFieldResult(float[,] heightMap, float[,] slopeMap,
        float[,] mountainMaskMap, float[,] riverMaskMap)
    {
        HeightMap = heightMap;
        SlopeMap = slopeMap;
        MountainMaskMap = mountainMaskMap;
        RiverMaskMap = riverMaskMap;
    }
}
