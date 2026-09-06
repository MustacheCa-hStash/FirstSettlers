using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class MountainDetailValidation
{
    private delegate float NoiseSample(float x, float z, Vector2[] offsets);
    private delegate float RiverSample(float x, float z, int seed, out float basin);
    private delegate float BasinSample(float height, float basin, float river, float level);
    private delegate float FbmSample(float x, float z, int seed, int octaves, float persistence, float lacunarity, Vector2[] offsets);

    private static T Bind<T>(string name, params Type[] parameters) where T : Delegate
    {
        MethodInfo method = typeof(HeightMapGenerator).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static,
            null, parameters, null);
        return (T)Delegate.CreateDelegate(typeof(T), method);
    }

    private static readonly NoiseSample Base = Bind<NoiseSample>("SampleBaseLand", typeof(float), typeof(float), typeof(Vector2[]));
    private static readonly NoiseSample Mask = Bind<NoiseSample>("SampleMountainMask", typeof(float), typeof(float), typeof(Vector2[]));
    private static readonly NoiseSample Mountain = Bind<NoiseSample>("SampleMountainTerrain", typeof(float), typeof(float), typeof(Vector2[]));
    private static readonly RiverSample River = Bind<RiverSample>("SampleRiverMask", typeof(float), typeof(float), typeof(int), typeof(float).MakeByRefType());
    private static readonly BasinSample Carve = Bind<BasinSample>("CarveRiverBasin", typeof(float), typeof(float), typeof(float), typeof(float));
    private static readonly FbmSample Fbm = Bind<FbmSample>("SampleBasicFbm", typeof(float), typeof(float), typeof(int), typeof(int), typeof(float), typeof(float), typeof(Vector2[]));

    [MenuItem("Tools/Terrain/Validate Mountain Detail")]
    public static void Run()
    {
        int raised = 0, cut = 0, protectedSamples = 0;
        float maxChange = 0f, oldSlopeSum = 0f, newSlopeSum = 0f;
        int slopeSamples = 0;
        foreach (int seed in new[] { 7, 42, 12345 })
        {
            var context = HeightMapGenerator.CreateSamplingContext(seed, TerrainWaterSettings.DefaultWaterLevel);
            ChunkCoord mountainChunk = default;
            bool foundMountain = false;
            for (int x = -32; x <= 32; x++)
            {
                for (int z = -32; z <= 32; z++)
                {
                    float wx = x * 751f, wz = z * 751f;
                    float broad = Reference(wx, wz, context, false, out float relief, out float mask, out float river);
                    float legacy = Reference(wx, wz, context, true, out _, out _, out _);
                    TerrainHeightSample current = HeightMapGenerator.SampleTerrainHeight(wx, wz, 600f, context);
                    float delta = current.Height - broad;
                    Require(!float.IsNaN(current.Height) && !float.IsInfinity(current.Height), "Non-finite mountain height.");
                    Require(Mathf.Abs(current.MountainMask - mask) < 0.000001f, "Mountain footprint changed.");
                    Require(Mathf.Abs(current.RiverMask - river) < 0.000001f, "Mountain detail changed river eligibility.");
                    Require(Mathf.Abs(delta) <= Mathf.Min(relief * 0.08f, 1.5f) + 0.00001f, $"Mountain relief exceeded its budget: seed={seed}, position={wx},{wz}, broad={broad:R}, current={current.Height:R}, delta={delta:R}, relief={relief:R}.");
                    Require((current.Height <= context.WaterLevel) == (broad <= context.WaterLevel), "Detail created or removed flooded terrain.");
                    if (river > 0f || relief / 45f <= 0.03f)
                    {
                        Require(Mathf.Abs(delta) < 0.00001f, "Detail entered a protected river or lowland region.");
                        protectedSamples++;
                    }
                    if (delta > 0.001f) raised++;
                    if (delta < -0.001f) cut++;
                    maxChange = Mathf.Max(maxChange, Mathf.Abs(current.Height - legacy));
                    if (relief > 3f)
                    {
                        oldSlopeSum += Slope(wx, wz, context, true);
                        newSlopeSum += Slope(wx, wz, context, false);
                        slopeSamples++;
                    }
                    if (!foundMountain && Mathf.Abs(delta) > 0.05f)
                    {
                        mountainChunk = new ChunkCoord(Mathf.FloorToInt(wx / 32f), Mathf.FloorToInt(wz / 32f));
                        foundMountain = true;
                    }
                }
            }
            Require(foundMountain, "Seed did not exercise mountain detail.");
            ValidateChunk(context, seed, mountainChunk);
        }
        Require(raised > 100 && cut > 100, "Detail must produce both ridges and cuts.");
        Debug.Log($"Mountain detail: raised={raised}, cut={cut}, protected={protectedSamples}, max legacy change={maxChange:F4}; mean slope {oldSlopeSum / slopeSamples:F5} -> {newSlopeSum / slopeSamples:F5} ({slopeSamples} mountain samples).");
        Require(newSlopeSum > oldSlopeSum, "Detail did not increase sampled mountain ruggedness.");
        ValidateShader();
        WaterGenerationValidation.Run();
        SurfaceBlendValidation.Run();
        Debug.Log("Mountain detail validation passed: bounded relief, unchanged masks/water coverage, protected rivers, signed detail, native parity, chunk edges, near/far heights, and both rock shader variants.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static float Reference(float x, float z, TerrainHeightSamplingContext c, bool legacy,
        out float relief, out float mask, out float river, float scale = 600f)
    {
        float baseLand = Base(x / (scale * 1.6f), z / (scale * 1.6f), c.BaseLandOffsets);
        mask = Mask(x / (scale * 6f), z / (scale * 6f), c.MountainMaskOffsets);
        float weight = Unity.Mathematics.math.pow(Unity.Mathematics.math.smoothstep(0.12f, 0.9f, mask), 1.8f);
        float mountain = Mountain(x / (scale * 3f), z / (scale * 3f), c.MountainTerrainOffsets);
        relief = mountain * weight * 45f;
        float height = baseLand + relief;
        if (legacy)
        {
            float rugged = Mathf.Max(0f, Fbm(x / (scale * 0.3f), z / (scale * 0.3f), 0, 3, 0.5f, 2f, c.MountainRuggedOffsets));
            height += rugged * Unity.Mathematics.math.smoothstep(0.25f, 0.8f, mountain) * weight * 2f;
        }
        river = River(x / (scale * 10f), z / (scale * 10f), c.RiverSeed, out float basin);
        float eligibility = (1f - Unity.Mathematics.math.smoothstep(0.012f, 0.03f, mountain * weight)) *
                            (1f - Unity.Mathematics.math.smoothstep(0.20f, 0.55f, weight));
        river *= eligibility;
        return Carve(height, basin * eligibility, river, c.WaterLevel);
    }

    private static float Slope(float x, float z, TerrainHeightSamplingContext c, bool legacy)
    {
        float Height(float wx, float wz) => legacy
            ? Reference(wx, wz, c, true, out _, out _, out _)
            : HeightMapGenerator.SampleTerrainHeight(wx, wz, 600f, c).Height;
        float dx = (Height(x + 4, z) - Height(x - 4, z)) / 8f;
        float dz = (Height(x, z + 4) - Height(x, z - 4)) / 8f;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static void ValidateChunk(TerrainHeightSamplingContext context, int seed, ChunkCoord chunk)
    {
        const int size = 32;
        HeightFieldResult near = HeightMapGenerator.GenerateTerrainHeightField(size, seed, 600f, chunk, context.WaterLevel);
        HeightFieldResult nextX = HeightMapGenerator.GenerateTerrainHeightField(size, seed, 600f, new ChunkCoord(chunk.x + 1, chunk.z), context.WaterLevel);
        HeightFieldResult nextZ = HeightMapGenerator.GenerateTerrainHeightField(size, seed, 600f, new ChunkCoord(chunk.x, chunk.z + 1), context.WaterLevel);
        for (int x = 0; x <= size; x++)
        {
            for (int z = 0; z <= size; z++)
            {
                float managed = HeightMapGenerator.SampleTerrainHeight(chunk.x * size + x, chunk.z * size + z, 600f, context).Height;
                Require(Mathf.Abs(managed - near.HeightMap[x + 1, z + 1]) < 0.0002f, "Managed/Burst mountain mismatch.");
            }
            Require(near.HeightMap[size + 1, x + 1] == nextX.HeightMap[1, x + 1], "X mountain chunk seam.");
            Require(near.HeightMap[x + 1, size + 1] == nextZ.HeightMap[x + 1, 1], "Z mountain chunk seam.");
        }
        var far = FarTerrainGenerator.Generate(chunk, 1, size, seed, 600f, 200f, 0.3f, 9, 16, 0f, context.WaterLevel);
        Mesh mesh = far.TerrainMeshData.CreateMesh();
        try
        {
            foreach (Vector3 vertex in mesh.vertices)
            {
                int x = Mathf.RoundToInt(vertex.x / 0.3f + size / 2f);
                int z = Mathf.RoundToInt(vertex.z / 0.3f + size / 2f);
                Require(Mathf.Abs(vertex.y - near.HeightMap[x + 1, z + 1] * 60f) < 0.002f, "Near/far mountain mismatch.");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }

    private static void ValidateShader()
    {
        Shader shader = Shader.Find("Custom/StylizedTerrainURP");
        Require(shader != null, "Missing terrain shader.");
        var material = new Material(shader);
        try
        {
            foreach (bool enabled in new[] { false, true })
            {
                if (enabled) material.EnableKeyword("_ROCK_DETAIL");
                else material.DisableKeyword("_ROCK_DETAIL");
                ShaderUtil.CompilePass(material, 0, true);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            }
            Require(material.HasProperty("_RockNormal") && material.HasProperty("_RockAlbedo"), "Rock asset slots missing.");
        }
        finally { UnityEngine.Object.DestroyImmediate(material); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
