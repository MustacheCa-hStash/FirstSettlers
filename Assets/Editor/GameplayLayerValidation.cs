using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GameplayLayerValidation
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static object Invoke(CharacterMotor motor, string method) => typeof(CharacterMotor).GetMethod(method, Private).Invoke(motor, null);
    private static GameObject Box(string name, Vector3 position, Vector3 size, int layer)
    {
        var obj = new GameObject(name) { layer = layer }; obj.transform.position = position;
        obj.AddComponent<BoxCollider>().size = size; return obj;
    }

    [MenuItem("Tools/Terrain/Validate Gameplay Layers")]
    public static void Run()
    {
        checks = 0;
        ValidateConfiguration();
        ValidateMotorQueries();
        ValidateRuntimeAssignments();
        TreeGameplayValidation.Run();
        ValidateScene();
        Debug.Log("GAMEPLAY LAYERS PASS: " + checks + " checks; physics matrix, solid grounding and headroom, " +
            "query/player exclusion, query trigger ray access, generated rock/terrain layers, tree collision regression and SmearScene settings.");
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateConfiguration()
    {
        Check(LayerMask.NameToLayer("WorldSolid") == GameplayLayers.WorldSolid, "WorldSolid index mismatch.");
        Check(LayerMask.NameToLayer("Player") == GameplayLayers.Player, "Player index mismatch.");
        Check(LayerMask.NameToLayer("QueryOnly") == GameplayLayers.QueryOnly, "QueryOnly index mismatch.");
        Check(LayerMask.NameToLayer("BreakableRock") == -1, "Object-specific BreakableRock layer remains configured.");
        for (int i = 0; i < 32; i++) Check(Physics.GetIgnoreLayerCollision(GameplayLayers.QueryOnly, i), "QueryOnly produces physical contacts with layer " + i);
        Check(!Physics.GetIgnoreLayerCollision(GameplayLayers.Player, GameplayLayers.WorldSolid), "Player does not collide with solid surfaces.");
    }

    private static void ValidateMotorQueries()
    {
        var player = new GameObject("Layer test player"); var controller = player.AddComponent<CharacterController>();
        var motor = player.AddComponent<CharacterMotor>(); Invoke(motor, "Awake");
        var slope = Box("Layer test slope", new Vector3(0, -.5f, 0), new Vector3(10, 1, 10), GameplayLayers.WorldSolid);
        slope.transform.rotation = Quaternion.Euler(0, 0, 20);
        var queryFloor = Box("Layer test query floor", new Vector3(0, .25f, 0), new Vector3(2, .05f, 2), GameplayLayers.QueryOnly);
        var otherPlayer = Box("Layer test other player", new Vector3(0, .4f, 0), new Vector3(2, .05f, 2), GameplayLayers.Player);
        var headroom = Box("Layer test headroom", new Vector3(0, 1.5f, 0), Vector3.one * .2f, GameplayLayers.QueryOnly);
        try
        {
            Check(player.layer == GameplayLayers.Player, "Motor did not assign the player layer.");
            Check(((LayerMask)typeof(CharacterMotor).GetField("solidSurfaceMask", Private).GetValue(motor)).value == GameplayLayers.SolidSurfaceMask,
                "Motor's default mask is not the explicit solid surface mask.");
            Physics.SyncTransforms();
            var normal = (Vector3)Invoke(motor, "ProbeGroundNormal");
            Check(Mathf.Abs(Vector3.Angle(normal, Vector3.up) - 20) < .1f, "Ground probe hit query/player geometry instead of WorldSolid slope.");
            Check((bool)Invoke(motor, "CanStand"), "Query-only/player geometry blocked headroom.");
            headroom.layer = GameplayLayers.WorldSolid; Physics.SyncTransforms();
            Check(!(bool)Invoke(motor, "CanStand"), "WorldSolid failed to block headroom.");
            headroom.layer = GameplayLayers.QueryOnly;
            queryFloor.GetComponent<BoxCollider>().isTrigger = true; Physics.SyncTransforms();
            Check(Physics.Raycast(new Vector3(0, 1, 0), Vector3.down, out var hit, 2, 1 << GameplayLayers.QueryOnly, QueryTriggerInteraction.Collide) &&
                hit.collider == queryFloor.GetComponent<BoxCollider>(), "Contact-disabled QueryOnly triggers cannot be explicitly ray queried.");
            slope.SetActive(false); queryFloor.SetActive(false); otherPlayer.SetActive(false); headroom.SetActive(false);
            var rock = Box("Layer test rock wall", new Vector3(2, 1.5f, 0), new Vector3(1, 3, 2), GameplayLayers.WorldSolid);
            try
            {
                Physics.SyncTransforms();
                var flags = controller.Move(Vector3.right * 6);
                Check((flags & CollisionFlags.Sides) != 0 && player.transform.position.x < 1.5f, "Player moved through a breakable rock.");
            }
            finally { Object.DestroyImmediate(rock); }
        }
        finally
        {
            Object.DestroyImmediate(player); Object.DestroyImmediate(slope); Object.DestroyImmediate(queryFloor);
            Object.DestroyImmediate(otherPlayer); Object.DestroyImmediate(headroom);
        }
    }

    private static void ValidateRuntimeAssignments()
    {
        var root = new GameObject("Layer test foliage root");
        var source = new GameObject("Layer test rock asset"); source.SetActive(false);
        var child = new GameObject("Rock shape"); child.transform.SetParent(source.transform, false); child.AddComponent<BoxCollider>();
        var trigger = new GameObject("Authored trigger"); trigger.transform.SetParent(source.transform, false);
        trigger.layer = GameplayLayers.QueryOnly; trigger.AddComponent<BoxCollider>().isTrigger = true;
        var runtime = new ChunkFoliageRuntime { root = root.transform, forestRockPrefabs = new[] { source }, grasslandRockPrefabs = new[] { source },
            grasslandLargeRockPrefabs = new[] { source } };
        try
        {
            runtime.RebuildRockGameObjects(new List<RockInstanceData>
            {
                new(Vector3.zero, Quaternion.identity, Vector3.one, WorldFeatureVariant.Boulder, 0),
                new(Vector3.right * 3, Quaternion.identity, Vector3.one, WorldFeatureVariant.GrasslandBoulder, 0),
                new(Vector3.right * 6, Quaternion.identity, Vector3.one, WorldFeatureVariant.GrasslandLargeBoulder, 0)
            }, root.transform);
            var colliders = root.GetComponentsInChildren<Collider>(true);
            Check(colliders.Length == 6 && colliders.Where(c => !c.isTrigger).All(c => c.gameObject.layer == GameplayLayers.WorldSolid),
                "Generated forest/grassland/large rocks are missing the shared solid role.");
            Check(colliders.Where(c => c.isTrigger).All(c => c.gameObject.layer == GameplayLayers.QueryOnly), "Rock spawn overwrote authored trigger roles.");
            Check(source.layer == 0 && child.layer == 0, "Runtime rock assignment changed source authoring.");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(source); }
        var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        var chunk = new ChunkRuntime(new ChunkRecord(default), 128, .3f, null, material, material, true);
        try { Check(chunk.Root.layer == GameplayLayers.WorldSolid, "Generated terrain layer is incorrect."); }
        finally
        {
            // Destroy only this fixture's owned native objects; the runtime's usual deferred disposal is for Play Mode.
            var terrainMat = (Material)typeof(ChunkRuntime).GetField("runtimeTerrainMaterial", Private).GetValue(chunk);
            var waterMat = (Material)typeof(ChunkRuntime).GetField("runtimeWaterMaterial", Private).GetValue(chunk);
            Object.DestroyImmediate(chunk.Root); Object.DestroyImmediate(terrainMat); Object.DestroyImmediate(waterMat); Object.DestroyImmediate(material);
        }
    }

    private static void ValidateScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity", OpenSceneMode.Single);
        var motor = Object.FindAnyObjectByType<CharacterMotor>();
        Check(motor != null && motor.gameObject.layer == GameplayLayers.Player, "SmearScene player layer is incorrect.");
        Check(((LayerMask)typeof(CharacterMotor).GetField("solidSurfaceMask", Private).GetValue(motor)).value == GameplayLayers.SolidSurfaceMask,
            "SmearScene serialized motor mask is incorrect.");
        foreach (var camera in Object.FindObjectsByType<Camera>())
            Check((camera.cullingMask & GameplayLayers.SolidSurfaceMask) == GameplayLayers.SolidSurfaceMask, "Camera hides terrain/rock layers: " + camera.name);
    }
}
