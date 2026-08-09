using System.Collections.Generic;

public class WorldFeaturePlan
{
    public readonly List<WorldFeaturePlacement> Placements = new List<WorldFeaturePlacement>();
    public float[,] CanopyDensityMap { get; }
    public ForestStructureFields ForestStructure { get; }

    public WorldFeaturePlan(int width, int height)
    {
        CanopyDensityMap = new float[width, height];
        ForestStructure = new ForestStructureFields(width, height);
    }
}
