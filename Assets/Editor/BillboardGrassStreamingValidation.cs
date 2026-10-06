using System.Reflection;
using UnityEditor;
using UnityEngine;

// Historical menu and fixture name retained for callers; validation now covers resident streaming.
public static class BillboardGrassStreamingValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    [MenuItem("Tools/Terrain/Validate Billboard Grass Streaming")]
    public static void Run() => GrassStreamingValidation.Run();
    public static ChunkRecord CreateRecord()
    {
        var record = new ChunkRecord(new ChunkCoord(2, -3));
        const int size = 19;
        var surface = new SurfaceType[size, size];
        var ground = new GroundCoverType[size, size];
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
            {
                surface[x, z] = SurfaceType.Grass;
                ground[x, z] = GroundCoverType.DarkGrass;
            }
        Set(record, "heightMap", new float[size, size]);
        Set(record, "surfaceTypeMap", surface);
        Set(record, "biomeMap", new BiomeType[size, size]);
        Set(record, "groundCoverMap", ground);
        return record;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, PrivateInstance).SetValue(target, value);
}
