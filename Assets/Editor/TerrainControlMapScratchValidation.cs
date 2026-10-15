using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static class TerrainControlMapScratchValidation
{
    private delegate ControlMapPixelData RentScratch(int width, int height, out bool poolOwned);
    private static RentScratch Renter(TerrainControlMapScratchPool pool) =>
        (RentScratch)typeof(TerrainControlMapScratchPool).GetMethod("Rent", BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate(typeof(RentScratch), pool);
    private static Action<ControlMapPixelData> Returner(TerrainControlMapScratchPool pool) =>
        (Action<ControlMapPixelData>)typeof(TerrainControlMapScratchPool).GetMethod("Return", BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<ControlMapPixelData>), pool);
    private static int Count(TerrainControlMapScratchPool pool, string name) =>
        (int)typeof(TerrainControlMapScratchPool).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    [MenuItem("Tools/Terrain/Validate Control Map Scratch Reuse")]
    public static void Run()
    {
        ValidateOwnershipAndBounds();
        ValidatePixelsAndFailure();
        ValidateConcurrentReuse();
        ValidateAllocations();
        Debug.Log("CONTROL MAP SCRATCH PASS: pixel equality, clean reuse, independent outputs, exception return, concurrent workers, bounded sizes/capacity, shutdown and measured allocation-event reduction.");
    }

    public static void RunBatch()
    {
        try { Run(); WorldStreamingSchedulingValidation.Run(); TerrainWorkerLifetimeValidation.RunBatch(); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateOwnershipAndBounds()
    {
        using var pool = new TerrainControlMapScratchPool(2);
        var rent = Renter(pool); var giveBack = Returner(pool);
        var a = rent(17, 13, out bool ownA);
        var b = rent(17, 13, out bool ownB);
        var overflow = rent(17, 13, out bool ownOverflow);
        var other = rent(19, 11, out bool ownOther);
        Check(ownA && ownB && !ownOverflow && !ownOther && !ReferenceEquals(a, b), "Pool ownership/capacity/size isolation failed.");
        giveBack(a); giveBack(b);
        var again = rent(17, 13, out bool ownAgain);
        Check(ownAgain && (ReferenceEquals(again, a) || ReferenceEquals(again, b)), "Returned scratch was not reused.");
        Check(Count(pool, "allocatedCount") == 2 && Count(pool, "idleCount") == 1, "Retention exceeded capacity.");
        pool.Dispose();
        Check(Count(pool, "idleCount") == 0, "Shutdown retained idle scratch.");
        again.Maps[0][0] = new Color32(1, 2, 3, 4); // Shutdown cannot invalidate a worker's borrowed scratch.
        giveBack(again);
        var after = rent(17, 13, out bool ownAfter);
        Check(!ownAfter && !ReferenceEquals(again, after) && Count(pool, "allocatedCount") == 0,
            "Late return or borrow revived a closed pool.");
    }

    private static void ValidatePixelsAndFailure()
    {
        const int width = 17, height = 13;
        using var pool = new TerrainControlMapScratchPool(1);
        var surfaces = new SurfaceType[width, height]; var covers = new GroundCoverType[width, height];
        var biomes = new BiomeType[width, height]; var membership = new byte[width, height];
        var snow = new float2[width, height]; var forest = new ForestStructureFields(width, height);
        forest.EnsureFloorEcologyMap(width, height);
        ControlMapPixelData retained = null, retainedReference = null;
        for (int pass = 0; pass < 5; pass++)
        {
            for (int x = 0; x < width; x++) for (int z = 0; z < height; z++)
            {
                surfaces[x, z] = pass == 4 ? SurfaceType.Sand : (SurfaceType)((x + z + pass) % 7);
                covers[x, z] = pass == 4 ? GroundCoverType.Default : (GroundCoverType)((x * 3 + z + pass) % 12);
                biomes[x, z] = (x + z) % 2 == 0 ? BiomeType.Forest : BiomeType.Taiga;
                membership[x, z] = (byte)((x * 29 + z * 17) % 256);
                forest.FloorEcologyMap[x, z] = new float4(.4f, .2f, .3f, .1f);
                snow[x, z] = new float2((x % 5) * .2f, .8f);
            }
            // Exercise forest overrides, snow/dusting and then completely empty secondary channels.
            var expected = TerrainControlMapBuilder.BuildRaw(surfaces, covers, pass == 4 ? null : snow,
                forestStructure: forest, biomeMap: biomes, forestMembership: membership);
            var actual = TerrainControlMapBuilder.BuildRaw(surfaces, covers, pass == 4 ? null : snow,
                forestStructure: forest, biomeMap: biomes, forestMembership: membership, scratchPool: pool);
            Equal(expected, actual);
            if (pass == 0) { retained = actual; retainedReference = expected; }
            else Equal(retainedReference, retained);
            if (pass == 4)
                for (int map = 1; map < 3; map++)
                    foreach (var pixel in actual.Maps[map]) Check(pixel.Equals(new Color32()), "Previous chunk leaked material channels.");
        }
        try
        {
            TerrainControlMapBuilder.BuildRaw(surfaces, new GroundCoverType[1, 1], scratchPool: pool);
            throw new Exception("Malformed fixture unexpectedly succeeded.");
        }
        catch (IndexOutOfRangeException) { }
        Check(Count(pool, "idleCount") == 1, "Failure lost the borrowed scratch slot.");
        Equal(TerrainControlMapBuilder.BuildRaw(surfaces, covers),
            TerrainControlMapBuilder.BuildRaw(surfaces, covers, scratchPool: pool));
    }

    private static void ValidateConcurrentReuse()
    {
        using var pool = new TerrainControlMapScratchPool(4);
        using var start = new ManualResetEventSlim(false);
        var tasks = new Task[8];
        for (int worker = 0; worker < tasks.Length; worker++)
        {
            int workerId = worker;
            tasks[worker] = Task.Run(() =>
            {
                var surfaces = new SurfaceType[31, 27]; var covers = new GroundCoverType[31, 27];
                for (int x = 0; x < 31; x++) for (int z = 0; z < 27; z++)
                { surfaces[x, z] = (SurfaceType)((x + workerId) % 7); covers[x, z] = (GroundCoverType)((z + workerId) % 12); }
                var expected = TerrainControlMapBuilder.BuildRaw(surfaces, covers);
                start.Wait();
                for (int pass = 0; pass < 12; pass++)
                    Equal(expected, TerrainControlMapBuilder.BuildRaw(surfaces, covers, scratchPool: pool));
            });
        }
        start.Set();
        Check(Task.WaitAll(tasks, 30000), "Concurrent reuse timed out.");
        Check(Count(pool, "allocatedCount") <= 4 && Count(pool, "idleCount") == Count(pool, "allocatedCount"),
            "Concurrent reuse lost scratch or exceeded retention.");
    }

    private static void ValidateAllocations()
    {
        const int size = 131, iterations = 8;
        var surfaces = new SurfaceType[size, size]; var covers = new GroundCoverType[size, size];
        using var pool = new TerrainControlMapScratchPool(4);
        // Warm both paths, including JIT and pool allocation, before sampling this thread.
        TerrainControlMapBuilder.BuildRaw(surfaces, covers);
        TerrainControlMapBuilder.BuildRaw(surfaces, covers, scratchPool: pool);
        long Sample(bool reuse)
        {
            // This Unity Mono runtime returns zero from GetAllocatedBytesForCurrentThread.
            // Record allocation events directly, without frame aggregation or other threads.
            using var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 256,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (int i = 0; i < iterations; i++)
                GC.KeepAlive(TerrainControlMapBuilder.BuildRaw(surfaces, covers, scratchPool: reuse ? pool : null));
            allocations.Stop();
            Check(allocations.Valid && allocations.Count > 0 && allocations.Count < allocations.Capacity,
                "Allocation recorder was unavailable or exceeded its capacity.");
            // GC.Alloc's recorder values are timing samples; Count measures allocation events.
            return allocations.Count;
        }
        Sample(false); Sample(true); // Warm recorder/JIT setup before comparing event counts.
        long fresh = Sample(false), reused = Sample(true);
        long scratchPayload = 3L * size * size * 4;
        Check(fresh == iterations * 10 && reused == iterations * 5,
            $"Unexpected allocation events over {iterations} builds: fresh={fresh}, reused={reused}.");
        Debug.Log($"CONTROL MAP ALLOCATION PASS: {fresh / iterations} -> {reused / iterations} managed allocation events/build after warmup; avoided padded pixel payload={scratchPayload:N0} bytes/build (source-derived, excludes object headers; 131-square input, 129-square output).");
    }

    private static void Equal(ControlMapPixelData expected, ControlMapPixelData actual)
    {
        Check(expected.Width == actual.Width && expected.Height == actual.Height && expected.Maps.Length == actual.Maps.Length,
            "Control map dimensions changed.");
        for (int map = 0; map < expected.Maps.Length; map++)
            for (int i = 0; i < expected.Maps[map].Length; i++)
                if (!expected.Maps[map][i].Equals(actual.Maps[map][i]))
                    throw new Exception($"Control pixels differ at map={map}, pixel={i}.");
    }
}
