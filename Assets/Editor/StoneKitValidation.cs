using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Read actual imported geometry/UVs, then exercise stone placement and support in Play mode.</summary>
[InitializeOnLoad]
public static class StoneKitValidation
{
    private const string Pending="FS.StoneKit.Validation";
    private static int checks;
    private static readonly List<string> errors=new();
    static StoneKitValidation()=>EditorApplication.playModeStateChanged+=OnState;
    public static void RunBatch()=>Run(true);
    public static void RunInstalledBatch()=>Run(false);
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static void Near(Vector3 a,Vector3 b,string name)=>Check((a-b).sqrMagnitude<.000001f,name+": "+a+" != "+b);
    private static void Log(string message,string trace,LogType type){if(type is LogType.Error or LogType.Exception or LogType.Assert)errors.Add(message);}
    private static void Run(bool install)
    {
        try
        {
            if(install)StoneKitSetup.Install();
            var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            Assets(catalog);
            SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog catalog)
    {
        int limestoneCount=0;foreach(var id in LimestoneKitSetup.Ids)if(catalog.Find(id)!=null)limestoneCount++;
        foreach(var id in LimestoneKitExtensionSetup.Ids)if(catalog.Find(id)!=null)limestoneCount++;
        foreach(var id in LimestoneCourtyardSetup.Ids)if(catalog.Find(id)!=null)limestoneCount++;
        foreach(var id in LimestoneAccessSetup.Ids)if(catalog.Find(id)!=null)limestoneCount++;
        int timberCount=0;foreach(var id in TimberInfillKitSetup.Ids)if(catalog.Find(id)!=null)timberCount++;
        Check(catalog.presets.Length==(catalog.Find(SquareInfillSetup.Id)!=null?13:12)+StoneKitSetup.Ids.Length+limestoneCount+
            timberCount+WoodExtensionKitSetup.PresentCount(catalog)+StoneCompletionKitSetup.PresentCount(catalog)+(catalog.Find("build.furniture.f01-three-leg-stool")!=null?6:0)+(catalog.Find("build.lighting.wall-torch")!=null?1:0),"Expected existing wood, stone, timber infill, optional furniture and lighting menu entries");
        var ids=new HashSet<string>();foreach(var d in catalog.presets)Check(ids.Add(d.contentId),"Duplicate catalog ID");
        foreach(var id in StoneKitSetup.Ids)
        {
            var d=catalog.Find(id);Check(d!=null,"Missing "+id);Check(d.preserveAuthoredMesh,"Stone mesh can be replaced by timber fitting");
            Check(d.uvVariants.Length==3 && d.mesh==d.uvVariants[0],"Three UV layouts not linked");
            Check(d.jointCoverVariants.Length==0,"Stone gained automatic timber posts");
            Check(d.material.shader.name=="Custom/BakedStoneWallMatte" && d.material.mainTexture!=null,"Missing baked matte UV material");
            var prefab=d.authoringPrefab;Check(prefab!=null,"Missing authoring prefab");
            Check(prefab.GetComponent<MeshFilter>().sharedMesh==d.mesh,"Prefab mesh mismatch");
            var solids=d.solidBoxes.Length>0?d.solidBoxes:new[]{d.LocalBounds};var colliders=prefab.GetComponents<BoxCollider>();
            Check(colliders.Length==solids.Length,"Prefab solid count mismatch");
            for(int j=0;j<solids.Length;j++){Near(colliders[j].size,solids[j].size,"Collider size");Near(colliders[j].center,solids[j].center,"Collider centre");}
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Missing actual-mesh thumbnail");
            Vector3[] baseVertices=null;Vector2[] baseUV=null;
            for(int variation=0;variation<3;variation++)
            {
                Mesh mesh=d.uvVariants[variation];Check(!mesh.isReadable && mesh.subMeshCount==1,"Import must be one non-readable static submesh");
                int stoneIndex=Array.IndexOf(StoneKitSetup.Ids,id);
                {
                    int expected=stoneIndex==0?12:BakedStoneKitSetup.Triangles[stoneIndex-1];
                    Check(mesh.GetIndexCount(0)==expected*3,"Baked stone triangle budget changed: "+id);
                    Check(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent),"Baked stone requires tangent-space normals");
                    Check(d.material.GetTexture("_BumpMap")!=null && d.material.enableInstancing,"Baked stone lost normal atlas or instancing");
                }
                Near(mesh.bounds.min,d.LocalBounds.min,"Export min");Near(mesh.bounds.max,d.LocalBounds.max,"Export max");
                using var data=MeshUtility.AcquireReadOnlyMeshData(mesh);var source=data[0];
                using var vertices=new NativeArray<Vector3>(source.vertexCount,Allocator.Temp);
                using var normals=new NativeArray<Vector3>(source.vertexCount,Allocator.Temp);
                using var uvs=new NativeArray<Vector2>(source.vertexCount,Allocator.Temp);
                source.GetVertices(vertices);source.GetNormals(normals);source.GetUVs(0,uvs);
                using var triangles=new NativeArray<int>(source.GetSubMesh(0).indexCount,Allocator.Temp);source.GetIndices(triangles,0);
                int mapped=0;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    int ia=triangles[i],ib=triangles[i+1],ic=triangles[i+2];
                    Vector2 a=uvs[ia],b=uvs[ib],c=uvs[ic];
                    float area=Mathf.Abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x));
                    Check(area>1e-12f,"Collapsed imported UV triangle: "+mesh.name);
                    Check(Vector3.Dot(Vector3.Cross(vertices[ib]-vertices[ia],vertices[ic]-vertices[ia]),normals[ia]+normals[ib]+normals[ic])>0,"Inverted face normal");
                    foreach(var uv in new[]{a,b,c})Check(float.IsFinite(uv.x)&&float.IsFinite(uv.y)&&uv.x>=.034f&&uv.x<=.966f&&uv.y>=.034f&&uv.y<=.966f,"UV outside padded texture area");
                    mapped++;
                }
                Check(mapped>0,"Empty stone model");
                if(variation==0){baseVertices=vertices.ToArray();baseUV=uvs.ToArray();}
                else
                {
                    Check(vertices.Length==baseVertices.Length,"UV layout changed vertex count");int different=0;
                    for(int i=0;i<vertices.Length;i++){Near(vertices[i],baseVertices[i],"UV layout changed geometry");if((uvs[i]-baseUV[i]).sqrMagnitude>.000001f)different++;}
                    Check(different>vertices.Length*.9f,"UV variant did not materially vary sampling");
                }
            }
        }
        Check(catalog.Find(StoneKitSetup.Ids[2]).requiresFullBearing,"Flagstone floor can span without solid support");
    }
    private static void OnState(PlayModeStateChange change)
    {
        if(change!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=SessionState.GetInt(Pending+".checks",0);errors.Clear();Application.logMessageReceived+=Log;
        try
        {
            var catalog=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            for(int yaw=0;yaw<8;yaw++){Runtime(catalog,yaw);BoundaryRuntime(catalog,yaw);}
            Check(errors.Count==0,"Runtime errors: "+string.Join(" | ",errors));
            Directory.CreateDirectory(".utmp/stone-kit");
            File.WriteAllText(".utmp/stone-kit/validation.json","{\"passed\":true,\"checks\":"+checks+",\"headings\":8,\"uvLayouts\":18,\"realGateWalks\":16,\"terrainGrounding\":true}");
            Debug.Log("STONE KIT PASS: "+checks+" checks; every imported UV triangle/normal, 18 FBX layouts, all headings, terrain grounding, empty gate colliders and 16 real gate walks, pier spacing and paving full bearing/removal.");
            Application.logMessageReceived-=Log;EditorApplication.Exit(0);
        }catch(Exception ex){Application.logMessageReceived-=Log;Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void BoundaryRuntime(BuildCatalog catalog,int yaw)
    {
        var origin=new Vector3(900+yaw*30,30,900);var q=BuildGeometry.Rotation(yaw);
        var data=new TerrainData{heightmapResolution=33,size=new Vector3(12,4,12)};
        var heights=new float[33,33];for(int x=0;x<33;x++)for(int z=0;z<33;z++)heights[x,z]=.0375f;
        data.SetHeights(0,0,heights);var ground=Terrain.CreateTerrainGameObject(data);ground.name="Boundary actual TerrainCollider";
        ground.layer=GameplayLayers.WorldSolid;ground.transform.position=origin-new Vector3(6,0,6);
        var terrainCollider=ground.GetComponent<TerrainCollider>();Physics.SyncTransforms();
        try
        {
            foreach(int index in new[]{4,5})
            {
                var d=catalog.Find(StoneKitSetup.Ids[index]);Check(d.canGroundOnTerrain&&d.kind==BuildPartKind.Wall,"Boundary wall not terrain-enabled");
                var host=new GameObject("Boundary runtime");host.transform.position=origin;var world=host.AddComponent<BuildWorld>();world.enabled=false;world.Focus=host.transform;
                try
                {
                    var preview=BuildPlacement.Solve(d,origin+q*new Vector3(2,.15f,0),Vector3.up,null,null,yaw,0,default);
                    world.Validate(ref preview);Check(preview.Valid&&!preview.Grounded,"Direct TerrainCollider placement rejected: "+preview.Message);
                    Near(preview.Origin,origin,"Boundary seed did not seat downward on grid");
                    var floating=preview;floating.Origin+=Vector3.up*.5f;world.Validate(ref floating);Check(floating.Valid&&!floating.Grounded,"Floating boundary wall grounded");
                    if(index==5)
                    {
                        Check(d.solidBoxes.Length==2&&d.wallOpenings.Length==1,"Gate bay solids/opening missing");
                        foreach(var socket in d.jointSockets)Check(socket.localPosition.x<1.375f||socket.localPosition.x>2.625f,"Gate void contains wall joint socket");
                        var prop=GameObject.CreatePrimitive(PrimitiveType.Cube);prop.layer=GameplayLayers.WorldSolid;prop.transform.localScale=Vector3.one*.2f;
                        try
                        {
                            prop.transform.position=origin+q*new Vector3(2,.65f,0);Physics.SyncTransforms();world.Validate(ref preview);Check(preview.Valid,"Prop inside empty gate falsely obstructs masonry");
                            prop.transform.position=origin+q*new Vector3(.5f,.65f,0);Physics.SyncTransforms();world.Validate(ref preview);Check(!preview.Valid&&preview.Failure==BuildPlacementFailure.External,"Solid wing did not reject external prop");
                        }finally{Object.DestroyImmediate(prop);Physics.SyncTransforms();}
                        terrainCollider.enabled=false;
                        GameObject Pad(string name,float x,float width,bool marked)
                        {
                            var pad=GameObject.CreatePrimitive(PrimitiveType.Cube);pad.name=name;pad.layer=GameplayLayers.WorldSolid;
                            pad.transform.SetPositionAndRotation(origin+q*new Vector3(x,-.05f,0),q);pad.transform.localScale=new Vector3(width,.4f,1);
                            if(marked)pad.AddComponent<WorldGroundSurface>();Physics.SyncTransforms();return pad;
                        }
                        var middle=Pad("Ground only inside empty gate",2,1,true);
                        world.Validate(ref preview);Check(preview.Valid&&!preview.Grounded,"Ground in empty gate counted as wing bearing");Object.DestroyImmediate(middle);
                        var left=Pad("Left wing ground",.6875f,1.375f,true);
                        world.Validate(ref preview);Check(preview.Valid&&!preview.Grounded,"Only one gate wing grounded the whole bay");
                        var right=Pad("Right wing ground",3.3125f,1.375f,true);
                        world.Validate(ref preview);Check(preview.Valid&&!preview.Grounded,"Two marked ground wings rejected");
                        Object.DestroyImmediate(left);Object.DestroyImmediate(right);Physics.SyncTransforms();terrainCollider.enabled=true;
                    }
                    world.Validate(ref preview);var piece=world.Commit(preview);Check(piece!=null&&!piece.Grounded&&piece.Supported,"Boundary commit lost terrain support");
                    typeof(BuildWorld).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(world,null);Physics.SyncTransforms();
                    Check(world.TryGetCollisionProxy(piece.Id,out var proxy),"Boundary runtime proxy missing");
                    var physical=new List<Collider>();proxy.GetEnabledColliders(physical);Check(physical.Count==(index==5?2:1),"Gate collider seals aperture");
                    bool Ray(float x)=>Physics.Raycast(origin+q*new Vector3(x,.7f,-1),q*Vector3.forward,2,GameplayLayers.SolidSurfaceMask,QueryTriggerInteraction.Ignore);
                    Check(Ray(.5f)&&Ray(3.5f),"Boundary solid wing collision missing");
                    if(index==5)
                    {
                        Check(!Ray(2),"Gate opening blocks forward ray");
                        Check(!Physics.Raycast(origin+q*new Vector3(2,.7f,1),q*Vector3.back,2,GameplayLayers.SolidSurfaceMask),"Gate opening blocks reverse ray");
                        foreach(bool reverse in new[]{false,true})GateWalk(origin,q,reverse);
                    }
                    else
                    {
                        var cap=catalog.Find(StoneKitSetup.Ids[3]);var top=BuildPlacement.Solve(cap,origin+q*new Vector3(2,1.25f,0),Vector3.up,piece,world.Session.Frame(piece.OwnFrameId),yaw,0,default);
                        Near(top.Origin,origin+Vector3.up*1.25f,"S07 cap seats at wrong height");Check(world.Commit(top)!=null,"Cap lost support from terrain-grounded wall");
                    }
                }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
            }
            var original=catalog.Find(StoneKitSetup.Ids[0]);Check(!original.canGroundOnTerrain,"Existing full-height wall grounding changed");
        }finally{Object.DestroyImmediate(ground);Object.DestroyImmediate(data);Physics.SyncTransforms();}
    }
    private static void GateWalk(Vector3 origin,Quaternion q,bool reverse)
    {
        var rig=new GameObject("S12 real gate traversal");var cc=rig.AddComponent<CharacterController>();cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.skinWidth=.08f;cc.stepOffset=.3f;cc.minMoveDistance=0;
        rig.transform.SetPositionAndRotation(origin+q*new Vector3(2,.23f,reverse?1.5f:-1.5f),q);
        var motor=rig.AddComponent<CharacterMotor>();typeof(CharacterMotor).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,null);Physics.SyncTransforms();
        try
        {
            for(int i=0;i<20;i++)motor.Simulate(default,1f/60);
            for(int i=0;i<52;i++)motor.Simulate(new CharacterMoveCommand(new Vector2(0,reverse?-1:1),false,false,false),1f/60);
            var local=Quaternion.Inverse(q)*(rig.transform.position-origin);
            Check(reverse?local.z<-.8f:local.z>.8f,"Player stuck in S12 gate: "+local);Check(Mathf.Abs(local.y-.15f)<.15f,"Gate crossing changed terrain walking level");
        }finally{Object.DestroyImmediate(rig);Physics.SyncTransforms();}
    }
    private static void Runtime(BuildCatalog catalog,int yaw)
    {
        var wall=catalog.Find(StoneKitSetup.Ids[0]);var pier=catalog.Find(StoneKitSetup.Ids[1]);var floor=catalog.Find(StoneKitSetup.Ids[2]);var cap=catalog.Find(StoneKitSetup.Ids[3]);
        var foundation=catalog.Find("build.prototype.foundation");
        using var session=new BuildSession();var frame=session.CreateFrame(new Vector3(500+yaw*30,20,500),yaw);var q=BuildGeometry.Rotation(yaw);
        var footing=session.Add(foundation,frame,default,0,true);
        var first=session.Add(wall,frame,default,0,false);
        var stack=BuildPlacement.Solve(wall,first.Origin+q*new Vector3(2,3,0),q*Vector3.up,first,session.Frame(first.OwnFrameId),yaw,0,default);
        Near(stack.Origin,first.Origin+Vector3.up*3,"Stone wall stack datum");
        var capPreview=BuildPlacement.Solve(cap,first.Origin+q*new Vector3(2,3,0),Vector3.up,first,session.Frame(first.OwnFrameId),yaw,0,default);
        Near(capPreview.Origin,first.Origin+Vector3.up*3,"Cap snap datum");
        var capRecord=session.Add(cap,frame,new Vector3Int(0,12,0),0,false);
        Check(first.Supported&&capRecord.Supported,"Wall/cap support missing");
        Check(first.Resolved.auxiliaryMesh==null&&capRecord.Resolved.auxiliaryMesh==null&&first.Resolved.attachments.Length==0,"Stone gained timber seams");
        var column=session.Add(pier,frame,new Vector3Int(8,0,8),0,false);
        var side=BuildPlacement.Solve(pier,column.Origin+q*new Vector3(.25f,1,0),q*Vector3.right,column,session.Frame(column.OwnFrameId),yaw,0,default);
        Check(Mathf.Abs((side.Origin-column.Origin).magnitude-.5f)<.0001f,"Stone pier adjacency still uses timber's 0.25m spacing");
        var paving=session.Add(floor,frame,default,0,false);
        Check(paving.Supported,"Fully bedded paving unsupported");Check(paving.Resolved.cuts.Length==0&&paving.Resolved.auxiliaryMesh==null,"Paving rebuilt with timber cuts/UVs");
        Check(Array.IndexOf(floor.uvVariants,paving.Resolved.mesh)>=0,"Paving lost its UV variation");
        using(var gameplay=new BuildGameplay(session))
        {
            gameplay.Update(frame.Origin+q*new Vector3(2,1,2),32,40,32);Physics.SyncTransforms();
            foreach(var p in new[]{first,column,paving,capRecord})
            {
                Check(gameplay.TryGetProxy(p.Id,out var proxy),"Missing pooled physical proxy");
                var box=proxy.GetComponent<BoxCollider>();Check(box!=null&&box.enabled,"Missing box collider");
                Near(box.size,p.Definition.LocalBounds.size,"Runtime collider extent");
            }
        }
        session.Remove(footing.Id);Check(paving.Supported,"Free placement unexpectedly depends on structural support");
        using var cantilever=new BuildSession();var cf=cantilever.CreateFrame(frame.Origin+Vector3.right*12,yaw);
        cantilever.Add(foundation,cf,default,0,true);
        var overhang=cantilever.Add(floor,cf,new Vector3Int(8,0,0),0,false);Check(overhang.Supported,"Free placement unexpectedly depends on structural support");
        var states=cantilever.ResolvePreview(new BuildPieceRecord{Id=ulong.MaxValue,Definition=wall,Origin=cf.Origin,WorldYawStep=(byte)yaw,WorldBounds=BuildSession.IndexBounds(wall,cf.Origin,yaw)});
        var all=new List<BuildPieceRecord>(cantilever.Pieces.Values);Check(BuildSupportState.Resolve(all,states).Contains(overhang.Id),"Preview/commit paving support disagree");
        // Grounded timber below a broad slab is still a framed floor, not masonry bedding.
        using var timber=new BuildSession();var tf=timber.CreateFrame(frame.Origin+Vector3.left*12,yaw);
        timber.Add(catalog.Find("build.prototype.floor"),tf,default,0,true);
        var stoneOverTimber=timber.Add(floor,tf,new Vector3Int(0,1,0),0,false);Check(stoneOverTimber.Supported,"Paving accepted on timber-only bearing");
        // The union of several supports must cover the whole footprint; removing one creates a hole.
        var small=Object.Instantiate(foundation);small.sizeUnits=new Vector3Int(8,2,8);
        try
        {
            using var tiled=new BuildSession();var sf=tiled.CreateFrame(frame.Origin+Vector3.forward*12,yaw);BuildPieceRecord last=null;
            foreach(int x in new[]{0,8})foreach(int z in new[]{0,8})last=tiled.Add(small,sf,new Vector3Int(x,0,z),0,true);
            var tile=tiled.Add(floor,sf,default,0,false);Check(tile.Supported,"Union of four footing tiles did not supply full bearing");
            tiled.Remove(last.Id);Check(tile.Supported,"Paving survived a hole in its bearing coverage");
        }finally{Object.DestroyImmediate(small);}
    }
}
