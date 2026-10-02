using UnityEngine;

// Reuses bounded forest-scatter streaming and instanced LOD batches, with its own assets/habitat.
[System.Serializable]
public sealed class FernSettings : LeafClusterSettings
{
    [Range(0,1)] public float minMoisture = 0.35f;
    [Min(.01f), InspectorName("Fern Size Multiplier"), Tooltip("Overall fern size, applied to the random Scale Range. 2 doubles the authored plant.")]
    public float sizeMultiplier = 2f;
    public FernSettings()
    {
        placementMultiplier=1; cellSize=1.5f; density=.65f; maxSlope=26;
        scaleRange=new Vector2(.75f,1.2f); seedOffset=48000;
        lodStart=22; lodEnd=38; matchGrassRenderDistance=false; renderDistance=65; fadeWidth=10;
    }
    public override bool IsFern => true;
    public override string DefaultPrefabPath => "Foliage/ForestFern_LOD0";
    public override string DefaultDistantPrefabPath => "Foliage/ForestFern_LOD1";
    public override float SizeMultiplier => Mathf.Max(.01f,sizeMultiplier);
    public override int PlacementSignature => System.HashCode.Combine(base.PlacementSignature,minMoisture,sizeMultiplier);
}
