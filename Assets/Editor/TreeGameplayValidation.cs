using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TreeGameplayValidation
{
    private const int Seed = 1937;
    private const float ChunkWidth = 10f;
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static TreeInstanceData Tree(int cell, Vector3 world, ChunkCoord coord = default, Vector3? scale = null) => new(
        world - new Vector3((coord.x + .5f) * ChunkWidth, 0, (coord.z + .5f) * ChunkWidth),
        Quaternion.identity, scale ?? Vector3.one, WorldFeatureVariant.SpruceTree,
        new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), 0,
        TreeId.Generated(Seed, coord, TreePlacementSource.Forest, cell));

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Prefab;
        public readonly TreeSettings Settings;
        public readonly TreeRegistry Registry = new(Seed, ChunkWidth);
        public readonly TreeGameplayManager Manager;
        public readonly CapsuleCollider Source;
        public Fixture()
        {
            Prefab = new GameObject("Gameplay test tree asset"); Prefab.SetActive(false);
            var branch = new GameObject("Gameplay"); branch.transform.SetParent(Prefab.transform, false);
            var trunk = new GameObject("Physical Trunk"); trunk.transform.SetParent(branch.transform, false);
            trunk.transform.localPosition = Vector3.up;
            Source = trunk.AddComponent<CapsuleCollider>(); Source.center = Vector3.up; Source.height = 4; Source.radius = .4f; Source.enabled = false;
            // Unassigned collider and rendering must never be copied into the gameplay proxy.
            branch.AddComponent<BoxCollider>().size = Vector3.one * 100;
            branch.AddComponent<MeshRenderer>(); branch.AddComponent<MeshFilter>();
            var authoring = Prefab.AddComponent<TreeGameplayAuthoring>();
            typeof(TreeGameplayAuthoring).GetField("physicalTrunkColliders", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(authoring, new Collider[] { Source, Source });
            Settings = new TreeSettings { spruceTreePrefab = Prefab, gameplay = new TreeGameplaySettings
                { activationRadiusChunks = 1, releaseRadiusChunks = 1.5f, maxActivationsPerFrame = 2, activationBudgetMs = 0, maxPooledProxies = 8 } };
            Manager = new TreeGameplayManager(Registry, Settings, ChunkWidth);
        }
        public void Dispose() { Manager.Dispose(); Object.DestroyImmediate(Prefab); }
    }

    [MenuItem("Tools/Terrain/Validate Tree Gameplay")]
    public static void Run()
    {
        checks = 0;
        ValidateActivationAndPool();
        ValidatePlacementAndState();
        ValidateDestroyedRootDisposal();
        ValidateCharacterCollision();
        Debug.Log("TREE GAMEPLAY PASS: " + checks + " checks; nearest activation budgets, hysteresis, bounded pool reuse, " +
            "registry changes, negative coordinates, hierarchy/scale, render-free proxies, physical character blocking, settled updates and already-destroyed root disposal.");
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateActivationAndPool()
    {
        using var f = new Fixture();
        var trees = new[] { Tree(0, Vector3.right * 6), Tree(1, Vector3.right * 2), Tree(2, Vector3.right * 4) };
        f.Registry.RegisterChunk(default, trees, TreePlacementDetail.Detailed);
        f.Manager.Update(Vector3.zero, 0);
        Check(f.Manager.ActiveCount == 2 && f.Manager.LastActivationCount == 2, "Activation count cap failed.");
        Check(f.Manager.TryGetProxy(trees[1].id, out var near) && f.Manager.TryGetProxy(trees[2].id, out _), "Nearest trees did not activate first.");
        Check(near.GetComponentsInChildren<Collider>().Length == 1 && near.GetComponentsInChildren<Renderer>().Length == 0 &&
            near.GetComponentsInChildren<Rigidbody>().Length == 0, "Proxy copied visual, unassigned or duplicate components.");
        Check(near.GetComponentsInChildren<CapsuleCollider>()[0].enabled && !f.Source.enabled, "Source disabled state leaked or template was changed.");
        f.Manager.Update(Vector3.zero, .01);
        Check(f.Manager.ActiveCount == 3, "Queued activation failed to progress.");
        f.Manager.Update(Vector3.left * 9, .02);
        Check(f.Manager.ActiveCount == 3, "Hysteresis released trees outside activation but inside release distance.");
        f.Manager.Update(Vector3.left * 10, .03);
        Check(f.Manager.ActiveCount == 2 && f.Manager.PooledCount == 1, "Release threshold failed.");
        f.Manager.Update(Vector3.right * 100, .04);
        Check(f.Manager.ActiveCount == 0 && f.Manager.PooledCount == 3 && !near.IsBound && !near.gameObject.activeSelf, "Released proxies retain active identity or collision.");
        var pooled = f.Manager.Root.GetComponentsInChildren<TreeGameplayProxy>(true).Select(p => p.GetInstanceID()).ToArray();
        f.Manager.Update(Vector3.zero, .05); f.Manager.Update(Vector3.zero, .06);
        Check(f.Manager.ActiveCount == 3 && f.Manager.PooledCount == 0 &&
            f.Manager.Root.GetComponentsInChildren<TreeGameplayProxy>(true).All(p => pooled.Contains(p.GetInstanceID())), "Pool reuse created replacement bodies.");
        f.Settings.gameplay.maxPooledProxies = 1;
        f.Manager.Update(Vector3.right * 100, .07);
        Check(f.Manager.PooledCount == 1 && f.Manager.Root.childCount == 1, "Inactive pool limit failed.");
        f.Settings.gameplay.enabled = false; f.Manager.Update(Vector3.zero, .08);
        Check(f.Manager.ActiveCount == 0, "Disabled gameplay activated collision.");
        f.Settings.gameplay.enabled = true; f.Manager.Update(Vector3.zero, .09);
        Check(f.Manager.ActiveCount == 2, "Re-enabling gameplay failed to rescan.");
        f.Settings.gameplay.releaseRadiusChunks = .1f;
        Check(f.Manager.ReleaseRadius > f.Manager.ActivationRadius, "Invalid release settings lost hysteresis.");
    }

    private static void ValidatePlacementAndState()
    {
        using var f = new Fixture();
        var coord = new ChunkCoord(-1, -1);
        var tree = Tree(0, new Vector3(-2, 0, -2), coord, Vector3.one * 2);
        f.Registry.RegisterChunk(coord, new[] { tree }, TreePlacementDetail.Detailed);
        f.Manager.Update(new Vector3(-2, 100, -2), 0);
        Check(f.Manager.TryGetProxy(tree.id, out var proxy), "Negative coordinates or XZ selection failed.");
        Check(proxy.transform.position == new Vector3(-2, 0, -2) && proxy.transform.localScale == Vector3.one * 2, "Placement transform differs from registry.");
        var collider = proxy.GetComponentInChildren<CapsuleCollider>();
        Check(collider.transform.position == new Vector3(-2, 2, -2) && collider.height == 4 && collider.radius == .4f,
            "Child hierarchy or collider shape was lost.");
        tree.localPosition.y = 3;
        tree.localRotation = Quaternion.Euler(0, 65, 0);
        f.Registry.RegisterChunk(coord, new[] { tree }, TreePlacementDetail.Detailed);
        f.Manager.Update(new Vector3(-2, 0, -2), .01);
        Check(f.Manager.TryGetProxy(tree.id, out var refreshed) && refreshed == proxy && refreshed.transform.position.y == 3 &&
            Quaternion.Angle(refreshed.transform.rotation, tree.localRotation) < .001f, "Placement refresh replaced identity or failed to move collision.");
        Check(f.Manager.SyncedPhysicsThisUpdate, "Changed collider transforms were not synchronized.");
        f.Manager.Update(new Vector3(-2, 0, -2), .02);
        Check(!f.Manager.SyncedPhysicsThisUpdate, "Settled colliders unnecessarily synchronized physics.");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) f.Manager.Update(new Vector3(-2, 0, -2), 1 + i * .001);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "Settled updates allocated " + allocated + " bytes.");
        foreach (var state in new[] { TreeState.Cut, TreeState.Fallen, TreeState.Removed })
        {
            f.Registry.TrySetState(tree.id, state); f.Manager.Update(new Vector3(-2, 0, -2), 3);
            Check(f.Manager.ActiveCount == 0 && !proxy.IsBound, "Non-standing state retains collision: " + state);
            f.Registry.TrySetState(tree.id, TreeState.Standing); f.Manager.Update(new Vector3(-2, 0, -2), 3.01);
            Check(f.Manager.ActiveCount == 1, "Restored standing state did not activate.");
        }
        f.Registry.RegisterChunk(coord, Array.Empty<TreeInstanceData>(), TreePlacementDetail.Detailed);
        f.Manager.Update(new Vector3(-2, 0, -2), 4);
        Check(f.Manager.ActiveCount == 0, "Removed placement retains a proxy.");
        f.Registry.RegisterChunk(coord, new[] { tree }, TreePlacementDetail.Detailed);
        f.Manager.Update(new Vector3(-2, 0, -2), 4.01);
        f.Registry.Clear(); f.Manager.Update(new Vector3(-2, 0, -2), 4.02);
        Check(f.Manager.ActiveCount == 0, "Cleared registry retains collision.");
        var root = f.Manager.Root; f.Manager.Dispose(); f.Manager.Dispose();
        Check(root == null, "Disposal left gameplay bodies in the scene.");
    }

    private static void ValidateDestroyedRootDisposal()
    {
        using var f = new Fixture();
        var trees = new[] { Tree(0, Vector3.right * 2), Tree(1, Vector3.right * 4) };
        f.Registry.RegisterChunk(default, trees, TreePlacementDetail.Detailed);
        f.Manager.Update(Vector3.zero, 0);
        f.Registry.TrySetState(trees[0].id, TreeState.Cut);
        f.Manager.Update(Vector3.zero, .01);
        Check(f.Manager.ActiveCount == 1 && f.Manager.PooledCount == 1,
            "Destroyed-root fixture must exercise active and pooled proxy cleanup.");

        // Scene teardown / leaving Play mode can destroy this independent root before WorldManager.OnDestroy.
        Object.DestroyImmediate(f.Manager.Root.gameObject);
        Check(f.Manager.Root == null, "Externally destroyed gameplay root still reports a live Transform.");
        f.Manager.Dispose();
        f.Manager.Dispose();
        Check(f.Manager.ActiveCount == 0 && f.Manager.PooledCount == 0 && f.Manager.BakedQueryMeshCount == 0,
            "Destroyed-root disposal retained manager-owned state.");
        var changed = typeof(TreeRegistry).GetField("ChunkChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(changed != null && changed.GetValue(f.Registry) == null,
            "Destroyed-root disposal retained the registry subscription.");
        f.Manager.Update(Vector3.zero, 1);
        f.Registry.RegisterChunk(default, trees, TreePlacementDetail.Detailed);
        f.Manager.Update(Vector3.zero, 2);
        Check(f.Manager.ActiveCount == 0 && f.Manager.Root == null,
            "A disposed tree manager resumed spawning bodies after scene teardown.");
    }

    private static void ValidateCharacterCollision()
    {
        using var f = new Fixture();
        var tree = Tree(0, Vector3.right * 2);
        f.Registry.RegisterChunk(default, new[] { tree }, TreePlacementDetail.Detailed);
        var player = new GameObject("Tree collision test character");
        bool previousAutoSync = Physics.autoSyncTransforms;
        try
        {
            Physics.autoSyncTransforms = false;
            var controller = player.AddComponent<CharacterController>(); controller.height = 1.8f; controller.radius = .3f;
            player.transform.position = Vector3.up;
            f.Manager.Update(Vector3.zero, 0);
            var flags = controller.Move(Vector3.right * 6);
            Check((flags & CollisionFlags.Sides) != 0 && player.transform.position.x < 1.5f, "Character passed through the active trunk.");
            Check(f.Manager.TryGetProxy(tree.id, out var proxy) && proxy.Id == tree.id, "Collider body has no stable tree identity.");
            f.Registry.TrySetState(tree.id, TreeState.Cut); f.Manager.Update(Vector3.zero, .01);
            controller.Move(Vector3.right * 3);
            Check(player.transform.position.x > 3, "Released trunk still blocks the character.");
        }
        finally { Physics.autoSyncTransforms = previousAutoSync; Object.DestroyImmediate(player); }
    }
}
