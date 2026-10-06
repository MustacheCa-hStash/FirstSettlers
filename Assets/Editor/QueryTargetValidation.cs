using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class QueryTargetValidation
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Set(Object component, string field, Object value)
    {
        var serialized = new SerializedObject(component);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [MenuItem("Tools/Terrain/Validate Query Targets")]
    public static void Run()
    {
        checks = 0;
        ValidateTreeResolution();
        ValidateGeneratedRocks();
        Debug.Log("QUERY TARGET PASS: " + checks + " checks; registry identity, child/query shapes, metadata, " +
            "solid obstruction, proxy reuse invalidation, rock spawn variants, authored overrides and settled allocations.");
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateTreeResolution()
    {
        const int seed = 391;
        const float width = 10;
        var origin = new Vector3(20000, 0, 20000);
        var coord = new ChunkCoord(2000, 2000);
        TreeInstanceData Placement(int cell) => new(new Vector3(-5, 0, -2), Quaternion.identity, Vector3.one,
            WorldFeatureVariant.SpruceTree, new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), 0,
            TreeId.Generated(seed, coord, TreePlacementSource.Forest, cell));
        var first = Placement(0);
        var second = Placement(1);
        var registry = new TreeRegistry(seed, width);
        var prefab = new GameObject("Target test source"); prefab.SetActive(false);
        var colliderObject = new GameObject("Trunk"); colliderObject.transform.SetParent(prefab.transform, false);
        var trunk = colliderObject.AddComponent<CapsuleCollider>(); trunk.height = 2; trunk.radius = .4f; trunk.center = Vector3.up;
        var authoring = prefab.AddComponent<TreeGameplayAuthoring>();
        typeof(TreeGameplayAuthoring).GetField("physicalTrunkColliders", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(authoring, new Collider[] { trunk });
        var texture = new Texture2D(2, 2);
        var icon = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        Set(authoring, "queryIcon", icon);
        var settings = new TreeSettings { spruceTreePrefab = prefab, gameplay = new TreeGameplaySettings
            { activationRadiusChunks = 1, releaseRadiusChunks = 1.5f, activationBudgetMs = 0 } };
        using var manager = new TreeGameplayManager(registry, settings, width);
        var cameraObject = new GameObject("Target test camera"); cameraObject.transform.position = origin + Vector3.up;
        var camera = cameraObject.AddComponent<Camera>();
        var query = cameraObject.AddComponent<PlayerQuery>(); Set(query, "viewCamera", camera);
        GameObject wall = null;
        try
        {
            registry.RegisterChunk(coord, new[] { first }, TreePlacementDetail.Detailed);
            manager.Update(origin, 0); query.Sample();
            Check(query.Current.HasTarget && query.Current.Kind == PlayerQueryHitKind.Solid, "Physical trunk does not resolve a named target.");
            var captured = query.Current.Target;
            Check(captured.DisplayName == "Spruce Tree" && captured.Icon == icon, "Tree species name/icon did not reach the proxy.");
            Check(captured.HasCapability(QueryTargetCapabilities.Breakable), "Standing tree lacks breakable capability.");
            Check(captured.TryGetData<TreeRecord>(out var record) && record.Id == first.id &&
                registry.TryGet(first.id, out var stored) && ReferenceEquals(record, stored), "Tree hit does not return its registry record and stable ID.");
            Check(manager.TryGetProxy(first.id, out var proxy) && captured.Source == proxy, "Tree target is not its pooled root provider.");

            var queryObject = new GameObject("Extra query shape") { layer = GameplayLayers.QueryOnly };
            queryObject.transform.SetParent(proxy.transform, false); queryObject.transform.localPosition = new Vector3(0, 1, -1);
            var queryShape = queryObject.AddComponent<BoxCollider>(); queryShape.size = Vector3.one * .2f; queryShape.isTrigger = true;
            Physics.SyncTransforms(); query.Sample();
            Check(query.Current.Kind == PlayerQueryHitKind.QueryOnly && query.Current.Target.Source == proxy &&
                query.Current.Target.TryGetData<TreeRecord>(out var canopyRecord) && ReferenceEquals(canopyRecord, record),
                "Additional query geometry does not identify the same tree record.");
            queryShape.enabled = false;
            Check(!query.Current.HasTarget, "Disabled hit collider retains a live target.");
            query.Sample();
            Check(query.Current.HasTarget, "Trunk target did not recover after query shape disable.");

            wall = new GameObject("Unnamed obstruction") { layer = GameplayLayers.WorldSolid };
            wall.transform.position = origin + new Vector3(0, 1, 1);
            wall.AddComponent<BoxCollider>().size = Vector3.one * .2f;
            Physics.SyncTransforms(); query.Sample();
            Check(query.Current.HasHit && !query.Current.HasTarget && query.Current.Collider.gameObject == wall,
                "Query looked through unnamed solid terrain/wall to a named tree.");
            Object.DestroyImmediate(wall); wall = null;
            Physics.SyncTransforms(); query.Sample();
            proxy.enabled = false; query.Sample();
            Check(query.Current.HasHit && !query.Current.HasTarget, "Disabled provider still resolves a tree.");
            proxy.enabled = true; query.Sample();
            registry.TrySetState(first.id, TreeState.Cut);
            Check(!captured.IsValid, "Cut tree retains a valid standing target before collider release.");
            manager.Update(origin, .2);
            Check(!captured.IsValid, "Released proxy retains a valid old target.");
            registry.RegisterChunk(coord, new[] { second }, TreePlacementDetail.Detailed);
            manager.Update(origin, .4); query.Sample();
            Check(manager.TryGetProxy(second.id, out var rebound) && rebound == proxy, "Fixture did not exercise actual proxy reuse.");
            Check(!captured.IsValid && !captured.TryGetData<TreeRecord>(out _), "Old query snapshot now resolves the reused proxy's new identity.");
            Check(query.Current.HasTarget && query.Current.Target.TryGetData<TreeRecord>(out var newRecord) && newRecord.Id == second.id,
                "Rebound tree hit did not expose its new registry record.");

            for (int i = 0; i < 100; i++) query.Sample();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) query.Sample();
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Settled tree target resolution allocates managed memory.");
        }
        finally
        {
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(wall); Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(icon); Object.DestroyImmediate(texture);
        }
    }

    private static void ValidateGeneratedRocks()
    {
        var root = new GameObject("Target test rock root"); root.transform.position = new Vector3(21000, 0, 21000);
        var prefab = new GameObject("Target test rock source"); prefab.SetActive(false);
        var child = new GameObject("Physical shape"); child.transform.SetParent(prefab.transform, false); child.AddComponent<BoxCollider>();
        var runtime = new ChunkFoliageRuntime { root = root.transform, forestRockPrefabs = new[] { prefab },
            grasslandRockPrefabs = new[] { prefab }, grasslandLargeRockPrefabs = new[] { prefab } };
        var placements = new List<RockInstanceData>
        {
            new(Vector3.zero, Quaternion.identity, Vector3.one, WorldFeatureVariant.Boulder, 0),
            new(Vector3.right * 3, Quaternion.identity, Vector3.one, WorldFeatureVariant.GrasslandBoulder, 0),
            new(Vector3.right * 6, Quaternion.identity, Vector3.one, WorldFeatureVariant.GrasslandLargeBoulder, 0)
        };
        try
        {
            runtime.RebuildRockGameObjects(placements, root.transform);
            var targets = root.GetComponentsInChildren<RockQueryTarget>(true);
            Check(targets.Length == 3, "Generated rock variants do not each have one target provider.");
            Check(prefab.GetComponent<RockQueryTarget>() == null, "Spawn registration modified the source rock prefab.");
            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i]; target.gameObject.SetActive(true);
                var collider = target.GetComponentInChildren<Collider>();
                Check(QueryTarget.TryResolve(collider, out var info) && info.Source == target &&
                    info.HasCapability(QueryTargetCapabilities.Breakable) && info.DisplayName == "Rock", "Generated rock child did not resolve a breakable rock.");
                Check(info.TryGetData<RockQueryData>(out var data) && data.IsGenerated && data.Placement.Value.variant == placements[i].variant &&
                    data.Placement.Value.prefabIndex == 0, "Rock query placement metadata is incorrect.");
            }
            var first = targets[0]; first.TryGetInfo(out var captured);
            first.Initialize(placements[1]);
            Check(!captured.IsValid && !captured.TryGetData<RockQueryData>(out _), "Reinitialized rock retains a valid old placement snapshot.");
            for (int i = 0; i < 100; i++) QueryTarget.TryResolve(first.GetComponentInChildren<Collider>(), out _);
            var firstCollider = first.GetComponentInChildren<Collider>();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) QueryTarget.TryResolve(firstCollider, out _);
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Settled rock resolution allocates managed memory.");

            var authored = new GameObject("Directly placed rock"); authored.transform.SetParent(root.transform, false);
            var authoredTarget = authored.AddComponent<RockQueryTarget>(); var authoredCollider = authored.AddComponent<BoxCollider>();
            var serialized = new SerializedObject(authoredTarget);
            serialized.FindProperty("displayName").stringValue = "Granite Boulder";
            serialized.FindProperty("breakable").boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
            Check(QueryTarget.TryResolve(authoredCollider, out var authoredInfo) && authoredInfo.DisplayName == "Granite Boulder" &&
                !authoredInfo.HasCapability(QueryTargetCapabilities.Breakable) && authoredInfo.TryGetData<RockQueryData>(out var authoredData) &&
                !authoredData.IsGenerated, "Authored rock overrides incorrectly depend on a generated placement or physics layer.");
            first.enabled = false;
            Check(!QueryTarget.TryResolve(firstCollider, out var disabled) && !disabled.IsValid, "Disabled target provider returns valid metadata.");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(prefab); }
    }
}
