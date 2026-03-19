public readonly struct HeightFieldResult
{
    public readonly float[,] HeightMap;
    public readonly float[,] GradientXMap;
    public readonly float[,] GradientZMap;
    public readonly float[,] MountainMaskMap;

    public HeightFieldResult(float[,] heightMap, float[,] gradientXMap, float[,] gradientZMap, float[,] mountainMaskMap)
    {
        HeightMap = heightMap;
        GradientXMap = gradientXMap;
        GradientZMap = gradientZMap;
        MountainMaskMap = mountainMaskMap;
    }
}