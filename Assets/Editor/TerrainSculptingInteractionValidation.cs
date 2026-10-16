using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>Real terrain raycasts, brush commands and asynchronous transactions, outside the main scene.</summary>
public static class TerrainSculptingInteractionValidation
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    private sealed class Fixture:IDisposable
    {
        internal readonly TerrainSculptingIntegrationValidation.Fixture Terrain;
        internal readonly TerrainSculptController Controller;
        internal readonly Camera Camera;
        internal Fixture(double minimumRemovalY=double.NegativeInfinity,TerrainEditStore store=null,long derivedBytes=128L*1024*1024)
        {
            Terrain=new TerrainSculptingIntegrationValidation.Fixture(store:store,saveClock:()=>0,minimumRemovalY:minimumRemovalY,derivedBytes:derivedBytes);
            Terrain.Viewer.transform.position=new Vector3(12,6,12);
            var eye=new GameObject("P5 eye");eye.transform.SetParent(Terrain.Viewer.transform,false);eye.transform.localRotation=Quaternion.Euler(45,0,0);
            Camera=eye.AddComponent<Camera>();Controller=Terrain.Viewer.AddComponent<TerrainSculptController>();
            Controller.Configure(Terrain.Runtime,Camera);Terrain.Find(new ChunkCoord(0,0));Terrain.Ready();Controller.Enter();
        }
        internal void Frame(TerrainSculptInputCommand command=default,double time=1)
        {Controller.Submit(command);Controller.ProcessFrame(time);}
        public void Dispose(){Controller.Configure(null,null);Terrain.Dispose();Physics.SyncTransforms();}
    }
    [MenuItem("Tools/Terrain/Validate Sculpting Development Interaction")]
    public static void Run()
    {
        checks=0;ValidateRepeat();ValidateAimingAndSizes();ValidateCutsAndBusy();ValidateLimitsAndLifecycle();ValidateDestroyedViews();ValidateSave();
        Debug.Log($"TERRAIN SCULPTING P5 INTERACTION PASS: {checks} checks; real closest-solid targeting, default scoop, sphere/box size limits, dry bounds, collision readiness, repeat/backpressure, save commands and preview cleanup.");
    }
    private static void ValidateRepeat()
    {
        var repeat=new TerrainSculptRepeat();
        Check(!repeat.WantsCut(true,false,1),"Entering with a held button armed digging.");
        Check(repeat.WantsCut(true,true,1),"Fresh LMB press did not arm digging.");repeat.Accepted(1);
        Check(!repeat.WantsCut(true,true,1.249),"Rapid taps exceeded the four-per-second cap.");
        Check(repeat.WantsCut(true,false,1.25),"Held LMB did not become eligible after 250 ms.");repeat.Accepted(1.25);
        Check(repeat.WantsCut(true,false,100),"Long frame did not resume digging.");repeat.Accepted(100);
        Check(!repeat.WantsCut(true,false,100),"Long frame created catch-up cuts.");
        repeat.Suspend();Check(!repeat.WantsCut(true,false,101),"Suspended hold resumed digging.");
        Check(!repeat.WantsCut(false,false,102)&&repeat.WantsCut(true,true,103),"Release/new press did not recover.");
    }
    private static void ValidateAimingAndSizes()
    {
        using var f=new Fixture();f.Frame();var c=f.Controller;
        Check(c.Active&&c.HasPreview&&c.CanExcavate&&c.Shape==TerrainDensityBrushShape.Sphere&&c.Size==.5f,"Default sphere/ready state is incorrect.");
        Check(Math.Abs(c.Preview.Center.y-3.95)<.00001&&Math.Abs(c.Preview.HalfExtents.x-.5)<.00001,"Preview is not inset 0.25 m with a one-metre default diameter.");
        long calls=f.Terrain.Baseline.Calls;for(int i=0;i<20;i++)f.Frame();
        Check(f.Terrain.Baseline.Calls==calls,"Idle preview generated terrain height/density on the main thread.");
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.layer=GameplayLayers.WorldSolid;
        blocker.transform.position=new Vector3(12,5.3f,12.7f);blocker.transform.localScale=Vector3.one*.6f;Physics.SyncTransforms();
        try
        {
            f.Frame(new TerrainSculptInputCommand(true,true));
            Check(!c.HasPreview&&!c.CanExcavate&&c.Status.Contains("solid object")&&!f.Terrain.Runtime.Busy,"Ray excavated through an obstructing prop.");
            blocker.GetComponent<Collider>().isTrigger=true;blocker.layer=GameplayLayers.QueryOnly;Physics.SyncTransforms();f.Frame();
            Check(c.CanExcavate,"Query-only trigger blocked the terrain-only solid cast.");
        }
        finally{Object.DestroyImmediate(blocker);Physics.SyncTransforms();}
        f.Frame(new TerrainSculptInputCommand(false,false,1,true));
        Check(c.Shape==TerrainDensityBrushShape.Box&&Math.Abs(c.Size-.6)<.00001,"Shape/0.1 m adjustment was not applied.");
        f.Frame(new TerrainSculptInputCommand(false,false,1000));Check(c.Size==2,"Maximum brush size exceeded two metres.");
        f.Frame(new TerrainSculptInputCommand(false,false,-1000));Check(c.Size==.5f,"Minimum brush size fell below half a metre.");
        f.Terrain.Viewer.transform.rotation=Quaternion.Euler(0,38,0);f.Frame();
        var view=typeof(TerrainSculptController).GetField("view",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
        var outline=Field<GameObject>(view,"outline");
        Check(Quaternion.Angle(outline.transform.rotation,Quaternion.identity)<.0001,"Box preview rotates with the player's heading.");
        Check(outline.GetComponentsInChildren<Collider>(true).Length==0,"Brush preview has physical/query colliders.");
        var lines=outline.GetComponentsInChildren<LineRenderer>();Check(lines.Length==12,"Box preview did not expose all twelve edges.");
        f.Camera.transform.SetPositionAndRotation(new Vector3(12,10.3f,12),Quaternion.Euler(90,0,0));f.Frame();
        Check(!c.HasPreview&&c.Status.Contains("6 m"),"Terrain beyond six metres was targetable.");
        f.Camera.transform.position=new Vector3(12,10.1f,12);f.Frame();Check(c.CanExcavate,"Terrain within six metres was rejected.");
        c.Exit();Check(!outline.activeSelf&&!c.HasPreview,"Exit left the preview visible.");
    }
    private static void ValidateCutsAndBusy()
    {
        using var f=new Fixture();f.Frame(new TerrainSculptInputCommand(true,true),1);
        Check(f.Terrain.Runtime.Busy&&f.Terrain.World.Revision==0&&!f.Controller.CanExcavate,"Digging bypassed the pending transaction.");
        for(int i=0;i<8;i++)f.Frame(new TerrainSculptInputCommand(true,false),10+i);
        Check(f.Terrain.Runtime.CommittedEdits==0,"Busy frames committed/queued additional cuts.");f.Terrain.Ready();
        Check(f.Terrain.Runtime.CommittedEdits==1,"Default scoop did not commit exactly once.");
        Physics.SyncTransforms();var ray=new Ray(f.Camera.transform.position,f.Camera.transform.forward);
        Check(Physics.Raycast(ray,out var hit,6,GameplayLayers.SolidSurfaceMask)&&hit.point.y<4.0,"Default brush did not take a useful visible scoop.");
        f.Controller.Suspend();f.Frame(new TerrainSculptInputCommand(true,false),20);
        Check(!f.Terrain.Runtime.Busy&&f.Terrain.Runtime.CommittedEdits==1,"Suspended mouse hold replayed a cut.");
        f.Frame(new TerrainSculptInputCommand(false,false),21);f.Frame(new TerrainSculptInputCommand(true,true),22);
        Check(f.Terrain.Runtime.Busy,"Fresh press after suspension did not excavate.");f.Terrain.Ready();
        Check(f.Terrain.Runtime.CommittedEdits==2,"Fresh post-suspension cut failed.");
        f.Frame(new TerrainSculptInputCommand(true,false),30);Check(f.Terrain.Runtime.Busy,"Held LMB failed to repeat after the previous transaction completed.");
        f.Terrain.Ready();Check(f.Terrain.Runtime.CommittedEdits==3,"Held LMB produced an incorrect number of cuts.");
    }
    private static void ValidateLimitsAndLifecycle()
    {
        using(var f=new Fixture(3.6))
        {
            f.Frame(new TerrainSculptInputCommand(true,true));
            Check(f.Controller.HasPreview&&!f.Controller.CanExcavate&&f.Controller.Preview.Minimum.y<3.6&&f.Controller.Status.Contains("dry excavation"),"Preview did not report the full brush's dry bound.");
            Check(f.Terrain.World.Revision==0&&!f.Terrain.Runtime.Busy,"Water-rejected brush reached the backend.");
        }
        using(var f=new Fixture())
        {
            f.Controller.SetFocus(false);f.Frame(new TerrainSculptInputCommand(true,true));
            Check(!f.Controller.HasPreview&&!f.Terrain.Runtime.Busy,"Lost focus allowed a cut.");
            f.Controller.SetFocus(true);f.Frame(new TerrainSculptInputCommand(true,false));Check(!f.Terrain.Runtime.Busy,"Focus recovery replayed a held click.");
            using(var edit=f.Terrain.World.StageExcavation(TerrainDensityBrush.Sphere(new double3(12,3,12),.5)))Check(f.Terrain.World.TryCommit(edit),"Readiness fixture cut failed.");
            f.Frame(new TerrainSculptInputCommand(true,true));Check(!f.Controller.CanExcavate&&f.Controller.Status.Contains("collision"),"Stale edited collision was targetable.");
            f.Terrain.Ready();f.Frame();
            var view=typeof(TerrainSculptController).GetField("view",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f.Controller);
            var hud=Field<GameObject>(view,"hud");var outline=Field<GameObject>(view,"outline");
            string label=hud.GetComponentInChildren<Text>().text;
            Check(!label.Contains("F10")&&label.Contains("Session only")&&label.Contains("Disk saves are disabled"),"Session HUD advertises disk saves or omits the edit lifecycle.");
            f.Frame(new TerrainSculptInputCommand(false,false,save:true));
            Check(f.Controller.Status.Contains("disk saving is disabled")&&f.Terrain.Runtime.Persistence==null,"Session save command did not remain a no-op.");
            f.Controller.Configure(null,null);
            Check(hud==null&&outline==null&&!f.Controller.Active,"Unbinding left owned HUD/preview resources alive.");
        }
        using(var f=new Fixture(derivedBytes:64*1024))
        {
            f.Frame(new TerrainSculptInputCommand(true,true));var timer=System.Diagnostics.Stopwatch.StartNew();
            while(f.Terrain.Runtime.Busy&&timer.ElapsedMilliseconds<10000)f.Terrain.Tick();
            Check(!f.Terrain.Runtime.Busy&&f.Terrain.Runtime.Status!="Ready"&&f.Terrain.World.Revision==0,"Capacity failure did not preserve baseline terrain.");
            f.Frame(new TerrainSculptInputCommand(true,false),10);
            Check(!f.Controller.CanExcavate&&!f.Terrain.Runtime.Busy&&f.Controller.Status.Contains("click to retry"),"Failure reason was hidden or a held stroke retried automatically.");
            f.Frame(new TerrainSculptInputCommand(true,true),11);Check(f.Terrain.Runtime.Busy,"A fresh click could not explicitly retry a failed transaction.");
        }
    }
    private static void ValidateSave()
    {
        string path=Path.Combine(TerrainSculptingValidation.SourceRoot(),".utmp","sculpt-p5",Guid.NewGuid().ToString("N").Substring(0,8));
        var identity=TerrainEditIdentity.Create("p5-fixture",WorldGenerationConfiguration.Default,TerrainDensityProfile.Default);
        using var f=new Fixture(store:new TerrainEditStore(path,identity));
        f.Frame(new TerrainSculptInputCommand(true,true));f.Terrain.Ready();
        Check(f.Terrain.Runtime.Persistence.Dirty,"Unflushed tool edit was reported saved.");
        f.Frame(new TerrainSculptInputCommand(false,false,save:true),2);
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(f.Terrain.Runtime.Persistence.Dirty&&timer.ElapsedMilliseconds<10000){f.Terrain.Tick();Thread.Sleep(1);}
        Check(f.Terrain.Runtime.Persistence.SavedRevision==1&&File.Exists(Path.Combine(path,"manifest.bin")),"Tool save command did not reach persistence.");
        f.Frame();var view=typeof(TerrainSculptController).GetField("view",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f.Controller);
        Check(Field<GameObject>(view,"hud").GetComponentInChildren<Text>().text.Contains("Saved revision 1"),"HUD failed to show completed save revision.");
    }
    private static void ValidateDestroyedViews()
    {
        foreach(bool outlineFirst in new[]{false,true})
        {
            using var f=new Fixture();f.Frame();
            var view=Field<object>(f.Controller,"view");
            var hud=Field<GameObject>(view,"hud");var outline=Field<GameObject>(view,"outline");
            Object.DestroyImmediate(outlineFirst?outline:hud);
            // Scene shutdown can destroy view objects before WorldManager unbinds its controller.
            f.Controller.Suspend();f.Controller.Exit();f.Controller.Configure(null,null);f.Controller.Configure(null,null);
            Check(hud==null&&outline==null&&!f.Controller.Active,"Partial view destruction did not finish cleanup.");
            ((IDisposable)view).Dispose(); // Repeated disposal, after both native objects are gone.
        }
        // A parent may destroy its child HUD and controller before the separately owned outline.
        var parent=new GameObject("Sculpt teardown parent");
        var type=typeof(TerrainSculptController).Assembly.GetType("TerrainSculptView");
        var orphan=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{parent.transform},null);
        var remaining=Field<GameObject>(orphan,"outline");Object.DestroyImmediate(parent);
        type.GetMethod("Hide",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(orphan,null);
        ((IDisposable)orphan).Dispose();((IDisposable)orphan).Dispose();
        Check(remaining==null,"Parent-first teardown leaked its independent outline.");
    }
}
