using System;
using Unity.Mathematics;

// Compatibility token for existing sampling clients. World generation no longer
// stretches or max-unions individual mountain copies.
public struct MountainExpansionAnchor { public float2 Position; public float Radius; }
public static partial class HeightMapGenerator
{
    public static MountainExpansionAnchor[] GetMountainAnchors(float2 minimum, float2 maximum, float sampleScale, TerrainHeightSamplingContext context)
        => Array.Empty<MountainExpansionAnchor>();
}
