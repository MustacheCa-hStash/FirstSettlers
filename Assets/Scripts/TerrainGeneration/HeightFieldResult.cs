public readonly struct HeightFieldResult
{
    public readonly float[,] HeightMap;
    public readonly float[,] GradientXMap;
    public readonly float[,] GradientZMap;

    public HeightFieldResult(float[,] heightMap, float[,] gradientXMap, float[,] gradientZMap)
    {
        HeightMap = heightMap;
        GradientXMap = gradientXMap;
        GradientZMap = gradientZMap;
    }
}