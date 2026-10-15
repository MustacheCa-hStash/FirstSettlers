using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class WorldStreamingSchedulingValidation
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    private static MeshData Triangle() => new MeshData(new[] { Vector3.zero, Vector3.right, Vector3.forward },
        new[] { Vector3.up, Vector3.up, Vector3.up }, new Vector2[3], new Color[3], new[] { 0, 1, 2 });

    [MenuItem("Tools/Terrain/Validate Shared Streaming Scheduling (synthetic)")]
    public static void Run()
    {
        ValidateBudget(); ValidateColliderPriority(); ValidateBackpressure(); ValidateConfiguration();
        ValidateTextureSubmissions(1); ValidateTextureSubmissions(2); ValidateTextureSubmissionFailure();
        WorldArchitectureValidation.Run(); FoliagePublicationValidation.ValidateSharedPublication();
        GrassStreamingValidation.Run(); GrassStreamingValidation.ValidateSharedStreaming(); WorldManagerInspectorValidation.Run();
        Debug.Log("SHARED STREAMING PASS: aggregate accounting, reservations/refunds, overrun recovery, exception cleanup, collider priority, stale preflight, backlog backpressure, shared near/far texture submission caps, deferred ownership/shutdown, configuration and architecture/foliage/grass/inspector regressions.");
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    private static void ValidateBudget()
    {
        long now = 0;
        var budget = new WorldStreamingBudget(WorldStreamingConfiguration.Default, () => now, .01);
        budget.BeginFrame();
        now += 900; // Unmeasured rendering/simulation must not consume streaming time.
        using (budget.Measure(WorldStreamingPhase.Results))
        {
            Check(Math.Abs(budget.RemainingMs - 1) < .0001, "Later phases were not reserved.");
            now += 100;
            Check(!budget.HasRemaining, "Publication started beyond its share.");
        }
        using (budget.Measure(WorldStreamingPhase.Content))
        {
            Check(Math.Abs(budget.RemainingMs - .5) < .0001, "Content reservation was not available.");
            now += 20;
        }
        using (budget.Measure(WorldStreamingPhase.Foliage))
        {
            Check(Math.Abs(budget.RemainingMs - .65) < .0001, "Unused content capacity did not return to the pool.");
            now += 15;
        }
        using (budget.Measure(WorldStreamingPhase.Grass))
        {
            Check(Math.Abs(budget.RemainingMs - .65) < .0001, "Grass reservation/refund incorrect.");
            now += 100; // Indivisible operation overrun.
            Check(!budget.HasRemaining, "Overrun allowed additional work.");
        }
        budget.EndFrame();
        Check(Math.Abs(budget.LastFrameMs - 2.35) < .0001 && budget.OverrunFrames == 1, "Actual consumption was not reported.");
        budget.BeginFrame();
        try { using (budget.Measure(WorldStreamingPhase.Results)) { now += 10; throw new InvalidOperationException("fixture"); } }
        catch (InvalidOperationException) { }
        budget.EndFrame();
        Check(Math.Abs(budget.LastFrameMs - .1) < .0001, "Exception corrupted phase accounting.");
        budget.BeginFrame();
        using (budget.Measure(WorldStreamingPhase.Results)) Check(budget.HasRemaining, "Next frame did not recover.");
        budget.EndFrame();
        var tiny = WorldStreamingConfiguration.Default; tiny.TotalMs = .1f;
        tiny = tiny.Sanitized();
        Check(tiny.ContentReserveMs + tiny.FoliageReserveMs + tiny.GrassReserveMs <= .05001f, "Reservations prevent progress with a small allowance.");
        var disabled = new WorldStreamingBudget(default, () => now, .01);
        disabled.BeginFrame();
        using (disabled.Measure(WorldStreamingPhase.Results)) { now += 100000; Check(disabled.HasRemaining, "Zero should disable shared limiting."); }
        disabled.EndFrame();
    }
    private static void ValidateColliderPriority()
    {
        long now = 0;
        var shared = new WorldStreamingBudget(WorldStreamingConfiguration.Default, () => now, .01);
        using var requests = new TerrainRequestManager(1, 1, 1, 1, new TerrainWaterSettings(2.4f, 10, 1));
        var record = new ChunkRecord(default);
        var meshes = Field<Queue<MeshRequestResult>>(requests, "completedMeshResults");
        var colliders = Field<Queue<ColliderRequestResult>>(requests, "completedColliderResults");
        int stale = record.BeginMeshRequest(0), current = record.BeginMeshRequest(0);
        // A stale result contains unusable geometry: preflight must discard it without touching CreateMesh.
        meshes.Enqueue(new MeshRequestResult(default, 0, stale, null, null));
        meshes.Enqueue(new MeshRequestResult(default, 0, current, Triangle(), new WaterMeshData(0)));
        colliders.Enqueue(new ColliderRequestResult(default, record.BeginColliderRequest(), Triangle()));
        var category = new TerrainPublicationBudget { TerrainDataCount = 1, FarTerrainCount = 1, LodMeshCount = 1, ColliderCount = 1 };
        int wakes = 0;
        var publisher = new TerrainResultPublisher(requests, category, c => record, k => null, r => false,
            c => { wakes++; now += 150; }, k => { }, shared);
        try
        {
            shared.BeginFrame(); publisher.Update(); shared.EndFrame();
            Check(record.ColliderReady && meshes.Count == 2 && wakes == 1, "LOD publication overtook collision or ignored shared exhaustion.");
            shared.BeginFrame(); publisher.Update(); shared.EndFrame();
            Check(meshes.Count == 1 && !record.TryGetLODTerrainMesh(0, out _), "Stale preflight/count limit failed.");
            shared.BeginFrame(); publisher.Update(); shared.EndFrame();
            Check(meshes.Count == 0 && record.TryGetLODTerrainMesh(0, out _), "Deferred visual result was lost.");
        }
        finally
        {
            if (record.TryGetColliderMesh(out var collider)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (var mesh in Field<Dictionary<int, Mesh>>(record, "LODTerrainMeshes").Values) UnityEngine.Object.DestroyImmediate(mesh);
            foreach (var mesh in Field<Dictionary<int, Mesh>>(record, "LODWaterMeshes").Values) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            record.Dispose();
        }
    }
    private static void ValidateBackpressure()
    {
        using var requests = new TerrainRequestManager(1, 1, 1, 1, new TerrainWaterSettings(2.4f, 10, 1));
        var meshes = Field<Queue<MeshRequestResult>>(requests, "completedMeshResults");
        meshes.Enqueue(default); meshes.Enqueue(default);
        Check(!requests.RequestLODMesh(default, 0, 1, null, 10, 1, 1) && requests.ActiveMeshJobCount == 0,
            "Full completion backlog acquired inputs or started another worker.");
        meshes.Clear();
    }
    private static void ValidateConfiguration()
    {
        var root = new GameObject("Streaming configuration"); root.SetActive(false);
        try
        {
            var world = root.AddComponent<WorldManager>();
            using var serialized = new SerializedObject(world);
            serialized.FindProperty("streamingBudgetMsPerFrame").floatValue = 3;
            serialized.FindProperty("streamingContentReserveMs").floatValue = .6f;
            serialized.FindProperty("streamingFoliageReserveMs").floatValue = .4f;
            serialized.FindProperty("streamingGrassReserveMs").floatValue = .2f;
            serialized.FindProperty("maxControlMapSetsSubmittedPerFrame").intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var config = (WorldConfiguration)typeof(WorldManager).GetMethod("BuildConfiguration", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world, null);
            Check(config.Streaming.TotalMs == 3 && config.Streaming.ContentReserveMs == .6f &&
                config.Streaming.FoliageReserveMs == .4f && config.Streaming.GrassReserveMs == .2f, "Inspector shared budget values were not transported.");
            Check(config.Publication.ControlMapSetsPerFrame == 2 &&
                new TerrainPublicationBudget().Sanitized().ControlMapSetsPerFrame == 1,
                "Texture submission setting was lost or zero could prevent progress.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static TerrainDataRequestResult TextureData(ChunkRecord record, int version, ControlMapPixelData raw)
    {
        var h = new float[3, 3]; var s = new float[3, 3]; var b = new BiomeType[3, 3];
        var surface = new SurfaceType[3, 3]; var water = new WaterState[3, 3]; var ground = new GroundCoverType[3, 3];
        var river = new float[3, 3];
        var native = new ChunkRecord.NativeTerrainData(h, s, b, surface, water, ground, river);
        return new(record.ChunkCoord, version, h, s, new float[3, 3], new float[3, 3], b, surface, water,
            ground, new WorldFeaturePlan(3, 3), river, raw, native);
    }

    private static void ValidateTextureSubmissions(int limit)
    {
        int frame = 100;
        using var requests = new TerrainRequestManager(1, 1, 1, 1, new TerrainWaterSettings(2.4f, 10, 1));
        var records = new Dictionary<ChunkCoord, ChunkRecord>();
        using var far = new FarTerrainTileRecord(new FarTerrainPatchKey(new ChunkCoord(20, 20), 4));
        var dataQueue = Field<Queue<TerrainDataRequestResult>>(requests, "completedTerrainDataResults");
        var farQueue = Field<Queue<FarTerrainRequestResult>>(requests, "completedFarTerrainResults");
        var normal = new ChunkRecord[3]; var results = new TerrainDataRequestResult[3];
        var staleResults = new List<TerrainDataRequestResult>();
        try
        {
            for (int i = 0; i < normal.Length; i++)
            {
                normal[i] = new ChunkRecord(new ChunkCoord(i, 0)); records.Add(normal[i].ChunkCoord, normal[i]);
                if (i < 2)
                {
                    // Unusable stale maps must be discarded, including after the frame's slot is spent.
                    var stale = TextureData(normal[i], normal[i].BeginTerrainDataRequest(), new ControlMapPixelData(3, 3, 3));
                    stale.ControlMapsRawData.Maps[1] = null; dataQueue.Enqueue(stale);
                    staleResults.Add(stale);
                }
                if (i == 1)
                {
                    var empty = new ChunkRecord(new ChunkCoord(10, 0)); records.Add(empty.ChunkCoord, empty);
                    dataQueue.Enqueue(TextureData(empty, empty.BeginTerrainDataRequest(), null));
                }
                results[i] = TextureData(normal[i], normal[i].BeginTerrainDataRequest(), new ControlMapPixelData(3, 3, 3));
                dataQueue.Enqueue(results[i]);
            }
            int farVersion = far.BeginRequest();
            // Discard an unwanted far result without creating its unusable assets.
            farQueue.Enqueue(new(new ChunkCoord(90, 90), 0, true, 4, null, new ControlMapPixelData(3, 3, 3)));
            farQueue.Enqueue(new(far.TileCoord, farVersion, true, 4, Triangle(), new ControlMapPixelData(3, 3, 3)));
            Field<Queue<ColliderRequestResult>>(requests, "completedColliderResults").Enqueue(
                new(normal[0].ChunkCoord, normal[0].BeginColliderRequest(), Triangle()));
            var publisher = new TerrainResultPublisher(requests,
                new TerrainPublicationBudget { TerrainDataCount = 10, FarTerrainCount = 10, LodMeshCount = 10,
                    ColliderCount = 10, ControlMapSetsPerFrame = limit },
                c => records.TryGetValue(c, out var record) ? record : null, k => far,
                r => r.ChunkCoord == far.TileCoord && far.IsRequestCurrent(r.RequestVersion), _ => { }, _ => { },
                frameIndex: () => frame);

            publisher.Update();
            Check(publisher.SubmittedControlMapSets == limit && dataQueue.Count == 3 - limit &&
                records[new ChunkCoord(10, 0)].HeightMap != null && normal[0].ColliderReady && !far.HasTerrain,
                "Texture cap, empty-result progress, queue retention or collider priority failed.");
            foreach (var stale in staleResults) Check(stale.NativeData == null, "Stale result retained its native owner after the cap was spent.");
            for (int i = 0; i < 3; i++)
                Check(i < limit ? normal[i].ControlMapData?.Length == 3 && results[i].NativeData == null :
                    normal[i].ControlMapData == null && results[i].NativeData.HeightMap.IsCreated,
                    "Partial maps were exposed or deferred native ownership was transferred/disposed.");

            Field<Queue<MeshRequestResult>>(requests, "completedMeshResults").Enqueue(
                new(normal[0].ChunkCoord, 0, normal[0].BeginMeshRequest(0), Triangle(), new WaterMeshData(0)));
            publisher.Update(); // Same engine frame must not replenish the texture cap.
            Check(publisher.SubmittedControlMapSets == limit && dataQueue.Count == 3 - limit &&
                normal[0].TryGetLODTerrainMesh(0, out _), "Repeated Update reset the cap or deferred textures blocked mesh publication.");

            int completedFrames = 1;
            while (!far.HasTerrain && completedFrames++ < 6)
            {
                int before = 0; foreach (var record in normal) if (record.ControlMapData != null) before++;
                frame++; publisher.Update();
                int after = 0; foreach (var record in normal) if (record.ControlMapData != null) after++;
                Check(after - before + (far.HasTerrain ? 1 : 0) <= limit && publisher.SubmittedControlMapSets <= limit,
                    "Near and far publication spent separate texture allowances.");
            }
            Check(far.HasTerrain && far.ControlMapData.Length == 3 && dataQueue.Count == 0 && farQueue.Count == 0,
                "Deferred terrain publication did not converge or unwanted far data spent the allowance.");
            foreach (var record in normal) Check(record.ControlMapData.Length == 3, "A completed chunk lost one of its maps.");

            // Shut down with an unpublished result still queued behind the spent cap.
            Check(publisher.SubmittedControlMapSets == limit, "Shutdown fixture requires a spent submission cap.");
            var pending = new ChunkRecord(new ChunkCoord(30, 0)); records.Add(pending.ChunkCoord, pending);
            dataQueue.Enqueue(TextureData(pending, pending.BeginTerrainDataRequest(), new ControlMapPixelData(3, 3, 3)));
            publisher.Update();
            Check(dataQueue.Count == 1, "Shutdown fixture did not leave one deferred result.");
            var heldNative = dataQueue.Peek().NativeData;
            requests.Dispose();
            Check(!heldNative.HeightMap.IsCreated && requests.CompletedTerrainDataResultCount == 0,
                "Shutdown leaked a cap-deferred native result.");
        }
        finally { foreach (var record in records.Values) record.Dispose(); }
    }

    private static void ValidateTextureSubmissionFailure()
    {
        int frame = 200;
        using var requests = new TerrainRequestManager(1, 1, 1, 1, new TerrainWaterSettings(2.4f, 10, 1));
        using var broken = new ChunkRecord(default);
        using var good = new ChunkRecord(new ChunkCoord(1, 0));
        var queue = Field<Queue<TerrainDataRequestResult>>(requests, "completedTerrainDataResults");
        var bad = TextureData(broken, broken.BeginTerrainDataRequest(), new ControlMapPixelData(3, 3, 3));
        bad.ControlMapsRawData.Maps[1] = null; queue.Enqueue(bad);
        queue.Enqueue(TextureData(good, good.BeginTerrainDataRequest(), new ControlMapPixelData(3, 3, 3)));
        var publisher = new TerrainResultPublisher(requests,
            new TerrainPublicationBudget { TerrainDataCount = 8 }, c => c == default ? broken : good, k => null,
            r => false, _ => { }, _ => { }, frameIndex: () => frame);
        int textureCount = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
        try { publisher.Update(); throw new Exception("Malformed texture set unexpectedly succeeded."); }
        catch (ArgumentException) { }
        Check(!broken.IsTerrainDataRequestInFlight && bad.NativeData == null &&
            Resources.FindObjectsOfTypeAll<Texture2D>().Length == textureCount, "Failed texture submission retained partial assets or ownership.");
        publisher.Update();
        Check(queue.Count == 1 && good.ControlMapData == null, "Failure refunded an already submitted texture set in the same frame.");
        frame++; publisher.Update();
        Check(queue.Count == 0 && good.ControlMapData.Length == 3, "Texture submission did not recover on the next frame.");
    }
}
