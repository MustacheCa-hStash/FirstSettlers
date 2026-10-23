using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Cursor = UnityEngine.Cursor;

/// <summary>Real UI Toolkit rendering and native input frames in a new empty scene; no sample items.</summary>
[InitializeOnLoad]
public static class InventoryUiValidation
{
    private const BindingFlags Methods = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Captures = "ArtReferences/UIRedesign/ImplementedInventory";
    private const string Pending = "FS.Inventory.InputValidation";
    private static GameObject rig;
    private static LocalPlayerInput input;
    private static BuildingController building;
    private static Keyboard keyboard;
    private static Mouse mouse;
    private static Key[] nextKeys = Array.Empty<Key>();
    private static bool injectPending;
    private static Vector2 delta;
    private static ushort mouseButtons;
    private static int stage;
    private static float started;
    private static Quaternion rotation;
    private static Vector3 position;
    private static double walkStarted;
    static InventoryUiValidation() => EditorApplication.playModeStateChanged += OnState;

    public static void RunBatch()
    {
        try { Validate(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Inventory/Validate Empty Stone UI (synthetic)")]
    public static void Validate()
    {
        Directory.CreateDirectory(Captures);
        foreach (var size in new[] { new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(3840,2160),new Vector2Int(1024,768) })
            Render(size.x, size.y);
        Debug.Log("INVENTORY UI PASS: empty layout, centered inert book, synced blue selection, passive HUD, independent live GUI scales, anchors and screen fit at four resolutions.");
    }

    private static void Render(int width, int height)
    {
        var host = new GameObject("Empty inventory render fixture");
        var target = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32); target.Create();
        InventoryView view = null;
        try
        {
            view = new InventoryView(host.transform, null);
            view.PanelSettings.targetTexture = target; view.PanelSettings.clearColor = true; view.PanelSettings.colorClearValue = Color.clear;
            typeof(PanelSettings).GetMethod("ApplyPanelSettings",Methods).Invoke(view.PanelSettings,null);
            var panel = view.Root.panel;
            void Draw()
            {
                for (int i = 0; i < 5; i++)
                {
                    panel.GetType().GetMethod("Update",Methods).Invoke(panel,null);
                    panel.GetType().GetMethod("ValidateLayout",Methods).Invoke(panel,null);
                    panel.GetType().GetMethod("Repaint",Methods,null,new[]{typeof(Event)},null).Invoke(panel,new object[]{new Event{type=EventType.Repaint}});
                    panel.GetType().GetMethod("Render",Methods).Invoke(panel,null);
                }
            }
            view.SetState(true,false); Draw();
            var root = view.Root;
            Require(root.Q("inventory-backpack").childCount == 27 && root.Q("inventory-hotbar").childCount == 9,"Wrong carried slot count");
            Require(root.Q("inventory-armor").childCount == 4 && root.Q("inventory-crafting").childCount == 4,"Wrong armor/crafting count");
            Require(root.Q("inventory-offhand").childCount == 1 && root.Q("inventory-output").childCount == 1,"Missing offhand/output");
            Require(root.Q<Image>("inventory-character").image == null,"Character placeholder must be blank");
            foreach (var slot in root.Query<VisualElement>(className:"inventory-slot").ToList())
                Require(slot.childCount == 1 && slot[0].childCount == 0,"A sample item was added");
            var window = root.Q("inventory-window").worldBound;
            Require(window.xMin >= 0 && window.yMin >= 0 && window.xMax <= width && window.yMax <= height,"Inventory frame overflow");
            Require(Mathf.Abs(window.center.x-width*.5f)<1 && Mathf.Abs(window.center.y-height*.5f)<1,"Inventory is not centered");
            var book = root.Q<Button>("inventory-book");
            Require(Mathf.Abs(book.worldBound.center.x-root.Q("inventory-crafting").worldBound.center.x)<1,"Book is not centered below 2x2");
            Require(book.worldBound.yMin > root.Q("inventory-crafting").worldBound.yMax,"Book overlaps crafting");
            Require(!book.focusable,"Inert book consumes keyboard focus");
            for(int i=0;i<27;i++)
            {
                var slot=root.Q("backpack-"+i).worldBound;
                Require(Mathf.Abs(slot.width-slot.height)<.1f && window.Contains(slot.min) && window.Contains(slot.max),"Backpack cells are not square/contained");
                if(i>=9)Require(Mathf.Abs(slot.x-root.Q("backpack-"+(i%9)).worldBound.x)<.1f,"Backpack columns do not align");
            }
            for(int i=0;i<9;i++)Require(Mathf.Abs(root.Q("inventory-hotbar-"+i).worldBound.x-root.Q("backpack-"+i).worldBound.x)<.1f,"Hotbar columns do not align");
            Save(target,$"inventory-{width}x{height}.png");
            view.SetSelection(8); Draw();
            Require(!root.Q("inventory-hotbar-0").ClassListContains("selected") && root.Q("inventory-hotbar-8").ClassListContains("selected"),"Inventory selection did not move");
            Require(root.Q("hud-hotbar-8").ClassListContains("selected"),"HUD selection was not synchronized");
            var selected=root.Q("inventory-hotbar-8")[0].resolvedStyle.backgroundColor;
            var empty=root.Q("inventory-hotbar-7")[0].resolvedStyle.backgroundColor;
            Require(selected.b>selected.r && empty.r>empty.b,"Selected/unselected palette mismatch");
            view.SetState(false,true); Draw();
            var hud=root.Q("inventory-hud");
            foreach(var node in hud.Query<VisualElement>().ToList())Require(node.pickingMode==PickingMode.Ignore,"HUD intercepts a world pointer");
            Require(hud.worldBound.xMin>=0 && hud.worldBound.xMax<=width && hud.worldBound.yMax<=height,"Hotbar overflow");
            Require(root.Q("hud-hotbar").childCount==9,"Gameplay hotbar must contain nine tiles");
            var previousTarget=RenderTexture.active;RenderTexture.active=target;
            var samples=new Texture2D(width,height,TextureFormat.RGBA32,false);
            try
            {
                samples.ReadPixels(new Rect(0,0,width,height),0,0);samples.Apply();
                float AlphaAt(Vector2 point)=>samples.GetPixel(Mathf.FloorToInt(point.x),height-1-Mathf.FloorToInt(point.y)).a;
                var unselected=root.Q("hud-hotbar-7").worldBound;
                var active=root.Q("hud-hotbar-8").worldBound;
                float wash=AlphaAt(unselected.center),blue=AlphaAt(active.center);
                Require(wash>.1f && wash<.6f && blue>.1f && blue<.65f,"Hotbar tile centers must render real transparency");
                Require(AlphaAt(new Vector2((unselected.xMax+active.xMin)*.5f,active.center.y))<.02f,"Opaque backplate fills the gap between tiles");
                Require(AlphaAt(root.Q("hud-offhand-slot").worldBound.center)>.1f && AlphaAt(root.Q("hud-offhand-slot").worldBound.center)<.6f,"Offhand did not receive the translucent tile treatment");
                Require(hud.Query<VisualElement>(className:"inventory-stone").ToList().Count==0,"HUD retained opaque stone artwork");
            }
            finally{RenderTexture.active=previousTarget;Object.DestroyImmediate(samples);}
            Save(target,$"hotbar-{width}x{height}.png");
            float defaultHudWidth = hud.worldBound.width;
            view.SetGuiScale(.75f, 1.5f); view.SetState(true,true); Draw();
            var scaledWindow = root.Q("inventory-window").worldBound;
            Require(Mathf.Abs(scaledWindow.width - window.width * .75f)<1,"Inventory GUI scale did not apply independently");
            Require(Mathf.Abs(hud.worldBound.width - defaultHudWidth * 1.5f)<1,"Hotbar GUI scale did not apply independently");
            Require(Mathf.Abs(scaledWindow.center.x-width*.5f)<1 && Mathf.Abs(scaledWindow.center.y-height*.5f)<1,"Scaled inventory lost its center anchor");
            Require(Mathf.Abs(hud.worldBound.yMax-height*.98f)<1,"Scaled hotbar lost its bottom margin");
            Require(view.HotbarClearanceFraction*height > height-hud.worldBound.yMin,"Building readout clearance did not follow hotbar scale");
            Require(Mathf.Abs(book.worldBound.center.x-root.Q("inventory-crafting").worldBound.center.x)<1,"Scaling moved book away from crafting center");
            view.SetGuiScale(2,2); Draw();
            scaledWindow = root.Q("inventory-window").worldBound;
            Require(scaledWindow.xMin>=0 && scaledWindow.yMin>=0 && scaledWindow.xMax<=width && scaledWindow.yMax<=height,"Maximum inventory scale overflowed screen");
            Require(hud.worldBound.xMin>=0 && hud.worldBound.xMax<=width && hud.worldBound.yMax<=height,"Maximum hotbar scale overflowed screen");
        }
        finally { view?.Dispose(); Object.DestroyImmediate(host); target.Release(); Object.DestroyImmediate(target); }
    }

    private static void Save(RenderTexture target,string file)
    {
        var previous=RenderTexture.active; RenderTexture.active=target;
        var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try
        {
            pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply();
            int visible=0;foreach(var pixel in pixels.GetPixels32())if(pixel.a>20)visible++;
            Require(visible>100,"Blank UI capture");File.WriteAllBytes(Path.Combine(Captures,file),pixels.EncodeToPNG());
        }
        finally { RenderTexture.active=previous; Object.DestroyImmediate(pixels); }
    }

    public static void RunInputBatch()
    {
        SessionState.SetBool(Pending,true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnState(PlayModeStateChange change)
    {
        if(change!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);
        try
        {
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground=true;
            rig=new GameObject("Empty inventory input fixture");rig.transform.position=new Vector3(100,100,100);
            var motor=rig.AddComponent<CharacterMotor>();
            var pivot=new GameObject("View pivot");pivot.transform.SetParent(rig.transform,false);
            var look=pivot.AddComponent<FirstPersonLook>();Set(look,"yawRoot",rig.transform);
            var host=new GameObject("Local input");host.transform.SetParent(rig.transform,false);
            building=host.AddComponent<BuildingController>();input=host.AddComponent<LocalPlayerInput>();
            Set(input,"motor",motor);Set(input,"look",look);input.SetBuilding(building);input.enabled=false;
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            stage=-1;started=Time.realtimeSinceStartup;
            InputSystem.onBeforeUpdate+=Inject;InputSystem.onAfterUpdate+=Check;EditorApplication.update+=Timeout;
        }
        catch(Exception ex){Finish(ex);}
    }
    private static void Set(Object obj,string field,Object value)
    {var so=new SerializedObject(obj);so.FindProperty(field).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    private static void Queue(params Key[] keys){nextKeys=keys;injectPending=true;}
    private static void Read()=>typeof(LocalPlayerInput).GetMethod("Update",Methods).Invoke(input,null);
    private static void Inject()
    {
        if(!injectPending || InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
        injectPending=false;InputSystem.QueueStateEvent(keyboard,new KeyboardState(nextKeys));
        InputSystem.QueueStateEvent(mouse,new MouseState{delta=delta,buttons=mouseButtons});delta=default;mouseButtons=0;
    }
    private static void Timeout(){if(Time.realtimeSinceStartup-started>20)Finish(new InvalidOperationException("Inventory input frames timed out"));}
    private static void Check()
    {
        if(InputState.currentUpdateType!=InputUpdateType.Dynamic || building==null || building.World==null)return;
        try
        {
            keyboard.MakeCurrent();mouse.MakeCurrent();var menus=input.Menus;
            switch(stage++)
            {
                case -1: building.Toggle();building.Select(building.World.Catalog.presets[0]);Queue();break;
                case 0: Read();rotation=rig.transform.rotation;position=rig.transform.position;walkStarted=Time.unscaledTimeAsDouble;delta=new Vector2(100,50);mouseButtons=1;Queue(Key.Tab,Key.W);break;
                case 1:
                    Read();Require(menus.InventoryOpen && !building.Active && !input.WorldInteractionAllowed,"Tab failed to replace placement/block interactions");
                    Require(Cursor.visible && Cursor.lockState==CursorLockMode.None,"Inventory cursor is not free");
                    Require(rig.transform.rotation==rotation,"Inventory mouse-look leaked");
                    Require(!building.Preview.Valid,"Inventory retained a placement preview");
                    delta=new Vector2(90,10);Queue(Key.Tab,Key.W);break;
                case 2:
                    Read();Require(menus.InventoryOpen && rig.transform.rotation==rotation,"Held Tab toggled or mouse look leaked");
                    // Hold through acceleration: tiny first-frame lateral moves are below CharacterController's threshold.
                    if(Time.unscaledTimeAsDouble-walkStarted<.15){stage=2;delta=new Vector2(90,10);Queue(Key.Tab,Key.W);}
                    else{Require(rig.transform.position.z>position.z,"Held W did not walk with inventory open");Queue();}
                    break;
                case 3:Read();Queue(Key.Tab);break;
                case 4:Read();Require(!menus.IsOpen && input.WorldInteractionAllowed && !Cursor.visible,"Second Tab failed to return to game");delta=new Vector2(100,50);Queue();break;
                case 5:Read();Require(rig.transform.rotation==rotation,"Cursor recapture jumped camera");Queue(Key.Tab);break;
                case 6:Read();Require(menus.InventoryOpen,"Inventory failed to reopen");Queue(Key.B);break;
                case 7:Read();Require(!menus.InventoryOpen && building.MenuOpen,"B failed to replace inventory");Queue();break;
                case 8:Read();Queue(Key.B);break;
                case 9:Read();Require(!menus.IsOpen && !building.Active && !menus.InventoryOpen,"Closing B returned to inventory");Queue(Key.Tab);break;
                case 10:Read();Queue(Key.Escape);break;
                case 11:Read();Require(!menus.IsOpen,"Escape failed to close inventory");Queue();break;
                case 12:
                    Read();menus.ToggleInventory();var other=new object();bool closed=false;
                    menus.Open(other,()=>closed=true);Require(!menus.InventoryOpen,"Generic menu did not replace inventory");
                    menus.Close(other);Require(closed && !menus.IsOpen,"Generic menu close restored inventory");
                    building.Toggle();menus.ToggleInventory();Require(menus.InventoryOpen && !building.Active,"Inventory did not replace building catalog");
                    Queue(Key.Digit1);break;
                default:
                    int index=stage-14;
                    Read();Require(menus.SelectedHotbarIndex==index,"Digit selection mismatch at "+index);
                    Require(menus.View.Root.Q("inventory-hotbar-"+index).ClassListContains("selected") && menus.View.Root.Q("hud-hotbar-"+index).ClassListContains("selected"),"Selection views disagree");
                    if(index<8)Queue((Key)((int)Key.Digit1+index+1));
                    else
                    {
                        Debug.Log("INVENTORY INPUT PASS: native Tab/held Tab/B/Escape/digits 1–9, movement, mouse-look suppression, world interaction/placement block, cursor recapture and generic menu replacement without return history; no items.");
                        Finish(null);
                    }
                    break;
            }
        }
        catch(Exception ex){Finish(ex);}
    }
    private static void Finish(Exception error)
    {
        InputSystem.onBeforeUpdate-=Inject;InputSystem.onAfterUpdate-=Check;EditorApplication.update-=Timeout;
        if(error!=null)Debug.LogException(error);
        if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
        if(rig!=null)Object.Destroy(rig);EditorApplication.Exit(error==null?0:1);
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
