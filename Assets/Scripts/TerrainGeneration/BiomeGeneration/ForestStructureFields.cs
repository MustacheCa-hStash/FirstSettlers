public class ForestStructureFields
{
    public float[,] CanopyIntentMap { get; }
    public float[,] ClearingMap { get; }
    public float[,] TreeClusterMap { get; }
    public float[,] RockinessMap { get; }
    public float[,] RockInfluenceMap { get; }
    public float[,] DampShadeMap { get; }
    public float[,] UnderstoryDensityMap { get; }
    public float[,] OrganicFloorIntentMap { get; }

    public ForestStructureFields(int width, int height)
    {
        CanopyIntentMap = new float[width, height];
        ClearingMap = new float[width, height];
        TreeClusterMap = new float[width, height];
        RockinessMap = new float[width, height];
        RockInfluenceMap = new float[width, height];
        DampShadeMap = new float[width, height];
        UnderstoryDensityMap = new float[width, height];
        OrganicFloorIntentMap = new float[width, height];
    }
}
