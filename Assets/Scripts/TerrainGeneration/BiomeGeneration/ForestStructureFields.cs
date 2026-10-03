public class ForestStructureFields
{
    public float[,] CanopyIntentMap { get; }
    public float[,] ClearingMap { get; }
    public float[,] TreeClusterMap { get; }
    public float[,] RockinessMap { get; }
    public float[,] RockInfluenceMap { get; }
    public float[,] TreeLitterBalanceMap { get; }
    public float[,] DampShadeMap { get; }
    public float[,] UnderstoryDensityMap { get; }
    public float[,] OrganicFloorIntentMap { get; }
    // Allocated only where forest membership is positive. X=blended density,
    // Y=weighted soil, Z=weighted moss, W=weighted mixed litter.
    public Unity.Mathematics.float4[,] FloorEcologyMap { get; private set; }

    public void EnsureFloorEcologyMap(int width, int height)
    {
        if (FloorEcologyMap == null)
        {
            FloorEcologyMap = new Unity.Mathematics.float4[width, height];
            // Preserve meadow density when jobs interpolate across the forest footprint.
            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                    FloorEcologyMap[x,z] = new Unity.Mathematics.float4(1f, 0f, 0f, 0f);
        }
    }

    public ForestStructureFields(int width, int height)
    {
        CanopyIntentMap = new float[width, height];
        ClearingMap = new float[width, height];
        TreeClusterMap = new float[width, height];
        RockinessMap = new float[width, height];
        RockInfluenceMap = new float[width, height];
        TreeLitterBalanceMap = new float[width, height];
        DampShadeMap = new float[width, height];
        UnderstoryDensityMap = new float[width, height];
        OrganicFloorIntentMap = new float[width, height];
    }
}
