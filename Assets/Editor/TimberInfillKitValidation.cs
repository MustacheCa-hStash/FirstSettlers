using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Imported meshes, live URP and Play-mode placement/physical apertures.</summary>
[InitializeOnLoad]
public static class TimberInfillKitValidation
{
    public const string Output="ArtReferences/LimestoneBuildKit/TimberInfill";
    private const string Pending="FS.TimberInfill.Validation";
    private static int checks;
    private static readonly List<string> errors=new();
    static TimberInfillKitValidation()=>EditorApplication.playModeStateChanged+=OnState;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.000001f,message+": "+a+" / "+b);
    public static void InstallAndRunBatch()=>Run(true);
    public static void RunInstalledBatch()=>Run(false);
    private static void Run(bool install)
    {
        try
        {
            checks=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            if(install)
            {
                var originals=new Dictionary<string,string>();
                foreach(var d in catalog.presets)if(Array.IndexOf(TimberInfillKitSetup.Ids,d.contentId)<0)originals.Add(AssetDatabase.GetAssetPath(d),File.ReadAllText(AssetDatabase.GetAssetPath(d)));
                TimberInfillKitSetup.Install();int count=catalog.presets.Length;
                var guids=new Dictionary<string,string>();foreach(var id in TimberInfillKitSetup.Ids)guids.Add(id,AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog.Find(id))));
                TimberInfillKitSetup.Install();Check(catalog.presets.Length==count,"Reinstall duplicated menu entries");
                foreach(var guid in guids)Check(guid.Value==AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog.Find(guid.Key))),"Reinstall changed definition GUID");
                foreach(var original in originals)Check(File.ReadAllText(original.Key)==original.Value,"Unrelated component changed: "+original.Key);
            }
            Assets(catalog);Render(catalog);
            SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog catalog)
    {
        var ids=new HashSet<string>();foreach(var d in catalog.presets)Check(d!=null&&ids.Add(d.contentId),"Duplicate or missing menu definition");
        var source=JsonUtility.FromJson<TimberInfillKitSetup.Manifest>(File.ReadAllText(TimberInfillKitSetup.ManifestPath));
        Check(source.parts.Length==7,"Expected six selectable parts and opposite gable slope");
        var shared=AssetDatabase.LoadAssetAtPath<Material>(TimberInfillKitSetup.MaterialPath);
        Check(shared!=null&&shared.enableInstancing&&shared.shader.name=="Custom/BakedStoneWallMatte"&&shared.GetFloat("_AlphaClip")==0,"Opaque instanced matte material missing");
        Check(shared.GetTexture("_BaseMap")!=null&&shared.GetTexture("_BumpMap")!=null,"Trim/normal pair absent");
        foreach(var part in source.parts)
        {
            var d=catalog.Find("build.wood."+part.slug);Check(d!=null,"Missing definition: "+part.tag);
            Near(d.LocalBounds.min,TimberInfillKitSetup.V(part.minimum),"Native origin");Near(d.LocalBounds.size,TimberInfillKitSetup.V(part.size),"Native dimensions");
            Check(d.preserveAuthoredMesh&&d.material==shared&&!d.canGroundOnTerrain&&d.jointCoverVariants.Length==0,"Authored framing contract missing");
            Check(d.mesh.subMeshCount==1&&d.mesh.triangles.Length/3==part.triangles,"Imported triangle/submesh count: "+part.tag);
            Near(d.mesh.bounds.min,TimberInfillKitSetup.V(part.visual_minimum),"FBX min");Near(d.mesh.bounds.max,TimberInfillKitSetup.V(part.visual_maximum),"FBX max");
            Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh==d.mesh&&d.authoringPrefab.GetComponent<MeshRenderer>().sharedMaterial==shared,"Prefab references differ");
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Mesh thumbnail missing");
            Check(!BuildUiContent.Description(d).StartsWith("Choose this component",StringComparison.Ordinal),"Description missing");
            var vertices=d.mesh.vertices;var uv=d.mesh.uv;var normals=d.mesh.normals;var tangents=d.mesh.tangents;var t=d.mesh.triangles;
            Check(uv.Length==vertices.Length&&normals.Length==vertices.Length&&tangents.Length==vertices.Length,"Imported attributes absent");
            for(int i=0;i<vertices.Length;i++)
            {
                Check(float.IsFinite(uv[i].x)&&float.IsFinite(uv[i].y)&&uv[i].x>=0&&uv[i].x<=1&&uv[i].y>=0&&uv[i].y<=1,"UV escaped approved trim");
                Vector3 tangent=tangents[i];Check(Mathf.Abs(normals[i].magnitude-1)<.001f&&Mathf.Abs(tangent.magnitude-1)<.002f&&Mathf.Abs(Vector3.Dot(normals[i],tangent))<.002f,"Normal/tangent basis invalid");
            }
            for(int i=0;i<t.Length;i+=3)
            {
                var a=vertices[t[i]];var b=vertices[t[i+1]];var c=vertices[t[i+2]];
                Check(Vector3.Cross(b-a,c-a).sqrMagnitude>1e-14f,"Degenerate triangle");
                var u=uv[t[i+1]]-uv[t[i]];var v=uv[t[i+2]]-uv[t[i]];
                Check(Mathf.Abs(u.x*v.y-u.y*v.x)>1e-12f,"Collapsed UV triangle");
                Check(Vector3.Dot(Vector3.Cross(b-a,c-a),normals[t[i]]+normals[t[i+1]]+normals[t[i+2]])>0,"Reversed imported face");
            }
            if(part.tag=="L27"||part.tag=="L28")
            {
                var aperture=d.wallOpenings[0];
                for(int x=1;x<5;x++)for(int y=1;y<5;y++)
                {
                    var p=Vector3.Lerp(aperture.min,aperture.max,.5f);p.x=Mathf.Lerp(aperture.min.x,aperture.max.x,x/5f);p.y=Mathf.Lerp(aperture.min.y,aperture.max.y,y/5f);p.z=-1;
                    Check(!MeshHit(d.mesh,p,Vector3.forward),"Visible mesh closes aperture: "+part.tag);
                }
                Check(MeshHit(d.mesh,new Vector3(.5f,1,-1),Vector3.forward),"Solid wall side missing");
                if(part.tag=="L28")Near(aperture.size,new Vector3(1,1,.25f),"Requested open window size");
            }
            if(d.IsHalfGable)
            {
                Check(d.flipVariant!=null&&d.flipVariant.flipVariant==d&&d.occupiedVolumes.Length==1&&d.solidBoxes.Length==0,"Gable flip/triangular occupancy absent");
                bool falling=d.gableSlope==GableSlope.Falling;
                Check(!MeshHit(d.mesh,new Vector3(falling?1.5f:.5f,1.5f,-1),Vector3.forward),"Gable fills its empty half");
                Check(MeshHit(d.mesh,new Vector3(falling?.5f:1.5f,.5f,-1),Vector3.forward),"Gable interior missing");
                CoplanarJoinFaces(d.mesh,-.125f,new Vector2(0,0),new Vector2(2,2));
            }
            if(part.tag=="L35")CoplanarJoinFaces(d.mesh,0,new Vector2(-1,-.75f),new Vector2(1,1.5f));
        }
        var roof=catalog.Find(TimberInfillKitSetup.Ids[4]);
        Check(roof.kind==BuildPartKind.Roof&&roof.occupiedVolumes.Length==2&&roof.roofContinuationMesh!=null,"Roof must use thin occupied slope and clipped continuation");
        Check(roof.mesh.bounds.min.z<-.20f&&roof.roofContinuationMesh.bounds.min.z>=-.00001f,"Roof eave clipping absent");
        int steps=0;foreach(var p in roof.mesh.vertices)if(p.z<.12f&&p.y-p.z>.22f)steps++;
        Check(steps>100&&roof.mesh.bounds.max.y>2.25f,"Roof lost solid overlapping course depth");
        var canopy=catalog.Find(TimberInfillKitSetup.Ids[5]);
        Check(canopy.furnitureWallMounted&&canopy.furnitureSupportPoints.Length==4&&canopy.occupiedVolumes.Length==6&&BuildUiContent.Category(canopy)==6,"Canopy mounts/real brackets/menu category missing");
        Check(Array.Find(catalog.presets,d=>d.contentId==TimberInfillKitSetup.FallingId)==null,"Opposite gable added redundant menu tile");
    }

    // Regression for the black patches seen in Cycles at overlapping frame joints.
    // Interior points on a visible planar timber face must have one surface only.
    private static void CoplanarJoinFaces(Mesh mesh,float z,Vector2 lo,Vector2 hi)
    {
        var vertices=mesh.vertices;var triangles=mesh.triangles;var faces=new List<(Vector2 a,Vector2 b,Vector2 c)>();
        for(int i=0;i<triangles.Length;i+=3)
        {
            var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
            if(Mathf.Abs(a.z-z)<.00001f&&Mathf.Abs(b.z-z)<.00001f&&Mathf.Abs(c.z-z)<.00001f)faces.Add((a,b,c));
        }
        for(int x=0;x<40;x++)for(int y=0;y<40;y++)
        {
            var point=new Vector2(Mathf.Lerp(lo.x,hi.x,(x+.371f)/40),Mathf.Lerp(lo.y,hi.y,(y+.613f)/40));int layers=0;
            foreach(var face in faces)
            {
                var a=face.b-face.a;var b=face.c-face.a;var p=point-face.a;float det=a.x*b.y-a.y*b.x;if(Mathf.Abs(det)<1e-10f)continue;
                float u=(p.x*b.y-p.y*b.x)/det,v=(a.x*p.y-a.y*p.x)/det;
                if(u>.0001f&&v>.0001f&&u+v<.9999f)layers++;
            }
            Check(layers<=1,"Coplanar timber faces overlap at "+point+" in "+mesh.name);
        }
    }

    private static bool MeshHit(Mesh mesh,Vector3 origin,Vector3 direction)
    {
        var v=mesh.vertices;var t=mesh.triangles;
        for(int i=0;i<t.Length;i+=3)
        {
            var e1=v[t[i+1]]-v[t[i]];var e2=v[t[i+2]]-v[t[i]];var h=Vector3.Cross(direction,e2);float det=Vector3.Dot(e1,h);if(Mathf.Abs(det)<1e-7f)continue;
            var delta=origin-v[t[i]];float u=Vector3.Dot(delta,h)/det;if(u<0||u>1)continue;
            var q=Vector3.Cross(delta,e1);float w=Vector3.Dot(direction,q)/det;float distance=Vector3.Dot(e2,q)/det;
            if(w>=0&&u+w<=1&&distance>0&&distance<2)return true;
        }
        return false;
    }
    private static void OnState(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=SessionState.GetInt(Pending+".checks",0);errors.Clear();
        void Log(string message,string trace,LogType type){if(type is LogType.Error or LogType.Exception or LogType.Assert)errors.Add(message);}
        Application.logMessageReceived+=Log;
        try
        {
            var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            for(int yaw=0;yaw<8;yaw++){Walls(c,yaw);Gables(c,yaw);Roofs(c,yaw);Canopy(c,yaw);}
            Check(errors.Count==0,"Runtime errors: "+string.Join(" | ",errors));
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/unity-validation.json","{\"passed\":true,\"checks\":"+checks+",\"parts\":6,\"gableVariants\":2,\"headings\":8,\"doorWalks\":16,\"roofJoinCases\":32,\"canopyMountCases\":32,\"urpRenders\":true,\"instancedRendering\":true}");
            Debug.Log("TIMBER INFILL PASS: "+checks+" checks; six components, two gable slopes, open apertures, layered roof joins, physical traversal and wall-mounted canopy.");
            Application.logMessageReceived-=Log;EditorApplication.Exit(0);
        }catch(Exception ex){Application.logMessageReceived-=Log;Debug.LogException(ex);EditorApplication.Exit(1);}
    }

    private static void Walls(BuildCatalog c,int yaw)
    {
        var origin=new Vector3(2000+yaw*40,30,2000);var q=BuildGeometry.Rotation(yaw);
        for(int part=0;part<3;part++)
        {
            using var s=new BuildSession();var frame=s.CreateFrame(origin,yaw);var foundation=s.Add(c.Find("build.prototype.foundation"),frame,default,0,true);
            var d=c.Find(TimberInfillKitSetup.Ids[part]);var wall=s.Add(d,frame,default,0,false);Check(wall.Supported,"Wall lost foundation bearing");
            var preview=BuildPlacement.Solve(d,origin+q*new Vector3(2,3,0),Vector3.up,wall,s.Frame(wall.OwnFrameId),yaw,0,default);
            Near(preview.Origin,origin+Vector3.up*3,"Timber wall stack datum");
            var upper=s.Add(d,frame,new Vector3Int(0,12,0),0,false);Check(upper.Supported&&upper.Resolved.mesh==d.mesh&&upper.Resolved.auxiliaryMesh==null,"Stack replaced integral timber frame");
            using var gameplay=new BuildGameplay(s);gameplay.Update(origin+q*new Vector3(2,1,1),32,40,32);Physics.SyncTransforms();
            Check(gameplay.TryGetProxy(wall.Id,out var proxy),"Wall proxy absent");var solids=new List<Collider>();proxy.GetEnabledColliders(solids);
            Check(solids.Count==(part==0?1:part==1?3:4),"Physical aperture sections wrong");
            bool Ray(Vector3 p,Vector3 dir){foreach(var collider in solids)if(collider.Raycast(new Ray(origin+q*p,q*dir),out _,2))return true;return false;}
            Check(Ray(new Vector3(.5f,1,-1),Vector3.forward),"Wall side collider missing");
            if(part>0)
            {
                var hole=d.wallOpenings[0].center;
                Check(!Ray(hole+Vector3.back,Vector3.forward)&&!Ray(hole+Vector3.forward,Vector3.back),"Aperture blocks physical ray");
                if(part==1)
                {
                    var pad=GameObject.CreatePrimitive(PrimitiveType.Cube);pad.layer=GameplayLayers.WorldSolid;pad.transform.position=origin+new Vector3(2,-.1f,0);pad.transform.localScale=new Vector3(12,.2f,12);Physics.SyncTransforms();
                    try{Walk(origin,q,false);Walk(origin,q,true);}finally{Object.DestroyImmediate(pad);Physics.SyncTransforms();}
                }
                else Check(Ray(new Vector3(2,.5f,-1),Vector3.forward)&&Ray(new Vector3(2,2.5f,-1),Vector3.forward),"Window sill/head collision missing");
            }
            s.Remove(foundation.Id);Check(!wall.Supported&&!upper.Supported,"Wall stack retained support after foundation removal");
        }
    }
    private static void Walk(Vector3 origin,Quaternion q,bool reverse)
    {
        var rig=new GameObject("L27 actual doorway traversal");var cc=rig.AddComponent<CharacterController>();cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.skinWidth=.08f;cc.stepOffset=.3f;cc.minMoveDistance=0;
        rig.transform.SetPositionAndRotation(origin+q*new Vector3(2,.03f,reverse?1.5f:-1.5f),q);var motor=rig.AddComponent<CharacterMotor>();Physics.SyncTransforms();
        try
        {
            for(int i=0;i<20;i++)motor.Simulate(default,1f/60);
            for(int i=0;i<52;i++)motor.Simulate(new CharacterMoveCommand(new Vector2(0,reverse?-1:1),false,false,false),1f/60);
            var p=Quaternion.Inverse(q)*(rig.transform.position-origin);Check(reverse?p.z<-.8f:p.z>.8f,"L27 blocks character: "+p);Check(Mathf.Abs(p.y)<.15f,"Doorway lost floor contact");
        }finally{Object.DestroyImmediate(rig);Physics.SyncTransforms();}
    }
    private static void Gables(BuildCatalog c,int yaw)
    {
        var host=new GameObject("L31 gable fixture");var world=host.AddComponent<BuildWorld>();world.enabled=false;
        var origin=new Vector3(3000+yaw*40,30,3000);var q=BuildGeometry.Rotation(yaw);var s=world.Session;var frame=s.CreateFrame(origin,yaw);
        try
        {
            var foundation=s.Add(c.Find("build.prototype.foundation"),frame,default,0,true);var wall=s.Add(c.Find(TimberInfillKitSetup.Ids[0]),frame,default,0,false);
            var rising=c.Find(TimberInfillKitSetup.Ids[3]);BuildPieceRecord first=null;
            foreach(var d in new[]{rising,rising.flipVariant})
            {
                bool falling=d.gableSlope==GableSlope.Falling;var p=BuildPlacement.Solve(d,origin+q*new Vector3(falling?3:1,2.9f,0),q*Vector3.forward,wall,s.Frame(wall.OwnFrameId),yaw,0,default);
                world.Validate(ref p);Check(p.Valid,"Gable rejected on framed wall: "+p.Message);var a=world.Commit(p);Check(a!=null&&a.Supported,"Gable commit failed");if(first==null)first=a;
                Near(a.Origin,origin+q*new Vector3(falling?2:0,3,0),"Gable base datum");
                using var gameplay=new BuildGameplay(s);gameplay.Update(origin+q*new Vector3(2,4,1),32,40,32);Physics.SyncTransforms();
                Check(gameplay.TryGetProxy(a.Id,out var proxy),"Gable collider absent");var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);Check(colliders.Count==1&&colliders[0] is MeshCollider,"Gable uses attic box");
                Check(!colliders[0].Raycast(new Ray(a.Origin+q*new Vector3(falling?1.5f:.5f,1.5f,-1),q*Vector3.forward),out _,2),"Empty gable half blocks ray");
            }
            var roof=c.Find(TimberInfillKitSetup.Ids[4]);var snap=BuildPlacement.Solve(roof,first.Origin+q*new Vector3(1,1,0),q*Vector3.forward,first,s.Frame(first.OwnFrameId),yaw,0,default,first.Origin+q*new Vector3(1,1,2));
            world.Validate(ref snap);Check(snap.Valid,"L33 cannot attach to L31: "+snap.Message);Check(world.Commit(snap)!=null,"L31 roof commit failed");
            s.Remove(foundation.Id);foreach(var a in s.Pieces.Values)Check(!a.Supported,"Gable roof retained support after removal");
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }
    private static void Roofs(BuildCatalog c,int yaw)
    {
        using var s=new BuildSession();var origin=new Vector3(4000+yaw*40,30,4000);var q=BuildGeometry.Rotation(yaw);var frame=s.CreateFrame(origin,yaw);
        var foundation=s.Add(c.Find("build.prototype.foundation"),frame,default,0,true);var wall=s.Add(c.Find(TimberInfillKitSetup.Ids[0]),frame,default,0,false);var d=c.Find(TimberInfillKitSetup.Ids[4]);
        var snap=BuildPlacement.Solve(d,wall.Origin+q*new Vector3(2,3,0),Vector3.up,wall,s.Frame(wall.OwnFrameId),yaw,0,default,origin+q*new Vector3(2,1,2));
        Near(snap.Origin,origin+Vector3.up*3,"Roof eave datum");var a=s.Add(d,frame,new Vector3Int(0,12,0),0,false);Check(a.Supported,"Roof lost eave bearing");
        foreach(var join in new[]{(new Vector3Int(16,12,0),0),(new Vector3Int(0,20,8),0),(new Vector3Int(16,12,16),4)})
        {
            var b=s.Add(d,frame,join.Item1,join.Item2,false);Check(b.Supported,"Roof join unsupported");Check(!BuildOccupancy.Overlaps(d,a.Origin,a.WorldYawStep,d,b.Origin,b.WorldYawStep),"Roof join colliders overlap");
            if(join.Item1.y==20)Check(b.Resolved.mesh==d.roofContinuationMesh,"Uphill eave not clipped");s.Remove(b.Id);
        }
        var mixed=s.Add(c.Find(ThatchRoofSetup.ContentId),frame,new Vector3Int(16,12,0),0,false);Check(mixed.Supported,"Shingle/thatch interface lost standard sockets");s.Remove(mixed.Id);
        using var gameplay=new BuildGameplay(s);gameplay.Update(origin+q*new Vector3(2,4,1),32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(a.Id,out var proxy),"Roof proxy missing");
        var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);Check(colliders.Count==2,"Roof missing thin slab and bearing");
        var attic=new Ray(a.Origin+q*new Vector3(-1,.5f,1.5f),q*Vector3.right);var slope=new Ray(a.Origin+q*new Vector3(2,4,1.5f),Vector3.down);bool hit=false;
        foreach(var col in colliders){Check(!col.Raycast(attic,out _,6),"Roof fills attic");hit|=col.Raycast(slope,out _,4);}Check(hit,"Roof slope is not physical");
        s.Remove(foundation.Id);Check(!a.Supported,"Roof kept support after eave bearing removal");
    }
    private static void Canopy(BuildCatalog c,int yaw)
    {
        var host=new GameObject("L35 wall attachment fixture");var world=host.AddComponent<BuildWorld>();world.enabled=false;
        var s=world.Session;var origin=new Vector3(5000+yaw*40,30,5000);var q=BuildGeometry.Rotation(yaw);var frame=s.CreateFrame(origin,yaw);
        try
        {
            s.Add(c.Find("build.prototype.foundation"),frame,default,0,true);var door=s.Add(c.Find(TimberInfillKitSetup.Ids[1]),frame,default,0,false);
            var d=c.Find(TimberInfillKitSetup.Ids[5]);var point=origin+q*new Vector3(2,2.5f,.125f);
            var preview=BuildPlacement.Solve(d,point,q*Vector3.forward,door,s.Frame(door.OwnFrameId),yaw,0,default);world.Validate(ref preview);
            Check(preview.Valid,"Canopy still requires every upper ledger mount");
            var upper=s.Add(c.Find(TimberInfillKitSetup.Ids[0]),frame,new Vector3Int(0,12,0),0,false);
            preview=BuildPlacement.Solve(d,point,q*Vector3.forward,door,s.Frame(door.OwnFrameId),yaw,0,default);world.Validate(ref preview);
            Check(preview.Valid,"Canopy rejected complete supported wall face: "+preview.Message);Near(preview.Origin,point,"Canopy moved away from actual wall face");
            var canopy=world.Commit(preview);Check(canopy!=null&&canopy.Supported,"Canopy failed commit");
            using var gameplay=new BuildGameplay(s);gameplay.Update(point,32,40,32);Physics.SyncTransforms();Check(gameplay.TryGetProxy(canopy.Id,out var proxy),"Canopy proxy missing");
            var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);Check(colliders.Count==6,"Canopy has no real roof/bracket hulls");
            foreach(var col in colliders)Check(!col.Raycast(new Ray(canopy.Origin+q*new Vector3(0,-.4f,-1),q*Vector3.forward),out _,3),"Canopy blocks clear headway between brackets");
            var invalid=BuildPlacement.Solve(d,origin,Vector3.up,null,null,yaw,0,default);Check(invalid.InvalidAttachment,"Canopy can start unsupported on terrain");
            s.Remove(upper.Id);Check(canopy.Supported,"Canopy lost supported lower bracket contact");
            var gable=c.Find(TimberInfillKitSetup.Ids[3]);var left=s.Add(gable,frame,new Vector3Int(0,12,0),0,false);var right=s.Add(gable.flipVariant,frame,new Vector3Int(8,12,0),0,false);
            Check(canopy.Supported,"Canopy cannot mount across a supported wall and triangular gables");
            var tooHigh=BuildPlacement.Solve(d,point+Vector3.up*6,q*Vector3.forward,door,s.Frame(door.OwnFrameId),yaw,0,default);world.Validate(ref tooHigh);
            Check(!tooHigh.Valid,"Empty upper half of gable falsely supports canopy mounts");
            s.Remove(right.Id);Check(canopy.Supported,"Canopy requires every gable mount");s.Remove(left.Id);s.Remove(door.Id);Check(!canopy.Supported,"Canopy survives loss of all structural hosts");
        }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
    }

    private static void Render(BuildCatalog c)
    {
        Directory.CreateDirectory(Output);var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
        var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;var oldSun=RenderSettings.sun;bool oldAsync=ShaderUtil.allowAsyncCompilation;var oldTarget=RenderTexture.active;
        var root=new GameObject("Timber infill render fixture");var cameraObject=new GameObject("Timber review camera");var lightObject=new GameObject("Timber review sun");
        var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.aspect=16f/9;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.19f,.20f);
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.None;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(28,-40,0);
        var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);target.Create();Action<ScriptableRenderContext,Camera> submit=null;
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");Check(pipeline!=null,"URP missing");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.3f,.3f,.3f);RenderSettings.sun=light;ShaderUtil.allowAsyncCompilation=false;
            var material=c.Find(TimberInfillKitSetup.Ids[0]).material;for(int i=0;i<material.passCount;i++)ShaderUtil.CompilePass(material,i,true);Check(!ShaderUtil.ShaderHasError(material.shader),"Timber shader error");
            var objects=new List<GameObject>();
            GameObject Add(BuildDefinition d,Vector3 pos,int yaw=0)
            {
                var obj=new GameObject(d.displayName){layer=31};obj.transform.SetParent(root.transform);obj.transform.SetPositionAndRotation(pos,BuildGeometry.Rotation(yaw));obj.AddComponent<MeshFilter>().sharedMesh=d.mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=d.material;return obj;
            }
            for(int i=0;i<6;i++)
            {
                var d=c.Find(TimberInfillKitSetup.Ids[i]);var obj=Add(d,Vector3.zero);objects.Add(obj);var centre=d.mesh.bounds.center;
                camera.orthographicSize=2.4f;camera.transform.position=centre+new Vector3(4,2,i==5?8:-8);camera.transform.LookAt(centre);Capture(camera,target,TimberInfillKitSetup.Slugs[i]+"-unity.png");
                camera.transform.position=centre+new Vector3(-4,2,i==5?-8:8);camera.transform.LookAt(centre);Capture(camera,target,TimberInfillKitSetup.Slugs[i]+"-unity-back.png");
                if(i==4)
                {
                    camera.orthographicSize=2.2f;camera.transform.position=new Vector3(6,.5f,-2);camera.transform.LookAt(new Vector3(2,.7f,.7f));Capture(camera,target,"L33-eave-depth-unity.png");
                    camera.transform.position=new Vector3(5,-2,4);camera.transform.LookAt(new Vector3(2,1,1));Capture(camera,target,"L33-underside-unity.png");
                }
                obj.SetActive(false);
            }
            for(int i=0;i<6;i++){objects[i].transform.position=new Vector3(i%3*6,0,i/3*5);objects[i].SetActive(true);}
            camera.orthographicSize=6.2f;camera.transform.position=new Vector3(18,13,-18);camera.transform.LookAt(new Vector3(8,1,3));Capture(camera,target,"timber-infill-unity-overview.png");foreach(var obj in objects)obj.SetActive(false);
            using(var session=new BuildSession())
            {
                var frame=session.CreateFrame(Vector3.zero,1);for(int i=0;i<6;i++)session.Add(c.Find(TimberInfillKitSetup.Ids[i]),frame,new Vector3Int(i%3*24,0,i/3*20),0,false);
                var renderer=new BuildRenderer(session);var rotation=BuildGeometry.Rotation(1);camera.transform.position=rotation*new Vector3(18,13,-18);camera.transform.LookAt(rotation*new Vector3(8,1,3));camera.cullingMask=~0;
                submit=(_,current)=>{if(current==camera)renderer.Draw(camera,100,0);};RenderPipelineManager.beginCameraRendering+=submit;Capture(camera,target,"timber-infill-instanced.png");Check(renderer.DrawCalls>0&&renderer.DrawCalls<=6,"Instanced timber rendering missing");RenderPipelineManager.beginCameraRendering-=submit;submit=null;camera.cullingMask=1<<31;
            }
            // Single-storey study on an L08 plinth: all new pieces at native scale.
            var plinth=c.Find(LimestoneKitExtensionSetup.Ids[4]);
            Add(plinth,Vector3.zero);Add(plinth,new Vector3(4,0,0),6);Add(plinth,new Vector3(0,0,4));Add(plinth,Vector3.zero,6);
            Add(c.Find(TimberInfillKitSetup.Ids[1]),new Vector3(0,.5f,0));
            Add(c.Find(TimberInfillKitSetup.Ids[2]),new Vector3(4,.5f,0),6);
            Add(c.Find(TimberInfillKitSetup.Ids[0]),new Vector3(0,.5f,4));Add(c.Find(TimberInfillKitSetup.Ids[0]),new Vector3(0,.5f,0),6);
            var gable=c.Find(TimberInfillKitSetup.Ids[3]);foreach(float z in new[]{0f,4f}){Add(gable,new Vector3(0,3.5f,z));Add(gable.flipVariant,new Vector3(2,3.5f,z));}
            Add(c.Find(TimberInfillKitSetup.Ids[4]),new Vector3(0,3.5f,4),2);Add(c.Find(TimberInfillKitSetup.Ids[4]),new Vector3(4,3.5f,0),6);
            Add(c.Find(TimberInfillKitSetup.Ids[5]),new Vector3(2,3,-.125f),4);
            camera.orthographicSize=4.3f;camera.transform.position=new Vector3(12,9,-13);camera.transform.LookAt(new Vector3(2,2.7f,2));Capture(camera,target,"mixed-limestone-timber-homestead-unity.png");
        }
        finally
        {
            if(submit!=null)RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;RenderSettings.sun=oldSun;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=oldTarget;
            Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);target.Release();Object.DestroyImmediate(target);
        }
    }
    private static void Capture(Camera camera,RenderTexture target,string name)
    {
        for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try
        {
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetPixels32();var background=pixels[0];int visible=0,pink=0;
            foreach(var p in pixels){if(p.r>200&&p.b>200&&p.g<50)pink++;if(Math.Abs(p.r-background.r)+Math.Abs(p.g-background.g)+Math.Abs(p.b-background.b)>12)visible++;}
            Check(visible>1000&&pink<100,"Blank/error render: "+name);File.WriteAllBytes(Output+"/"+name,image.EncodeToPNG());
        }finally{Object.DestroyImmediate(image);}
    }
}
