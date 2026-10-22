using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

/// <summary>Installed assets, cursor sweeps, persistent rotation, support parity and village-size preview cost.</summary>
[InitializeOnLoad]
public static class BuildInteractionValidation
{
    private const string Pending="FS.BuildInteraction.Validation";
    private static int checks;
    static BuildInteractionValidation()=>EditorApplication.playModeStateChanged+=State;
    private static void Check(bool yes,string message){checks++;if(!yes)throw new InvalidOperationException(message);}
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.000002f,message+": "+a+" / "+b);
    public static void RunBatch()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    private static void State(PlayModeStateChange change)
    {
        if(change!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=0;
        try
        {
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");Assets(c);
            for(int yaw=0;yaw<8;yaw++){Footings(c,yaw);FacesAndFloors(c,yaw);Stairs(c,yaw);Canopy(c,yaw);Parity(c,yaw);}
            Rotation(c);LockedHeadings(c);FittedSupportAndRendering(c);var performance=Performance(c);
            Directory.CreateDirectory(".utmp/interaction");File.WriteAllText(".utmp/interaction/validation.json","{\"passed\":true,\"checks\":"+checks+",\"performance\":"+performance+"}");
            Debug.Log("BUILD INTERACTION PASS: "+checks+" checks; "+performance);EditorApplication.Exit(0);
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static BuildDefinition D(BuildCatalog c,string suffix)
    {foreach(var d in c.presets)if(d.contentId.EndsWith(suffix,StringComparison.Ordinal))return d;throw new Exception("Missing "+suffix);}
    private static BuildPieceRecord Add(BuildSession s,BuildDefinition d,Vector3 origin,int yaw,bool ground=true)=>s.Add(d,s.CreateFrame(origin,yaw),default,0,ground);
    private static BuildPreview Snap(BuildSession s,BuildDefinition d,BuildPieceRecord target,Vector3 point,Vector3 normal,int? heading=null)
    {
        var q=BuildGeometry.Rotation(target.WorldYawStep);
        return BuildPlacement.Solve(d,target.Origin+q*point,q*normal,target,s.Frame(target.OwnFrameId),0,0,default,target.Origin+q*(point+normal*4),false,heading);
    }
    private static void Assets(BuildCatalog c)
    {
        Check(c!=null&&c.presets.Length>=56,"Installed catalog missing");
        foreach(var d in c.presets)
        {
            if(d.contentId.StartsWith("build.stone.l",StringComparison.Ordinal)&&d.kind==BuildPartKind.Wall&&d.topCourseDepth==0&&d.LocalBounds.size.z<.6f)
            {Check(Mathf.Abs(d.LocalBounds.size.z-.25f)<.001f,"Limestone thickness: "+d.name);Near(d.mesh.bounds.min,d.LocalBounds.min,"Thin mesh min");Near(d.mesh.bounds.max,d.LocalBounds.max,"Thin mesh max");}
            if(d.contentId.Contains("limestone-stair")||d.contentId.EndsWith("l19-threshold-steps"))
            {Check(Mathf.Abs(d.LocalBounds.size.x-2)<.001f,"Modular stair width");Near(d.collisionMesh.bounds.max,d.LocalBounds.max,"Walking hull envelope");Check(d.authoringPrefab.GetComponent<MeshCollider>().sharedMesh==d.collisionMesh,"Prefab walking hull stale");}
            if(d.occupiedVolumes!=null)foreach(var v in d.occupiedVolumes){Near(v.bounds.min,v.mesh.bounds.min,"Hull min parity");Near(v.bounds.max,v.mesh.bounds.max,"Hull max parity");}
        }
        Check(D(c,"l35-braced-door-canopy").relaxedWallMount,"Canopy policy not installed");
        Check(Mathf.Abs(D(c,"l22-solid-bedded-limestone-landing").LocalBounds.size.x-2)<.001f,"Landing not modular");
        Check(BuildTopCourse.Compatible(D(c,"l01-coursed-limestone"),D(c,"l18-limestone-eaves-cornice")),"Thin cornice host compatibility");
    }
    private static void Footings(BuildCatalog c,int yaw)
    {
        using var s=new BuildSession();var d=D(c,"l06-limestone-footing");var p=Add(s,d,new Vector3(20,10,20),yaw);var b=d.LocalBounds;var inverse=Quaternion.Inverse(BuildGeometry.Rotation(yaw));
        for(int turn=0;turn<8;turn++)foreach(var normal in new[]{Vector3.left,Vector3.right,Vector3.back,Vector3.forward})
        {
            var point=b.center+Vector3.Scale(normal,b.extents);var preview=Snap(s,d,p,point,normal,BuildGeometry.Turn(yaw+turn));
            var shape=BuildGeometry.WorldBounds(d.LocalBounds,inverse*(preview.Origin-p.Origin),turn);
            Check(Mathf.Abs(shape.max.y-b.max.y)<.001f,"Footing top changed");
            float distance=normal.x<0?b.min.x-shape.max.x:normal.x>0?shape.min.x-b.max.x:normal.z<0?b.min.z-shape.max.z:shape.min.z-b.max.z;
            Check(Mathf.Abs(distance)<.001f,"Offset footing seam gap/overlap");
        }
        // A run along its own axis retains the centred width.
        var end=Snap(s,d,p,new Vector3(4,-.2f,0),Vector3.right);
        Near(inverse*(end.Origin-p.Origin),new Vector3(4,0,0),"Footing end drift");
    }
    private static void FacesAndFloors(BuildCatalog c,int yaw)
    {
        using var s=new BuildSession();var low=D(c,"l13-low-courtyard-wall");var p=Add(s,low,new Vector3(30,20,30),yaw);
        for(int i=1;i<10;i++)
        {
            var end=Snap(s,low,p,new Vector3(0,i*.125f,0),Vector3.left);
            Check(end.Hint!="Stack wall","End face overridden by height");
            Check(Mathf.Abs(end.Origin.y-p.Origin.y)<.001f,"End face raised");
        }
        var top=Snap(s,low,p,new Vector3(2,1.25f,0),Vector3.up);Check(top.Hint=="Stack wall","Top face no longer stacks");
        var wall=Add(s,c.presets[0],new Vector3(50,20,30),yaw);var inverse=Quaternion.Inverse(BuildGeometry.Rotation(yaw));float last=float.NegativeInfinity;
        for(int tick=0;tick<=16;tick++)
        {
            var preview=Snap(s,c.presets[1],wall,new Vector3(tick*.25f,2,.125f),Vector3.forward);
            float x=(inverse*(preview.Origin-wall.Origin)).x;
            if(tick>0)Check(Mathf.Abs(x-last-.25f)<.001f,"Floor cursor sweep jumps or stalls");last=x;
        }
        var gable=D(c,"l31-framed-gable-rising");var a=Add(s,gable,new Vector3(70,23,30),yaw);var b=Add(s,gable,a.Origin+BuildGeometry.Rotation(yaw)*new Vector3(2,0,0),yaw);
        var left=Snap(s,c.presets[1],a,new Vector3(1.75f,.5f,.125f),Vector3.forward);
        var right=Snap(s,c.presets[1],b,new Vector3(0,.5f,.125f),Vector3.forward);
        Near(right.Origin-left.Origin,BuildGeometry.Rotation(yaw)*new Vector3(.25f,0,0),"Adjacent gable grid discontinuity");
    }
    private static void Stairs(BuildCatalog c,int yaw)
    {
        using var s=new BuildSession();var d=D(c,"l20-half-storey-limestone-stair");var stair=Add(s,d,new Vector3(20,40,60),yaw);var q=BuildGeometry.Rotation(yaw);
        foreach(var side in new[]{Vector3.left,Vector3.right})
        {
            var preview=Snap(s,d,stair,new Vector3(side.x<0?0:2,.5f,1),side);
            Near(Quaternion.Inverse(q)*(preview.Origin-stair.Origin),new Vector3(side.x*2,0,0),"Stair side alignment");
            var p=new BuildPieceRecord{Definition=d,Origin=preview.Origin,WorldYawStep=(byte)preview.WorldYaw};
            Check(BuildStairAttachment.Connects(p,stair,stair.Resolved),"Side snap lacks support");
            p.Origin+=Vector3.up*.25f;Check(!BuildStairAttachment.Connects(p,stair,stair.Resolved),"Mismatched stair side supported");
        }
        var floor=Add(s,c.presets[1],new Vector3(40,43,60),yaw);
        var first=Snap(s,d,floor,new Vector3(.05f,-.1f,0),Vector3.back);var second=Snap(s,d,floor,new Vector3(3.95f,-.1f,0),Vector3.back);
        Near(Quaternion.Inverse(q)*(second.Origin-first.Origin),new Vector3(2,0,0),"Two stair widths do not fill bay");
        for(int heading=0;heading<8;heading++)
        {
            var edge=Snap(s,d,floor,new Vector3(2,-.1f,0),Vector3.back,heading);
            var top=Snap(s,d,floor,new Vector3(2,0,2),Vector3.up,heading);
            var wall=Add(s,c.presets[0],new Vector3(60+heading*8,40,60),yaw);
            var face=Snap(s,d,wall,new Vector3(2,2,.125f),Vector3.forward,heading);
            Check(edge.WorldYaw==heading&&top.WorldYaw==heading&&face.WorldYaw==heading,"Stair manual heading overwritten by face");
        }
    }
    private static void Canopy(BuildCatalog c,int yaw)
    {
        var obj=new GameObject("Canopy regression");try
        {
            var world=obj.AddComponent<BuildWorld>();var s=world.Session;var wall=Add(s,D(c,"l10-arched-limestone-doorway"),new Vector3(80,60,20),yaw);
            var d=D(c,"l35-braced-door-canopy");var p=Snap(s,d,wall,new Vector3(2,2.5f,.125f),Vector3.forward);world.Validate(ref p);
            Check(p.Valid,"Canopy still requires every mount: "+p.Message);var placed=world.Commit(p);Check(placed!=null&&placed.Supported,"Canopy commit support");
            s.Remove(wall.Id);Check(!placed.Supported,"Canopy survives loss of all support");
        }finally{Object.DestroyImmediate(obj);}
    }
    private static void Parity(BuildCatalog c,int yaw)
    {
        var obj=new GameObject("Preview parity");try
        {
            var world=obj.AddComponent<BuildWorld>();var s=world.Session;var origin=new Vector3(120,80,20);var foundation=Add(s,c.presets[2],origin,yaw);
            var q=BuildGeometry.Rotation(yaw);var wall=Add(s,c.presets[0],origin,yaw,false);
            Add(s,c.presets[0],origin+q*new Vector3(4,0,0),yaw,false);
            foreach(var d in new[]{c.presets[0],c.presets[1],c.presets[3],D(c,"l20-half-storey-limestone-stair")})
            for(int tick=0;tick<8;tick++)
            {
                var p=Snap(s,d,foundation,new Vector3(.5f+tick*.25f,0,2),Vector3.up);world.Validate(ref p);
                var candidate=new BuildPieceRecord{Id=ulong.MaxValue,Definition=d,Origin=p.Origin,WorldYawStep=(byte)p.WorldYaw,Grounded=p.Grounded};candidate.WorldBounds=BuildSession.IndexBounds(d,candidate.Origin,candidate.WorldYawStep);
                var all=new List<BuildPieceRecord>(s.Pieces.Values){candidate};using var resolver=new BuildResolution();var full=resolver.Resolve(all,r=>s.NeighboursFor(r,candidate));
                var supported=BuildSupportState.Resolve(all,full,r=>s.NeighboursFor(r,candidate));bool loss=false;foreach(var existing in s.Pieces.Values)loss|=existing.Supported&&!supported.Contains(existing.Id);
                Check(p.Valid==(supported.Contains(candidate.Id)&&!loss&&p.Failure!=BuildPlacementFailure.Solid),"Local/global eligibility mismatch: "+d.name+" "+p.Message);
                foreach(var pair in world.DebugStates)
                {
                    var expected=full[pair.Key];Check(pair.Value.key==expected.key&&pair.Value.boxes.Length==expected.boxes.Length&&pair.Value.attachments.Length==expected.attachments.Length,"Sparse/full resolution mismatch");
                    for(int i=0;i<expected.boxes.Length;i++){Near(pair.Value.boxes[i].min,expected.boxes[i].min,"Sparse solid min");Near(pair.Value.boxes[i].max,expected.boxes[i].max,"Sparse solid max");}
                }
            }
        }finally{Object.DestroyImmediate(obj);}
    }
    private static void Rotation(BuildCatalog c)
    {
        var obj=new GameObject("Rotation regression");var cameraObj=new GameObject("Aim camera");var proxies=new List<GameObject>();
        try
        {
            var camera=cameraObj.AddComponent<Camera>();var controller=obj.AddComponent<BuildingController>();var world=obj.GetComponent<BuildWorld>();
            typeof(BuildingController).GetField("viewCamera",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,camera);
            typeof(BuildingController).GetMethod("Start",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,null);
            var a=Add(world.Session,c.presets[2],new Vector3(0,100,0),0);var b=Add(world.Session,c.presets[2],new Vector3(4,100,0),2);
            foreach(var p in new[]{a,b}){var proxy=new GameObject("Target proxy");proxy.AddComponent<BuildGameplayProxy>().Bind(p);proxies.Add(proxy);}
            Physics.SyncTransforms();controller.Toggle();controller.Select(D(c,"w22-full-storey-stair"));
            var late=typeof(BuildingController).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
            void Aim(BuildPieceRecord p){camera.transform.position=p.Origin+BuildGeometry.Rotation(p.WorldYawStep)*new Vector3(2,3,2);camera.transform.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);}
            Aim(a);late.Invoke(controller,null);controller.Submit(new BuildInputCommand(1,default,false,false));late.Invoke(controller,null);int heading=controller.Preview.WorldYaw;
            Aim(b);late.Invoke(controller,null);Check(controller.Preview.WorldYaw==heading,"Target change reset manual turn");
            camera.transform.rotation=Quaternion.LookRotation(Vector3.up);late.Invoke(controller,null);Aim(a);late.Invoke(controller,null);Check(controller.Preview.WorldYaw==heading,"Aim miss reset manual turn");
            controller.OpenMenu();controller.Select(D(c,"w22-full-storey-stair"));Aim(b);late.Invoke(controller,null);Check(controller.Preview.WorldYaw==heading,"Reselecting same piece reset heading");
            controller.OpenMenu();controller.Select(c.presets[0]);Check(typeof(BuildingController).GetField("manualHeading",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller)==null,"Changing piece retained rotation lock");
        }finally{foreach(var proxy in proxies)Object.DestroyImmediate(proxy);Object.DestroyImmediate(obj);Object.DestroyImmediate(cameraObj);}
    }
    private static void LockedHeadings(BuildCatalog c)
    {
        using var s=new BuildSession();var targets=new List<BuildPieceRecord>();
        foreach(var d in new[]{c.presets[2],c.presets[0],c.presets[3],D(c,"w22-full-storey-stair"),D(c,"l33-layered-shingle-roof")})
            targets.Add(Add(s,d,new Vector3(1000+targets.Count*10,400,1000),targets.Count%8));
        foreach(var d in c.presets)foreach(var target in targets)for(int heading=0;heading<8;heading++)
        {
            var b=target.Definition.LocalBounds;
            var preview=Snap(s,d,target,b.center+Vector3.forward*b.extents.z,Vector3.forward,heading);
            Check(preview.WorldYaw==heading,"Manual heading overwritten: "+d.contentId+" at "+target.Definition.contentId);
        }
    }
    private static string Performance(BuildCatalog c)
    {
        var obj=new GameObject("Village performance");try
        {
            var world=obj.AddComponent<BuildWorld>();var s=world.Session;var target=Add(s,c.presets[2],new Vector3(0,200,0),0);var times=new List<double>();int localSmall=0,localLarge=0;
            for(int stage=0;stage<2;stage++)
            {
                if(stage==1)for(int i=0;i<512;i++)Add(s,c.presets[0],new Vector3(20+(i%32)*5,200,(i/32)*5),0);
                for(int i=0;i<48;i++)
                {
                    var p=BuildPlacement.Solve(c.presets[0],target.Origin+new Vector3(2,0,.1f),Vector3.up,target,s.Frame(target.OwnFrameId),0,0,new Vector3Int(i%15,0,0));
                    var timer=Stopwatch.StartNew();world.Validate(ref p);timer.Stop();if(stage==1&&i>7)times.Add(timer.Elapsed.TotalMilliseconds);
                    Check(p.Valid,"Village preview rejected: "+p.Message);Check(!world.PreviewUsedFullSupport,"Ordinary wall preview recomputed global support");
                    if(stage==0)localSmall=s.PreviewResolutionCount;else localLarge=s.PreviewResolutionCount;
                }
            }
            Check(localLarge==localSmall,"Distant village increases preview geometry work");times.Sort();
            var fittedTimes=new List<double>();
            for(int i=0;i<48;i++)
            {
                var floor=BuildPlacement.Solve(c.presets[1],target.Origin+new Vector3(2,0,2),Vector3.up,target,s.Frame(target.OwnFrameId),0,0,new Vector3Int(i%15,0,0));
                var timer=Stopwatch.StartNew();world.Validate(ref floor);timer.Stop();
                Check(floor.Valid&&world.PreviewUsedFullSupport,"Fitted village preview lost support validation");
                if(i>7)fittedTimes.Add(timer.Elapsed.TotalMilliseconds);
            }
            fittedTimes.Sort();
            var candidate=new BuildPieceRecord{Id=ulong.MaxValue,Definition=c.presets[0],Origin=new Vector3(0,200,0),WorldYawStep=0};candidate.WorldBounds=BuildSession.IndexBounds(candidate.Definition,candidate.Origin,0);
            var all=new List<BuildPieceRecord>(s.Pieces.Values){candidate};using var fullResolver=new BuildResolution();var oldTimes=new List<double>();
            for(int i=0;i<12;i++){candidate.Origin=new Vector3((i%15)*.25f,200,0);candidate.WorldBounds=BuildSession.IndexBounds(candidate.Definition,candidate.Origin,0);var timer=Stopwatch.StartNew();var states=fullResolver.Resolve(all,p=>s.NeighboursFor(p,candidate));BuildSupportState.Resolve(all,states,p=>s.NeighboursFor(p,candidate));timer.Stop();if(i>1)oldTimes.Add(timer.Elapsed.TotalMilliseconds);}oldTimes.Sort();
            return "{\"villagePieces\":"+s.Pieces.Count+",\"resolvedPreviewPieces\":"+localLarge+",\"localMedianMs\":"+times[times.Count/2].ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+",\"localP95Ms\":"+times[(int)(times.Count*.95)].ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+",\"fittedMedianMs\":"+fittedTimes[fittedTimes.Count/2].ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+",\"fullSessionMedianMs\":"+oldTimes[oldTimes.Count/2].ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+"}";
        }finally{Object.DestroyImmediate(obj);}
    }
    private static void FittedSupportAndRendering(BuildCatalog c)
    {
        var obj=new GameObject("Fitted support regression");var cameraObj=new GameObject("Batch camera");
        try
        {
            var world=obj.AddComponent<BuildWorld>();var s=world.Session;var foundation=Add(s,c.presets[2],new Vector3(0,300,0),0);
            var camera=cameraObj.AddComponent<Camera>();camera.transform.position=new Vector3(2,305,-5);camera.transform.LookAt(new Vector3(2,300,2));
            var renderer=new BuildRenderer(s);renderer.Draw(camera,100,0);int rebuilds=renderer.BatchRebuilds;
            var p=Snap(s,c.presets[1],foundation,new Vector3(2,0,2),Vector3.up);world.Validate(ref p);
            Check(p.Valid&&world.PreviewUsedFullSupport,"Fitted foundation did not take authoritative support path");
            var changes=new Dictionary<ulong,BuildResolvedState>{{foundation.Id,world.DebugStates[foundation.Id]}};
            renderer.SetPreview(changes);renderer.Draw(camera,100,0);Check(renderer.BatchRebuilds==rebuilds,"Preview rebuilt stable village batches");
            renderer.SetPreview(null);renderer.Draw(camera,100,0);Check(renderer.BatchRebuilds==rebuilds,"Clearing preview rebuilt stable batches");
            var wall=Add(s,D(c,"l01-coursed-limestone"),new Vector3(20,300,0),0);
            var course=Snap(s,D(c,"l18-limestone-eaves-cornice"),wall,new Vector3(2,3,0),Vector3.up);world.Validate(ref course);
            Check(course.Valid&&world.PreviewUsedFullSupport,"Cornice fitting skipped global support validation: "+course.Message);
            var placed=world.Commit(course);Check(placed!=null&&placed.Supported,"Thin cornice failed commit");s.Remove(wall.Id);Check(!placed.Supported,"Cornice survived host loss");
            var landing=D(c,"l22-solid-bedded-limestone-landing");var bed=Add(s,c.presets[2],new Vector3(40,300,0),0);
            var tile=Snap(s,landing,bed,new Vector3(1,0,1),Vector3.up);world.Validate(ref tile);Check(tile.Valid,"Two-metre landing rejected full bedding: "+tile.Message);
            var slab=world.Commit(tile);Check(slab!=null&&slab.Supported,"Landing support missing");s.Remove(bed.Id);Check(!slab.Supported,"Landing survived loss of bedding");
        }finally{Object.DestroyImmediate(obj);Object.DestroyImmediate(cameraObj);}
    }
}
