using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Cursor-driven gable choice, geometric datums and actual controller flips/commits.</summary>
[InitializeOnLoad]
public static class BuildGablePlacementValidation
{
    private const string Pending="FS.GablePlacement.Validation";
    private static int checks;
    static BuildGablePlacementValidation()=>EditorApplication.playModeStateChanged+=State;
    private static void Check(bool yes,string message){checks++;if(!yes)throw new InvalidOperationException(message);}
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.000002f,message+": "+a+" / "+b);
    public static void RunBatch()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    private static void State(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=0;
        try
        {
            var catalog=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");int families=0;
            foreach(var d in catalog.presets)if(d.IsHalfGable)
            {
                families++;Check(d.flipVariant!=null,"Missing opposite gable: "+d.name);
                for(int yaw=0;yaw<8;yaw++){Geometry(catalog,d,yaw);GapAiming(catalog,d,yaw);}
                Controller(catalog,d);
            }
            Check(families==4,"Expected wood, limestone, timber-frame and ordinary-stone gables");
            Directory.CreateDirectory(".utmp/gable-placement");
            File.WriteAllText(".utmp/gable-placement/validation.json","{\"passed\":true,\"checks\":"+checks+",\"families\":"+families+",\"headings\":8}");
            Debug.Log("GABLE PLACEMENT PASS: "+checks+" checks; all four families, cursor sweeps, reversed headings, roof ends, neighbouring edges, manual flips and controller commits.");
            EditorApplication.Exit(0);
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static BuildPieceRecord Add(BuildSession s,BuildDefinition d,Vector3 origin,int yaw)
        =>s.Add(d,s.CreateFrame(origin,yaw),default,0,false);
    private static BuildPreview Snap(BuildSession s,BuildDefinition d,BuildPieceRecord target,Vector3 point,Vector3 normal,int? heading=null,bool automatic=true,Vector3Int nudge=default)
    {
        var q=BuildGeometry.Rotation(target.WorldYawStep);
        return BuildPlacement.Solve(d,target.Origin+q*point,q*normal,target,s.Frame(target.OwnFrameId),0,0,nudge,target.Origin+q*(point+normal*4),false,heading,automatic);
    }
    private static Vector3 LocalPeak(BuildPreview p,BuildPieceRecord target)
        =>Quaternion.Inverse(BuildGeometry.Rotation(target.WorldYawStep))*(p.Origin+BuildGeometry.Rotation(p.WorldYaw)*BuildWallShape.Peak(p.Definition)-target.Origin);
    private static Bounds LocalBounds(BuildPreview p,BuildPieceRecord target)
        =>BuildGeometry.WorldBounds(p.Definition.LocalBounds,Quaternion.Inverse(BuildGeometry.Rotation(target.WorldYawStep))*(p.Origin-target.Origin),BuildGeometry.Turn(p.WorldYaw-target.WorldYawStep));
    private static void Geometry(BuildCatalog c,BuildDefinition d,int yaw)
    {
        using var s=new BuildSession();var wall=Add(s,c.presets[0],new Vector3(100,40,100),yaw);float top=wall.Definition.StackRise;
        // Either selected variant must fit the same aimed half, from either face or above.
        foreach(var selected in new[]{d,d.flipVariant})foreach(var face in new[]{Vector3.forward,Vector3.back,Vector3.up})foreach(int turn in new[]{0,4})
            for(int i=0;i<40;i++)
            {
                float x=.05f+i*.1f;var p=Snap(s,selected,wall,new Vector3(x,top-.01f,0),face,BuildGeometry.Turn(yaw+turn));
                Near(LocalPeak(p,wall),new Vector3(2,top+2,0),"Aimed wall half has outward-facing peak: "+d.name);
                var b=LocalBounds(p,wall);Check(Mathf.Abs(b.min.x-(x<2?0:2))<.001f,"Wall half jumped bay after rotation");
                Check(p.WorldYaw==BuildGeometry.Turn(yaw+turn),"Automatic slope changed manual heading");
            }
        foreach(var roofId in new[]{ThatchRoofSetup.ContentId,"build.wood.l33-layered-shingle-roof"})
        {
            var roof=Add(s,c.Find(roofId),new Vector3(130,40,100),yaw);
            foreach(float end in new[]{0f,4f})foreach(int turn in new[]{0,4})foreach(var selected in new[]{d,d.flipVariant})
            {
                var heading=BuildGeometry.Turn(yaw+6+turn);
                var p=Snap(s,selected,roof,new Vector3(end,1,1),end==0?Vector3.left:Vector3.right,heading);
                Near(LocalPeak(p,roof),new Vector3(end,2,2),"Gable peak missed aimed roof ridge");
                var b=LocalBounds(p,roof);Near(b.center,new Vector3(end,1,1),"Roof half footprint moved");
                var manual=Snap(s,p.Definition.flipVariant,roof,new Vector3(end,1,1),Vector3.up,heading,false);
                Check(manual.Definition==p.Definition.flipVariant,"Manual roof flip overwritten");
                Near(LocalBounds(manual,roof).center,b.center,"Manual roof flip moved to unaimed half");
            }
        }
        foreach(var neighbour in new[]{d,d.flipVariant})
        {
            var gable=Add(s,neighbour,new Vector3(160,40,100),yaw);
            foreach(bool left in new[]{false,true})foreach(int turn in new[]{0,4})foreach(var selected in new[]{d,d.flipVariant})
            {
                float x=left?0:2;var p=Snap(s,selected,gable,new Vector3(x,1,0),left?Vector3.left:Vector3.right,BuildGeometry.Turn(yaw+turn));
                var b=LocalBounds(p,gable);Check(Mathf.Abs(b.min.x-(left?-2:2))<.001f,"Wrong neighbour edge");
                var peak=LocalPeak(p,gable);bool rising=neighbour.gableSlope==GableSlope.Rising;
                Near(peak,new Vector3(rising?(left?-2:2):(left?0:4),2,0),"Adjoining gable edges do not meet");
            }
        }
        var automatic=Snap(s,d,wall,new Vector3(3,top,0),Vector3.up);
        var flipped=Snap(s,automatic.Definition.flipVariant,wall,new Vector3(3,top,0),Vector3.up,null,false);
        Check(flipped.Definition==automatic.Definition.flipVariant,"Manual wall flip overwritten");
        Near(LocalBounds(flipped,wall).center,LocalBounds(automatic,wall).center,"Manual wall flip changed slot");
        var offset=new Vector3Int(1,2,-1);var nudged=Snap(s,d,wall,new Vector3(3,top,0),Vector3.up,null,true,offset);
        Near(nudged.Origin-automatic.Origin,BuildGeometry.Rotation(yaw)*(Vector3)offset*BuildGeometry.Unit,"Gable nudge changed frame");
    }
    private static void Controller(BuildCatalog c,BuildDefinition d)
    {
        var host=new GameObject("Gable controller regression");var cameraObj=new GameObject("Gable aim camera");var proxy=new GameObject("Wall aim proxy");
        try
        {
            var camera=cameraObj.AddComponent<Camera>();var controller=host.AddComponent<BuildingController>();var world=host.GetComponent<BuildWorld>();world.enabled=false;
            typeof(BuildingController).GetField("viewCamera",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,camera);
            Invoke(controller,"Start");controller.enabled=false;
            var wall=Add(world.Session,c.presets[0],new Vector3(200,100,200),0);proxy.AddComponent<BuildGameplayProxy>().Bind(wall);Physics.SyncTransforms();
            controller.enabled=true;controller.Toggle();controller.Select(d);
            void Aim(float x,float height=2.96f){camera.transform.position=wall.Origin+new Vector3(x,height,-4);camera.transform.rotation=Quaternion.identity;Invoke(controller,"LateUpdate");}
            Aim(1,3.5f);Check(controller.Preview.Definition==d&&controller.Preview.Hint.StartsWith("Gable guide"),"Cold aim into empty left gap did not acquire wall");
            Aim(3,3.5f);Check(controller.Preview.Definition.gableSlope==GableSlope.Falling,"Empty right gap selected wrong triangle");
            Aim(3);Check(controller.Preview.Definition.gableSlope==GableSlope.Falling,"Controller retained selected slope over right gap");
            var origin=controller.Preview.Origin;
            controller.Submit(new BuildInputCommand(0,default,false,false,true));Invoke(controller,"LateUpdate");
            Check(controller.Preview.Definition.gableSlope==GableSlope.Rising,"R did not flip the displayed automatic variant");Near(controller.Preview.Origin,origin,"R moved wall slot");
            controller.Submit(new BuildInputCommand(0,default,false,false,true));Invoke(controller,"LateUpdate");
            Check(controller.Preview.Definition.gableSlope==GableSlope.Falling,"Second R did not restore displayed slope");
            controller.OpenMenu();controller.Select(d);Aim(1);Check(controller.Preview.Definition.gableSlope==GableSlope.Rising,"Re-selection failed to restore automatic slope");
            Aim(3);var preview=controller.Preview;var committed=world.Commit(preview);
            Check(committed!=null&&committed.Definition==preview.Definition&&committed.Definition.gableSlope==GableSlope.Falling,"Commit used selected definition instead of displayed definition");
            Near(committed.Origin,preview.Origin,"Commit changed preview position");
            controller.OpenMenu();controller.Select(d.flipVariant);Aim(1);
            Check(controller.Preview.Definition==d.flipVariant,"Explicit menu flip lost manual slope");
            var single=Add(world.Session,d,wall.Origin+new Vector3(10,3,0),0);controller.OpenMenu();controller.Select(d);
            camera.transform.position=single.Origin+new Vector3(-.5f,.25f,-4);Invoke(controller,"LateUpdate");
            Check(controller.Preview.Definition.gableSlope==GableSlope.Falling,"Empty bay beside standalone gable did not acquire matching slope");
            Near(controller.Preview.Origin,single.Origin+Vector3.left*2,"Empty left bay missed neighbour base");
        }finally{Object.DestroyImmediate(proxy);Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObj);Physics.SyncTransforms();}
    }
    private static void GapAiming(BuildCatalog c,BuildDefinition d,int yaw)
    {
        var q=BuildGeometry.Rotation(yaw);var candidates=new List<BuildPieceRecord>();
        using(var s=new BuildSession())
        {
            var wall=Add(s,c.presets[0],new Vector3(20,20,20),yaw);float top=wall.Definition.StackRise;
            foreach(float x in new[]{.75f,3.25f})foreach(int side in new[]{-1,1})
            {
                var ray=new Ray(wall.Origin+q*new Vector3(x,top+.5f,side*4),q*Vector3.back*side);
                Check(BuildGablePlacement.TryAim(s,d,ray,8,candidates,out var target,out var hit,out var normal)&&target==wall,"Empty wall-top gap requires previous hit");
                Check(BuildGablePlacement.Solve(d,hit,normal,target,0,default,ray.origin,out var p),"Gap aim cannot produce preview");
                Near(LocalPeak(p,wall),new Vector3(2,top+2,0),"Gap guide picked wrong wall half");
                Check(!BuildGablePlacement.TryAim(s,d,ray,2,candidates,out _,out _,out _),"Gap guide bypassed nearer obstruction/reach");
            }
            var outside=new Ray(wall.Origin+q*new Vector3(.25f,top+1.5f,-4),q*Vector3.forward);
            Check(!BuildGablePlacement.TryAim(s,d,outside,8,candidates,out _,out _,out _),"Guide acquired space above triangle");
        }
        foreach(var neighbour in new[]{d,d.flipVariant})using(var s=new BuildSession())
        {
            var gable=Add(s,neighbour,new Vector3(20,20,20),yaw);
            foreach(float x in new[]{-.5f,2.5f})foreach(int side in new[]{-1,1})
            {
                var ray=new Ray(gable.Origin+q*new Vector3(x,.25f,side*4),q*Vector3.back*side);
                Check(BuildGablePlacement.TryAim(s,d,ray,8,candidates,out var target,out var hit,out var normal)&&target==gable,"Adjacent empty gable bay not acquired");
                Check(BuildGablePlacement.Solve(d,hit,normal,target,0,default,ray.origin,out var p),"Neighbour gap aim cannot produce preview");
                var peak=LocalPeak(p,gable);bool rising=neighbour.gableSlope==GableSlope.Rising;
                Near(peak,new Vector3(rising?(x<0?-2:2):(x<0?0:4),2,0),"Gap aim chose unmatched neighbour edge");
            }
            var tooFar=new Ray(gable.Origin+q*new Vector3(4.5f,.25f,-4),q*Vector3.forward);
            Check(!BuildGablePlacement.TryAim(s,d,tooFar,8,candidates,out _,out _,out _),"Gap guide extends more than one bay");
            var removed=new Ray(gable.Origin+q*new Vector3(-.5f,.25f,-4),q*Vector3.forward);s.Remove(gable.Id);
            Check(!BuildGablePlacement.TryAim(s,d,removed,8,candidates,out _,out _,out _),"Removed gable retained virtual aim target");
        }
    }
    private static void Invoke(object o,string method)=>o.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);
}
