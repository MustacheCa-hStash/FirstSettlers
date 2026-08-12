using UnityEngine;

public struct WorldFeaturePlacement
{
    public WorldFeatureType featureType;
    public WorldFeatureVariant variant;
    public float sampleX;
    public float sampleZ;
    public Quaternion rotation;
    public Vector3 scale;
    public float exclusionRadius;
    public float influenceRadius;
    public int prefabIndex;

    public WorldFeaturePlacement(
        WorldFeatureType featureType,
        WorldFeatureVariant variant,
        float sampleX,
        float sampleZ,
        Quaternion rotation,
        Vector3 scale,
        float exclusionRadius,
        float influenceRadius,
        int prefabIndex = 0)
    {
        this.featureType = featureType;
        this.variant = variant;
        this.sampleX = sampleX;
        this.sampleZ = sampleZ;
        this.rotation = rotation;
        this.scale = scale;
        this.exclusionRadius = exclusionRadius;
        this.influenceRadius = influenceRadius;
        this.prefabIndex = prefabIndex;
    }
}
