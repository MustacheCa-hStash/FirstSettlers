using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TerrainOwnershipValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static MeshData Triangle() => new(new[] { Vector3.zero, Vector3.right, Vector3.forward },
        new[] { Vector3.up, Vector3.up, Vector3.up }, new Vector2[3], new Color[3], new[] { 0, 1, 2 });
    private static TerrainRequestManager Requests() => new(2, 1, 1, 1, new TerrainWaterSettings(.24f, 200, 1));
    private static ChunkRecord.NativeTerrainData Native(int n = 5) => new(new float[n, n], new float[n, n],
        new BiomeType[n, n], new SurfaceType[n, n], new WaterState[n, n], new GroundCoverType[n, n], new float[n, n]);
    private static TerrainDataRequestResult Data(ChunkCoord coord, int version, ControlMapPixelData raw,
        ChunkRecord.NativeTerrainData native) => new(coord, version, new float[5, 5], new float[5, 5],
            new float[5, 5], new float[5, 5], new BiomeType[5, 5], new SurfaceType[5, 5], new WaterState[5, 5],
            new GroundCoverType[5, 5], new WorldFeaturePlan(5, 5), new float[5, 5], raw, native);
    private static TerrainResultPublisher Publisher(TerrainRequestManager requests, ChunkRecord record,
        Action<ChunkCoord> wake = null, FarTerrainTileRecord tile = null) => new(requests,
            new TerrainPublicationBudget { TerrainDataCount = 8, LodMeshCount = 8, ColliderCount = 8, FarTerrainCount = 8 },
            c => c == record.ChunkCoord ? record : null, p => tile, r => true, wake ?? (_ => { }), _ => { });

    [MenuItem("Tools/Terrain/Validate Terrain Ownership Cleanup")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before running the isolated ownership fixture.");
        ValidateAssetOwnership(); ValidateNativeReaders(); ValidateRetirement();
        ValidatePublicationFailure(); ValidateFailuresAndShutdown(); ValidateRunningShutdown();
        Debug.Log("TERRAIN OWNERSHIP PASS: generated-asset replacement/aliasing/disposal, native reader leases, guarded retirement, retained tree placement/state, recreated-record rejection, publication failure cleanup and all-category shutdown/late completion.");
    }
    public static void RunBatch()
    {
        try
        {
            Run(); TerrainControlMapScratchValidation.Run(); WorldStreamingSchedulingValidation.Run();
            TerrainWorkerLifetimeValidation.RunBatch();
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateAssetOwnership()
    {
        for (int iteration = 0; iteration < 12; iteration++)
        {
            using var record = new ChunkRecord(default);
            var shared = Triangle().CreateMesh(); var replacement = Triangle().CreateMesh();
            record.StoreLODTerrainMesh(0, shared); record.StoreLODWaterMesh(0, shared);
            Check(record.TryCompleteColliderRequest(record.BeginColliderRequest(), shared), "Collider fixture rejected.");
            record.StoreLODTerrainMesh(0, replacement);
            Check(shared != null && replacement != null, "Replacing one slot destroyed another slot's asset.");
            record.ClearAllLODMeshes();
            Check(replacement == null && shared != null, "LOD release failed or destroyed a live collider alias.");
            record.ClearColliderMesh(); Check(shared == null, "Last asset slot was not released.");
            var a = Triangle().CreateMesh(); var b = Triangle().CreateMesh(); var texture = new Texture2D(2, 2);
            record.TryCompleteFarTerrainRequest(record.BeginFarTerrainRequest(), a, new[] { texture, texture }, b, new float[2, 2]);
            record.TryCompleteFarTerrainRequest(record.BeginFarTerrainRequest(), b, new[] { texture }, a);
            Check(a != null && b != null && texture != null, "Asset swaps destroyed still-owned objects.");
            record.Dispose(); record.Dispose();
            Check(a == null && b == null && texture == null && !record.HasFarTerrain && record.FarTreeHeightGrid == null,
                "Record teardown retained generated assets.");
        }
        using var tile = new FarTerrainTileRecord(new FarTerrainPatchKey(default, 4));
        var oldMesh = Triangle().CreateMesh(); var oldTexture = new Texture2D(2, 2);
        tile.TryCompleteRequest(tile.BeginRequest(), oldMesh, new[] { oldTexture }, oldMesh);
        var nextMesh = Triangle().CreateMesh(); var nextTexture = new Texture2D(2, 2);
        tile.TryCompleteRequest(tile.BeginRequest(), nextMesh, new[] { nextTexture }, heightGrid: new float[2, 2]);
        Check(oldMesh == null && oldTexture == null && nextMesh != null, "Far replacement retained obsolete assets.");
        tile.Dispose(); tile.Dispose();
        Check(nextMesh == null && nextTexture == null && tile.FarTreeHeightGrid == null, "Far teardown retained assets.");
    }

    private static void ValidateNativeReaders()
    {
        var native = Native(); var lease = native.AcquireLease();
        native.Dispose();
        Check(native.HeightMap.IsCreated && native.AcquireLease() == null, "Native owner release invalidated an active reader or accepted a new one.");
        Check(lease.Data.HeightMap[0] == 0, "Active reader lost its data.");
        lease.Dispose(); lease.Dispose(); native.Dispose();
        Check(!native.HeightMap.IsCreated && !native.ForestMembershipMap.IsCreated &&
            Field<float[,]>(native, "sourceHeightMap") == null, "Last-reader disposal retained native storage or managed aliases.");
    }

    private static void ValidateRetirement()
    {
        var root = new GameObject("Ownership fixture");
        var config = new WorldConfiguration { Scene = new WorldSceneReferences { Viewer = root.transform, ChunkParent = root.transform } };
        var manager = new ChunkManager(config);
        try
        {
            var coord = new ChunkCoord(40, -40); var record = new ChunkRecord(coord);
            Field<Dictionary<ChunkCoord, ChunkRecord>>(manager, "chunkRecords").Add(coord, record);
            var native = Native(); var requests = Field<TerrainRequestManager>(manager, "terrainRequestManager");
            var data = Data(coord, record.BeginTerrainDataRequest(), new ControlMapPixelData(3, 3, 3), native);
            Field<Queue<TerrainDataRequestResult>>(requests, "completedTerrainDataResults").Enqueue(data);
            Publisher(requests, record).Update();
            var textures = record.ControlMapData; record.FoliageData = new ChunkFoliageData();
            var tree = new TreeInstanceData(Vector3.zero, Quaternion.identity, Vector3.one, WorldFeatureVariant.SpruceTree,
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                id: TreeId.Generated(config.Generation.Seed, coord, TreePlacementSource.Forest, 1));
            manager.Trees.RegisterChunk(coord, new[] { tree }, TreePlacementDetail.Detailed);
            manager.Trees.TrySetState(tree.id, TreeState.Cut);
            var active = Field<HashSet<ChunkCoord>>(manager, "activeLastUpdate"); active.Add(coord);
            Check(!manager.TryReleaseInactiveChunkData(coord), "Desired coverage was retired."); active.Remove(coord);
            var loaded = Field<Dictionary<ChunkCoord, ChunkRuntime>>(manager, "loadedChunks"); loaded.Add(coord, null);
            Check(!manager.TryReleaseInactiveChunkData(coord), "Attached/handoff coverage was retired."); loaded.Remove(coord);
            var foliage = Field<FoliageManager>(manager, "foliageManager");
            var discovery = Field<FoliageDiscoveryScheduler>(foliage, "discoveryScheduler");
            Set(discovery, "activeGroundRecord", record);
            Check(!manager.TryReleaseInactiveChunkData(coord), "An active foliage reader was retired.");
            Set(discovery, "activeGroundRecord", null);
            int oldVersion = record.BeginMeshRequest(0);
            Field<Queue<MeshRequestResult>>(requests, "completedMeshResults").Enqueue(new(coord, 0, oldVersion, Triangle(), new WaterMeshData(0)));
            Check(manager.TryReleaseInactiveChunkData(coord), "Unused chunk was not retired.");
            Check(manager.GetChunkRecord(coord) == null && record.HeightMap == null && record.FoliageData == null &&
                record.WorldFeaturePlan == null && !native.HeightMap.IsCreated && requests.CompletedMeshResultCount == 0,
                "Retirement left terrain references or queued results.");
            foreach (var texture in textures) Check(texture == null, "Retirement retained a control texture.");
            Check(manager.Trees.ContainsChunk(coord) && manager.Trees.TryGet(tree.id, out var retained) && retained.State == TreeState.Cut,
                "Retirement unloaded tree placement/state despite its deferred scope.");
            using var recreated = new ChunkRecord(coord);
            int newVersion = recreated.BeginMeshRequest(0);
            Check(newVersion != oldVersion && !recreated.IsMeshRequestCurrent(0, oldVersion), "Recreated record accepted an old token.");
            int newDataVersion = recreated.BeginTerrainDataRequest(); var lateNative = Native();
            Field<Queue<TerrainDataRequestResult>>(requests, "completedTerrainDataResults").Enqueue(
                Data(coord, data.RequestVersion, new ControlMapPixelData(3, 3, 3), lateNative));
            Publisher(requests, recreated).Update();
            Check(!lateNative.HeightMap.IsCreated && recreated.HeightMap == null && recreated.IsTerrainDataRequestCurrent(newDataVersion),
                "Late data revived an evicted chunk, leaked outputs or cancelled its replacement.");
            var patch = new FarTerrainPatchKey(new ChunkCoord(30, 30), 4);
            var far = new FarTerrainTileRecord(patch); var farMesh = Triangle().CreateMesh();
            far.TryCompleteRequest(far.BeginRequest(), farMesh, new[] { new Texture2D(2, 2) });
            Field<Dictionary<FarTerrainPatchKey, FarTerrainTileRecord>>(manager, "farTerrainTileRecords").Add(patch, far);
            Check(manager.TryReleaseInactiveFarTileData(patch) && farMesh == null, "Inactive far data was not released.");
        }
        finally { manager.Dispose(); manager.Dispose(); Object.DestroyImmediate(root); }
    }

    private static void ValidatePublicationFailure()
    {
        using var requests = Requests(); using var record = new ChunkRecord(default);
        var raw = new ControlMapPixelData(3, 3, 3); raw.Maps[1] = null;
        var native = Native(); int version = record.BeginTerrainDataRequest();
        Field<Queue<TerrainDataRequestResult>>(requests, "completedTerrainDataResults").Enqueue(Data(default, version, raw, native));
        int textureCount = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
        try { Publisher(requests, record).Update(); throw new Exception("Malformed controls unexpectedly succeeded."); }
        catch (ArgumentException) { }
        Check(Resources.FindObjectsOfTypeAll<Texture2D>().Length == textureCount && !native.HeightMap.IsCreated &&
            !record.IsTerrainDataRequestInFlight, "Partial texture publication leaked assets/native output or pinned the request.");
        int meshCount = Resources.FindObjectsOfTypeAll<Mesh>().Length;
        version = record.BeginMeshRequest(0);
        Field<Queue<MeshRequestResult>>(requests, "completedMeshResults").Enqueue(new(default, 0, version, Triangle(), null));
        try { Publisher(requests, record).Update(); throw new Exception("Malformed water mesh unexpectedly succeeded."); }
        catch (NullReferenceException) { }
        Check(Resources.FindObjectsOfTypeAll<Mesh>().Length == meshCount && !record.IsMeshRequestInFlight(0),
            "Publication failure leaked the already-created terrain mesh.");
        using var far = new FarTerrainTileRecord(new FarTerrainPatchKey(default, 4));
        version = far.BeginRequest();
        Field<Queue<FarTerrainRequestResult>>(requests, "completedFarTerrainResults").Enqueue(
            new(default, version, true, 4, null, new ControlMapPixelData(3, 3, 3)));
        try { Publisher(requests, record, tile: far).Update(); throw new Exception("Malformed far mesh unexpectedly succeeded."); }
        catch (NullReferenceException) { }
        Check(Resources.FindObjectsOfTypeAll<Texture2D>().Length == textureCount && !far.IsRequestInFlight,
            "Far publication failure retained textures or pinned the request.");
        version = record.BeginMeshRequest(0);
        Field<Queue<MeshRequestResult>>(requests, "completedMeshResults").Enqueue(new(default, 0, version, Triangle(), new WaterMeshData(0)));
        try { Publisher(requests, record, _ => throw new InvalidOperationException("wake fixture")).Update(); }
        catch (InvalidOperationException) { }
        Check(record.TryGetLODTerrainMesh(0, out var accepted) && accepted != null,
            "A wakeup exception destroyed assets after ownership transfer.");
    }

    private static void ValidateFailuresAndShutdown()
    {
        using var requests = Requests(); using var record = new ChunkRecord(default);
        var report = typeof(TerrainRequestManager).GetMethod("ReportFailure", Private);
        var kind = report.GetParameters()[0].ParameterType;
        int stale = record.BeginMeshRequest(0), current = record.BeginMeshRequest(0);
        report.Invoke(requests, new object[] { Enum.ToObject(kind, 2), default(ChunkCoord), stale, 0, 1, false });
        Publisher(requests, record).Update(); Check(record.IsMeshRequestCurrent(0, current), "Stale failure cancelled a newer request.");
        report.Invoke(requests, new object[] { Enum.ToObject(kind, 2), default(ChunkCoord), current, 0, 1, false });
        Publisher(requests, record).Update(); Check(!record.IsMeshRequestInFlight(0), "Worker failure pinned the request.");
        var native = Native();
        Field<Queue<TerrainDataRequestResult>>(requests, "completedTerrainDataResults").Enqueue(Data(default, 1, null, native));
        Field<Queue<MeshRequestResult>>(requests, "completedMeshResults").Enqueue(new(default, 0, 1, Triangle(), null));
        Field<Queue<ColliderRequestResult>>(requests, "completedColliderResults").Enqueue(new(default, 1, Triangle()));
        Field<Queue<FarTerrainRequestResult>>(requests, "completedFarTerrainResults").Enqueue(new(default, 1, true, 4, Triangle(), null));
        requests.Dispose(); requests.Dispose();
        Check(!native.HeightMap.IsCreated && requests.CompletedTerrainDataResultCount == 0 && requests.CompletedMeshResultCount == 0 &&
            requests.CompletedFarTerrainResultCount == 0 && requests.CompletedColliderResultCount == 0, "Shutdown left completed results.");
        Check(!requests.RequestTerrainData(default, 1, 32, 12345, 50, 3, .5f, 2, WorldFeatureGenerationSettings.Default) &&
            !requests.RequestFarTerrainData(default, 1, 32, 12345, 50, 200, 1, 9, 9, 6) &&
            !requests.RequestLODMesh(default, 0, 1, null, 200, 1, 1) &&
            !requests.RequestColliderMesh(default, 1, null, 200, 1), "Closed manager accepted work.");
    }

    private static void ValidateRunningShutdown()
    {
        using var requests = Requests(); var native = Native(35);
        try
        {
            Check(requests.RequestTerrainData(default, 1, 32, 12345, 50, 3, .5f, 2, WorldFeatureGenerationSettings.Default), "Terrain dispatch failed.");
            Check(requests.RequestFarTerrainData(default, 1, 32, 12345, 50, 200, 1, 9, 9, 6), "Far dispatch failed.");
            Check(requests.RequestLODMesh(default, 0, 1, native, 200, 1, 1), "LOD dispatch failed.");
            Check(requests.RequestColliderMesh(default, 1, new float[35, 35], 200, 1), "Collider dispatch failed.");
            requests.Dispose(); native.Dispose();
            requests.WaitForActiveRequestsToFinish(20000);
            Check(requests.ActiveTerrainDataJobCount + requests.ActiveFarTerrainJobCount + requests.ActiveMeshJobCount + requests.ActiveColliderJobCount == 0,
                "Shutdown workers failed to finish.");
            Check(requests.CompletedTerrainDataResultCount + requests.CompletedFarTerrainResultCount + requests.CompletedMeshResultCount +
                requests.CompletedColliderResultCount == 0 && !native.HeightMap.IsCreated, "Late completion revived closed queues or retained a lease.");
        }
        finally { requests.Dispose(); requests.WaitForActiveRequestsToFinish(20000); native.Dispose(); }
    }
}
