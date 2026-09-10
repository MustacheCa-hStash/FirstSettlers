using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;

public static class BillboardGrassStreamingValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Terrain/Validate Billboard Grass Streaming")]
    public static void Run()
    {
        ValidateGeneration();
        ValidateInvalidation();
        foreach (float density in new[] { 0f, 0.37f, 1f }) ValidateBatches(density);
        Debug.Log("Billboard grass streaming validation passed: sliced publication, deterministic generation, invalidation, cancellation, density and batch packing.");
    }

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

    private static FoliageGenerator.BillboardGrassGenerationJob Schedule(ChunkRecord record) =>
        FoliageGenerator.ScheduleBillboardGrassForChunk(record, new GrassSettings { cellsPerAxis = 64 },
            null, null, 1234, 16, 1f, 10f);

    private static void ValidateGeneration()
    {
        var record = CreateRecord();
        using (var job = Schedule(record))
        {
            Require(!record.FoliageData.billboardGenerated, "Scheduling published incomplete grass.");
            job.CompleteForSynchronousCaller();
            Require(!job.ApplySlice(1), "A single-result slice consumed the whole chunk.");
            Require(record.FoliageData.billboardGrassInstances.Count == 0, "Partial results escaped staging.");
            while (!job.ApplySlice(37)) { }
            Require(record.FoliageData.billboardGenerated, "Finished grass was not published.");
        }
        var instances = record.FoliageData.billboardGrassInstances;
        Require(instances.Count == 4096, "Flat grass coverage changed.");
        for (int i = 1; i < instances.Count; i++)
            Require(instances[i - 1].selectionRank <= instances[i].selectionRank, "Rank ordering changed.");
        var repeated = CreateRecord();
        FoliageGenerator.GenerateBillboardGrassForChunk(repeated, new GrassSettings { cellsPerAxis = 64 },
            null, null, 1234, 16, 1f, 10f);
        for (int i = 0; i < instances.Count; i++)
            Require(instances[i].localPosition == repeated.FoliageData.billboardGrassInstances[i].localPosition,
                "Slice boundaries changed deterministic placement.");

        var empty = CreateRecord();
        Set(empty, "surfaceTypeMap", new SurfaceType[19, 19]);
        using (var job = Schedule(empty))
        {
            job.CompleteForSynchronousCaller();
            while (!job.ApplySlice(1)) { }
            Require(empty.FoliageData.billboardGenerated && empty.FoliageData.billboardGrassInstances.Count == 0,
                "Empty discovery did not publish a valid empty result.");
        }
    }

    private static void ValidateInvalidation()
    {
        var record = CreateRecord();
        using (var job = Schedule(record))
        {
            record.FoliageData.ClearBillboards();
            job.CompleteForSynchronousCaller();
            Require(job.ApplySlice(1) && !record.FoliageData.billboardGenerated, "Cleared data was repopulated by stale work.");
        }
        using (var job = Schedule(record))
        {
            record.FoliageData = new ChunkFoliageData();
            job.CompleteForSynchronousCaller();
            Require(job.ApplySlice(1) && !record.FoliageData.billboardGenerated, "Replaced data was overwritten.");
        }
        var cancelled = Schedule(record);
        cancelled.Dispose();
        cancelled.Dispose();
        Require(!record.FoliageData.billboardGenerated, "Cancellation published results.");
    }

    private static void ValidateBatches(float density)
    {
        var record = CreateRecord();
        FoliageGenerator.GenerateBillboardGrassForChunk(record, new GrassSettings { cellsPerAxis = 64 },
            null, null, 1234, 16, 1f, 10f);
        var manager = (FoliageManager)FormatterServices.GetUninitializedObject(typeof(FoliageManager));
        Set(manager, "chunkSize", 16);
        Set(manager, "worldScale", 1f);
        Set(manager, "worldSeed", 1234);
        var runtime = new ChunkFoliageRuntime();
        var matrix = Matrix4x4.TRS(new Vector3(50, 2, -30), Quaternion.Euler(0, 15, 0), Vector3.one);
        var workType = typeof(FoliageManager).GetNestedType("BillboardBatchWork", BindingFlags.NonPublic);
        object work = Activator.CreateInstance(workType);
        foreach (var pair in new Dictionary<string, object> {
            { "Record", record }, { "Data", record.FoliageData }, { "FoliageRuntime", runtime },
            { "CellsPerAxis", 4 }, { "Density", density }, { "LocalToWorld", matrix } })
            workType.GetField(pair.Key).SetValue(work, pair.Value);
        var steps = (IEnumerator<bool>)typeof(FoliageManager)
            .GetMethod("RebuildBillboardMatricesIncrementally", PrivateInstance).Invoke(manager, new[] { work });
        int slices = 0;
        using (steps)
            while (steps.MoveNext())
            {
                slices++;
                Require(((List<GrassRenderBatch>)Get(runtime, "billboardRenderBatches")).Count == 0,
                    "Partial batches escaped staging.");
            }
        Require(slices > 1, "Batch construction was not sliced.");

        // Independent reference selection: group by cell, sort, then take the density prefix.
        var expected = new List<BillboardFoliageInstanceData>();
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
            {
                var bucket = record.FoliageData.billboardGrassInstances.FindAll(instance =>
                    Mathf.Clamp(Mathf.FloorToInt((instance.localPosition.x + 8f) / 4f), 0, 3) == x &&
                    Mathf.Clamp(Mathf.FloorToInt((instance.localPosition.z + 8f) / 4f), 0, 3) == z);
                bucket.Sort((a, b) => a.selectionRank.CompareTo(b.selectionRank));
                int count = (int)typeof(FoliageManager).GetMethod("GetBillboardGrassRenderCount", PrivateInstance)
                    .Invoke(manager, new object[] { bucket.Count, density, record.ChunkCoord, x, z });
                expected.AddRange(bucket.GetRange(0, count));
            }
        var batches = (List<GrassRenderBatch>)Get(runtime, "billboardRenderBatches");
        Require(batches.Count == (expected.Count + 1022) / 1023, "Batch packing increased draw calls.");
        int index = 0;
        foreach (var batch in batches)
            for (int i = 0; i < batch.matrices.Length; i++, index++)
            {
                var source = expected[index];
                Require(batch.matrices[i] == matrix * Matrix4x4.TRS(source.localPosition, source.localRotation, source.localScale),
                    "Instance transform changed.");
                Require(batch.instanceData[i] == new Vector4(source.forestBlend,
                    (source.selectionRank & 0x00FFFFFFu) / 16777216f, 0, 0), "Instance tint/phase changed.");
            }
        Require(index == expected.Count, "Selected instance count changed.");
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, PrivateInstance).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, PrivateInstance).SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
