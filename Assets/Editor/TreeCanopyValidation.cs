using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TreeCanopyValidation
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Assign(TreeGameplayAuthoring authoring, string field, Collider[] value) =>
        typeof(TreeGameplayAuthoring).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(authoring, value);

    private sealed class Fixture : IDisposable
    {
        public readonly Vector3 Origin = new(30000, 0, 30000);
        public readonly GameObject Prefab = new("Canopy validation prefab");
        public readonly Mesh Cone = CreateCone();
        public readonly MeshCollider Source;
        public readonly TreeSettings Settings;
        public readonly TreeRegistry Registry = new(739, 10);
        public readonly TreeGameplayManager Manager;
        public readonly TreeId Id = TreeId.Generated(739, new ChunkCoord(3000, 3000), TreePlacementSource.Forest, 0);
        public Fixture(bool physical = true)
        {
            Prefab.SetActive(false);
            var parent = new GameObject("Gameplay"); parent.transform.SetParent(Prefab.transform, false);
            var trunk = new GameObject("Physical trunk"); trunk.transform.SetParent(parent.transform, false);
            var trunkCollider = trunk.AddComponent<CapsuleCollider>(); trunkCollider.radius = .2f;
            trunkCollider.height = 2; trunkCollider.center = Vector3.up;
            var canopy = new GameObject("Query canopy"); canopy.transform.SetParent(parent.transform, false);
            Source = canopy.AddComponent<MeshCollider>(); Source.convex = true; Source.sharedMesh = Cone; Source.enabled = false;
            canopy.AddComponent<MeshRenderer>(); // Visual/script components must not be copied.
            parent.AddComponent<BoxCollider>(); // Unassigned shape must not be copied.
            var authoring = Prefab.AddComponent<TreeGameplayAuthoring>();
            Assign(authoring, "physicalTrunkColliders", physical ? new Collider[] { trunkCollider } : Array.Empty<Collider>());
            Assign(authoring, "queryCanopyColliders", new Collider[] { Source, Source });
            Settings = new TreeSettings { spruceTreePrefab = Prefab, gameplay = new TreeGameplaySettings
                { activationRadiusChunks = 1, releaseRadiusChunks = 1.5f, queryCanopyActivationRadiusChunks = .5f,
                  queryCanopyReleaseRadiusChunks = .7f, activationBudgetMs = 0 } };
            Manager = new TreeGameplayManager(Registry, Settings, 10);
            Registry.RegisterChunk(new ChunkCoord(3000, 3000), new[]
            {
                new TreeInstanceData(new Vector3(-5, 0, -2), Quaternion.identity, Vector3.one, WorldFeatureVariant.SpruceTree,
                    new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), 0, Id)
            }, TreePlacementDetail.Detailed);
        }
        public Vector3 TreeOrigin => Origin + Vector3.forward * 3;
        public void UpdateAtDistance(float distance, double time) => Manager.Update(TreeOrigin + Vector3.right * distance, time);
        public void Dispose() { Manager.Dispose(); Object.DestroyImmediate(Prefab); Object.DestroyImmediate(Cone); }
    }

    private static Mesh CreateCone()
    {
        const int sides = 12;
        var vertices = new Vector3[sides + 2];
        vertices[0] = Vector3.up * 4;
        vertices[1] = Vector3.zero;
        var triangles = new int[sides * 6];
        for (int i = 0; i < sides; i++)
            vertices[i + 2] = new Vector3(Mathf.Cos(i * Mathf.PI * 2 / sides), 0, Mathf.Sin(i * Mathf.PI * 2 / sides));
        for (int i = 0; i < sides; i++)
        {
            int current = i + 2, next = (i + 1) % sides + 2, index = i * 6;
            triangles[index] = 0; triangles[index + 1] = next; triangles[index + 2] = current;
            triangles[index + 3] = 1; triangles[index + 4] = current; triangles[index + 5] = next;
        }
        var mesh = new Mesh { name = "Validation 12-sided closed cone", vertices = vertices, triangles = triangles };
        mesh.RecalculateBounds(); return mesh;
    }

    [MenuItem("Tools/Terrain/Validate Tree Canopy Queries")]
    public static void Run()
    {
        checks = 0;
        ValidateRolesAndPooling(); ValidateQueryAndMovement(); ValidateQueryOnlyTree(); ValidateInvalidAssignments();
        Debug.Log("TREE CANOPY PASS: " + checks + " checks; convex shared mesh, independent roles/hysteresis, pooling, " +
            "query identity/obstruction, passable canopies, query-only trees, rejected assignments and settled allocations.");
        TreeGameplayValidation.Run();
        PlayerQueryValidation.Run();
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateRolesAndPooling()
    {
        using var f = new Fixture();
        f.UpdateAtDistance(6, 0);
        Check(f.Manager.TryGetProxy(f.Id, out var proxy) && proxy.PhysicalTrunkActive && !proxy.QueryCanopyActive,
            "Physical and canopy activation distances are not independent.");
        Check(proxy.PhysicalTrunkColliders.Count == 1 && proxy.QueryCanopyColliders.Count == 1,
            "Assigned shapes were duplicated or unassigned shapes were copied.");
        var mesh = (MeshCollider)proxy.QueryCanopyColliders[0];
        Check(mesh.convex && mesh.isTrigger && mesh.gameObject.layer == GameplayLayers.QueryOnly && !mesh.enabled,
            "Canopy role did not force a QueryOnly convex trigger.");
        Check(ReferenceEquals(mesh.sharedMesh, f.Cone) && mesh.cookingOptions == f.Source.cookingOptions,
            "Query mesh was cloned or cooking options differ from the template.");
        Check(!f.Source.enabled && !f.Source.isTrigger && f.Source.gameObject.layer == 0,
            "Runtime canopy setup modified source authoring.");
        Check(proxy.GetComponentsInChildren<Renderer>(true).Length == 0 && proxy.GetComponentsInChildren<MeshFilter>(true).Length == 0 &&
            proxy.GetComponentsInChildren<Rigidbody>(true).Length == 0, "Query proxy copied rendering or a rigidbody.");
        f.UpdateAtDistance(4, .1);
        Check(proxy.QueryCanopyActive && mesh.enabled && f.Manager.SyncedPhysicsThisUpdate, "Canopy failed to activate inside its radius.");
        f.UpdateAtDistance(6, .2);
        Check(proxy.QueryCanopyActive && !f.Manager.SyncedPhysicsThisUpdate, "Canopy lost release hysteresis or unnecessarily synchronized physics.");
        f.UpdateAtDistance(8, .3);
        Check(!proxy.QueryCanopyActive && !mesh.enabled && proxy.PhysicalTrunkActive && f.Manager.SyncedPhysicsThisUpdate,
            "Canopy release also disabled the physical trunk or retained the canopy.");
        f.UpdateAtDistance(16, .4);
        Check(!proxy.IsBound && !proxy.gameObject.activeSelf && !proxy.PhysicalTrunkActive && !proxy.QueryCanopyActive,
            "Pooled proxy retained identity or active roles.");
        f.UpdateAtDistance(4, .5);
        Check(f.Manager.TryGetProxy(f.Id, out var reused) && reused == proxy && ReferenceEquals(mesh.sharedMesh, f.Cone) && mesh.enabled,
            "Pool reuse rebuilt the proxy/mesh or lost canopy state.");
        Check(f.Manager.BakedQueryMeshCount == 1, "Shared mesh was not cached once per cooking configuration.");
        f.Settings.gameplay.enableCanopyQueries = false; f.UpdateAtDistance(4, .6);
        Check(!mesh.enabled && proxy.PhysicalTrunkActive, "Disabling canopy queries disabled physical gameplay.");
        f.Settings.gameplay.enableCanopyQueries = true; f.UpdateAtDistance(4, .7);
        Check(mesh.enabled && f.Manager.BakedQueryMeshCount == 1, "Re-enabling canopy queries failed or rebaked the mesh.");
        f.Settings.gameplay.queryCanopyReleaseRadiusChunks = .1f;
        Check(f.Manager.QueryCanopyReleaseRadius > f.Manager.QueryCanopyActivationRadius, "Invalid canopy release setting lost hysteresis.");
        for (int i = 0; i < 100; i++) f.UpdateAtDistance(4, 1 + i * .01);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) f.UpdateAtDistance(4, 3 + i * .001);
        Check(GC.GetAllocatedBytesForCurrentThread() == before && !f.Manager.SyncedPhysicsThisUpdate,
            "Settled canopy management allocates or synchronizes physics.");
        f.Registry.TrySetState(f.Id, TreeState.Cut); f.UpdateAtDistance(4, 5);
        Check(!proxy.QueryCanopyActive && !mesh.enabled && !proxy.IsBound, "Cut tree retained its query canopy.");
    }

    private static void ValidateQueryAndMovement()
    {
        using var f = new Fixture(); f.UpdateAtDistance(0, 0);
        var cameraObject = new GameObject("Canopy test camera"); cameraObject.transform.position = f.Origin + Vector3.up;
        var camera = cameraObject.AddComponent<Camera>();
        var query = cameraObject.AddComponent<PlayerQuery>();
        var serialized = new SerializedObject(query); serialized.FindProperty("viewCamera").objectReferenceValue = camera;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var player = new GameObject("Canopy pass-through character") { layer = GameplayLayers.Player };
        GameObject wall = null;
        try
        {
            query.Sample();
            Check(query.Current.HasTarget && query.Current.Kind == PlayerQueryHitKind.QueryOnly &&
                query.Current.Target.TryGetData<TreeRecord>(out var record) && record.Id == f.Id,
                "Cone surface did not resolve the existing tree identity through PlayerQuery.");
            Check(query.SolidHit.HasTarget && query.QueryOnlyHit.HasTarget &&
                query.SolidHit.Target.Source == query.QueryOnlyHit.Target.Source,
                "Separate ray results do not expose the same tree's trunk and canopy.");
            var captured = query.Current.Target;
            f.Manager.TryGetProxy(f.Id, out var proxy); proxy.QueryCanopyColliders[0].enabled = false;
            Physics.SyncTransforms(); query.Sample();
            Check(query.Current.Kind == PlayerQueryHitKind.Solid && query.Current.Target.Source == captured.Source,
                "Physical trunk and cone do not resolve the same target.");
            proxy.QueryCanopyColliders[0].enabled = true; Physics.SyncTransforms();
            wall = new GameObject("Canopy test wall") { layer = GameplayLayers.WorldSolid };
            wall.transform.position = f.Origin + new Vector3(0, 1, 1);
            wall.AddComponent<BoxCollider>().size = new Vector3(2, 2, .2f);
            Physics.SyncTransforms(); query.Sample();
            Check(query.Current.HasHit && !query.Current.HasTarget && query.Current.Collider.gameObject == wall,
                "Cone query ignored the solid obstruction.");
            Object.DestroyImmediate(wall); wall = null;
            var controller = player.AddComponent<CharacterController>(); controller.radius = .2f;
            controller.height = 1.8f; controller.center = Vector3.up * .9f;
            player.transform.position = f.Origin + Vector3.right * .7f;
            Physics.SyncTransforms(); controller.Move(Vector3.forward * 6);
            Check(player.transform.position.z > f.Origin.z + 5, "Query cone physically blocked character traversal beside the trunk.");
            for (int i = 0; i < 100; i++) query.Sample();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) query.Sample();
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Settled convex canopy query allocates managed memory.");
            query.enabled = false;
            typeof(PlayerQuery).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(query, null);
            Check(!query.Current.HasHit && !query.SolidHit.HasHit && !query.QueryOnlyHit.HasHit,
                "Disabling the player query retained one of its role results.");
        }
        finally { Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(player); Object.DestroyImmediate(wall); }
    }

    private static void ValidateQueryOnlyTree()
    {
        using var f = new Fixture(false);
        f.UpdateAtDistance(4, 0);
        Check(f.Manager.TryGetProxy(f.Id, out var proxy) && !proxy.PhysicalTrunkActive && proxy.QueryCanopyActive,
            "A canopy-only template failed to allocate its query proxy.");
        f.Settings.gameplay.enableCanopyQueries = false; f.UpdateAtDistance(4, .1);
        Check(f.Manager.ActiveCount == 0 && !proxy.IsBound, "Disabling the only active role retained a proxy.");
    }

    private static void ValidateInvalidAssignments()
    {
        using var f = new Fixture();
        var authoring = f.Prefab.GetComponent<TreeGameplayAuthoring>();
        var trunk = f.Prefab.GetComponentInChildren<CapsuleCollider>(true);
        var wrongMeshObject = new GameObject("Nonconvex query"); wrongMeshObject.transform.SetParent(f.Prefab.transform, false);
        var wrongMesh = wrongMeshObject.AddComponent<MeshCollider>(); wrongMesh.sharedMesh = f.Cone;
        var missingMeshObject = new GameObject("Missing mesh"); missingMeshObject.transform.SetParent(f.Prefab.transform, false);
        var missingMesh = missingMeshObject.AddComponent<MeshCollider>(); missingMesh.convex = true;
        var wrongOwner = new GameObject("Foreign query shape"); var foreign = wrongOwner.AddComponent<BoxCollider>();
        try
        {
            Assign(authoring, "queryCanopyColliders", new Collider[] { trunk, wrongMesh, missingMesh, foreign, f.Source, f.Source });
            f.UpdateAtDistance(4, 0);
            Check(f.Manager.TryGetProxy(f.Id, out var proxy) && proxy.QueryCanopyColliders.Count == 1 &&
                proxy.QueryCanopyColliders[0] is MeshCollider, "Invalid/same-object/duplicate query assignments were copied.");
            Check(proxy.PhysicalTrunkColliders.All(c => c.gameObject.layer == GameplayLayers.WorldSolid),
                "Query assignment changed a physical trunk collider's layer.");
        }
        finally { Object.DestroyImmediate(wrongOwner); }
    }
}
