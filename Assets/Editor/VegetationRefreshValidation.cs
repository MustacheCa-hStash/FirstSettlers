using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class VegetationRefreshValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    public static void RunBatch()
    {
        try
        {
            Directory.CreateDirectory(".utmp/vegetation-performance");
            ValidateRetainedSurfaces(); ValidateFlameBounds(); ValidateProtectionOrder(); ValidateTorchLodRefresh();
            DistantTreeGpuValidation.Run();
            LocalFlameValidation.Run();
            Debug.Log("VEGETATION_REFRESH_PASS: retained far support, preferred replacement, negative coordinates, unpublished/hidden rejection, empty tree publication, canopy removal, prepopulated registry and stable ordering parity; existing GPU suite passed.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void ValidateTorchLodRefresh()
    {
        var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        var record = new ChunkRecord(new ChunkCoord(0, 0));
        var runtime = new ChunkRuntime(record, 16, 1, null, material, material, false);
        var meshes = new List<Mesh>(); var sources = new List<LocalFlameSource>();
        var camera = new GameObject("Torch LOD camera").AddComponent<Camera>(); camera.enabled = false;
        camera.transform.position = new Vector3(8, 2, -5);
        var manager = LocalFlameLighting.Ensure(); manager.automaticRendering = false; manager.staticRefreshesPerFrame = 1;
        Mesh Terrain()
        {
            var mesh = new Mesh { vertices = new[] { new Vector3(-8, 0, -8), new Vector3(8, 0, -8), new Vector3(-8, 0, 8), new Vector3(8, 0, 8) }, triangles = new[] { 0, 2, 1, 1, 2, 3 } };
            mesh.RecalculateBounds(); meshes.Add(mesh); return mesh;
        }
        try
        {
            runtime.SetVisible(true); runtime.SetMeshes(Terrain(), null, 0);
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("LOD test torch " + i); go.transform.position = new Vector3(7 + i * .5f, 2, 8);
                var source = go.AddComponent<LocalFlameSource>(); source.range = 8; sources.Add(source); manager.Register(source);
            }
            for (int i = 0; i < 4; i++) manager.RenderForCamera(camera, .016f);
            Check(manager.ActiveLights == 4 && sources.All(s => manager.IsCached(s)), "Torch fixture did not settle");
            runtime.SetMeshes(Terrain(), null, 1);
            Check(sources.All(s => manager.IsCached(s)), "Visual terrain LOD change discarded valid torch shadows");
            manager.RenderForCamera(camera, .016f);
            Check(manager.ActiveLights == 4 && manager.StaticRefreshes == 1 && manager.PendingRefreshes == 3, "LOD refresh blinked lights or exceeded refresh budget");
            for (int i = 0; i < 3; i++) manager.RenderForCamera(camera, .016f);
            Check(manager.PendingRefreshes == 0, "LOD refresh queue did not drain");
            // A same-LOD mesh replacement can be a real geometry edit, and must
            // still suppress stale visibility immediately rather than leak light.
            runtime.SetMeshes(Terrain(), null, 1);
            Check(sources.All(s => !manager.IsCached(s)), "Real mesh replacement retained stale shadow visibility");
            manager.RenderForCamera(camera, .016f);
            Check(manager.ActiveLights == 1 && manager.PendingRefreshes == 3, "Real edit bypassed strict shadow invalidation");
            Debug.Log("TORCH_LOD_REFRESH_PASS: four cached torches remain lit with one refresh per frame; real geometry invalidates immediately.");
        }
        finally
        {
            foreach (var source in sources) { manager.Unregister(source); Object.DestroyImmediate(source.gameObject); }
            Object.DestroyImmediate(manager.gameObject); Object.DestroyImmediate(camera.gameObject);
            runtime.DestroyRuntime(); record.Dispose(); foreach (var mesh in meshes) Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
        }
    }
    static void ValidateRetainedSurfaces()
    {
        var policy = new TerrainCoveragePolicy(new TerrainCoverageConfiguration { EnableFarTerrain = true, FarTerrainStartRing = 8, FarTerrainMacroTileSize = 4 });
        foreach (var test in new[] { (new ChunkCoord(0, 0), new ChunkCoord(1, 0), new ChunkCoord(16, 0)),
            (new ChunkCoord(1, 0), new ChunkCoord(0, 0), new ChunkCoord(16, 0)),
            (new ChunkCoord(0, 0), new ChunkCoord(-2, 0), new ChunkCoord(-17, 0)) })
        {
            Check(policy.TryGetFarTerrainPatch(test.Item1, test.Item3, out var outgoing), "Missing outgoing fixture patch");
            Check(policy.TryGetFarTerrainPatch(test.Item2, test.Item3, out var incoming) && incoming != outgoing, "Fixture did not cross a hierarchy boundary");
            var manager = (ChunkManager)FormatterServices.GetUninitializedObject(typeof(ChunkManager));
            var records = new Dictionary<FarTerrainPatchKey, FarTerrainTileRecord>();
            var runtimes = new Dictionary<FarTerrainPatchKey, FarTerrainTileRuntime>();
            Set(manager, "loadedChunks", new Dictionary<ChunkCoord, ChunkRuntime>());
            Set(manager, "loadedFarTerrainTiles", runtimes); Set(manager, "farTerrainTileRecords", records);
            Set(manager, "coveragePolicy", policy); Set(manager, "chunkSize", 128); Set(manager, "worldScale", .3f); Set(manager, "farTerrainMacroTileSize", 4);
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            FarTerrainTileRuntime Install(FarTerrainPatchKey key, float level)
            {
                var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward }, triangles = new[] { 0, 1, 2 } }; mesh.RecalculateBounds();
                var record = new FarTerrainTileRecord(key); records.Add(key, record);
                record.TryCompleteRequest(record.BeginRequest(), mesh, Array.Empty<Texture2D>(), heightGrid: new[,] { { level, level }, { level, level } });
                var runtime = new FarTerrainTileRuntime(record, key.SizeInChunks * 128, .3f, null, material, false);
                runtime.SetMesh(mesh, null); runtime.SetVisible(true); runtimes.Add(key, runtime); return runtime;
            }
            try
            {
                var retained = Install(outgoing, 1);
                Check(!runtimes.ContainsKey(incoming), "Legacy lookup would unexpectedly succeed");
                Check(manager.TryGetDistantTreeSurface(test.Item3, test.Item2, out _, out var grid, out var origin, out var size) && grid[0, 0] == 1, "Retained visible terrain lost its billboard support");
                Check(size == outgoing.SizeInChunks * 128 * .3f && origin == new Vector2(outgoing.Origin.x * size, outgoing.Origin.z * size), "Retained grid world mapping changed");
                retained.SetVisible(false);
                Check(!manager.TryGetDistantTreeSurface(test.Item3, test.Item2, out _, out _, out _, out _), "Invisible outgoing terrain was used");
                retained.SetVisible(true);
                var replacement = Install(incoming, 2);
                Check(manager.TryGetDistantTreeSurface(test.Item3, test.Item2, out _, out grid, out _, out _) && grid[0, 0] == 2, "Published preferred replacement did not win");
                replacement.SetMesh(null, null);
                Check(manager.TryGetDistantTreeSurface(test.Item3, test.Item2, out _, out grid, out _, out _) && grid[0, 0] == 1, "Unattached replacement hid retained support");
            }
            finally
            {
                foreach (var runtime in runtimes.Values) runtime.DestroyRuntime();
                foreach (var record in records.Values) record.Dispose(); Object.DestroyImmediate(material);
            }
        }
    }
    static void ValidateFlameBounds()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RedMaple_LOD0_v03.prefab");
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Spruce_LOD0_v06.prefab");
        Check(prefab != null, "No real tree fixture prefab");
        var settings = new TreeSettings { treeLOD0GameObjectPrefab = prefab };
        var registry = new TreeRegistry(7, 38.4f); var coord = new ChunkCoord(0, 0);
        var tree = new TreeInstanceData(new Vector3(0, 10, 0), Quaternion.identity, Vector3.one, WorldFeatureVariant.MapleTree,
            Color.white, Color.white, id: TreeId.Generated(7, coord, TreePlacementSource.Forest, 0));
        registry.RegisterChunk(coord, new[] { tree }, TreePlacementDetail.Detailed);
        using var renderer = new StandingTreeRenderer(settings, registry);
        var bounds = new List<Bounds>(); Action<Bounds> changed = b => bounds.Add(b); LocalFlameGeometry.Changed += changed;
        try
        {
            registry.RegisterChunk(new ChunkCoord(1, 0), Array.Empty<TreeInstanceData>(), TreePlacementDetail.Distant);
            Check(bounds.Count == 0, "Empty tree publication invalidated nearby flames");
            registry.TrySetState(tree.id, TreeState.Removed);
            Check(bounds.Count == 1 && bounds[0].Contains(registry.GetChunk(coord)[0].WorldPosition), "Removing pre-existing canopy failed to invalidate its old shadow");
            Check(bounds[0].size.y < 1000, "Invalidation still spans the entire vertical world");
            registry.TrySetState(tree.id, TreeState.Standing);
            Check(bounds.Count == 2, "Restored canopy failed to invalidate shadows");
            registry.RegisterChunk(coord, Array.Empty<TreeInstanceData>(), TreePlacementDetail.Detailed, 1);
            Check(bounds.Count == 3, "Replacement that removes a canopy lost old bounds");
        }
        finally { LocalFlameGeometry.Changed -= changed; }
    }
    static int[] Reference(TreeInstanceData[] trees, float[] priority)
    {
        var chosen = new bool[trees.Length]; var order = new int[trees.Length];
        for (int rank = 0; rank < trees.Length; rank++)
        {
            int best = -1; float score = -1;
            for (int i = 0; i < trees.Length; i++)
            {
                if (chosen[i]) continue;
                float nearest = rank == 0 ? 1 - priority[i] : float.MaxValue;
                for (int j = 0; j < trees.Length; j++) if (chosen[j]) nearest = Mathf.Min(nearest, (trees[i].localPosition - trees[j].localPosition).sqrMagnitude);
                if (nearest > score) { score = nearest; best = i; }
            }
            chosen[best] = true; order[best] = rank;
        }
        return order;
    }
    static void ValidateProtectionOrder()
    {
        var method = typeof(DistantTreeManager).GetMethod("FillProtectionOrder", BindingFlags.Static | BindingFlags.NonPublic);
        var random = new System.Random(712);
        foreach (int count in new[] { 0, 1, 2, 24, 64, 128 })
        {
            var trees = new TreeInstanceData[count]; var priority = new float[count]; var order = new int[count];
            for (int i = 0; i < count; i++) { trees[i].localPosition = new Vector3(random.Next(8), random.Next(3), random.Next(8)); priority[i] = (float)random.NextDouble(); }
            method.Invoke(null, new object[] { trees, priority, order });
            Check(order.SequenceEqual(Reference(trees, priority)), "Protection priority changed at count " + count);
            if (count == 128)
            {
                var timer = Stopwatch.StartNew(); for (int i = 0; i < 100; i++) Reference(trees, priority); double before = timer.Elapsed.TotalMilliseconds / 100;
                timer.Restart(); for (int i = 0; i < 100; i++) method.Invoke(null, new object[] { trees, priority, order });
                File.WriteAllText(".utmp/vegetation-performance/protection-order.txt", $"128-tree stress manifest, same order: old {before:F4} ms, new {timer.Elapsed.TotalMilliseconds / 100:F4} ms. Main-thread editor microbenchmark; not frame time.\n");
            }
        }
    }
}
