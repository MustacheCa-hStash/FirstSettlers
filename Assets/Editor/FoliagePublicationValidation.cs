using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FoliagePublicationValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
    [MenuItem("Tools/Terrain/Validate Incremental Foliage Publication")]
    public static void Run()
    {
        foreach (var kind in new[] { FoliagePublicationKind.Flower, FoliagePublicationKind.Clover,
            FoliagePublicationKind.Dandelion, FoliagePublicationKind.LilyPad, FoliagePublicationKind.Cattail }) ValidateGround(kind);
        ValidateInvalidation(); ValidateObjects(); ValidateQueue();
        Debug.Log("FOLIAGE PUBLICATION PASS: sliced final batches, atomic replacement, empty completion, transforms/variation, invalidation, inactive object staging, cancellation and exhausted-budget progress.");
    }
    private sealed class Fixture : IDisposable
    {
        public readonly ChunkRecord Record = BillboardGrassStreamingValidation.CreateRecord();
        public readonly GameObject Root = new("Publication fixture");
        public readonly ChunkRuntime Runtime = (ChunkRuntime)FormatterServices.GetUninitializedObject(typeof(ChunkRuntime));
        public readonly ChunkFoliageRuntime Target;
        public Fixture()
        {
            Set(Runtime, "root", Root); Target = new ChunkFoliageRuntime { root = Root.transform };
            Runtime.FoliageRuntime = Target; Record.SetActiveRuntime(Runtime);
            Record.FoliageData = new ChunkFoliageData { flowersGenerated = true, cloverGenerated = true,
                dandelionsGenerated = true, lilyPadsGenerated = true, cattailsGenerated = true, bushesGenerated = true, rocksGenerated = true };
            Root.transform.position = new Vector3(11, 2, -9);
        }
        public FoliagePublication Work(FoliagePublicationKind kind) => new(Record, Runtime, kind, 3, Matrix4x4.Translate(Vector3.up));
        public void Dispose() { Target.ClearCachedBatches(); Object.DestroyImmediate(Root); Record.Dispose(); }
    }
    private static void Populate(ChunkFoliageData data, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var p = new Vector3(i * .01f, i % 7, 4);
            var q = Quaternion.Euler(0, i % 360, 0); var scale = Vector3.one * 1.3f;
            data.flowerInstances.Add(new FlowerInstanceData(p,q,scale,new Color32(71,129,210,255),i % 3 == 1,i % 3 == 2));
            data.cloverInstances.Add(new CloverInstanceData(p,q,scale,(uint)i * 156719u,1,i % 3));
            data.dandelionInstances.Add(new DandelionInstanceData(p,q,scale,(uint)i * 156719u));
            data.lilyPadInstances.Add(new LilyPadInstanceData(p,q,1.3f));
            data.cattailInstances.Add(new CattailInstanceData(p,q,1.3f));
        }
    }
    private static void Drain(FoliagePublication work)
    {
        int steps = 0;
        while (!work.Advance()) { Check(work.LastSliceInstances <= 128, "Unbounded transform slice."); Check(++steps < 10000, "Publication failed to finish."); }
        Check(work.Published, "Current work was discarded.");
    }
    private static void ValidateGround(FoliagePublicationKind kind)
    {
        using var f = new Fixture(); Populate(f.Record.FoliageData, 4000);
        if (kind == FoliagePublicationKind.Flower)
            f.Target.CacheFlowerBatches(new[] { Matrix4x4.identity }, new[] { Vector4.one });
        using var work = f.Work(kind);
        Check(!work.Advance() && !work.Published, "Publication completed synchronously.");
        if (kind == FoliagePublicationKind.Flower) Check(f.Target.GpuFlowerInstanceCount == 1, "Replacement erased displayed batches before completion.");
        Drain(work);
        string field = kind == FoliagePublicationKind.Flower ? "flowerRenderBatches" : kind == FoliagePublicationKind.Clover ? "cloverRenderBatches" :
            kind == FoliagePublicationKind.Dandelion ? "dandelionRenderBatches" : kind == FoliagePublicationKind.LilyPad ? "lilyPadRenderBatches" : "cattailRenderBatches";
        var batches = Field<IList>(f.Target, field); int total = 0; var perGroup = new int[3];
        foreach (var b in batches)
        {
            Matrix4x4[] matrices; Vector4[] values = null; int group = 0;
            if (b is FlowerRenderBatch flower) { matrices = flower.matrices; values = flower.petalColors; group = flower.isDaisyWeed ? 2 : flower.isTallFlower ? 1 : 0; }
            else if (b is CloverRenderBatch clover) { matrices = clover.matrices; values = clover.instanceData; group = clover.prefabIndex; }
            else if (b is GrassRenderBatch dandelion) { matrices = dandelion.matrices; values = dandelion.instanceData; }
            else matrices = (Matrix4x4[])b;
            Check(matrices.Length > 0 && matrices.Length <= 1023, "Final batch capacity changed.");
            for (int j = 0; j < matrices.Length; j++)
            {
                int i = kind == FoliagePublicationKind.Flower || kind == FoliagePublicationKind.Clover ? perGroup[group]++ * 3 + group : total;
                var expected = f.Root.transform.localToWorldMatrix * Matrix4x4.TRS(new Vector3(i * .01f, i % 7, 4), Quaternion.Euler(0,i % 360,0),Vector3.one * 1.3f);
                if (kind == FoliagePublicationKind.LilyPad || kind == FoliagePublicationKind.Cattail) expected *= Matrix4x4.Translate(Vector3.up);
                for (int axis = 0; axis < 16; axis++) Check(Mathf.Abs(matrices[j][axis] - expected[axis]) < .0001f, "Transform or grouped ordering changed.");
                if (kind == FoliagePublicationKind.Flower) Check(values[j] == new Vector4(71,129,210,255)/255f, "Petal color changed.");
                if (kind == FoliagePublicationKind.Clover || kind == FoliagePublicationKind.Dandelion)
                {
                    float phase = ((uint)i * 156719u & 0xffffffu) / 16777216f;
                    Check(Mathf.Abs(values[j].x-phase)<.00001f && Mathf.Abs(values[j].y-Mathf.Repeat(phase*37.618034f,1))<.00001f, "Instance variation changed.");
                }
                total++;
            }
        }
        Check(total == 4000, "Publication lost or duplicated instances.");
        f.Record.FoliageData = new ChunkFoliageData { flowersGenerated=true,cloverGenerated=true,dandelionsGenerated=true,lilyPadsGenerated=true,cattailsGenerated=true };
        using var empty = f.Work(kind); Drain(empty);
        Check(Field<IList>(f.Target,field).Count == 0, "Empty replacement left stale batches.");
    }
    private static void ValidateInvalidation()
    {
        foreach (int mutation in new[] { 0,1,2,3,4,5 })
        {
            using var f = new Fixture(); Populate(f.Record.FoliageData,400);
            using var work = f.Work(FoliagePublicationKind.Flower); work.Advance();
            switch (mutation)
            {
                case 0: f.Record.FoliageData.ClearFlowers(); break;
                case 1: f.Record.FoliageData = new ChunkFoliageData(); break;
                case 2: f.Target.ClearCachedBatches(); break;
                case 3: f.Root.transform.position += Vector3.right; break;
                case 4: Set(f.Record,"heightMap",new float[19,19]); break;
                case 5: f.Record.FoliageData.flowerInstances = new List<FlowerInstanceData>(f.Record.FoliageData.flowerInstances); break;
            }
            Check(work.Advance() && !work.Published && f.Target.GpuFlowerInstanceCount == 0, "Invalidation published stale work.");
        }
    }
    private static void ValidateObjects()
    {
        using var f = new Fixture(); var prefab = new GameObject("Synthetic rock"); prefab.AddComponent<BoxCollider>();
        f.Target.forestRockPrefabs = new[] { prefab }; f.Target.SetVisible(true);
        try
        {
            for (int i = 0; i < 30; i++) f.Record.FoliageData.rockInstances.Add(new RockInstanceData(new Vector3(i,0,0),Quaternion.identity,Vector3.one,WorldFeatureVariant.Boulder,0));
            using (var work = f.Work(FoliagePublicationKind.Rock))
            {
                work.Advance(); work.Advance();
                Check(f.Root.GetComponentsInChildren<BoxCollider>(true).Length == 1 && f.Root.GetComponentsInChildren<BoxCollider>().Length == 0,
                    "Object staging activated incomplete collision or built more than one object.");
                f.Record.FoliageData.ClearRocks(); Check(work.Advance() && !work.Published, "Changed rocks published stale objects.");
            }
            Check(f.Root.GetComponentsInChildren<BoxCollider>(true).Length == 0, "Cancellation leaked staging objects.");
            for (int i = 0; i < 30; i++) f.Record.FoliageData.rockInstances.Add(new RockInstanceData(new Vector3(i,0,0),Quaternion.identity,Vector3.one,WorldFeatureVariant.Boulder,0));
            f.Record.FoliageData.rocksGenerated = true;
            using var complete = f.Work(FoliagePublicationKind.Rock); Drain(complete);
            Check(f.Root.GetComponentsInChildren<BoxCollider>().Length == 30 && f.Target.HasCurrentRockRepresentation(), "Completed objects were not activated.");
        }
        finally { Object.DestroyImmediate(prefab); }
    }
    private static void ValidateQueue()
    {
        using var f = new Fixture(); Populate(f.Record.FoliageData,400);
        // Production queue validation also checks deduplication while its active work spans frames.
        Set(f.Record,"slopeMap",new float[19,19]); Set(f.Record,"moistureMap",new float[19,19]); Set(f.Record,"temperatureMap",new float[19,19]);
        Set(f.Record,"waterStateMap",new WaterState[19,19]); Set(f.Record,"riverMaskMap",new float[19,19]);
        Set(f.Record,"worldFeaturePlan",new WorldFeaturePlan(19,19)); Set(f.Record,"controlMapData",Array.Empty<Texture2D>());
        var manager = (ChunkManager)FormatterServices.GetUninitializedObject(typeof(ChunkManager));
        Set(manager,"chunkRecords",new Dictionary<ChunkCoord,ChunkRecord>{{f.Record.ChunkCoord,f.Record}});
        Set(manager,"loadedChunks",new Dictionary<ChunkCoord,ChunkRuntime>{{f.Record.ChunkCoord,f.Runtime}});
        var mesh = new Mesh(); var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        var prefab = new GameObject("Synthetic publication asset");
        prefab.AddComponent<MeshFilter>().sharedMesh = mesh; prefab.AddComponent<MeshRenderer>().sharedMaterial = material;
        var foliage = new FoliageManager(new WorldGenerationConfiguration { Seed = 1234, ChunkSize = 16, WorldScale = 1, MeshHeightMultiplier = 10, Water = new TerrainWaterSettings(2.4f,10,1) },
            new WorldFoliageConfiguration { Grass = new GrassSettings { grassPrefab= prefab, billboardGrassPrefab=prefab }, Flowers = new FlowerSettings { flowerPrefab=prefab }, LilyPads = null, Cattails = null, Clover = null, Dandelions = null, Trees = new TreeSettings() });
        try
        {
            var type = typeof(FoliagePublicationKind);
            var enqueue = typeof(FoliageManager).GetMethod("EnqueueFoliageBatchRebuild",Private);
            var process = typeof(FoliageManager).GetMethod("ProcessPendingFoliageBatchWork",Private);
            for (int step = 0; step < 50 && f.Target.GpuFlowerInstanceCount != 400; step++)
            {
                enqueue.Invoke(foliage,new[]{(object)f.Record,Enum.Parse(type,"Flower")});
                process.Invoke(foliage,new object[]{manager,f.Record.ChunkCoord,default(SubChunkCoord),0L,.001f});
                Check(Field<FoliagePublicationScheduler>(foliage,"publicationScheduler").PendingCount == 0,"Repeated requests duplicated active publication.");
            }
            Check(f.Target.GpuFlowerInstanceCount == 400,"Publication starved under an exhausted shared budget.");
        }
        finally { foliage.Dispose(); Object.DestroyImmediate(prefab); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
    }
}
