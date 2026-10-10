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
    private static double arrowStarted;
    private static int arrowRepeats;
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
            // Legacy dimensional checks belong to the old 3.5m kit. Full-span geometry has its own Play-mode suite.
            var inputCatalog=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            if(inputCatalog.presets[0].jointSockets==null || inputCatalog.presets[0].jointSockets.Length==0)BuildingPrototypeValidation.Run();
            ValidateNudgeMappingAndRepeat();
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
    private static void ValidateNudgeMappingAndRepeat()
    {
        for (int grid=0; grid<8; grid++)
        for (int yaw=0; yaw<360; yaw+=15)
        foreach (float pitch in new[] { -80f,0f,80f,90f })
        {
            Quaternion view=Quaternion.Euler(pitch,yaw,0);
            Vector3 forward=Quaternion.Euler(0,yaw,0)*Vector3.forward, right=Quaternion.Euler(0,yaw,0)*Vector3.right;
            var f=BuildGeometry.ViewRelativeNudge(Vector3Int.forward,view,grid);
            var r=BuildGeometry.ViewRelativeNudge(Vector3Int.right,view,grid);
            Vector3 fw=BuildGeometry.Rotation(grid)*((Vector3)f*BuildGeometry.Unit);
            Vector3 rw=BuildGeometry.Rotation(grid)*((Vector3)r*BuildGeometry.Unit);
            Require(Mathf.Abs(fw.magnitude-BuildGeometry.Unit)<.00001f && Mathf.Abs(rw.magnitude-BuildGeometry.Unit)<.00001f,
                "An arrow moved more than one grid unit.");
            Require(Vector3.Dot(fw,forward)>=BuildGeometry.Unit*.707f && Vector3.Dot(rw,right)>=BuildGeometry.Unit*.707f,
                "Arrow movement points away from the player's intended direction.");
            Require(Mathf.Abs(Vector3.Dot(fw,rw))<.00001f,"Forward/right nudge axes are not perpendicular.");
            Require(BuildGeometry.ViewRelativeNudge(Vector3Int.back,view,grid)==-f && BuildGeometry.ViewRelativeNudge(Vector3Int.left,view,grid)==-r,
                "Opposite arrows are not exact inverse grid steps.");
            Require(BuildGeometry.ViewRelativeNudge(Vector3Int.up,view,grid)==Vector3Int.up,"Camera pitch tilted the vertical nudge.");
        }
        var repeat=new BuildNudgeRepeat();
        Require(repeat.Sample(true,true,1,true,.3,.1),"Arrow press did not immediately nudge.");
        Require(!repeat.Sample(true,false,1.29,true,.3,.1),"Held arrow repeated before its initial delay.");
        Require(repeat.Sample(true,false,1.31,true,.3,.1),"Held arrow failed to repeat after its initial delay.");
        Require(!repeat.Sample(true,false,1.4,true,.3,.1) && repeat.Sample(true,false,1.42,true,.3,.1),"Arrow repeat interval failed.");
        Require(repeat.Sample(true,false,10,true,.3,.1) && !repeat.Sample(true,false,10,true,.3,.1),"A stalled frame produced a burst of nudges.");
        Require(!repeat.Sample(false,false,10.1,true,.3,.1),"Released arrow kept repeating.");
        Require(repeat.Sample(true,true,10.2,false,.3,.1) && !repeat.Sample(true,false,11,false,.3,.1),"Repeat-disabled mode did not retain tap behavior.");
        repeat=default;
        Require(!repeat.Sample(true,false,12,true,.3,.1),"A menu/reset hold resumed without a fresh press.");
        Debug.Log("BUILDING NUDGE MATH PASS: player-relative unit steps across eight grids, yaw/pitch incl. vertical look, inverse arrows, repeat timing/release/reset/disabled mode and no stall catch-up.");
    }
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
                    controller.Toggle(); controller.Select(controller.World.Catalog.presets[0]); Queue(); break;
                case 11: Read(); Queue(Key.DownArrow); break;
                case 12:
                    Read(); Require(PendingNudge()==Vector3Int.back,"Down arrow did not send a backward step.");
                    arrowStarted=Time.unscaledTimeAsDouble; arrowRepeats=0; Queue(Key.DownArrow); break;
                case 13:
                    Read();
                    if (PendingNudge()!=Vector3Int.zero)
                    {
                        Require(PendingNudge()==Vector3Int.back,"Held down arrow sent a wrong direction.");
                        Require(Time.unscaledTimeAsDouble-arrowStarted>=.28,"Held arrow repeated immediately.");
                        arrowRepeats++;
                    }
                    if (arrowRepeats<2) { stage=13; Queue(Key.DownArrow); }
                    else Queue(); break;
                case 14:
                    Read(); Require(PendingNudge()==Vector3Int.zero,"Released arrow generated another nudge."); Queue(Key.RightArrow); break;
                case 15:
                    Read(); Require(PendingNudge()==Vector3Int.right,"Right arrow did not send a rightward step.");
                    controller.OpenMenu(); Queue(Key.RightArrow); break;
                case 16:
                    Read(); controller.Select(controller.World.Catalog.presets[0]);
                    arrowStarted=Time.unscaledTimeAsDouble; Queue(Key.RightArrow); break;
                case 17:
                    Read(); Require(PendingNudge()==Vector3Int.zero,"Picker-held arrow resumed repeating after selection.");
                    if (Time.unscaledTimeAsDouble-arrowStarted<.4) { stage=17; Queue(Key.RightArrow); }
                    else Queue(); break;
                case 18: Read(); Queue(Key.LeftArrow); break;
                case 19:
                    Read(); Require(PendingNudge()==Vector3Int.left,"Fresh press after picker suppression did not nudge.");
                    Queue(Key.LeftArrow,Key.RightArrow); break;
                case 20:
                    Read(); Require(PendingNudge()==Vector3Int.zero,"Opposite held arrows did not cancel.");
                    Queue(Key.F8);break;
                case 21:
                    Read();Require(controller.World.ColliderDebug.Mode==BuildColliderDebugMode.Solids,"F8 did not enable collider diagnostics.");
                    Read();Require(controller.World.ColliderDebug.Mode==BuildColliderDebugMode.Solids,"Held F8 cycled twice.");
                    Queue();break;
                case 22:Read();Queue(Key.F9);break;
                case 23:
                    Read();Require(controller.World.ColliderDebug.Frozen,"F9 did not freeze diagnostics.");
                    Read();Require(controller.World.ColliderDebug.Frozen,"Held F9 unfroze diagnostics.");
                    controller.Exit();Queue();break;
                case 24:Read();Queue(Key.F8);break;
                case 25:
                    Read();Require(controller.World.ColliderDebug.Mode==BuildColliderDebugMode.SolidsAndClearance && !controller.World.ColliderDebug.Frozen,"F8 failed outside build mode.");
                    Queue();break;
                case 26:Read();Queue(Key.F8);break;
                case 27:
                    Read();Require(controller.World.ColliderDebug.Mode==BuildColliderDebugMode.Off,"F8 did not disable diagnostics.");
                    var cameraObject=new GameObject("Gable flip input camera");cameraObject.transform.SetParent(rig.transform,false);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;Set(controller,"viewCamera",camera);
                    controller.Toggle();controller.Select(controller.World.Catalog.Find(OpeningWallSetup.GableId));Queue(Key.R);break;
                case 28:
                    Read();Require(PendingCommand().Flip,"R did not submit slope flip");ApplyController();
                    Require(Selected().contentId==OpeningWallSetup.FallingId,"R did not select mirrored representation");
                    Read();Require(!PendingCommand().Flip,"Held R repeated flip");ApplyController();Require(Selected().contentId==OpeningWallSetup.FallingId,"Held R flipped back");Queue();break;
                case 29:Read();Queue(Key.R);break;
                case 30:
                    Read();ApplyController();Require(Selected().contentId==OpeningWallSetup.GableId,"Second fresh R did not restore rising slope");controller.OpenMenu();Queue(Key.R);break;
                case 31:Read();controller.Select(controller.World.Catalog.Find(OpeningWallSetup.GableId));Queue(Key.R);break;
                case 32:
                    Read();Require(!PendingCommand().Flip,"Picker-held R replayed slope flip");
                    Debug.Log("BUILDING INPUT PASS: native B/E/Escape/F8/F9/R, gable representation flip/held/menu guards, picker capture/look, arrow repeat and diagnostics; empty synthetic scene only.");Finish(null);break;
            }
        }
        catch (Exception ex) { Finish(ex); }
    }
    private static Vector3Int PendingNudge() => ((BuildInputCommand)typeof(BuildingController).GetField("pending",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller)).Nudge;
    private static BuildInputCommand PendingCommand()=>(BuildInputCommand)typeof(BuildingController).GetField("pending",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
    private static BuildDefinition Selected()=>(BuildDefinition)typeof(BuildingController).GetField("selected",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
    private static void ApplyController()=>typeof(BuildingController).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);
    private static void Finish(Exception error)
    {
        InputSystem.onBeforeUpdate -= Inject; InputSystem.onAfterUpdate -= CheckFrame; EditorApplication.update -= CheckTimeout;
        if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse);
        if (rig != null) Object.DestroyImmediate(rig);
        if (error != null) Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
