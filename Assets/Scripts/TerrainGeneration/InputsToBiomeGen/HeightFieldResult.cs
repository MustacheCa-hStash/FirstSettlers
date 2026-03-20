public readonly struct HeightFieldResult
{
    public readonly float[,] HeightMap;
    public readonly float[,] GradientXMap;
    public readonly float[,] GradientZMap;
    public readonly float[,] SlopeMap;
    public readonly float[,] MountainMaskMap;
    public readonly float[,] RiverMaskMap;

    public HeightFieldResult(float[,] heightMap, float[,] gradientXMap, float[,] gradientZMap, float [,] slopeMap,
        float[,] mountainMaskMap, float[,] riverMaskMap)
    {
        HeightMap = heightMap;
        GradientXMap = gradientXMap;
        GradientZMap = gradientZMap;
        SlopeMap = slopeMap;
        MountainMaskMap = mountainMaskMap;
        RiverMaskMap = riverMaskMap;
    }
}