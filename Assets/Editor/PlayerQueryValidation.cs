using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class PlayerQueryValidation
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Set(PlayerQuery query, string field, Object value)
    {
        var serialized = new SerializedObject(query);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetRange(PlayerQuery query, float value)
    {
        var serialized = new SerializedObject(query);
        serialized.FindProperty("maxDistance").floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    private static BoxCollider Box(Transform root, string name, float z, int layer, bool trigger = false)
    {
        var obj = new GameObject(name) { layer = layer };
        obj.transform.SetParent(root, false);
        obj.transform.localPosition = Vector3.forward * z;
        var collider = obj.AddComponent<BoxCollider>();
        collider.isTrigger = trigger;
        return collider;
    }

    [MenuItem("Tools/Terrain/Validate Player Query")]
    public static void Run()
    {
        checks = 0;
        ValidateQuery();
        QueryTargetValidation.Run();
        ValidateScene();
        Debug.Log("PLAYER QUERY PASS: " + checks + " checks; closest solid/query hit, obstruction, role filters, " +
            "camera pose/projection, range, lifecycle clearing, zero settled managed allocations and SmearScene wiring.");
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateQuery()
    {
        // Remote fixture coordinates keep the test away from objects in the user's open scene.
        var root = new GameObject("Player query validation fixture");
        root.transform.position = new Vector3(10000, 10000, 10000);
        var cameraObject = new GameObject("Fixture camera"); cameraObject.transform.SetParent(root.transform, false);
        var camera = cameraObject.AddComponent<Camera>(); camera.nearClipPlane = .3f;
        var query = root.AddComponent<PlayerQuery>();
        Set(query, "viewCamera", camera);
        var far = Box(root.transform, "Far solid", 4, GameplayLayers.WorldSolid);
        var near = Box(root.transform, "Near solid", 2, GameplayLayers.WorldSolid);
        var ownBody = Box(root.transform, "Excluded player", 1, GameplayLayers.Player);
        var physicalTrigger = Box(root.transform, "Ignored solid trigger", 1.1f, GameplayLayers.WorldSolid, true);
        var queryShape = Box(root.transform, "Query volume", 3, GameplayLayers.QueryOnly, true);
        bool oldQueriesHitTriggers = Physics.queriesHitTriggers;
        try
        {
            Physics.queriesHitTriggers = false;
            Physics.SyncTransforms(); query.Sample();
            Check(query.Current.Collider == near, "Nearest solid lost to another collider or a query shape behind it.");
            Check(query.Current.Kind == PlayerQueryHitKind.Solid, "Physical hit has incorrect role.");
            Check(Mathf.Abs(query.Current.Distance - 1.2f) < .01f, "Distance is not measured from the near-plane ray origin.");
            Check((query.Current.Point - (root.transform.position + Vector3.forward * 1.5f)).sqrMagnitude < .0001f,
                "Hit point is incorrect.");
            Check(Vector3.Dot(query.Current.Normal, Vector3.back) > .999f, "Hit normal is incorrect.");

            near.enabled = false; Physics.SyncTransforms(); query.Sample();
            Check(query.Current.Collider == queryShape && query.Current.Kind == PlayerQueryHitKind.QueryOnly,
                "QueryOnly trigger does not work with global trigger queries disabled.");
            queryShape.isTrigger = false; Physics.SyncTransforms(); query.Sample();
            Check(query.Current.Collider == queryShape, "A QueryOnly non-trigger shape is missing from the view query.");
            queryShape.enabled = false; Physics.SyncTransforms(); query.Sample();
            Check(query.Current.Collider == far, "Ignored trigger/player geometry obscures the solid hit.");

            near.enabled = true; queryShape.enabled = true; queryShape.isTrigger = true;
            // More hits than a typical nonalloc buffer can hold: closest-hit queries must remain correct.
            for (int i = 0; i < 40; i++)
                Box(root.transform, "Dense query shape", 3.1f + i * .01f, GameplayLayers.QueryOnly, true);
            Physics.SyncTransforms(); query.Sample();
            Check(query.Current.Collider == near, "Dense query geometry bypassed the solid obstruction.");

            camera.transform.localRotation = Quaternion.Euler(0, 90, 0);
            typeof(PlayerQuery).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(query, null);
            Check(!query.Current.HasHit && query.Current.Kind == PlayerQueryHitKind.None, "Turning away leaves a stale hit.");
            Check(Vector3.Dot(query.ViewRay.direction, Vector3.right) > .999f,
                "Query ray did not use this frame's camera rotation.");
            camera.transform.localRotation = Quaternion.identity;
            camera.transform.localPosition = Vector3.right * 2; query.Sample();
            Check(!query.Current.HasHit, "Query ray ignored the camera's updated position.");
            camera.transform.localPosition = Vector3.zero;
            camera.lensShift = new Vector2(.25f, .1f); query.Sample();
            Ray expected = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            Check((query.ViewRay.origin - expected.origin).sqrMagnitude < .0001f &&
                Vector3.Dot(query.ViewRay.direction, expected.direction) > .999f, "Ray is not at viewport center for a shifted projection.");
            camera.lensShift = Vector2.zero;
            camera.orthographic = true; camera.orthographicSize = 3; query.Sample();
            Check(query.Current.Collider == near, "Orthographic center query failed.");
            camera.orthographic = false;

            SetRange(query, 1); query.Sample();
            Check(!query.Current.HasHit, "Out-of-range solid remains selected.");
            SetRange(query, 0); query.Sample();
            Check(!query.Current.HasHit && !query.HasViewRay, "Zero range leaves a stale result.");
            SetRange(query, 5); query.Sample();
            Check(query.Current.Collider == near, "Hit did not recover when range was restored.");
            camera.enabled = false; query.Sample();
            Check(!query.Current.HasHit && !query.HasViewRay, "Disabled camera leaves a stale result.");
            camera.enabled = true; query.Sample();
            query.enabled = false;
            // Non-ExecuteAlways behaviours do not receive normal Play Mode callbacks in this Edit Mode fixture.
            typeof(PlayerQuery).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(query, null);
            Check(!query.Current.HasHit && !query.HasViewRay, "Disabling the local query does not clear its result.");
            query.Sample(); Check(!query.Current.HasHit, "Disabled query still samples geometry.");
            query.enabled = true; query.Sample();
            Set(query, "viewCamera", null); query.Sample();
            Check(!query.Current.HasHit && !query.HasViewRay, "Missing camera leaves a stale result.");
            Set(query, "viewCamera", camera);

            for (int i = 0; i < 100; i++) query.Sample();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) query.Sample();
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Settled query samples allocate managed memory.");
            Object.DestroyImmediate(near.gameObject);
            Check(!query.Current.HasHit, "Destroyed collider still reports a live hit.");
            query.Sample();
            Check(query.Current.Kind == PlayerQueryHitKind.QueryOnly, "Query did not recover after collider destruction.");
        }
        finally
        {
            Physics.queriesHitTriggers = oldQueriesHitTriggers;
            Object.DestroyImmediate(root);
            Physics.SyncTransforms();
        }
    }

    private static void ValidateScene()
    {
        const string path = "Assets/Scenes/SmearScene.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            PlayerQuery query = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentsInChildren<PlayerQuery>(true);
                Check(found.Length <= 1 && (found.Length == 0 || query == null), "SmearScene has duplicate player query components.");
                if (found.Length == 1) query = found[0];
            }
            Check(query != null && query.enabled && query.gameObject.name == "Interaction", "Query is missing from the character Interaction object.");
            Check(query.GetComponentInParent<CharacterMotor>() != null, "Query is outside the character hierarchy.");
            Check(query.ViewCamera != null && query.ViewCamera.transform.IsChildOf(query.transform.parent), "Query camera is not explicitly assigned to this character's view.");
            Check(Mathf.Approximately(query.MaxDistance, 5), "Initial query range is incorrect.");
        }
        finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
    }
}
