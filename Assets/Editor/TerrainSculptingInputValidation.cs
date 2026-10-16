using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>Native Input System frames and real automatic controller LateUpdate, in an empty Play Mode scene.</summary>
[InitializeOnLoad]
public static class TerrainSculptingInputValidation
{
    private const string Pending="FS.Sculpt.NativeInputValidation";
    private static TerrainSculptingIntegrationValidation.Fixture fixture;
    private static TerrainSculptController sculpt;
    private static LocalPlayerInput input;
    private static BuildingController building;
    private static Camera camera;
    private static Keyboard keyboard;
    private static Mouse mouse;
    private static Key[] keys=Array.Empty<Key>();
    private static ushort buttons;
    private static Vector2 scroll;
    private static bool inject,finishing;
    private static bool nativeFrameReady;
    private static int stage,waitFrames,checks;
    private static float started;
    private static string savePath;
    private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static TerrainSculptingInputValidation()=>EditorApplication.playModeStateChanged+=OnState;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static void Read()=>typeof(LocalPlayerInput).GetMethod("Update",Hidden).Invoke(input,null);
    private static void Set(object target,string field,object value)=>target.GetType().GetField(field,Hidden).SetValue(target,value);
    private static T Field<T>(object target,string field)=>(T)target.GetType().GetField(field,Hidden).GetValue(target);
    private static void Focus(bool focused)=>typeof(LocalPlayerInput).GetMethod("OnApplicationFocus",Hidden).Invoke(input,new object[]{focused});
    private static void Queue(ushort held=0,params Key[] nextKeys){buttons=held;keys=nextKeys;inject=true;}

    public static void RunBatch()
    {
        SessionState.SetBool(Pending,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
    }
    private static void OnState(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);
        try
        {
            checks=0;finishing=false;waitFrames=0;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground=true;
            savePath=Path.Combine(TerrainSculptingValidation.SourceRoot(),".utmp","sculpt-p5",Guid.NewGuid().ToString("N").Substring(0,8));
            var identity=TerrainEditIdentity.Create("p5-native",WorldGenerationConfiguration.Default,TerrainDensityProfile.Default);
            fixture=new TerrainSculptingIntegrationValidation.Fixture(store:new TerrainEditStore(savePath,identity),saveClock:()=>0);
            fixture.Viewer.transform.position=new Vector3(12,4.2f,12);var motor=fixture.Viewer.AddComponent<CharacterMotor>();
            motor.TerrainGuard=fixture.Runtime;
            var eye=new GameObject("P5 native camera");eye.transform.SetParent(fixture.Viewer.transform,false);eye.transform.localPosition=Vector3.up*1.6f;
            eye.transform.localRotation=Quaternion.Euler(45,0,0);camera=eye.AddComponent<Camera>();camera.nearClipPlane=.03f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.1f,.13f);
            var look=eye.AddComponent<FirstPersonLook>();Set(look,"yawRoot",fixture.Viewer.transform);
            var host=new GameObject("P5 native local input");host.transform.SetParent(fixture.Viewer.transform,false);
            building=host.AddComponent<BuildingController>();input=host.AddComponent<LocalPlayerInput>();
            Set(input,"motor",motor);Set(input,"look",look);input.SetBuilding(building);Set(building,"viewCamera",camera);
            input.enabled=false; // Harness reads this owner exactly once per native Dynamic input frame.
            sculpt=host.AddComponent<TerrainSculptController>();sculpt.Configure(fixture.Runtime,camera);Focus(true);
            fixture.Find(new ChunkCoord(0,0));fixture.Ready();
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            keyboard.MakeCurrent();mouse.MakeCurrent();
            // The loop driver is copied into the isolated project only; no test behaviour
            // is added to the real game's runtime assembly or saved scene.
            var driverType=Type.GetType("TerrainSculptInputValidationDriver, Assembly-CSharp");
            if(driverType==null)throw new InvalidOperationException("Run this batch through Tools/ValidateTerrainSculptingInput.ps1.");
            driverType.GetField("AfterFrame").SetValue(null,(Action)AfterPlayerFrame);fixture.Viewer.AddComponent(driverType);
            stage=-1;started=Time.realtimeSinceStartup;InputSystem.onBeforeUpdate+=Inject;InputSystem.onAfterUpdate+=NativeFrame;EditorApplication.update+=Timeout;
            Queue();
        }
        catch(Exception error){Finish(error);}
    }
    private static void Inject()
    {
        if(!inject||InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
        inject=false;InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        InputSystem.QueueStateEvent(mouse,new MouseState{buttons=buttons,scroll=scroll});scroll=default;
    }
    private static void Timeout(){if(Time.realtimeSinceStartup-started>45)Finish(new TimeoutException("P5 input fixture timed out at stage "+stage));}
    private static void NativeFrame(){if(InputState.currentUpdateType==InputUpdateType.Dynamic)nativeFrameReady=true;}
    public static void AfterPlayerFrame()
    {
        if(finishing||!nativeFrameReady||building==null||building.World==null)return;
        nativeFrameReady=false;
        try
        {
            keyboard.MakeCurrent();mouse.MakeCurrent();fixture.Tick();
            switch(stage++)
            {
                case -1:
                    building.Toggle();building.Select(building.World.Catalog.presets[0]);Queue(1,Key.F6);break;
                case 0:
                    Read();Check(sculpt.Active&&!building.Active,"F6 did not replace building mode.");Queue(1,Key.F6);break;
                case 1:
                    Read();Check(sculpt.Active,"Held F6 toggled twice.");Check(fixture.Runtime.CommittedEdits==0&&!fixture.Runtime.Busy,"Held click on entry excavated.");
                    if(++waitFrames<5){stage=1;Queue(1,Key.F6);}else{waitFrames=0;Queue();}break;
                case 2:Read();scroll=new Vector2(0,120);Queue(0,Key.R);break;
                case 3:Read();Queue(0,Key.R);break;
                case 4:
                    Check(sculpt.Shape==TerrainDensityBrushShape.Box&&Math.Abs(sculpt.Size-.6f)<.00001,
                        $"Native R/wheel did not change shape/size: {sculpt.Shape}/{sculpt.Size}; cursor={Cursor.lockState}; skip={Field<int>(input,"suppressLookFrames")}; active={sculpt.Active}; preview={sculpt.HasPreview}; status={sculpt.Status}.");
                    Read();Queue();break;
                case 5:Check(sculpt.Shape==TerrainDensityBrushShape.Box,"Held R cycled shape twice.");Read();Queue(1);break;
                case 6:Read();Queue();break;
                case 7:
                    Read();Check(fixture.Runtime.Busy||fixture.Runtime.CommittedEdits==1,"Native LMB did not submit excavation.");Queue();break;
                case 8:
                    Read();if(fixture.Runtime.Busy){stage=8;Queue();break;}
                    Check(fixture.Runtime.CommittedEdits==1&&fixture.World.Revision==1,"Native click committed more/less than one cut.");
                    Check(building.World.Session.Pieces.Count==0,"Sculpt click also placed a building.");Queue(0,Key.F10);break;
                case 9:
                    Read();Check(Field<TerrainSculptInputCommand>(sculpt,"pending").Save,"F10 did not route a save command.");Queue(0,Key.F10);break;
                case 10:
                    Read();Check(!Field<TerrainSculptInputCommand>(sculpt,"pending").Save,"Held F10 repeated save input.");
                    if(fixture.Runtime.Persistence.SavedRevision!=1){stage=10;Queue(0,Key.F10);break;}
                    Check(!fixture.Runtime.Persistence.Dirty&&File.Exists(Path.Combine(savePath,"manifest.bin")),"Native save command did not become durable.");Queue(1,Key.Tab);break;
                case 11:Read();Check(input.Menus.InventoryOpen&&sculpt.Active,"Tab did not suspend active sculpt mode.");Queue(1,Key.Tab);break;
                case 12:
                    Check(!sculpt.HasPreview&&fixture.World.Revision==1,"Inventory allowed sculpt preview/edits.");Read();Queue(1);break;
                case 13:Read();Queue(1,Key.Escape);break;
                case 14:Read();Check(!input.Menus.IsOpen&&sculpt.Active,"Escape from inventory did not return to suspended sculpt mode.");Queue(1);break;
                case 15:
                    Read();Check(fixture.World.Revision==1&&!fixture.Runtime.Busy,"Inventory-held click replayed after cursor capture.");
                    if(++waitFrames<5){stage=15;Queue(1);}else{waitFrames=0;Queue();}break;
                case 16:Read();Focus(false);Queue(1,Key.R);break;
                case 17:
                    Read();Check(!sculpt.HasPreview&&fixture.World.Revision==1,"Focus loss permitted interaction.");Focus(true);Queue(1,Key.R);break;
                case 18:
                    Read();Check(fixture.World.Revision==1&&sculpt.Shape==TerrainDensityBrushShape.Box,"Focus recovery replayed held input.");
                    if(++waitFrames<5){stage=18;Queue(1,Key.R);}else{waitFrames=0;Queue();}break;
                case 19:Read();Queue(1,Key.B);break;
                case 20:Read();Check(!sculpt.Active&&building.MenuOpen,"B did not replace sculpt mode with building.");Queue(1,Key.F6);break;
                case 21:Read();Check(sculpt.Active&&!building.Active&&!input.Menus.IsOpen,"F6 did not replace the building catalog.");Queue(1,Key.F6);break;
                case 22:
                    Read();Check(fixture.World.Revision==1&&!fixture.Runtime.Busy,"Building-menu click leaked into sculpting.");
                    if(++waitFrames<4){stage=22;Queue(1,Key.F6);}else{waitFrames=0;Queue();}break;
                case 23:Read();Queue(0,Key.Escape);break;
                case 24:Read();Check(!sculpt.Active&&!sculpt.HasPreview,"Escape did not exit sculpt mode.");Queue();break;
                case 25:Read();Queue(0,Key.F6);break;
                case 26:Read();Queue();break;
                case 27:
                    Read();if(++waitFrames<4){stage=27;Queue();}else{waitFrames=0;Queue(0,Key.R);}break;
                case 28:Read();Queue();break;
                case 29:
                    Read();Check(sculpt.Shape==TerrainDensityBrushShape.Sphere&&sculpt.HasPreview,"Sphere render fixture not ready.");
                    Capture("TerrainSculptingP5Sphere.png");Queue(0,Key.R);break;
                case 30:Read();Queue();break;
                case 31:
                    Read();Check(sculpt.Shape==TerrainDensityBrushShape.Box&&sculpt.HasPreview,"Box render fixture not ready.");
                    Capture("TerrainSculptingP5Box.png");
                    Debug.Log($"TERRAIN SCULPTING P5 NATIVE INPUT PASS: {checks} checks; F6/R/wheel/LMB/F10/Escape, held edges, building/inventory replacement, focus/cursor suppression, real saved excavation and rendered sphere/box HUD passed.");
                    Finish(null);break;
            }
        }
        catch(Exception error){Finish(error);}
    }
    private static void Capture(string filename)
    {
        var template=Resources.Load<Material>("TerrainSculpting/BrushPreview");
        Check(template!=null&&template.shader!=null&&template.shader.isSupported,"Brush preview material/shader is not usable.");
        foreach(var message in ShaderUtil.GetShaderMessages(template.shader))Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,"Brush shader compilation failed: "+message.message);
        var view=Field<object>(sculpt,"view");var hud=Field<GameObject>(view,"hud");var outline=Field<GameObject>(view,"outline");
        Check(outline.activeSelf&&outline.GetComponentsInChildren<Collider>(true).Length==0,"Rendered preview is missing or obstructive.");
        var canvas=hud.GetComponent<Canvas>();var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;var oldTarget=RenderTexture.active;
        var target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);target.Create();
        var stone=new Material(Shader.Find("Universal Render Pipeline/Unlit"));stone.SetColor("_BaseColor",new Color(.29f,.32f,.29f));
        var materials=new System.Collections.Generic.Dictionary<MeshRenderer,Material>();
        try
        {
            foreach(var runtime in fixture.Runtimes.Values)foreach(var renderer in runtime.Root.GetComponentsInChildren<MeshRenderer>())
            {materials[renderer]=renderer.sharedMaterial;renderer.sharedMaterial=stone;}
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Check(pipeline!=null,"Native render fixture lacks the configured URP pipeline.");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.1f;
            camera.targetTexture=target;Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;var png=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try
            {
                png.ReadPixels(new Rect(0,0,target.width,target.height),0,0);png.Apply();
                string folder=Path.Combine(TerrainSculptingValidation.SourceRoot(),"Documentation","Images");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,filename),png.EncodeToPNG());
                Color[] pixels=png.GetPixels();int green=0;foreach(var color in pixels)if(color.g>color.r*1.4f&&color.g>color.b*1.15f&&color.g>.35f)green++;
                Check(green>40,"Brush outline did not render into the capture.");
            }
            finally{Object.DestroyImmediate(png);}
        }
        finally
        {
            camera.targetTexture=null;canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;
            foreach(var pair in materials)if(pair.Key!=null)pair.Key.sharedMaterial=pair.Value;
            GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;RenderTexture.active=oldTarget;
            target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(stone);
        }
    }
    private static void Finish(Exception error)
    {
        if(finishing)return;finishing=true;
        InputSystem.onBeforeUpdate-=Inject;InputSystem.onAfterUpdate-=NativeFrame;EditorApplication.update-=Timeout;
        if(error!=null)Debug.LogException(error);
        if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
        fixture?.Dispose();fixture=null;EditorApplication.Exit(error==null?0:1);
    }
}
