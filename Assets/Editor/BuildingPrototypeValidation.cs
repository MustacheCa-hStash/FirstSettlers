using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Cursor = UnityEngine.Cursor;

public static class BuildingPrototypeValidation
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(Vector3 actual, Vector3 expected, string message) => Check((actual - expected).sqrMagnitude < .000001f, message + $" ({actual} != {expected})");
    private static void Call(object obj, string method) => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null);
    private static void Set(Object obj, string field, Object value)
    { var serialized = new SerializedObject(obj); serialized.FindProperty(field).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }

    [MenuItem("Tools/Building/Validate Prototype (synthetic)")]
    public static void Run()
    {
        checks = 0;
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        Check(catalog != null && catalog.presets.Length == 3, "Prototype catalog is missing.");
        ValidateAssets(catalog); ValidateGeometry(catalog); ValidateSession(catalog); ValidatePhysicsAndUI(catalog);
        Debug.Log("BUILDING PROTOTYPE PASS: " + checks + " checks; dimensions/colliders, rotated discrete frames, face connections, cross-cell records, " +
            "alternate support and detached cycles, foundation placement, blockers, pooled query identity and UI lifecycle.");
    }
    public static void RunBatch()
    {
        try { BuildingPrototypeSetup.CreateAssets(); Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    private static void ValidateAssets(BuildCatalog catalog)
    {
        foreach (var definition in catalog.presets)
        {
            Near(definition.mesh.bounds.center, definition.LocalBounds.center, "Mesh pivot/bounds disagree.");
            Near(definition.mesh.bounds.size, definition.LocalBounds.size, "Mesh dimensions disagree.");
            var collider = definition.authoringPrefab.GetComponent<BoxCollider>();
            Near(collider.center, definition.LocalBounds.center, "Collider centre is wrong."); Near(collider.size, definition.LocalBounds.size, "Collider size is wrong.");
            Check(!collider.isTrigger && definition.material.enableInstancing, "Collision or instancing is disabled.");
            Check(definition.material.GetTexture("_BaseMap") == null, "The prototype material is not plain.");
        }
        Near(catalog.presets[0].LocalBounds.size, new Vector3(4, 2.75f, .25f), "Wall contract changed.");
        Check(catalog.panel != null && catalog.layout != null, "Build UI assets are absent.");
    }
    private static void ValidateGeometry(BuildCatalog catalog)
    {
        Check(BuildGeometry.Tick(-.375f) == -2 && BuildGeometry.Tick(.375f) == 2, "Negative-coordinate snapping is asymmetric.");
        Check(BuildGeometry.Turn(-1) == 7 && BuildGeometry.Turn(8) == 0, "Rotation wrapping failed.");
        var frame = new BuildGridFrame { Origin = new Vector3(12, 6, -7), YawStep = 1 };
        Vector3 world = BuildGeometry.WorldPoint(frame, new Vector3Int(16, 0, 0));
        Near(BuildGeometry.LocalPoint(frame, world), new Vector3(4, 0, 0), "Diagonal grid roundtrip failed.");
        Check(Mathf.Abs(Vector3.Distance(frame.Origin, world) - 4) < .00001f, "Diagonal placement changed component length.");
        var wall = catalog.presets[0]; var floor = catalog.presets[1]; var foundation = catalog.presets[2];
        var ground = BuildPlacement.Solve(foundation, new Vector3(-1.11f, 5.13f, 2.91f), Vector3.up, null, null, 1, 0, default);
        Check(ground.WorldYaw == 1 && Mathf.Abs(ground.Origin.y - 5.5f) < .001f, "Ground foundation did not quantize footing/elevation.");
        var target = new BuildPieceRecord { Definition = foundation, WorldYawStep = 1 };
        var attached = BuildPlacement.Solve(wall, frame.Origin + BuildGeometry.Rotation(1) * new Vector3(2, 0, 0), Vector3.up, target, frame, 0, 0, default);
        Check(attached.WorldYaw == 1, "A diagonal floor failed to align the wall.");
        Near(attached.Origin, frame.Origin, "Wall edge does not align with the foundation.");
        var nudged = BuildPlacement.Solve(wall, frame.Origin + BuildGeometry.Rotation(1) * new Vector3(2, 0, 0), Vector3.up, target, frame, 0, 1, new Vector3Int(1, 1, 0));
        Check(nudged.WorldYaw == 2, "Relative 45-degree rotation failed.");
        Near(BuildGeometry.LocalPoint(frame, nudged.Origin), new Vector3(.25f, .25f, 0), "Nudges left the local lattice.");
        Check(BuildGeometry.Connects(foundation.LocalBounds, frame.Origin, 1, wall.LocalBounds, frame.Origin, 1), "A wall on a diagonal foundation has no face connection.");
        Check(!BuildGeometry.Overlaps(foundation.LocalBounds, frame.Origin, 1, wall.LocalBounds, frame.Origin, 1), "Allowed face contact counts as penetration.");
        Check(BuildGeometry.Overlaps(wall.LocalBounds, Vector3.zero, 0, wall.LocalBounds, Vector3.zero, 1), "Rotated penetration was missed.");
        Check(!BuildGeometry.Connects(floor.LocalBounds, Vector3.zero, 0, floor.LocalBounds, new Vector3(4, 0, 4), 0), "Corner-only floor contact transmits support.");
        Check(BuildGeometry.Distance(floor.LocalBounds, Vector3.zero, 1, new Vector3(.1f, 0, 2.5f)) > 1,
            "Rotated empty AABB corners captured snapping.");
        var onTop = BuildPlacement.Solve(floor, frame.Origin, Vector3.up, target, frame, 0, 0, default);
        Check(!BuildGeometry.Overlaps(foundation.LocalBounds, frame.Origin, 1, floor.LocalBounds, onTop.Origin, onTop.WorldYaw), "Floor placement intersects foundation.");
    }
    private static void ValidateSession(BuildCatalog catalog)
    {
        var session = new BuildSession(); var floor = catalog.presets[1]; var foundation = catalog.presets[2];
        var frame = session.CreateFrame(new Vector3(-4, 10, -4), 0);
        var left = session.Add(foundation, frame, default, 0, true);
        var middle = session.Add(floor, frame, new Vector3Int(16, 0, 0), 0, false);
        var right = session.Add(foundation, frame, new Vector3Int(32, 0, 0), 0, true);
        Check(middle.Supported && middle.Connections.Count == 2, "Bridge support connections are missing.");
        session.Remove(left.Id); Check(middle.Supported, "Removing one of two roots collapsed a supported bridge.");
        session.Remove(right.Id); Check(!middle.Supported, "Detached bridge falsely supports itself.");
        Near(session.Frame(left.OwnFrameId).Origin, left.Origin, "Removing an anchor discarded its grid frame.");
        var restored = session.Add(foundation, frame, default, 0, true); Check(middle.Supported, "Replacement support did not restore a detached piece.");
        var result = new List<BuildPieceRecord>();
        session.Query(new Bounds(new Vector3(.01f, 9.9f, -2), new Vector3(.02f, .1f, .1f)), result);
        Check(result.Contains(middle), "Cross-cell piece indexing lost geometry at a boundary.");
        var loopFrame = session.CreateFrame(new Vector3(40, 10, 40), 0);
        var loopA = session.Add(floor, loopFrame, default, 0, false);
        var loopB = session.Add(floor, loopFrame, new Vector3Int(16, 0, 0), 0, false);
        var loopC = session.Add(floor, loopFrame, new Vector3Int(16, 0, 16), 0, false);
        var loopD = session.Add(floor, loopFrame, new Vector3Int(0, 0, 16), 0, false);
        Check(!loopA.Supported && !loopB.Supported && !loopC.Supported && !loopD.Supported, "Disconnected cycle acquired support.");
        Check(session.Damage(restored.Id, 100) && !session.TryGet(restored.Id, out _) && !middle.Supported, "Damage did not update support and identity.");
    }
    private static void ValidatePhysicsAndUI(BuildCatalog catalog)
    {
        Vector3 origin = new(20000, 20000, 20000);
        var root = new GameObject("Building synthetic fixture"); root.transform.position = origin;
        var host = new GameObject("Building host"); host.transform.SetParent(root.transform, false);
        var groundObject = new GameObject("Fixture terrain") { layer = GameplayLayers.WorldSolid };
        groundObject.transform.position = origin + Vector3.down * .5f;
        var ground = groundObject.AddComponent<BoxCollider>(); ground.size = new Vector3(30, 1, 30); groundObject.AddComponent<WorldGroundSurface>();
        BuildMenuView view = null; BuildGameplay gameplay = null;
        var oldLock = Cursor.lockState; bool oldVisible = Cursor.visible;
        try
        {
            var world = host.AddComponent<BuildWorld>(); if (world.Session == null) Call(world, "Awake"); Physics.SyncTransforms();
            var foundation = BuildPlacement.Solve(catalog.presets[2], origin, Vector3.up, null, null, 1, 0, default);
            world.Validate(ref foundation); Check(foundation.Valid && foundation.Grounded, "Ground foundation was rejected: " + foundation.Message);
            var first = world.Commit(foundation); Check(first != null && first.Supported, "Foundation commit failed.");
            var unsupported = BuildPlacement.Solve(catalog.presets[0], origin + Vector3.right * 10, Vector3.up, null, null, 0, 0, default);
            world.Validate(ref unsupported); Check(!unsupported.Valid, "Unsupported wall was accepted.");
            var wall = BuildPlacement.Solve(catalog.presets[0], first.Origin + BuildGeometry.Rotation(first.WorldYawStep) * new Vector3(2, 0, 0), Vector3.up,
                first, world.Session.Frame(first.OwnFrameId), 0, 0, default);
            world.Validate(ref wall); Check(wall.Valid, "Supported wall was rejected: " + wall.Message);
            var placedWall = world.Commit(wall); Check(placedWall != null && placedWall.Supported, "Wall commit failed.");
            world.Validate(ref wall); Check(!wall.Valid, "Duplicate wall placement was accepted.");
            var blocked = BuildPlacement.Solve(catalog.presets[0], first.Origin + BuildGeometry.Rotation(first.WorldYawStep) * new Vector3(2, 0, 4), Vector3.up,
                first, world.Session.Frame(first.OwnFrameId), 0, 0, default);
            var blocker = new GameObject("Fixture player") { layer = GameplayLayers.Player };
            blocker.transform.SetParent(root.transform, true); blocker.transform.position = blocked.Origin + BuildGeometry.Rotation(blocked.WorldYaw) * blocked.Definition.LocalBounds.center;
            blocker.AddComponent<BoxCollider>().size = Vector3.one * .1f; Physics.SyncTransforms();
            world.Validate(ref blocked); Check(!blocked.Valid && blocked.Message == "Move clear of the preview", "Player penetration is not blocked.");
            Object.DestroyImmediate(blocker);
            world.Remove(first.Id); Check(!placedWall.Supported, "Wall retained support after its last foundation was removed.");
            var repair = BuildPlacement.Solve(catalog.presets[2], placedWall.Origin, Vector3.up, placedWall,
                world.Session.Frame(placedWall.OwnFrameId), 0, 0, default);
            world.Validate(ref repair); Check(repair.Valid && repair.Grounded, "Aiming at the detached wall could not replace its footing.");
            first = world.Commit(repair); Check(first != null && placedWall.Supported, "Replacing the footing did not restore support.");
            gameplay = new BuildGameplay(world.Session); gameplay.Update(first.Origin, 32, 40, 8);
            Check(gameplay.ActiveCount == 2, "Nearby pieces did not activate pooled colliders.");
            var proxies = Object.FindObjectsByType<BuildGameplayProxy>();
            BuildGameplayProxy proxy = null;
            foreach (var candidate in proxies) if (candidate.Record == placedWall) proxy = candidate;
            Check(proxy != null && proxy.GetComponent<MeshRenderer>() == null && proxy.TryGetInfo(out _), "Gameplay proxy owns rendering or lacks identity.");
            proxy.TryGetInfo(out var identity);
            gameplay.Update(origin + Vector3.one * 100, 32, 40, 8);
            Check(gameplay.ActiveCount == 0 && !identity.IsValid && world.Session.Pieces.Count == 2, "Collider release lost records or retained stale identity.");
            BuildDefinition clicked = null; view = new BuildMenuView(catalog, root.transform, item => clicked = item);
            view.SetState(true, true);
            var document = root.GetComponentInChildren<UIDocument>();
            Check(document.rootVisualElement.Q<VisualElement>("build-menu").style.display.value == DisplayStyle.Flex &&
                document.rootVisualElement.Query<Button>().ToList().Count == 3, "Picker layout/preset population failed.");
            view.SetState(true, false); view.Show("Plain wall", "Ready", true);
            Check(document.rootVisualElement.Q<Label>("build-status").text == "Ready" &&
                document.rootVisualElement.Q<VisualElement>("build-hud").pickingMode == PickingMode.Ignore, "Placement HUD failed or captures input.");
            view.Dispose(); view = null;
            var controller = host.AddComponent<BuildingController>(); Call(controller, "Start"); controller.Toggle();
            var proximity = typeof(BuildingController).GetMethod("NearbyTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 At(Vector3 local) => first.Origin + BuildGeometry.Rotation(first.WorldYawStep) * local;
            Check(proximity.Invoke(controller, new object[] { At(new Vector3(2, -.3f, -.05f)) }) == first, "Small ground margin did not acquire the local frame.");
            Check(proximity.Invoke(controller, new object[] { At(new Vector3(2, -.3f, -.25f)) }) == null, "Distant ground retained a structure's bias.");
            typeof(BuildingController).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, first);
            Check(proximity.Invoke(controller, new object[] { At(new Vector3(2, -.3f, -.12f)) }) == first, "Snap hysteresis released too soon.");
            Check(proximity.Invoke(controller, new object[] { At(new Vector3(2, -.3f, -.16f)) }) == null, "Snap hysteresis retained a distant target.");
            Check(controller.Active && controller.MenuOpen && Cursor.lockState == CursorLockMode.None, "B mode did not release the menu cursor.");
            controller.Select(catalog.presets[0]); Check(controller.Active && !controller.MenuOpen, "Selection did not enter placement.");
            controller.Exit(); Check(!controller.Active && Cursor.lockState == oldLock && Cursor.visible == oldVisible, "Exit did not restore cursor state.");
        }
        finally
        {
            view?.Dispose(); gameplay?.Dispose();
            foreach (var build in root.GetComponentsInChildren<BuildWorld>(true)) Call(build, "OnDestroy");
            Object.DestroyImmediate(root); Object.DestroyImmediate(groundObject); Cursor.lockState = oldLock; Cursor.visible = oldVisible; Physics.SyncTransforms();
        }
    }
}
