using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Graphics checks require a real device: do not launch with -nographics.
public static class DistantTreeResidentValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }

    public static void RunBatch() => Execute(false);
    public static void RunAllBatch() => Execute(true);

    private static void Execute(bool includeStandingRegressions)
    {
        try
        {
            checks = 0;
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Resident test pipeline bootstrap").AddComponent<Camera>();
            camera.enabled = false;
            var target = new RenderTexture(16, 16, 24);
            try
            {
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            }
            finally { target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject); }
            DistantTreeGpuValidation.Run();
            DistantTreeGroundingValidation.Run();
            ValidateRegions();
            ValidateManager();
            Debug.Log($"RESIDENT_TREE_PASS: {checks} checks; region submission, CPU/GPU handoff parity, sparse updates, residency, fallback and registry edits.");
            if (includeStandingRegressions) StandingTreeSpatialValidation.RunBatch();
            else EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void ValidateRegions()
    {
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/DistantTreeCompact.compute");
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
        mesh.RecalculateBounds();
        var material = new Material(Shader.Find("Custom/TreeBillboardInstancedSimpleLit")) { enableInstancing = true };
        var settings = new TreeSettings { distantTreeDensity = 1f, distantTreeDensityAware = false, distantTreeProtectedCount = 0 };
        var planes = new Vector4[6];
        var bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
        using var batch = new DistantTreeGpuBatch(compute, mesh, material);
        try
        {
            int region = batch.CreateRegion(), second = batch.CreateRegion();
            var transforms = new Matrix4x4[1300];
            var slots = new int[1300];
            var ecology = new Vector4(.5f, 10f, 0f, 0f);
            for (int i = 0; i < slots.Length; i++)
            {
                transforms[i] = Matrix4x4.Translate(new Vector3(i % 2 == 0 ? -10f : 10f, 3f, 10f + i % 120));
                slots[i] = batch.Register(transforms[i], new Vector4(i, 0, 0, 1), ecology, 1f,
                    i < 1000 ? region : second, i % 3 == 0 ? 0f : 1f);
                batch.UpdateSource(slots[i], 11f, ecology);
            }
            void Draw(Vector3 viewer, bool submitSecond = true, float fadeStep = 1f)
            {
                batch.BeginFrame();
                batch.SubmitRegion(region, .8f, bounds, 1000, true);
                if (submitSecond) batch.SubmitRegion(second, .6f, bounds, 300, true);
                batch.Draw(settings, null, viewer, planes, 200f, 20f, 150f, fadeStep, 40f, 80f);
            }
            foreach (var viewer in new[] { Vector3.zero, new Vector3(-30f, 0, 30f), new Vector3(10f, 0, 70f) })
            {
                Draw(viewer);
                var visible = batch.ReadVisibleForValidation();
                var seen = new HashSet<int>();
                foreach (var item in visible)
                {
                    int id = Mathf.RoundToInt(item.Tint.x); seen.Add(id);
                    var m = transforms[id];
                    float distance = Vector2.Distance(new Vector2(m.m03, m.m23), new Vector2(viewer.x, viewer.z));
                    float handoff = id % 3 == 0 ? 1f : Mathf.InverseLerp(40f, 80f, distance);
                    Check(handoff > 0f && Mathf.Abs(item.Fade.y - handoff) < .00001f, "GPU handoff differs from CPU distance rule");
                    Check(Mathf.Abs(item.ObjectToWorld.m13 - Mathf.Lerp(3f, 11f, handoff)) < .00001f,
                        "Ground conformity was not blended with mesh handoff");
                    Check(Mathf.Abs(item.Fade.x - (id < 1000 ? .8f : .6f)) < .00001f, "Region load fades were mixed");
                }
                for (int i = 0; i < slots.Length; i++)
                {
                    var m = transforms[i];
                    float distance = Vector2.Distance(new Vector2(m.m03, m.m23), new Vector2(viewer.x, viewer.z));
                    Check(seen.Contains(i) == (i % 3 == 0 || distance > 40f), "GPU handoff lost or exposed a tree");
                }
            }
            Check(batch.UploadedSourceCount == 0 && batch.UploadedRegionCount == 2, "Camera movement uploaded tree records");
            Draw(Vector3.zero, false);
            foreach (var item in batch.ReadVisibleForValidation()) Check(item.Tint.x < 1000, "Unsubmitted region retained stale visibility");
            batch.UpdateSource(slots[0], 19f, ecology);
            batch.UpdateSource(slots[1299], 21f, ecology);
            Draw(Vector3.zero);
            Check(batch.UploadedSourceCount == 2, "Sparse grounding edit uploaded unchanged tree records");
            foreach (var item in batch.ReadVisibleForValidation())
                if (item.Tint.x == 0) Check(item.ObjectToWorld.m13 == 19f, "Grounding edit did not reach GPU");
            batch.BeginFrame(); batch.SubmitRegion(region, 1f, bounds, 1000, false);
            batch.Draw(settings, null, Vector3.zero, planes, 200, 20, 150, 1, 40, 80);
            Check(batch.ReadVisibleForValidation().Length == 0, "Hidden region retained geometry");
            // Warm steady work, then verify that region submission allocates nothing.
            Draw(Vector3.zero);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++) Draw(Vector3.zero);
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Settled region submission allocated managed memory");
            Check(batch.UploadedSourceCount == 0, "Steady frames re-uploaded source data");
            for (int i = 8; i < slots.Length; i++) batch.Release(slots[i]);
            Draw(Vector3.zero, false);
            Check(batch.CandidateCount == 8, "Freed slots still contribute to compute dispatch size");
            foreach (var item in batch.ReadVisibleForValidation()) Check(item.Tint.x < 8, "Dense slot list retained a released tree");
            batch.ReleaseRegion(second);
            int reused = batch.CreateRegion();
            Check(reused == second, "Region handle was not reused");
            int replacement = batch.Register(Matrix4x4.Translate(new Vector3(0, 0, 100)), new Vector4(9000, 0, 0, 1), ecology, 0f, reused, 0f);
            batch.BeginFrame(); batch.SubmitRegion(reused, 1, bounds, 1, true);
            batch.Draw(settings, null, Vector3.zero, planes, 200, 20, 0, 0, 40, 80);
            Check(batch.ReadVisibleForValidation().Length == 0, "Reused source inherited the old slot's density");
            batch.Release(replacement); batch.ReleaseRegion(reused);
            for (int i = 0; i < 8; i++) batch.Release(slots[i]);
            batch.ReleaseRegion(region);
            batch.BeginFrame(); batch.Draw(settings, null, Vector3.zero, planes, 200, 20, 0, 1);
            Check(batch.CandidateCount == 0 && batch.ReadVisibleForValidation().Length == 0, "Empty batch retained candidates");
            Debug.Log("RESIDENT_REGION_PASS: CPU/GPU handoff and seating parity, no per-tree uploads while moving, sparse edits, frame stamps, hidden regions, zero steady allocations and dense dispatch after release.");
        }
        finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);

    private static void ValidateManager()
    {
        const int size = 16, seed = 1456;
        var chunk = new ChunkCoord(0, 5);
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
        mesh.RecalculateBounds();
        var material = new Material(Shader.Find("Custom/TreeBillboardInstancedSimpleLit")) { enableInstancing = true };
        var fallbackMaterial = new Material(material); fallbackMaterial.SetOverrideTag("DistantTreeIndirect", "False");
        GameObject Prefab(string name, Material mat)
        {
            var go = new GameObject(name); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat; go.SetActive(false); return go;
        }
        var near = Prefab("Resident test near source", material);
        var far = Prefab("Resident test billboard source", material);
        var fallback = Prefab("Resident test CPU billboard source", fallbackMaterial);
        var settings = new TreeSettings { treeLOD0GameObjectPrefab = near, treeBillboardPrefab = far,
            spruceTreeBillboardPrefab = fallback, distantTreeDistanceChunks = 10f,
            distantTreeDensity = 1f, distantTreeDensityAware = false, gameObjectTreeChunkRingRadius = 0,
            distantTreeCompactShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/DistantTreeCompact.compute") };
        var registry = new TreeRegistry(seed, size);
        var trees = new List<TreeInstanceData>();
        for (int i = 0; i < 16; i++) trees.Add(new TreeInstanceData(new Vector3(i * .1f, 0, 0), Quaternion.identity, Vector3.one,
            i == 15 ? WorldFeatureVariant.SpruceTree : WorldFeatureVariant.MapleTree,
            new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            id: TreeId.Generated(seed, chunk, TreePlacementSource.Forest, i)));
        registry.RegisterChunk(chunk, trees, TreePlacementDetail.Detailed);
        var record = new ChunkRecord(chunk);
        var runtime = new ChunkRuntime(record, size, 1f, null, material, material, false);
        runtime.SetVisible(true); runtime.SetMeshes(mesh, null, 0);
        var manager = (ChunkManager)FormatterServices.GetUninitializedObject(typeof(ChunkManager));
        Set(manager, "loadedChunks", new Dictionary<ChunkCoord, ChunkRuntime> { [chunk] = runtime });
        Set(manager, "loadedFarTerrainTiles", new Dictionary<FarTerrainPatchKey, FarTerrainTileRuntime>());
        Set(manager, "farTerrainTileRecords", new Dictionary<FarTerrainPatchKey, FarTerrainTileRecord>());
        Set(manager, "coveragePolicy", new TerrainCoveragePolicy(new TerrainCoverageConfiguration { EnableFarTerrain = false }));
        Set(manager, "chunkSize", size); Set(manager, "worldScale", 1f); Set(manager, "farTerrainMacroTileSize", 4);
        using var renderer = new DistantTreeManager(settings, seed, size, 600f, 2, .05f, 10f, 1f, 200f, .24f, 1f,
            default, treeRegistry: registry);
        try
        {
            renderer.Update(manager, Vector3.zero, null, 10);
            // The edit-mode clock can be zero; force a settled load fade for this synchronous fixture.
            foreach (DictionaryEntry pair in (IDictionary)typeof(DistantTreeManager).GetField("cache", Private).GetValue(renderer))
                pair.Value.GetType().GetField("LoadFade").SetValue(pair.Value, 1f);
            renderer.Update(manager, Vector3.zero, null, 10);
            Check(renderer.CpuTreeSubmissions == 1 && renderer.GpuCandidates == 15,
                $"Far manager still walked GPU trees, or lost CPU fallback: CPU={renderer.CpuTreeSubmissions}, GPU={renderer.GpuCandidates}");
            Check(renderer.GpuSourceUploads == 0 && renderer.GpuRegionUploads == 1, "Settled manager uploaded tree state");
            renderer.Update(manager, new Vector3(2f, 0, 1f), null, 10);
            Check(renderer.CpuTreeSubmissions == 1 && renderer.GpuSourceUploads == 0, "Viewer movement restored per-tree CPU work");
            settings.distantTreeHeightBlendSpeed = 0f;
            typeof(ChunkRecord).GetProperty("FarTreeHeightGrid").SetValue(record, new[,] { { .05f, .05f }, { .05f, .05f } });
            runtime.SetMeshes(mesh, null, 5);
            renderer.Update(manager, Vector3.zero, null, 10);
            Check(renderer.GpuSourceUploads == 15, "Terrain handoff did not upload changed grounding");
            foreach (var item in (IEnumerable)typeof(DistantTreeManager).GetField("uniqueBatches", Private).GetValue(renderer))
            {
                var gpu = (DistantTreeGpuBatch)item.GetType().GetField("Gpu").GetValue(item);
                if (gpu != null) foreach (var visible in gpu.ReadVisibleForValidation())
                    Check(Mathf.Abs(visible.ObjectToWorld.m13 - 10f) < .0001f, "Coarse terrain seating was not applied");
            }
            renderer.Update(manager, Vector3.zero, null, 10);
            Check(renderer.GpuSourceUploads == 0, "Settled coarse terrain kept uploading grounding");
            runtime.SetMeshes(mesh, null, 0);
            renderer.Update(manager, Vector3.zero, null, 10);
            Check(renderer.GpuSourceUploads == 15, "Detailed terrain handoff did not restore original grounding");
            registry.TrySetState(trees[0].id, TreeState.Cut);
            renderer.Update(manager, Vector3.zero, null, 10);
            Check(renderer.GpuCandidates == 14, "Registry cut left resident geometry");
            settings.distantTreeGpuCompaction = false;
            renderer.Update(manager, Vector3.zero, null, 10);
            Check(renderer.CpuTreeSubmissions == 15 && renderer.GpuCandidates == 0, "GPU toggle did not restore CPU fallback");
            settings.distantTreeGpuCompaction = true;
            renderer.Update(manager, Vector3.zero, null, 10);
            Check(renderer.CpuTreeSubmissions == 1 && renderer.GpuCandidates == 14, "GPU re-enable did not rebuild residency");
            runtime.SetVisible(false);
            renderer.Update(manager, Vector3.zero, null, 10);
            Check(renderer.CpuTreeSubmissions == 0 && renderer.GpuRegionUploads == 0, "Hidden terrain submitted trees");
            runtime.SetVisible(true);
            renderer.Update(manager, new Vector3(8f, 0, 80f), null, 10);
            Check(renderer.CpuTreeSubmissions == 15, "Approaching trees did not restore near mesh submission");
            Debug.Log("RESIDENT_MANAGER_PASS: far meshes skipped, mixed CPU/GPU materials, camera movement, coarse/detailed grounding uploads, cut-state rebuild, GPU toggle, support visibility and near approach.");
        }
        finally
        {
            runtime.DestroyRuntime(); record.Dispose();
            Object.DestroyImmediate(near); Object.DestroyImmediate(far); Object.DestroyImmediate(fallback);
            Object.DestroyImmediate(material); Object.DestroyImmediate(fallbackMaterial); Object.DestroyImmediate(mesh);
        }
    }
}
