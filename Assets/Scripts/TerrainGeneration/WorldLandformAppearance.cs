using UnityEngine;

/// <summary>One active rendered world, shared by terrain, excavated stone and instanced rocks.</summary>
public static class WorldLandformAppearance
{
    private static object owner;
    private static readonly int SeedId = Shader.PropertyToID("_WorldLandformSeed");
    private static readonly int ParamsId = Shader.PropertyToID("_WorldLandformParams");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        owner = null;
        Shader.SetGlobalVector(ParamsId, Vector4.zero);
        Shader.SetGlobalInteger(SeedId, 0);
    }

    public static void Bind(object world, WorldGenerationConfiguration generation)
    {
        owner = world;
        var s = generation.Erosion.Sanitized().landforms;
        // SetGlobalInt stores a float on older Unity paths and loses high seed bits.
        Shader.SetGlobalInteger(SeedId, generation.Seed);
        Shader.SetGlobalVector(ParamsId, new Vector4(s.enabled ? 1f : 0f,
            Mathf.Max(1f, generation.SampleScale) * generation.WorldScale,
            s.desertRegionScale, generation.WorldScale));
    }

    public static void Release(object world) { if (ReferenceEquals(owner, world)) Reset(); }
}
