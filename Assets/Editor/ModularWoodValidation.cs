using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

/// <summary>Asset/UV inspection followed by real Play-mode full-span snapping, cover lifecycle and collider checks.</summary>
[InitializeOnLoad]
public static class ModularWoodValidation
{
    private const string Pending="FS.ModularWood.Validation";
    private static int checks;
    private static readonly List<string> errors=new();
    static ModularWoodValidation()=>EditorApplication.playModeStateChanged+=OnState;
    public static void RunBatch()=>Run(true);
    // Validate the assets actually installed by the user without rebuilding away stale references.
    public static void RunInstalledBatch()=>Run(false);
    private static void Run(bool rebuild)
    {
        try
        {
            if(rebuild)BuildingPrototypeSetup.CreateAssets();
            Assets(Resources.Load<BuildCatalog>("Building/PrototypeCatalog"));
            SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Check(bool condition,string message){checks++;if(!condition)throw new InvalidOperationException(message);}
    private static void Log(string message,string stack,LogType type){if(type is LogType.Error or LogType.Exception or LogType.Assert)errors.Add(message);}
    private static void OnState(PlayModeStateChange change)
    {
        if(change!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=SessionState.GetInt(Pending+".checks",0);errors.Clear();Application.logMessageReceived+=Log;
        try
        {
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");Check(Application.isPlaying,"Validation must run in Play mode");
            GameplayLifetime(c);
            OpeningWallValidation.Runtime(c,Check);
            var walls=new[]{c.presets[0],c.Find(WattleWallSetup.ContentId)};
            for(int yaw=0;yaw<8;yaw++)
            {
                Junctions(c,yaw);
                foreach(var wall in walls)foreach(var mate in walls)Placement(c,yaw,wall,mate);
            }
            var walk=typeof(W21StairValidation).GetMethod("Walk",BindingFlags.NonPublic|BindingFlags.Static);
            var stair=c.Find(W21StairSetup.ContentId);
            foreach(int yaw in new[]{0,1,2,7})foreach(float x in new[]{.43f,.625f,.82f})foreach(bool down in new[]{false,true})foreach(float dt in new[]{1f/60,1f/120})foreach(int flights in new[]{1,2})
                walk.Invoke(null,new object[]{stair,c,yaw,x,down,dt,flights});
            Render(c);Check(errors.Count==0,"Runtime errors: "+string.Join(" | ",errors));
            Directory.CreateDirectory(".utmp/modular-kit");File.WriteAllText(".utmp/modular-kit/validation.json","{\"passed\":true,\"checks\":"+checks+",\"realMotorWalks\":96}");
            Debug.Log("MODULAR WOOD PASS: "+checks+" checks; full-span dimensions, authored UVs/normals, varied floors, exact endpoint snaps, butt/corner/T/cross covers, explicit-post suppression/restoration, construction order, three storeys, retained colliders, 96 real-motor stair walks and actual rendered views.");
            Application.logMessageReceived-=Log;EditorApplication.Exit(0);
        }catch(Exception ex){Application.logMessageReceived-=Log;Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog c)
    {
        Check(c.presets.Length==10 && c.Find(SplitPlankWallSetup.InfillContentId)==null,"Removed infill still in menu or catalog changed unexpectedly");
        OpeningWallValidation.Assets(c,Check);
        var wall=c.presets[0];var floor=c.presets[1];var post=c.presets[3];var wattle=c.Find(WattleWallSetup.ContentId);
        foreach(var d in new[]{wall,wattle})
        {
            Check(d.kind==BuildPartKind.Wall && d.wallPlacementMode==WallPlacementMode.Panel && d.sizeUnits==new Vector3Int(16,12,1) && d.wallEndInsetUnits==0 && d.stackRiseUnits==12 && d.jointSockets.Length==17,"Full-span profile missing: "+d.name);
            Near(d.mesh.bounds.min,new Vector3(0,0,-.125f),"Wall native origin: "+d.name);Near(d.mesh.bounds.max,new Vector3(4,3,.125f),"Wall native dimensions: "+d.name);
            Check(d.jointCoverVariants.Length==3 && d.jointCoverMaterial==wall.material,"Shared post references missing");
            var prefab=d.authoringPrefab;Check(prefab!=null && prefab.GetComponent<MeshFilter>().sharedMesh==d.mesh,"Wall prefab mesh reference mismatch");
            Near(prefab.GetComponent<BoxCollider>().center,d.LocalBounds.center,"Wall prefab collider centre");Near(prefab.GetComponent<BoxCollider>().size,d.LocalBounds.size,"Wall prefab collider size");
            Check(prefab.transform.localScale==Vector3.one && prefab.transform.localRotation==Quaternion.identity,"Wall prefab transform changes envelope");
            string path=d==wall?SplitPlankWallSetup.ModelPath:WattleWallSetup.ModelPath;
            Check(d.mesh==ModularWoodSetup.WallMesh(AssetDatabase.LoadAssetAtPath<GameObject>(path)),"Wall uses stale mesh copy instead of its authored FBX: "+d.name);
        }
        Near(post.LocalBounds.size,new Vector3(.25f,3,.25f),"Post physics size");Near(post.mesh.bounds.size,new Vector3(.26f,3.003f,.26f),"Post visual relief");
        Check(floor.uvVariants.Length==3 && floor.floorOpeningUvVariants.Length==3,"Floor UV layouts missing");
        var all=new List<Mesh>{wall.mesh,wattle.mesh};all.AddRange(post.uvVariants);all.AddRange(floor.uvVariants);all.AddRange(floor.floorOpeningUvVariants);
        foreach(var id in new[]{OpeningWallSetup.DoorId,OpeningWallSetup.WindowId,OpeningWallSetup.GableId,OpeningWallSetup.FallingId})all.Add(c.Find(id).mesh);
        all.Add(c.Find(OpeningWallSetup.GableId).gableRoofSeamMesh);all.Add(c.Find(OpeningWallSetup.FallingId).gableRoofSeamMesh);
        foreach(var mesh in all)UVs(mesh);
        foreach(var variant in floor.uvVariants)
        {
            var (v,n,uv)=Read(variant);var rectangles=new HashSet<string>();
            for(int board=0;board<14;board++)
            {
                Vector2 lo=Vector2.one*10,hi=Vector2.one*-10;int found=0;
                for(int i=0;i<v.Length;i++)if(n[i].y>.99f && Mathf.Abs(v[i].y)<.00001f && v[i].x>.25f+board*.25f+.002f && v[i].x<.25f+board*.25f+.248f && v[i].z>=.25f-.00001f && v[i].z<=3.75f+.00001f)
                {lo=Vector2.Min(lo,uv[i]);hi=Vector2.Max(hi,uv[i]);found++;}
                Check(found>=4,"Floorboard top UV vertices missing");rectangles.Add(lo.ToString("F5")+hi.ToString("F5"));
            }
            Check(rectangles.Count==14,"Floorboards repeat identical UV space");
        }
        var baseUv=Read(floor.uvVariants[0]).uv;foreach(var variant in new[]{floor.uvVariants[1],floor.uvVariants[2]})
        {var other=Read(variant);Check(other.v.Length==baseUv.Length,"UV variant changed geometry");int different=0;for(int i=0;i<baseUv.Length;i++)if((other.uv[i]-baseUv[i]).sqrMagnitude>.000001f)different++;Check(different>baseUv.Length/2,"Floor UV variant is effectively identical");}
        foreach(string path in new[]{SplitPlankWallSetup.ModelPath,WattleWallSetup.ModelPath,BayPostSetup.ModelPath,FloorFramingSetup.Full,FloorFramingSetup.Opening})
        {var importer=(ModelImporter)AssetImporter.GetAtPath(path);Check(importer.bakeAxisConversion && !importer.isReadable && importer.materialImportMode==ModelImporterMaterialImportMode.None,"Import settings inconsistent: "+path);}
    }
    private static (Vector3[] v,Vector3[] n,Vector2[] uv) Read(Mesh mesh)
    {
        using var data=MeshUtility.AcquireReadOnlyMeshData(mesh);var source=data[0];
        using var v=new NativeArray<Vector3>(source.vertexCount,Allocator.Temp);using var n=new NativeArray<Vector3>(source.vertexCount,Allocator.Temp);using var uv=new NativeArray<Vector2>(source.vertexCount,Allocator.Temp);
        source.GetVertices(v);source.GetNormals(n);source.GetUVs(0,uv);return(v.ToArray(),n.ToArray(),uv.ToArray());
    }
    private static void UVs(Mesh mesh)
    {
        var (v,n,uv)=Read(mesh);Check(mesh.subMeshCount==1 && uv.Length==v.Length,"Missing UVs/material submesh: "+mesh.name);
        using var snapshot=MeshUtility.AcquireReadOnlyMeshData(mesh);var source=snapshot[0];var sub=source.GetSubMesh(0);using var t=new NativeArray<int>(sub.indexCount,Allocator.Temp);source.GetIndices(t,0);
        for(int i=0;i<t.Length;i+=3)
        {
            int a=t[i],b=t[i+1],c=t[i+2];Vector2 x=uv[b]-uv[a],y=uv[c]-uv[a];
            Check(Mathf.Abs(x.x*y.y-x.y*y.x)>1e-12f,"Collapsed face UV: "+mesh.name+" triangle "+i/3);
            Check(Vector3.Cross(v[b]-v[a],v[c]-v[a]).sqrMagnitude>1e-14f,"Degenerate face: "+mesh.name);
        }
        for(int i=0;i<uv.Length;i++){Check(uv[i].x>=0 && uv[i].x<=1 && uv[i].y>=0 && uv[i].y<=1,"UV outside atlas");Check(Mathf.Abs(n[i].magnitude-1)<.001f,"Invalid face normal");}
    }
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.000001f,message+": "+a+" != "+b);
    private static void GameplayLifetime(BuildCatalog c)
    {
        using var s=new BuildSession();var f=s.CreateFrame(new Vector3(31000,31000,31000),0);
        var a=s.Add(c.presets[0],f,default,0,true);
        // Individual active proxy disappears while the pool is still running.
        using(var gameplay=new BuildGameplay(s))
        {
            gameplay.Update(f.Origin,32,40,8);Check(gameplay.TryGetProxy(a.Id,out var proxy),"No lifetime fixture proxy");
            Object.DestroyImmediate(proxy.gameObject);Check(!gameplay.TryGetProxy(a.Id,out _),"Destroyed proxy exposed to queries");
            gameplay.Update(f.Origin,32,40,8);Check(gameplay.TryGetProxy(a.Id,out var replacement) && replacement!=null,"Destroyed active proxy was not recreated");
            replacement.TryGetInfo(out var info);gameplay.Dispose();gameplay.Dispose();
            Check(!info.IsValid && !replacement.Shape.enabled && gameplay.ActiveCount==0,"Dispose did not immediately retire collision/query identity");
            gameplay.Update(f.Origin,32,40,8);Check(gameplay.ActiveCount==0,"Disposed gameplay recreated collisions");
        }
        // A destroyed pooled object must not be returned to a new occupant.
        using(var gameplay=new BuildGameplay(s))
        {
            gameplay.Update(f.Origin,32,40,8);gameplay.TryGetProxy(a.Id,out var proxy);gameplay.Remove(a.Id);
            Object.DestroyImmediate(proxy.gameObject);gameplay.Update(f.Origin,32,40,8);
            Check(gameplay.TryGetProxy(a.Id,out var replacement) && replacement.Shape.enabled,"Destroyed pooled proxy reused");
            Object.DestroyImmediate(replacement.gameObject);gameplay.Remove(a.Id);Check(gameplay.ActiveCount==0,"Dead proxy removal failed");
        }
        // Simulate Unity destroying the collision hierarchy before BuildWorld.OnDestroy.
        foreach(bool updateFirst in new[]{false,true})
        {
            using var gameplay=new BuildGameplay(s);gameplay.Update(f.Origin,32,40,8);gameplay.TryGetProxy(a.Id,out var proxy);
            Object.DestroyImmediate(proxy.transform.parent.gameObject);
            if(updateFirst)gameplay.Update(f.Origin,32,40,8);
            gameplay.Dispose();gameplay.Dispose();proxy.Unbind();Check(gameplay.ActiveCount==0,"Destroyed hierarchy retained active entries");
        }
        var host=new GameObject("World teardown fixture");host.transform.position=f.Origin;var world=host.AddComponent<BuildWorld>();world.enabled=false;world.Focus=host.transform;
        var frame=world.Session.CreateFrame(f.Origin,0);var wall=world.Session.Add(c.presets[0],frame,default,0,true);
        typeof(BuildWorld).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(world,null);
        Check(world.TryGetCollisionProxy(wall.Id,out var target),"BuildWorld teardown fixture missing proxy");
        Object.DestroyImmediate(target.transform.parent.gameObject);Object.DestroyImmediate(host);Physics.SyncTransforms();
        Check(errors.Count==0,"Gameplay lifetime errors: "+string.Join(" | ",errors));
        Debug.Log("GAMEPLAY LIFETIME PASS: destroyed active/pooled proxies, root-before-world teardown, immediate retirement and repeated Dispose.");
    }
    private static BuildPreview At(BuildDefinition d,BuildGridFrame f,Vector3Int a,int turn=0)=>new(){Definition=d,Frame=f,Anchor=a,YawStep=turn,Origin=BuildGeometry.WorldPoint(f,a),WorldYaw=BuildGeometry.Turn(f.YawStep+turn)};
    private static int Covers(BuildSession s){int count=0;foreach(var p in s.Pieces.Values)count+=p.Resolved.attachments.Length;return count;}
    private static void Junctions(BuildCatalog c,int yaw)
    {
        var walls=new[]{c.presets[0],c.Find(WattleWallSetup.ContentId)};
        foreach(var d in walls)foreach(var mate in walls)
        foreach(var kind in new[]{BuildJointKind.Corner,BuildJointKind.Butt,BuildJointKind.Tee,BuildJointKind.Cross})foreach(bool reverse in new[]{false,true})
        {
            using var s=new BuildSession();var f=s.CreateFrame(new Vector3(200+yaw*20,50,200),yaw);BuildPieceRecord a,b;
            var position=kind==BuildJointKind.Butt?new Vector3Int(16,0,0):kind==BuildJointKind.Corner?new Vector3Int(16,0,0):kind==BuildJointKind.Tee?new Vector3Int(8,0,0):new Vector3Int(8,0,8);
            int turn=kind==BuildJointKind.Butt?0:2;
            if(reverse){b=s.Add(mate,f,position,turn,true);a=s.Add(d,f,default,0,true);}else{a=s.Add(d,f,default,0,true);b=s.Add(mate,f,position,turn,true);}
            Check(Covers(s)==1,"Junction cover duplicated/missing: "+kind+" yaw="+yaw);
            foreach(var p in s.Pieces.Values)foreach(var attachment in p.Resolved.attachments)Check(attachment.material==c.presets[0].material,"Wattle joint used cutout rather than solid wood");
            bool seen=false;foreach(var contact in a.Resolved.joints)if(contact.Kind==kind)seen=true;Check(seen,"Junction type wrong: "+kind);
            Vector3 point=kind is BuildJointKind.Corner or BuildJointKind.Butt?new Vector3(4,0,0):new Vector3(2,0,0);
            var post=s.Add(c.presets[3],f,BuildGeometry.Ticks(point),0,false);Check(Covers(s)==0,"Explicit post failed to suppress generated cover");
            s.Remove(post.Id);Check(Covers(s)==1,"Removing explicit post did not restore cover");
            var upperA=s.Add(d,f,new Vector3Int(0,12,0),0,false);var upperB=s.Add(mate,f,position+new Vector3Int(0,12,0),turn,false);
            Check(Covers(s)==2,"Storey junctions were merged or duplicated");
            s.Remove(b.Id);Check(Covers(s)==1,"Removing a wall left its cover orphaned");
            Check(a.Resolved.mesh==d.mesh,"Cover replaced original wall body");
        }
        // Three walls meeting at the same endpoint still produce one cover.
        using(var s=new BuildSession())
        {
            var f=s.CreateFrame(new Vector3(100,20,100),yaw);s.Add(c.presets[0],f,default,0,true);s.Add(c.presets[0],f,new Vector3Int(16,0,0),2,false);s.Add(c.Find(WattleWallSetup.ContentId),f,new Vector3Int(32,0,0),4,false);
            Check(Covers(s)==1,"Three-wall joint generated multiple posts");
        }
    }
    private static void Placement(BuildCatalog c,int yaw,BuildDefinition d,BuildDefinition mate)
    {
        var host=new GameObject("Full-span placement fixture");host.transform.position=new Vector3(23000+yaw*20,23000,23000);var w=host.AddComponent<BuildWorld>();w.enabled=false;w.Focus=host.transform;var f=w.Session.CreateFrame(host.transform.position,yaw);var q=BuildGeometry.Rotation(yaw);
        try
        {
            var foundation=w.Session.Add(c.presets[2],f,default,0,true);
            var start=BuildPlacement.Solve(d,f.Origin+q*new Vector3(2,0,0),Vector3.up,foundation,w.Session.Frame(foundation.OwnFrameId),0,0,default);
            var wall=w.Commit(start);Check(wall!=null,"Full-span wall on bay boundary failed");Near(wall.Origin,f.Origin,"Wall still has an end reservation");
            var frame=w.Session.Frame(wall.OwnFrameId);
            var next=BuildPlacement.Solve(mate,wall.Origin+q*new Vector3(3.9f,.5f,0),q*Vector3.forward,wall,frame,0,2,default,wall.Origin+q*new Vector3(3,1,-2));
            var joined=w.Commit(next);Check(joined!=null && Covers(w.Session)==1,"Socket corner preview/commit failed");
            Near(joined.Origin,wall.Origin+q*new Vector3(4,0,0),"Corner snapped to physical face instead of boundary line");
            var post=BuildPlacement.Solve(c.presets[3],wall.Origin+q*new Vector3(3.98f,.5f,.125f),q*Vector3.forward,wall,frame,0,0,default);
            var placed=w.Commit(post);Check(placed!=null && Covers(w.Session)==0,"Post centre snapping/suppression failed");
            Near(placed.Origin,wall.Origin+q*new Vector3(4,0,0),"Post centre shifted by half thickness");w.Remove(placed.Id);Check(Covers(w.Session)==1,"Post removal did not restore junction visual");
            var upper=BuildPlacement.Solve(mate,wall.Origin+q*new Vector3(2,2.9f,0),q*Vector3.forward,wall,frame,0,0,default);var upperRecord=w.Commit(upper);
            Check(upperRecord!=null && upperRecord.Resolved.auxiliaryMesh==null,"Full-height stack added old bearing band");Near(upperRecord.Origin,wall.Origin+Vector3.up*3,"Wrong storey height");
            var deck=BuildPlacement.Solve(c.presets[1],wall.Origin+q*new Vector3(2,3,0),Vector3.up,wall,frame,0,0,default,wall.Origin+q*new Vector3(2,4,2));
            Check(w.Commit(deck)!=null,"Upper floor seating failed");Near(deck.Origin,wall.Origin+Vector3.up*3,"Floor shifted off boundary grid");
            var roof=BuildPlacement.Solve(c.Find(ThatchRoofSetup.ContentId),wall.Origin+q*new Vector3(2,3,0),Vector3.up,wall,frame,0,0,default,wall.Origin+q*new Vector3(2,4,2));
            Check(w.Commit(roof)!=null,"Roof seating failed");Near(roof.Origin,wall.Origin+Vector3.up*3,"Roof has quarter-metre offset");
            var angled=BuildPlacement.Solve(mate,wall.Origin+q*new Vector3(4,.5f,0),q*Vector3.forward,wall,frame,0,1,default,wall.Origin+q*new Vector3(3,1,-2));
            var candidate=w.Commit(angled);Check(candidate!=null,"Rotated endpoint was rejected");Check(candidate.Resolved.joints.Length>0,"Rotated endpoint rounded away from the declared joint");
            var endMate=BuildPlacement.Solve(mate,wall.Origin+q*new Vector3(4,.5f,0),q*Vector3.forward,wall,frame,0,1,default,wall.Origin+q*new Vector3(3,1,2));
            var endRecord=w.Commit(endMate);Check(endRecord!=null && endRecord.Resolved.joints.Length>0,"Non-grid rotated end socket was rounded away from its joint");
            var duplicate=start;w.Validate(ref duplicate);Check(!duplicate.Valid,"Duplicate full-span wall accepted");
            typeof(BuildWorld).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,null);
            Check(w.TryGetCollisionProxy(wall.Id,out var proxy) && proxy.Shape.size==new Vector3(4,3,.25f),"Wall collider is old size");
            Check(w.TryGetCollisionProxy(joined.Id,out var other) && other.Shape.enabled,"Joint cover disabled wall collision");
            var stairs=At(c.Find(W21StairSetup.ContentId),f,new Vector3Int(5,0,0));
            w.Validate(ref stairs);Check(stairs.Valid && stairs.Warning!=BuildPlacementWarning.None,"Wall/stair overlap policy differed: "+d.name);
            var flight=w.Commit(stairs);Check(flight!=null,"Wall/stair overlap failed commit");
            var nextFlight=At(flight.Definition,f,new Vector3Int(5,6,8));Check(w.Commit(nextFlight)!=null,"Second stair flight next to wall failed");
            var storey=At(mate,f,new Vector3Int(0,24,0));var third=w.Commit(storey);Check(third!=null && third.Resolved.mesh==mate.mesh,"Wall above stairs changed eligibility/body mesh");
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Render(BuildCatalog c)
    {
        Directory.CreateDirectory(".utmp/modular-kit");using var s=new BuildSession();var f=s.CreateFrame(Vector3.zero,0);
        s.Add(c.presets[2],f,new Vector3Int(0,0,-16),0,true);var a=s.Add(c.presets[0],f,default,0,false);var b=s.Add(c.Find(WattleWallSetup.ContentId),f,new Vector3Int(16,0,0),2,false);
        s.Add(c.presets[0],f,new Vector3Int(0,12,0),0,false);s.Add(c.Find(WattleWallSetup.ContentId),f,new Vector3Int(16,12,0),2,false);
        s.Add(c.presets[1],f,new Vector3Int(0,12,-16),0,false);
        var camObj=new GameObject("Modular render camera");var camera=camObj.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.aspect=1.5f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.13f);
        var lightObj=new GameObject("Modular light");var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;lightObj.transform.rotation=Quaternion.Euler(35,-35,0);
        var renderer=new BuildRenderer(s);var target=new RenderTexture(1200,800,24);target.Create();var old=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;var active=RenderTexture.active;
        Action<ScriptableRenderContext,Camera> submit=(_,cam)=>{if(cam==camera)renderer.Draw(camera,3000,140);};
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;RenderPipelineManager.beginCameraRendering+=submit;
            void Capture(string name,Vector3 position,Vector3 look,float size)
            {
                camera.transform.position=position;camera.transform.LookAt(look);camera.orthographicSize=size;
                for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;var pixels=new Texture2D(1200,800,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1200,800),0,0);pixels.Apply();File.WriteAllBytes(".utmp/modular-kit/"+name+".png",pixels.EncodeToPNG());Object.Destroy(pixels);Check(renderer.DrawCalls>0,"No actual instanced draw submissions");
            }
            Capture("two-storey-corner",new Vector3(9,7,-9),new Vector3(2,2.8f,-1),4.5f);
            Capture("wall-back",new Vector3(-4,4,8),new Vector3(2,3,0),4);
            var post=s.Add(c.presets[3],f,new Vector3Int(16,0,0),0,false);Capture("explicit-post",new Vector3(8,4,-8),new Vector3(3,2,-1),3.3f);s.Remove(post.Id);
            Capture("cover-restored",new Vector3(8,4,-8),new Vector3(3,2,-1),3.3f);
            s.Remove(a.Id);s.Remove(b.Id);Capture("floor-underside",new Vector3(7,0,-7),new Vector3(2,3,-2),3.7f);
            foreach(ulong id in new List<ulong>(s.Pieces.Keys))s.Remove(id);
            for(int i=0;i<3;i++)s.Add(c.presets[1],f,new Vector3Int(i*16,0,0),0,true);
            Capture("varied-floorboards",new Vector3(6,12,3),new Vector3(6,0,2),5.2f);
            foreach(ulong id in new List<ulong>(s.Pieces.Keys))s.Remove(id);
            s.Add(c.Find(OpeningWallSetup.DoorId),f,default,0,true);s.Add(c.Find(OpeningWallSetup.WindowId),f,new Vector3Int(20,0,0),0,true);
            s.Add(c.Find(OpeningWallSetup.GableId),f,new Vector3Int(40,0,0),0,true);s.Add(c.Find(OpeningWallSetup.FallingId),f,new Vector3Int(48,0,0),0,true);
            Capture("opening-kit-front",new Vector3(7,5,-16),new Vector3(7,1.5f,0),5.2f);
            Capture("opening-kit-back",new Vector3(7,5,16),new Vector3(7,1.5f,0),5.2f);
            foreach(ulong id in new List<ulong>(s.Pieces.Keys))s.Remove(id);
            s.Add(c.presets[2],f,default,0,true);s.Add(c.Find(OpeningWallSetup.DoorId),f,default,0,false);
            s.Add(c.Find(OpeningWallSetup.WindowId),f,new Vector3Int(16,0,0),6,false);s.Add(c.presets[0],f,new Vector3Int(0,0,16),0,false);
            s.Add(c.Find(OpeningWallSetup.GableId),f,new Vector3Int(0,12,0),0,false);s.Add(c.Find(OpeningWallSetup.FallingId),f,new Vector3Int(8,12,0),0,false);
            s.Add(c.Find(ThatchRoofSetup.ContentId),f,new Vector3Int(0,12,16),2,false);s.Add(c.Find(ThatchRoofSetup.ContentId),f,new Vector3Int(16,12,0),6,false);
            Capture("opening-kit-house",new Vector3(9,7,-11),new Vector3(2,2.5f,2),4.6f);
        }finally{RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=old;QualitySettings.renderPipeline=quality;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(camObj);Object.DestroyImmediate(lightObj);}
    }
}
