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
    // Allocated only for chunks containing forest land. X=density, Y=soil, Z=moss, W=mixed litter.
    public Unity.Mathematics.float4[,] FloorEcologyMap { get; private set; }

    public void EnsureFloorEcologyMap(int width, int height)
    {
        if (FloorEcologyMap == null)
            FloorEcologyMap = new Unity.Mathematics.float4[width, height];
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
