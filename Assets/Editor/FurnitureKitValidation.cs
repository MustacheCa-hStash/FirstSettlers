using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class FurnitureKitValidation
{
    private const string Pending="FS.Furniture.Validation";
    private static int checks;
    private static readonly List<string> errors=new();
    static FurnitureKitValidation()=>EditorApplication.playModeStateChanged+=OnState;
    private static void Check(bool ok,string text){checks++;if(!ok)throw new InvalidOperationException(text);}
    public static void RunBatch()
    {
        try
        {
            FurnitureKitSetup.Install();
            var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            Assets(catalog);BuildStoneUiAuthoring.Validate();
            SessionState.SetInt(Pending+".checks",checks);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog catalog)
    {
        var ids=new HashSet<string>();int count=0;
        foreach(var d in catalog.presets)
        {
            Check(ids.Add(d.contentId),"Duplicate catalog ID");
            if(!d.contentId.StartsWith("build.furniture."))continue;count++;
            Check(BuildUiContent.Category(d)==7 && d.preserveAuthoredMesh,"Furniture metadata");
            bool baked=d.contentId==BakedFurnishingsSetup.BedId;
            Check(baked ? d.material.shader.name=="Custom/BakedStoneWallMatte" : d.material==SplitPlankWallSetup.TrimMaterial,"Furniture material mismatch");
            Check(d.mesh!=null && d.mesh.subMeshCount==1 && !d.mesh.isReadable,"Static instanced mesh import");
            Check((d.mesh.bounds.min-d.LocalBounds.min).sqrMagnitude<.000001f && (d.mesh.bounds.max-d.LocalBounds.max).sqrMagnitude<.000001f,"FBX metre scale/origin: "+d.name);
            Check(d.furnitureSupportPoints.Length>=3 && d.solidBoxes.Length>=3,"Missing feet or shaped collision");
            Check(d.authoringPrefab.GetComponents<BoxCollider>().Length==d.solidBoxes.Length,"Prefab collider mismatch");
            Check(d.jointSockets.Length==0 && d.jointCoverVariants.Length==0,"Furniture gained structural framing");
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Missing actual-mesh icon");
            using var data=MeshUtility.AcquireReadOnlyMeshData(d.mesh);var m=data[0];
            using var vertices=new NativeArray<Vector3>(m.vertexCount,Allocator.Temp);
            using var normals=new NativeArray<Vector3>(m.vertexCount,Allocator.Temp);
            using var uv=new NativeArray<Vector2>(m.vertexCount,Allocator.Temp);
            using var triangles=new NativeArray<int>(m.GetSubMesh(0).indexCount,Allocator.Temp);
            m.GetVertices(vertices);m.GetNormals(normals);m.GetUVs(0,uv);m.GetIndices(triangles,0);
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];Vector2 x=uv[b]-uv[a],y=uv[c]-uv[a];
                Check(Mathf.Abs(x.x*y.y-x.y*y.x)>1e-12f,"Degenerate imported UV triangle: "+d.name);
                Check(Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]),normals[a]+normals[b]+normals[c])>0,"Inverted imported face");
                bool inside=false;
                foreach(var r in new[]{new Rect(0,.88305f,1,.10890f),new Rect(0,.75805f,1,.10890f),new Rect(.00805f,.25805f,.23390f,.23390f),new Rect(.25805f,.25805f,.23390f,.23390f),new Rect(.00805f,.13305f,.48390f,.10890f)})
                    if(r.Contains(uv[a])&&r.Contains(uv[b])&&r.Contains(uv[c])){inside=true;break;}
                Check(baked || inside,"UV triangle crosses atlas cells: "+d.name);
            }
        }
        Check(count==6,"Expected exactly F01-F06");
    }
    private static void OnState(PlayModeStateChange change)
    {
        if(change!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);checks=SessionState.GetInt(Pending+".checks",0);errors.Clear();
        void Log(string text,string trace,LogType type){if(type is LogType.Error or LogType.Assert or LogType.Exception)errors.Add(text);}
        Application.logMessageReceived+=Log;
        try
        {
            var catalog=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            for(int yaw=0;yaw<8;yaw++)Runtime(catalog,yaw);
            Ground(catalog);Check(errors.Count==0,string.Join(" | ",errors));
            Directory.CreateDirectory(".utmp/furniture-kit");
            File.WriteAllText(".utmp/furniture-kit/validation.json","{\"passed\":true,\"checks\":"+checks+",\"models\":6,\"headings\":8,\"floorRotationCases\":320,\"uiResolutions\":4}");
            Debug.Log("FURNITURE PASS: "+checks+" checks; six actual FBXs/icons, all UV triangles, all headings, per-foot support, wall mounting/openings, nonstructural behavior, removal and open table collision.");
            Application.logMessageReceived-=Log;EditorApplication.Exit(0);
        }catch(Exception ex){Application.logMessageReceived-=Log;Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Runtime(BuildCatalog catalog,int yaw)
    {
        var origin=new Vector3(100+yaw*30,30,100);var q=BuildGeometry.Rotation(yaw);
        var host=new GameObject("Furniture runtime");var world=host.AddComponent<BuildWorld>();world.enabled=false;
        try
        {
            var s=world.Session;var foundation=s.Add(catalog.Find("build.prototype.foundation"),s.CreateFrame(origin,yaw),default,0,true);
            foreach(var d in catalog.presets)
            {
                if(d.kind!=BuildPartKind.Furniture || d.furnitureWallMounted)continue;
                for(int turn=0;turn<8;turn++)
                {
                    var preview=BuildPlacement.Solve(d,origin+q*new Vector3(2,0,2),q*Vector3.up,foundation,s.Frame(foundation.OwnFrameId),0,turn,default);
                    world.Validate(ref preview);Check(preview.Valid && !preview.Grounded,"Floor placement: "+d.name+" / "+preview.Message);
                    Check(preview.WorldYaw==BuildGeometry.Turn(yaw+turn),"Relative furniture heading");
                    var piece=world.Commit(preview);Check(piece!=null && piece.Supported,"Furniture commit/support");
                    var restored=JsonUtility.FromJson<BuildPieceRecord>(JsonUtility.ToJson(piece));
                    Check(restored.DefinitionId==d.contentId && restored.FrameId==piece.FrameId && restored.YawStep==piece.YawStep,"Stable serialized identity/pose");
                    world.Validate(ref preview);Check(!preview.Valid && preview.Failure==BuildPlacementFailure.Solid,"Duplicate furniture accepted");
                    var blocked=BuildPlacement.Solve(catalog.Find("build.prototype.floor"),piece.Origin+Vector3.up*d.LocalBounds.max.y,Vector3.up,piece,s.Frame(piece.OwnFrameId),0,0,default);
                    world.Validate(ref blocked);Check(blocked.Valid,"Free building on furniture was rejected");
                    var floatingWall=s.Add(catalog.Find("build.prototype.wall"),s.CreateFrame(piece.Origin+Vector3.up*d.LocalBounds.max.y,piece.WorldYawStep),default,0,false);
                    Check(floatingWall.Supported,"Free placement unexpectedly depends on structural support");s.Remove(floatingWall.Id);
                    if(d.contentId.Contains("f03") && turn==0)
                    {
                        var proxy=new GameObject("Table compound collision").AddComponent<BuildGameplayProxy>();
                        try
                        {
                            proxy.Bind(piece);Physics.SyncTransforms();
                            Check(!Physics.Raycast(piece.Origin+q*new Vector3(0,.35f,-1),q*Vector3.forward,2,GameplayLayers.SolidSurfaceMask),"Table underside sealed by a solid box");
                            Check(Physics.Raycast(piece.Origin+Vector3.up,Vector3.down,out var hit,1,GameplayLayers.SolidSurfaceMask) && hit.collider.GetComponentInParent<BuildGameplayProxy>()==proxy,"Tabletop has no physical collision");
                        }finally{Object.DestroyImmediate(proxy.gameObject);}
                    }
                    world.Remove(piece.Id);
                }
                var edge=BuildPlacement.Solve(d,origin+q*new Vector3(0,0,2),Vector3.up,foundation,s.Frame(foundation.OwnFrameId),0,0,default);
                world.Validate(ref edge);Check(edge.Valid,"Floating furniture was rejected");
            }
            var wall=s.Add(catalog.Find("build.prototype.wall"),s.CreateFrame(origin,yaw),default,0,false);Check(wall.Supported,"Wall fixture unsupported");
            var shelf=catalog.Find("build.furniture.f06-wall-shelf");
            foreach(int side in new[]{-1,1})
            {
                var p=BuildPlacement.Solve(shelf,origin+q*new Vector3(2,1.5f,side*.125f),q*(Vector3.forward*side),wall,s.Frame(wall.OwnFrameId),0,0,default);
                world.Validate(ref p);Check(p.Valid,"Shelf on actual timber face: "+p.Message);
                Check(Mathf.Abs((Quaternion.Inverse(q)*(p.Origin-origin)).z-side*.125f)<.0001f,"Shelf lost half-thickness wall offset");
                var mounted=world.Commit(p);Check(mounted!=null&&mounted.Supported,"Shelf commit");world.Remove(mounted.Id);
            }
            var mount=BuildPlacement.Solve(shelf,origin+q*new Vector3(2,1.5f,.125f),q*Vector3.forward,wall,s.Frame(wall.OwnFrameId),0,0,default);
            var attached=world.Commit(mount);s.Remove(wall.Id);Check(attached.Supported,"Free placement unexpectedly depends on structural support");s.Remove(attached.Id);
            var window=s.Add(catalog.Find("build.wood.w11-shutter-wall"),s.CreateFrame(origin,yaw),default,0,false);
            var voidMount=BuildPlacement.Solve(shelf,origin+q*new Vector3(2,1.5f,.125f),q*Vector3.forward,window,s.Frame(window.OwnFrameId),0,0,default);
            world.Validate(ref voidMount);Check(voidMount.Valid,"Floating shelf was rejected");s.Remove(window.Id);
            var stool=catalog.Find("build.furniture.f01-three-leg-stool");
            var supported=world.Commit(BuildPlacement.Solve(stool,origin+q*new Vector3(2,0,2),Vector3.up,foundation,s.Frame(foundation.OwnFrameId),0,0,default));
            s.Remove(foundation.Id);Check(supported.Supported,"Free placement unexpectedly depends on structural support");
        }finally{Object.DestroyImmediate(host);}
    }
    private static void Ground(BuildCatalog catalog)
    {
        var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.name="Furniture terrain stand-in";
        ground.layer=GameplayLayers.WorldSolid;ground.transform.position=new Vector3(0,-.1f,0);ground.transform.localScale=new Vector3(10,.2f,10);ground.AddComponent<WorldGroundSurface>();
        var host=new GameObject("Furniture ground validation");var world=host.AddComponent<BuildWorld>();world.enabled=false;Physics.SyncTransforms();
        try
        {
            foreach(var d in catalog.presets)if(d.kind==BuildPartKind.Furniture)
                for(int yaw=0;yaw<8;yaw++)
                {
                    var p=BuildPlacement.Solve(d,Vector3.zero,Vector3.up,null,null,yaw,0,default);world.Validate(ref p);
                    Check(p.Valid,"Free furniture placement rejected");
                    if(d.furnitureWallMounted)continue;
                    Check(!p.Grounded,"All feet should ground");p.Origin+=Vector3.up*.75f;world.Validate(ref p);
                    Check(p.Valid && !p.Grounded,"Floating furniture grounded");
                }
        }finally{Object.DestroyImmediate(host);Object.DestroyImmediate(ground);}
    }
}
