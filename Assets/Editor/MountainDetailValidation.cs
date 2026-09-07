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
        Debug.Log(ValidateMountainWidth());
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
        float mountainScale = scale;
        mask = Mask(x / (mountainScale * 6f), z / (mountainScale * 6f), c.MountainMaskOffsets);
        float weight = Unity.Mathematics.math.pow(Unity.Mathematics.math.smoothstep(0.12f, 0.9f, mask), 1.8f);
        float mountain = Mountain(x / (mountainScale * 3f), z / (mountainScale * 3f), c.MountainTerrainOffsets);
        relief = mountain * weight * 45f;
        float height = baseLand + relief;
        if (legacy)
        {
            float rugged = Mathf.Max(0f, Fbm(x / (mountainScale * 0.3f), z / (mountainScale * 0.3f), 0, 3, 0.5f, 2f, c.MountainRuggedOffsets));
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
        HeightFieldResult near = HeightMapGenerator.GenerateTerrainHeightField(size, seed, 600f, chunk, context.WaterLevel, context.MountainHorizontalScale);
        HeightFieldResult nextX = HeightMapGenerator.GenerateTerrainHeightField(size, seed, 600f, new ChunkCoord(chunk.x + 1, chunk.z), context.WaterLevel, context.MountainHorizontalScale);
        HeightFieldResult nextZ = HeightMapGenerator.GenerateTerrainHeightField(size, seed, 600f, new ChunkCoord(chunk.x, chunk.z + 1), context.WaterLevel, context.MountainHorizontalScale);
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
        var far = FarTerrainGenerator.Generate(chunk, 1, size, seed, 600f, 200f, 0.3f, 9, 16, 0f, context.WaterLevel, false, context.MountainHorizontalScale);
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

    public static string ValidateMountainWidth(bool validateJobs = true)
    {
        Require(HeightMapGenerator.SanitizeMountainHorizontalScale(float.NaN) == 1f, "Invalid width fallback.");
        Require(HeightMapGenerator.SanitizeMountainHorizontalScale(0f) == 1f, "Width lower clamp failed.");
        int expanded = 0, protectedRivers = 0, widenedUpperSlopes = 0;
        foreach (int seed in new[] { 9, 42 })
        {
            var original = HeightMapGenerator.CreateSamplingContext(seed, TerrainWaterSettings.DefaultWaterLevel);
            foreach (float width in new[] { 1.5f, 2f, 3f })
            {
                var context = HeightMapGenerator.CreateSamplingContext(seed, original.WaterLevel, width);
                var anchors = HeightMapGenerator.GetMountainAnchors(
                    new Unity.Mathematics.float2(-12016f), new Unity.Mathematics.float2(12016f), 600f, context);
                Require(anchors.Length > 0, "No mountain anchors found.");
                var dominantPeak = anchors[0].Position;
                float peakHeight = float.MinValue;
                foreach (var anchor in anchors)
                {
                    float height = HeightMapGenerator.SampleTerrainHeight(anchor.Position.x, anchor.Position.y, 600f, original).Height;
                    if (height > peakHeight) { peakHeight = height; dominantPeak = anchor.Position; }
                    Require(Unity.Mathematics.math.distance(
                        HeightMapGenerator.MountainExpansionSource(anchor.Position, anchor, width), anchor.Position) < 0.001f,
                        "Summit anchor moved.");
                    var offset = new Unity.Mathematics.float2(100f, 50f);
                    var source = HeightMapGenerator.MountainExpansionSource(anchor.Position + offset * width, anchor, width);
                    Require(Unity.Mathematics.math.distance(source, anchor.Position + offset) < 0.003f,
                        "Upper profile did not stretch around the summit.");
                }
                float expandedPeakHeight = HeightMapGenerator.SampleTerrainHeight(dominantPeak.x, dominantPeak.y, 600f, context).Height;
                Require(Mathf.Abs(expandedPeakHeight - peakHeight) < 0.001f, "Dominant summit height changed during expansion.");
                foreach (var direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
                {
                    var source = dominantPeak + new Unity.Mathematics.float2(direction.x, direction.y) * 200f;
                    var destination = dominantPeak + (source - dominantPeak) * width;
                    var before = HeightMapGenerator.SampleTerrainHeight(destination.x, destination.y, 600f, original);
                    var after = HeightMapGenerator.SampleTerrainHeight(destination.x, destination.y, 600f, context);
                    var sourceSample = HeightMapGenerator.SampleTerrainHeight(source.x, source.y, 600f, original);
                    float sourceBase = Base(source.x / 960f, source.y / 960f, original.BaseLandOffsets);
                    float destinationBase = Base(destination.x / 960f, destination.y / 960f, original.BaseLandOffsets);
                    Require(after.Height - destinationBase >= sourceSample.Height - sourceBase - 0.001f,
                        "Expanded geometry does not contain the stretched source profile.");
                    if (after.Height > before.Height + 0.01f && sourceSample.Height > peakHeight * 0.5f)
                        widenedUpperSlopes++;
                }
                for (int x = -16; x <= 16; x++)
                    for (int z = -16; z <= 16; z++)
                    {
                        float wx = x * 751f, wz = z * 751f;
                        var a = HeightMapGenerator.SampleTerrainHeight(wx, wz, 600f, original);
                        var b = HeightMapGenerator.SampleTerrainHeight(wx, wz, 600f, context);
                        Require(b.Height >= a.Height - 0.00001f, "Expansion lowered existing terrain.");
                        Require(b.RiverMask <= a.RiverMask + 0.00001f, "Expansion allowed rivers into mountains.");
                        if (b.Height > a.Height + 0.2f) expanded++;
                        if (a.RiverMask > 0.1f && b.RiverMask == 0f) protectedRivers++;
                    }
                if (validateJobs)
                {
                    var p = anchors[0].Position;
                    ValidateChunk(context, seed, new ChunkCoord(Mathf.FloorToInt(p.x / 32f), Mathf.FloorToInt(p.y / 32f)));
                }
            }
        }
        Require(expanded > 0 && protectedRivers > 0, $"Expansion did not exercise larger mountains and excluded rivers: expanded={expanded}, protected={protectedRivers}.");
        Require(widenedUpperSlopes > 0, "Only lower slopes expanded.");
        return $"Mountain width validation passed: fixed summit anchors, preserved dominant summit heights, {widenedUpperSlopes} widened upper slopes, {expanded} raised samples, {protectedRivers} newly excluded rivers.";
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
            Require(material.HasProperty("_SandNormal") && material.HasProperty("_SandAlbedo"), "Sand asset slots missing.");
        }
        finally { UnityEngine.Object.DestroyImmediate(material); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
