public class GrasslandStructureFields
{
    public float[,] OpenGrassIntentMap { get; }
    public float[,] MeadowMoistureIntentMap { get; }
    public float[,] RiparianIntentMap { get; }
    public float[,] GroveIntentMap { get; }
    public float[,] RockinessMap { get; }
    public float[,] RockInfluenceMap { get; }

    public GrasslandStructureFields(int width, int height)
    {
        OpenGrassIntentMap = new float[width, height];
        MeadowMoistureIntentMap = new float[width, height];
        RiparianIntentMap = new float[width, height];
        GroveIntentMap = new float[width, height];
        RockinessMap = new float[width, height];
        RockInfluenceMap = new float[width, height];
    }
}
