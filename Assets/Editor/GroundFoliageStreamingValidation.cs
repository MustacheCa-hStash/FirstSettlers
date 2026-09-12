using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;

// Synthetic data only: does not open a scene, enter play mode, or move a camera.
public static class GroundFoliageStreamingValidation
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static ChunkRecord Record()
    {
        var record = BillboardGrassStreamingValidation.CreateRecord();
        var biomes = record.BiomeMap;
        for (int x = 0; x < biomes.GetLength(0); x++)
            for (int z = 0; z < biomes.GetLength(1); z++) biomes[x, z] = BiomeType.Grassland;
        return record;
    }

    private static int Drain(IEnumerator<bool> steps)
    {
        int slices = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (steps.MoveNext())
        {
            if (steps.Current) slices++;
            else System.Threading.Thread.Yield();
            Check(timer.Elapsed.TotalSeconds < 30, "Discovery did not converge.");
        }
        return slices;
    }

    private static void Validate<T>(Func<ChunkRecord, IEnumerator<bool>> create,
        Func<ChunkRecord, List<T>> instances, Func<ChunkRecord, bool> generated, Action<ChunkRecord> clear)
    {
        var first = Record();
        using (var steps = create(first))
        {
            Check(steps.MoveNext() && !steps.Current, "Scheduling did not defer the job.");
            Check(!generated(first), "Scheduling published incomplete data.");
            Check(Drain(steps) > 1, "Result collection was not sliced.");
        }
        Check(generated(first) && instances(first).Count > 0, "Expected nonempty foliage.");
        var second = Record();
        using (var steps = create(second)) Drain(steps);
        Check(instances(first).Count == instances(second).Count, "Nondeterministic count.");
        for (int i = 0; i < instances(first).Count; i++)
            Check(EqualityComparer<T>.Default.Equals(instances(first)[i], instances(second)[i]), "Nondeterministic instance.");

        var stale = Record();
        using (var steps = create(stale))
        {
            steps.MoveNext();
            clear(stale);
            Drain(steps);
            Check(!generated(stale) && instances(stale).Count == 0, "Clear was overwritten by a stale job.");
        }
        using (var steps = create(stale))
        {
            steps.MoveNext();
            typeof(ChunkRecord).GetField("heightMap", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(stale, new float[19, 19]);
            Drain(steps);
            Check(!generated(stale), "Replaced height map accepted stale foliage.");
        }
        using (var steps = create(stale)) steps.MoveNext();
        Check(!generated(stale), "Disposal published unfinished data.");
        using (var steps = create(stale)) Drain(steps);
        Check(generated(stale), "Cancelled request could not be retried.");
    }

    [MenuItem("Tools/Terrain/Validate Ground Foliage Streaming")]
    public static void Run()
    {
        var flowers = new FlowerSettings { patchCellSize = 4, patchSpawnChance = 1, patchNoiseThreshold = 0,
            minFlowersPerPatch = 12, maxFlowersPerPatch = 24 };
        var clover = new CloverSettings { patchCellSize = 4, patchSpawnChance = 1, patchNoiseThreshold = 0,
            minClumpsPerPatch = 12, maxClumpsPerPatch = 24 };
        var dandelions = new DandelionSettings { patchCellSize = 4, patchSpawnChance = 1, patchNoiseThreshold = 0 };
        Validate(r => FoliageGenerator.GenerateFlowersIncrementally(r, flowers, 1234, 16, 1, 10),
            r => r.FoliageData.flowerInstances, r => r.FoliageData.flowersGenerated, r => r.FoliageData.ClearFlowers());
        Validate(r => FoliageGenerator.GenerateCloverIncrementally(r, clover, 2, 1234, 16, 1, 10),
            r => r.FoliageData.cloverInstances, r => r.FoliageData.cloverGenerated, r => r.FoliageData.ClearClover());
        Validate(r => FoliageGenerator.GenerateDandelionsIncrementally(r, dandelions, 1234, 16, 1, 10),
            r => r.FoliageData.dandelionInstances, r => r.FoliageData.dandelionsGenerated, r => r.FoliageData.ClearDandelions());
        ValidateGrassDependency(clover);
        Debug.Log("GROUND FOLIAGE PASS: deferred jobs, sliced deterministic output, clear/map invalidation, cancellation and retry.");
    }

    private static void ValidateGrassDependency(CloverSettings clover)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var record = Record();
        record.FoliageData = new ChunkFoliageData { treeCubesGenerated = true, bushesGenerated = true, rocksGenerated = true };
        var manager = (ChunkManager)FormatterServices.GetUninitializedObject(typeof(ChunkManager));
        typeof(ChunkManager).GetField("chunkRecords", flags).SetValue(manager,
            new Dictionary<ChunkCoord, ChunkRecord> { { record.ChunkCoord, record } });
        var mesh = new Mesh();
        var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        var foliage = new FoliageManager(null, new GrassSettings { groundFoliageGenerationBudgetMsPerFrame = 0.05f },
            null, clover, null, new TreeSettings(), 1234, 16, 1, 10);
        try
        {
            typeof(FoliageManager).GetField("cloverRenderData", flags).SetValue(foliage,
                new[] { new CloverRenderData(mesh, material) });
            var prepare = typeof(FoliageManager).GetMethod("PrepareStreamingGrass", flags);
            var process = typeof(FoliageManager).GetMethod("ProcessPendingGroundFoliageGenerationWork", flags);
            prepare.Invoke(foliage, new object[] { record });
            Check(!record.FoliageData.cloverGenerated, "Grass preparation synchronously generated clover.");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                // Repeated requests must not duplicate active work, including beyond clover's render range.
                prepare.Invoke(foliage, new object[] { record });
                process.Invoke(foliage, new object[] { manager, new ChunkCoord(0, 0), 0L, 0.001f });
                System.Threading.Thread.Yield();
                Check(timer.Elapsed.TotalSeconds < 30, "Ground queue starved under an exhausted shared budget.");
            } while (!record.FoliageData.cloverGenerated);
            Check(record.FoliageData.nearGrassRevision > 0, "Finished clover did not invalidate older grass exclusions.");
            var expected = Record();
            using (var steps = FoliageGenerator.GenerateCloverIncrementally(expected, clover, 1, 1234, 16, 1, 10)) Drain(steps);
            Check(expected.FoliageData.cloverInstances.Count == record.FoliageData.cloverInstances.Count,
                "Repeated grass requests duplicated clover candidates.");
        }
        finally
        {
            foliage.Dispose();
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
