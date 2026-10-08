using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using System.Reflection;
using Object = UnityEngine.Object;

/// <summary>Input System button edge tests require a player update phase. Only a new empty synthetic scene is used.</summary>
[InitializeOnLoad]
public static class BuildingPrototypeInputValidation
{
    private const string Pending = "FS.Building.SyntheticInputValidation";
    private static GameObject rig;
    private static Keyboard keyboard;
    private static Mouse mouse;
    private static LocalPlayerInput input;
    private static BuildingController controller;
    private static int stage;
    private static float started;
    private static Quaternion beforeMenu;
    private static Key[] nextKeys;
    private static bool injectPending;
    private static Vector2 nextMouseDelta;
    static BuildingPrototypeInputValidation() { EditorApplication.playModeStateChanged += OnState; }
    public static void RunBatch()
    {
        BuildingPrototypeSetup.CreateAssets();
        SessionState.SetBool(Pending, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    private static void OnState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        try
        {
            BuildingPrototypeValidation.Run();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true;
            rig = new GameObject("Synthetic input rig"); rig.transform.position = new Vector3(20000, 20000, 20000);
            var motor = rig.AddComponent<CharacterMotor>();
            var viewPivot = new GameObject("Synthetic view pivot"); viewPivot.transform.SetParent(rig.transform, false);
            var look = viewPivot.AddComponent<FirstPersonLook>();
            Set(look, "yawRoot", rig.transform);
            var host = new GameObject("Synthetic local input"); host.transform.SetParent(rig.transform, false);
            controller = host.AddComponent<BuildingController>(); input = host.AddComponent<LocalPlayerInput>();
            Set(input, "motor", motor); Set(input, "look", look); input.SetBuilding(controller);
            // Native input frames run, while the hardware reader is invoked once by this fixture.
            input.enabled = false;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            stage = -1; started = Time.realtimeSinceStartup;
            InputSystem.onBeforeUpdate += Inject; InputSystem.onAfterUpdate += CheckFrame; EditorApplication.update += CheckTimeout;
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    private static void Set(Object target, string field, Object value)
    { var properties = new SerializedObject(target); properties.FindProperty(field).objectReferenceValue = value; properties.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Read() => typeof(LocalPlayerInput).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Queue(params Key[] keys) { nextKeys = keys; injectPending = true; }
    private static void Inject()
    {
        if (!injectPending || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        injectPending = false;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(nextKeys));
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = nextMouseDelta }); nextMouseDelta = default;
    }
    private static void CheckTimeout()
    { if (Time.realtimeSinceStartup - started > 15) Finish(new InvalidOperationException("Synthetic native input frames timed out.")); }
    private static void CheckFrame()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic || controller == null || controller.World == null) return;
        try
        {
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            switch (stage++)
            {
                case -1:
                    controller.Toggle(); controller.Select(controller.World.Catalog.presets[0]); Queue(); break;
                case 0:
                    Read();
                    var pending = (BuildInputCommand)typeof(BuildingController).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                    Require(pending.Turn == 0, "Idle wheel rotates the preview."); Queue(Key.E); break;
                case 1:
                    Read();
                    pending = (BuildInputCommand)typeof(BuildingController).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                    Require(pending.Turn == 1, "E did not rotate by one step.");
                    Read(); pending = (BuildInputCommand)typeof(BuildingController).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                    Require(pending.Turn == 0, "Holding E repeated rotation."); Queue(); break;
                case 2: Queue(Key.B); break;
                case 3:
                    Read(); Require(!controller.Active, "B did not exit building.");
                    Require(controller.GameplayCursorRequested, "B exit did not request gameplay cursor capture.");
                    Read(); Require(!controller.Active, "Holding B reopened building."); Queue(); break;
                case 4:
                    Read(); beforeMenu = rig.transform.rotation; nextMouseDelta = new Vector2(100, 50); Queue(Key.B); break;
                case 5:
                    Read(); Require(controller.Active && controller.MenuOpen && rig.transform.rotation == beforeMenu, "B picker did not block look input.");
                    Require(!controller.GameplayCursorRequested, "Picker retained cursor capture."); Queue(); break;
                case 6:
                    Read(); controller.Select(controller.World.Catalog.presets[3]);
                    Require(controller.GameplayCursorRequested && !controller.MenuOpen, "Preset selection did not restore capture intent.");
                    beforeMenu = rig.transform.rotation; nextMouseDelta = new Vector2(100, 50); Queue(); break;
                case 7:
                    Read(); Require(rig.transform.rotation == beforeMenu, "Relocking mouse jumped the camera.");
                    nextMouseDelta = new Vector2(100, 50); Queue(); break;
                case 8:
                    Read(); Require(rig.transform.rotation == beforeMenu, "Deferred mouse delta jumped the camera.");
                    nextMouseDelta = new Vector2(10, 0); Queue(); break;
                case 9:
                    Read(); Require(Quaternion.Angle(rig.transform.rotation, beforeMenu) > .1f,
                        $"Look did not resume (delta={mouse.delta.ReadValue()}, skip={typeof(LocalPlayerInput).GetField("suppressLookFrames", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input)}, menu={controller.MenuOpen}).");
                    Queue(Key.Escape); break;
                case 10:
                    Read(); Require(!controller.Active && controller.GameplayCursorRequested, "Escape exit did not restore gameplay capture intent.");
                    Debug.Log("BUILDING INPUT PASS: native B/E/Escape, held-key guards, picker capture transitions, two-frame mouse-delta suppression and resumed look; empty synthetic scene only."); Finish(null); break;
            }
        }
        catch (Exception ex) { Finish(ex); }
    }
    private static void Finish(Exception error)
    {
        InputSystem.onBeforeUpdate -= Inject; InputSystem.onAfterUpdate -= CheckFrame; EditorApplication.update -= CheckTimeout;
        if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse);
        if (rig != null) Object.DestroyImmediate(rig);
        if (error != null) Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
