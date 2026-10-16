using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Phase-one fixtures use real production types; the main scene is never opened or changed.</summary>
public static class TerrainSculptingValidation
{
    private static int checks;
    private static readonly TerrainDensityProfile Profile = TerrainDensityProfile.Default;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, double tolerance, string message) => Check(Math.Abs(actual - expected) <= tolerance, message + $" ({actual:R} versus {expected:R})");
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { checks++; return; }
        throw new InvalidOperationException(message);
    }
    private sealed class Flat : ITerrainDensityGenerator
    {
        private readonly double height;
        public int Calls;
        public int FailAfter = int.MaxValue;
        public Flat(double height = 100) { this.height = height; }
        public double SampleDensity(double3 p)
        {
            if (++Calls > FailAfter) throw new InvalidOperationException("injected baseline failure");
            return p.y - height;
        }
    }
    private static void Cut(TerrainDensityWorld world, TerrainDensityBrush brush)
    {
        using var edit = world.StageExcavation(brush);
        Check(world.TryCommit(edit), "Current edit did not commit.");
    }

    [MenuItem("Tools/Terrain/Validate Sculpting Density Foundation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run the isolated fixture outside Play Mode.");
        checks = 0;
        ValidateCoordinatesAndEncoding(); ValidateCarving(); ValidateOverlapsAndSnapshots();
        ValidateHalos(); ValidateUniformOverrides(); ValidateFailuresAndBudgets(); ValidateTriangleInterpolation();
        Debug.Log($"TERRAIN SCULPTING FOUNDATION PASS: {checks} assertions across ownership, interpolation, real brush operations, snapshot reconstruction, halos and failure admission.");
    }

    public static void RunBatch()
    {
        try
        {
            Run(); ValidateProductionBaseline(SourceRoot());
            TerrainHeightSamplingValidation.Run();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-sculptMeshChecks")>=0)TerrainSculptingMeshValidation.Run();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-sculptIntegrationChecks")>=0)TerrainSculptingIntegrationValidation.Run();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-sculptPersistenceChecks")>=0)TerrainSculptingPersistenceValidation.Run();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-sculptInteractionChecks")>=0)TerrainSculptingInteractionValidation.Run();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-sculptPerformanceChecks")>=0)TerrainSculptingPerformanceValidation.Run();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-sculptWorldChecks")>=0)TerrainSculptingWorldValidation.Run();
            Debug.Log($"TERRAIN SCULPTING P1 COMPLETE: {checks} assertions; no main-scene sculpt integration is enabled.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void ValidateCoordinatesAndEncoding()
    {
        foreach (int x in new[] { -1000000, -129, -128, -33, -32, -17, -16, -15, -1, 0, 1, 15, 16, 17, 127, 128, 1000000 })
        {
            var sample = new int3(x, -x, x / 3);
            int3 owner = TerrainDensityCoordinates.BrickOf(sample), local = TerrainDensityCoordinates.LocalOf(sample);
            Check(math.all(local >= 0) && math.all(local < 16), "Negative-coordinate sample escaped its owner.");
            Check(math.all(TerrainDensityCoordinates.Origin(owner) + local == sample), "Canonical owner does not reconstruct sample.");
            Check(math.all(TerrainDensityCoordinates.FloorSample(TerrainDensityCoordinates.ToWorld(sample, Profile), Profile) == sample), "World/lattice round-trip failed.");
            Check(math.all(TerrainDensityCoordinates.LocalFromIndex(TerrainDensityCoordinates.Index(local)) == local), "Local indexing failed.");
        }
        Check(TerrainDensityCoordinates.FloorSample(new double3(-.001), Profile).x == -1, "Small negative world position rounded toward zero.");
        Check(TerrainDensityCoordinates.FloorSample(new double3(Profile.Spacing - 1e-8), Profile).x == 0, "Lattice tolerance swallowed a real boundary offset.");
        for (int code = -32767; code <= 32767; code += 113)
            Check(Profile.Encode(Profile.Decode((short)code)) == code, "Encoding is not stable across reconstruction.");
        Check(Profile.Encode(-100) == -32767 && Profile.Encode(100) == 32767 && Profile.Encode(0) == 0, "Clamp/zero rule changed.");
        Throws<ArgumentException>(() => Profile.Decode(short.MinValue), "Reserved invalid code was accepted.");
        Throws<ArgumentOutOfRangeException>(() => Profile.Encode(double.NaN), "NaN density accepted.");
        Throws<ArgumentException>(() => default(TerrainDensityProfile).Validate(), "Unconstructed profile accepted.");
    }

    private static void ValidateCarving()
    {
        using var world = new TerrainDensityWorld(Profile, new Flat());
        double3 center = new double3(Profile.Spacing * 7.5);
        var box = TerrainDensityBrush.Box(center, new double3(.5));
        using (var cancelled = world.StageExcavation(box))
        {
            Check(cancelled.ChangedSamples > 0 && world.ModifiedBrickCount == 0 && world.Revision == 0, "Staging exposed uncommitted edits.");
            Check(world.ReadEncoded(new int3(7)) < 0, "Pending cut changed committed solid queries.");
            using (var pendingMesh = world.CaptureMeshInput(new int3(0), cancelled))
            {
                Check(pendingMesh.IncludesPendingEdit && pendingMesh.Revision == world.Revision && pendingMesh.Read(8, 8, 8) > 0,
                    "Mesher cannot see the staged cavity before authoritative publication.");
                Check(world.ReadEncoded(new int3(7)) < 0, "Preparing replacement geometry published pending density.");
            }
            Throws<InvalidOperationException>(() => world.StageExcavation(box), "Concurrent edit was admitted.");
        }
        Check(world.ReservedPayloadBytes == 0 && world.ModifiedBrickCount == 0, "Cancellation leaked payload or committed stone changes.");
        Cut(world, box);
        int air = 0;
        for (int z = 4; z <= 11; z++) for (int y = 4; y <= 11; y++) for (int x = 4; x <= 11; x++)
            if (world.ReadEncoded(new int3(x, y, z)) >= 0) air++;
        Check(air == 64, "One-metre box at this sub-grid offset should contain 4^3 air sample positions.");
        double inside = Profile.Decode(world.ReadEncoded(new int3(9, 7, 7)));
        double outside = Profile.Decode(world.ReadEncoded(new int3(10, 7, 7)));
        Check(inside > 0 && outside < 0 && outside > -Profile.Band, "Stone-side distance band missing.");
        double relativeInsideX = 9 * Profile.Spacing - center.x;
        double boundary = relativeInsideX + Profile.Spacing * inside / (inside - outside);
        Near(boundary, .5, Profile.EncodingStep * 2, "Interpolation did not recover intended box face.");
        Check(world.ReadEncoded(new int3(30)) == -32767, "Cut affected distant stone outside its support band.");
        long revision = world.Revision, bytes = world.ResidentPayloadBytes;
        using (var repeated = world.StageExcavation(box))
        {
            Check(repeated.ChangedSamples == 0, "Identical repeated cut changed quantized values.");
            Check(world.TryCommit(repeated), "No-op edit rejected.");
        }
        Check(world.Revision == revision && world.ResidentPayloadBytes == bytes, "No-op edit grew revision or memory.");

        using var sphereWorld = new TerrainDensityWorld(Profile, new Flat());
        Cut(sphereWorld, TerrainDensityBrush.Sphere(center, .5));
        int sphereAir = 0;
        for (int z = 4; z <= 11; z++) for (int y = 4; y <= 11; y++) for (int x = 4; x <= 11; x++)
            if (sphereWorld.ReadEncoded(new int3(x, y, z)) >= 0) sphereAir++;
        Check(sphereAir == 32, "Sphere brush became a cube or lost offset boundary samples.");
        using var zeroWorld = new TerrainDensityWorld(Profile, new Flat());
        Cut(zeroWorld, TerrainDensityBrush.Sphere(new double3(0), Profile.Spacing));
        Check(zeroWorld.ReadEncoded(new int3(1, 0, 0)) == 0, "Exact brush boundary must retain a zero code.");
    }

    private static void ValidateOverlapsAndSnapshots()
    {
        using var world = new TerrainDensityWorld(Profile, new Flat());
        using var before = world.CaptureSnapshot();
        // Centre on the intersection of eight owners, including negative coordinates.
        Cut(world, TerrainDensityBrush.Sphere(new double3(0), .5));
        Check(world.ModifiedBrickCount == 8, "Brick corner excavation did not update all eight owners.");
        Check(before.ReadEncoded(new int3(0)) == -32767, "Snapshot was mutated by a later commit.");
        using var first = world.CaptureSnapshot();
        Cut(world, TerrainDensityBrush.Sphere(new double3(.6, 0, 0), .5));
        for (int z = -4; z <= 4; z++) for (int y = -4; y <= 4; y++) for (int x = -4; x <= 6; x++)
        {
            var q = new int3(x, y, z);
            Check(world.ReadEncoded(q) >= first.ReadEncoded(q), "Overlapping excavation refilled an earlier cavity.");
        }
        using var snapshot = world.CaptureSnapshot();
        using var restored = TerrainDensityWorld.FromSnapshot(snapshot);
        Check(restored.Revision == world.Revision && restored.ModifiedBrickCount == world.ModifiedBrickCount, "Snapshot reconstruction lost metadata.");
        for (int z = -5; z <= 5; z++) for (int y = -5; y <= 5; y++) for (int x = -5; x <= 7; x++)
        {
            var q = new int3(x, y, z);
            Check(restored.ReadEncoded(q) == world.ReadEncoded(q), "Reconstructed samples differ from committed excavation.");
        }
        short saved = snapshot.ReadEncoded(new int3(8, 0, 0));
        Cut(restored, TerrainDensityBrush.Sphere(TerrainDensityCoordinates.ToWorld(new int3(8, 0, 0), Profile), .5));
        Check(snapshot.ReadEncoded(new int3(8, 0, 0)) == saved && world.ReadEncoded(new int3(8, 0, 0)) == saved,
            "Copy-on-write branch modified the source save snapshot.");
        short[] exported = snapshot.CopyBrickSamples(new int3(0)); exported[0] = -32767;
        Check(snapshot.ReadEncoded(new int3(0)) > 0, "Exported copy exposed mutable authoritative storage.");
    }

    private static void ValidateHalos()
    {
        using var world = new TerrainDensityWorld(Profile, new Flat());
        Cut(world, TerrainDensityBrush.Sphere(new double3(0), .5));
        using (var left = world.CaptureMeshInput(new int3(-1, 0, 0)))
        using (var right = world.CaptureMeshInput(new int3(0)))
        {
            Check(left.Revision == right.Revision, "Mesh inputs did not capture the same committed revision.");
            for (int z = 0; z < 19; z++) for (int y = 0; y < 19; y++) for (int x = 16; x < 19; x++)
                Check(left.Read(x, y, z) == right.Read(x - 16, y, z), "Neighbour halos disagree at shared corner or gradient sample.");
            short original = right.Read(3, 1, 1);
            Cut(world, TerrainDensityBrush.Sphere(new double3(.3, 0, 0), .5));
            Check(world.ReadEncoded(new int3(2, 0, 0)) > original && right.Read(3, 1, 1) == original,
                "Mesh halo changed while a worker could be reading it.");
        }
        Check(world.ReservedPayloadBytes == 0, "Halo/snapshot reservation leaked after disposal.");
    }

    private static void ValidateUniformOverrides()
    {
        using var world = new TerrainDensityWorld(Profile, new Flat());
        Cut(world, TerrainDensityBrush.Box(new double3(Profile.Spacing * 7.5), new double3(3.6)));
        using var snapshot = world.CaptureSnapshot();
        Check(snapshot.UniformBrickCount > 0 && snapshot.ModifiedBrickCount > 0, "Uniform-air override was discarded as implicit baseline.");
        using var restored = TerrainDensityWorld.FromSnapshot(snapshot);
        for (int i = 0; i < 4096; i++)
            Check(restored.ReadEncoded(TerrainDensityCoordinates.LocalFromIndex(i)) == 32767, "Saved uniform-air brick reconstructed as stone.");
    }

    private static void ValidateFailuresAndBudgets()
    {
        var box = TerrainDensityBrush.Box(new double3(Profile.Spacing * 7.5), new double3(.5));
        using (var zero = new TerrainDensityWorld(Profile, new Flat(), 0))
        {
            Check(zero.ReadEncoded(new int3(0)) < 0 && zero.ResidentPayloadBytes == 0, "Implicit stone allocated storage.");
            Throws<TerrainDensityCapacityException>(() => zero.StageExcavation(box), "Zero budget admitted a dense edit.");
            Check(zero.Revision == 0 && zero.ReservedPayloadBytes == 0, "Failed admission changed state.");
        }
        using (var limited = new TerrainDensityWorld(Profile, new Flat(), 2 * TerrainDensityProfile.DetailedBrickBytes))
        {
            Cut(limited, box);
            using (var pin = limited.CaptureSnapshot())
                Throws<TerrainDensityCapacityException>(() => limited.StageExcavation(box), "Pinned snapshots bypassed density budget.");
            using var pending = limited.StageExcavation(box);
            Check(limited.ReservedPayloadBytes > 0, "Staged payload was not reserved.");
        }
        var failing = new Flat { FailAfter = 300 };
        using (var world = new TerrainDensityWorld(Profile, failing))
        {
            Throws<InvalidOperationException>(() => world.StageExcavation(box), "Injected generator failure not observed.");
            Check(world.ModifiedBrickCount == 0 && world.Revision == 0 && world.ReservedPayloadBytes == 0,
                "Failed partial generation committed or leaked a reservation.");
            failing.FailAfter = int.MaxValue; Cut(world, box);
        }
        using (var dry = new TerrainDensityWorld(Profile, new Flat(), minimumRemovalY: 14.7))
        {
            Throws<ArgumentException>(() => dry.StageExcavation(TerrainDensityBrush.Sphere(new double3(0, 15, 0), .5)), "Underwater excavation admitted.");
            Check(dry.ReservedPayloadBytes == 0, "Dry-bound rejection allocated an edit.");
        }
        Throws<ArgumentException>(() => { using var w = new TerrainDensityWorld(Profile, new Flat()); w.StageExcavation(default); }, "Default brush admitted.");
        var disposed = new TerrainDensityWorld(Profile, new Flat()); disposed.Dispose();
        Throws<ObjectDisposedException>(() => disposed.ReadEncoded(new int3(0)), "Disposed world accepted a query.");
    }

    private static void ValidateTriangleInterpolation()
    {
        var generation = WorldGenerationConfiguration.Default;
        generation.ChunkSize = 16; generation.WorldScale = .3f; generation.MeshHeightMultiplier = 1f;
        float[,] Provider(ChunkCoord coord)
        {
            var map = new float[19, 19];
            for (int x = 0; x < 19; x++) for (int z = 0; z < 19; z++)
            {
                int gx = coord.x * 16 + x - 1, gz = coord.z * 16 + z - 1;
                map[x, z] = gx == 1 && gz == 1 ? 10 : 0;
            }
            return map;
        }
        var baseline = new TerrainBaselineSampler(generation, 2, Provider);
        double s = generation.WorldScale, high = 10f * generation.WorldScale;
        Near(baseline.SampleHeight(.75 * s, .25 * s), high * .25, 1e-10, "Lower triangle used bilinear interpolation.");
        Near(baseline.SampleHeight(.25 * s, .75 * s), high * .25, 1e-10, "Upper triangle used wrong diagonal.");
        Near(baseline.SampleHeight(.5 * s, .5 * s), high * .5, 1e-10, "Shared a-c diagonal is inconsistent.");
        baseline.SampleHeight(-s, -s); baseline.SampleHeight(20 * s, 20 * s);
        Check(baseline.CachedChunkCount <= 2, "Height cache exceeded its configured cap.");
    }

    public static void ValidateProductionBaseline(string root)
    {
        var generation = ReadSceneGeneration(Path.Combine(root, "Assets/Scenes/SmearScene.unity"));
        Check(generation.ChunkSize == 128 && generation.WorldScale == .3f, "Fixture is not using the approved scene lattice.");
        var sampler = new TerrainBaselineSampler(generation, 8);
        var watch = Stopwatch.StartNew();
        long comparisons = 0;
        foreach (var coord in new[] { new ChunkCoord(0, 0), new ChunkCoord(-1, -1), new ChunkCoord(1, 0) })
        {
            var field = HeightMapGenerator.GenerateTerrainHeightField(generation.ChunkSize, generation.Seed,
                generation.SampleScale, coord, generation.Water.WaterLevel, generation.MountainHorizontalScale,
                generation.MeshHeightMultiplier, generation.Erosion);
            foreach (int x in new[] { 0, 1, 16, 63, 127, 128 }) foreach (int z in new[] { 0, 1, 16, 63, 127, 128 })
            {
                int gx = coord.x * 128 + x, gz = coord.z * 128 + z;
                double expected = field.HeightMap[x + 1, z + 1] * generation.MeshHeightMultiplier * generation.WorldScale;
                double actual = sampler.SampleHeight(gx * (double)generation.WorldScale, gz * (double)generation.WorldScale);
                Near(actual, expected, 0, "Native baseline differs from production at a lattice/chunk boundary."); comparisons++;
                double signed = sampler.SampleDensity(new double3(gx * (double)generation.WorldScale, expected + .15, gz * (double)generation.WorldScale));
                Near(signed, .15, 1e-10, "World height multiplier or sign convention is wrong.");
            }
        }
        watch.Stop(); double coldMs = watch.Elapsed.TotalMilliseconds;
        // The bounded cache may legitimately evict origin while checking the other chunks.
        // Prime this measurement's region before timing cache-only reads.
        sampler.SampleHeight(0, 0);
        int generated = sampler.GeneratedChunkCount;
        watch.Restart(); double checksum = 0;
        for (int i = 0; i < 10000; i++) checksum += sampler.SampleHeight((i % 64) * (double)generation.WorldScale, (i / 64 % 64) * (double)generation.WorldScale);
        watch.Stop();
        Check(sampler.GeneratedChunkCount == generated, "Warm XZ sampling regenerated height maps.");
        string report = $"SCULPT BASELINE FIXTURE: source=SmearScene, seed={generation.Seed}, spacing={generation.WorldScale:R}, heightMultiplier={generation.MeshHeightMultiplier:R}, erosionEnabled={generation.Erosion.enabled}, comparisons={comparisons}, maxError=0, coldFixtureMs={coldMs:F3}, warm10000Ms={watch.Elapsed.TotalMilliseconds:F3}, heightChunksGenerated={generated}, cachedChunks={sampler.CachedChunkCount}, checksum={checksum:R}. This is a CPU fixture, not live walking FPS.";
        Debug.Log(report);
        Directory.CreateDirectory(Path.Combine(root, ".utmp/terrain-sculpting"));
        File.WriteAllText(Path.Combine(root, ".utmp/terrain-sculpting/baseline-observation.txt"), report + Environment.NewLine);
    }

    internal static WorldGenerationConfiguration ReadSceneGeneration(string path)
    {
        string yaml = File.ReadAllText(path);
        float Number(string key) => float.Parse(Regex.Match(yaml, @"(?m)^  " + Regex.Escape(key) + @": ([^\r\n]+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var g = WorldGenerationConfiguration.Default;
        g.Seed = (int)Number("worldSeed"); g.ChunkSize = (int)Number("chunkSize"); g.SampleScale = Number("sampleScale");
        g.WorldScale = Number("worldScale"); g.MeshHeightMultiplier = Number("meshHeightMultiplier");
        g.MountainHorizontalScale = Number("mountainWidth"); g.Octaves = (int)Number("octaves");
        g.Persistence = Number("persistence"); g.Lacunarity = Number("lacunarity");
        g.MountainSnowRenderCoverageGamma = Number("mountainSnowBlendGamma");
        g.Water = new TerrainWaterSettings(Number("globalWaterY"), g.MeshHeightMultiplier, g.WorldScale);
        string erosion = Regex.Match(yaml, @"(?m)^  erosion:\r?\n(?<settings>(?:    [^\r\n]*\r?\n)+)").Groups["settings"].Value;
        if (erosion.Length == 0) throw new InvalidOperationException("Scene erosion profile was not found.");
        object boxed = g.Erosion;
        foreach (var field in typeof(WorldErosionSettings).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var match = Regex.Match(erosion, @"(?m)^    " + Regex.Escape(field.Name) + @": ([^\r\n]+)");
            if (!match.Success) throw new InvalidOperationException("Scene fixture missing erosion setting: " + field.Name);
            string value = match.Groups[1].Value;
            if (field.FieldType == typeof(float)) field.SetValue(boxed, float.Parse(value, CultureInfo.InvariantCulture));
            else if (field.FieldType == typeof(int)) field.SetValue(boxed, int.Parse(value, CultureInfo.InvariantCulture));
            else if (field.FieldType == typeof(bool)) field.SetValue(boxed, value == "1");
            else if (field.FieldType.IsEnum) field.SetValue(boxed, Enum.ToObject(field.FieldType, int.Parse(value, CultureInfo.InvariantCulture)));
            else if (field.FieldType == typeof(Vector2))
            {
                var v = Regex.Match(value, @"\{x: ([^,]+), y: ([^}]+)\}");
                field.SetValue(boxed, new Vector2(float.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(v.Groups[2].Value, CultureInfo.InvariantCulture)));
            }
            else throw new InvalidOperationException("Unsupported erosion fixture field: " + field.Name);
        }
        g.Erosion = ((WorldErosionSettings)boxed).Sanitized(); return g;
    }

    internal static string SourceRoot()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-terrainSourceRoot");
        if (i < 0 || i + 1 >= args.Length) throw new InvalidOperationException("Missing -terrainSourceRoot.");
        return Path.GetFullPath(args[i + 1]);
    }
}
