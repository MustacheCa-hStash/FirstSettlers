using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class ThatchRoofValidation
{
    private static int checks;
    private static void Check(bool value,string message) { checks++;if(!value)throw new InvalidOperationException(message); }
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.000001f,message+" "+a+" != "+b);
    public static void RunBatch()
    {
        try
        {
            BuildingPrototypeSetup.CreateAssets();
            var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            Validate(catalog);Render(catalog);BuildingPrototypeRenderValidation.Run();
            Debug.Log("THATCH ROOF PASS: "+checks+" checks; 8 rotations, 4x4/4x8 pairs, physical attic clearance, wall/post/gable/beam contact, support loss/reconnection, closed prism and face UVs, imported UVs/normals and real instanced rendering/menu.");
            BuildingShadowValidation.RunThatchBatch();
        }
        catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Validate(BuildCatalog catalog)
    {
        string[] order={"build.prototype.wall","build.prototype.floor","build.prototype.foundation","build.prototype.corner",SplitPlankWallSetup.InfillContentId,WattleWallSetup.ContentId,W21StairSetup.ContentId};
        for(int i=0;i<order.Length;i++)Check(catalog.presets[i].contentId==order[i],"Existing catalog order changed");
        {
            var d=catalog.Find(ThatchRoofSetup.ContentId);Check(d!=null,"Missing single roof");
            Near(d.LocalBounds.size,new Vector3(4,2,2),"Structural dimensions");
            var imported=WattleWallSetup.BakeStaticMesh(AssetDatabase.LoadAssetAtPath<GameObject>(ThatchRoofSetup.ModelPath),new Vector3(4,2,2),"Roof validation",out _,true);
            try
            {
                Check(d.mesh.subMeshCount==1 && d.mesh.uv.Length==d.mesh.vertexCount,"Submeshes/UVs");
                var vertices=d.mesh.vertices;var uv=d.mesh.uv;var normals=d.mesh.normals;
                var iv=imported.vertices;var iu=imported.uv;
                for(int j=0;j<vertices.Length;j++)
                {
                    Near(vertices[j],iv[j],"Hierarchy bake moved vertex");Check(uv[j]==iu[j],"UV changed during bake");
                    Check(Mathf.Abs(normals[j].magnitude-1)<.001f,"Invalid normal");
                    Check(uv[j].x>=0 && uv[j].x<=1 && uv[j].y>=0 && uv[j].y<=1,"Atlas UV escaped");
                }
                bool up=false,down=false;
                for(int j=0;j<vertices.Length;j++)
                { up|=vertices[j].y>2.3f && normals[j].y>.5f;down|=vertices[j].y<.2f && normals[j].y<-.5f; }
                Check(up && down,"Roof covering/bearing normals reversed");
                Check(d.material.shader.name==SplitPlankWallSetup.ShaderName && d.material.enableInstancing && d.material.GetShaderPassEnabled("ShadowCaster"),"Matte instancing/shadows changed");
                Check(d.authoringPrefab.GetComponentsInChildren<MeshCollider>().Length==2,"Thin collision volumes absent");
                Check(d.authoringPrefab.GetComponent<BoxCollider>()==null,"Roof has attic box");
            }
            finally{Object.DestroyImmediate(imported);}
        }
        Check(catalog.presets.Length==8,"Expected seven original options plus one roof");
        var single=catalog.Find(ThatchRoofSetup.ContentId);var left=single;var right=single;
        Check(single.displayName=="Thatch roof","Roof menu still shows variants");
        Check(single.mesh.triangles.Length/3==62,"Roof geometry exceeded the authored 62 triangles");
        Near(single.mesh.bounds.min,new Vector3(0,-.13f,-.25f),"Straight roof minimum");
        Near(single.mesh.bounds.max,new Vector3(4,2.12f+Mathf.Sqrt(2)*.30f,2),"Straight roof maximum");
        ValidateSurfaceUVs(single);
        Check(((ModelImporter)AssetImporter.GetAtPath(ThatchRoofSetup.ModelPath)).bakeAxisConversion,"Roof axis conversion is not baked");
        Check(single.roofContinuationMesh!=null && single.roofContinuationMesh.triangles.Length==single.mesh.triangles.Length,"Joined roof visual missing or geometry grew");
        Near(single.roofContinuationMesh.bounds.min,new Vector3(0,0,0),"Joined overhang still enters the preceding slope");
        for(int yaw=0;yaw<8;yaw++)
        {
            var session=new BuildSession();var frame=session.CreateFrame(new Vector3(-12,3,9),yaw);Quaternion q=BuildGeometry.Rotation(yaw);
            Vector3 At(Vector3 p)=>frame.Origin+q*p;
            var foundation=session.Add(catalog.presets[2],frame,default,0,true);
            var wall=session.Add(catalog.presets[0],frame,new Vector3Int(1,0,0),0,false);
            Check(wall.Supported,"Fixture wall unsupported");
            var wallFrame=session.Frame(wall.OwnFrameId);
            var snap=BuildPlacement.Solve(single,At(new Vector3(2,2.75f,.125f)),Vector3.up,wall,wallFrame,yaw,0,default,At(new Vector3(2,1,2)));
            Near(snap.Origin,At(new Vector3(0,2.75f,0)),"Wall top roof snap");Check(snap.WorldYaw==yaw,"Wall roof yaw");
            var a=session.Add(single,frame,new Vector3Int(0,11,0),0,false);
            var post=session.Add(catalog.presets[3],frame,new Vector3Int(0,0,0),0,false);
            var postSnap=BuildPlacement.Solve(single,At(new Vector3(.125f,2.75f,.125f)),Vector3.up,post,session.Frame(post.OwnFrameId),yaw,0,default,At(new Vector3(2,1,2)));
            Near(postSnap.Origin,At(new Vector3(0,2.75f,0)),"Post top socket shifted bay");
            session.Remove(post.Id);
            var uphill=BuildPlacement.Solve(single,At(new Vector3(2,4.75f,2)),q*new Vector3(0,1,-1).normalized,a,session.Frame(a.OwnFrameId),yaw,0,default);
            Near(uphill.Origin,At(new Vector3(0,4.75f,2)),"High edge must continue the slope");Check(uphill.WorldYaw==yaw,"Uphill snap reversed direction");
            var upper=session.Add(single,frame,new Vector3Int(0,19,8),0,false);
            Check(upper.Supported && !BuildOccupancy.Overlaps(single,a.Origin,yaw,single,upper.Origin,yaw),"Uphill continuation is unsupported/overlapping");
            Check(BuildRoof.VisualMesh(upper,session)==single.roofContinuationMesh,"Actual renderer retains overlapping eave");
            var down=BuildPlacement.Solve(single,At(new Vector3(2,4.8f,2.1f)),q*Vector3.back,upper,session.Frame(upper.OwnFrameId),yaw,0,default);
            Near(down.Origin,a.Origin,"Low edge does not continue downhill");Check(down.WorldYaw==yaw,"Downhill snap reversed heading");
            var third=BuildPlacement.Solve(single,At(new Vector3(2,6.75f,4)),q*Vector3.up,upper,session.Frame(upper.OwnFrameId),yaw,0,default);
            Near(third.Origin,At(new Vector3(0,6.75f,4)),"Third slope segment moved off grid");
            session.Remove(upper.Id);
            // Opposing slopes are started from the other wall, as requested.
            var farWall=session.Add(catalog.presets[0],frame,new Vector3Int(15,0,16),4,false);
            var opposite=BuildPlacement.Solve(single,At(new Vector3(2,2.75f,3.9f)),Vector3.up,farWall,session.Frame(farWall.OwnFrameId),yaw,0,default,At(new Vector3(2,1,2)));
            Near(opposite.Origin,At(new Vector3(4,2.75f,4)),"Other wall cannot start opposing roof");Check(opposite.WorldYaw==BuildGeometry.Turn(yaw+4),"Wall-side roof orientation");
            session.Remove(farWall.Id);
            var b=session.Add(single,frame,new Vector3Int(16,11,16),4,false);
            Check(a.Supported && b.Supported,"4x4 roof pair unsupported");
            Check(!BuildOccupancy.Overlaps(a.Definition,a.Origin,a.WorldYawStep,b.Definition,b.Origin,b.WorldYawStep),"Opposing collision overlaps");
            Check(!BuildOccupancy.Overlaps(a.Definition,a.Origin,yaw,wall.Definition,wall.Origin,yaw),"Roof cuts into wall");
            var small=ScriptableObject.CreateInstance<BuildDefinition>();small.sizeUnits=new Vector3Int(1,1,1);small.wallEndInsetUnits=0;small.kind=BuildPartKind.Corner;
            try
            {
                Check(!BuildOccupancy.Overlaps(single,a.Origin,yaw,small,At(new Vector3(1,3.25f,1.5f)),yaw),"Attic falsely occupied");
                Check(BuildOccupancy.Overlaps(single,a.Origin,yaw,small,At(new Vector3(1,4.25f,1.5f)),yaw),"Real slope penetration missed");
                Vector3 beamOrigin=At(new Vector3(1,3.75f,1.25f));
                Check(!BuildOccupancy.Overlaps(single,a.Origin,yaw,small,beamOrigin,yaw),"Interior beam clearance");
                Check(BuildGeometry.Connects(single,a.Origin,yaw,small,beamOrigin,yaw),"Interior beam contact absent");
                var beamSnap=BuildPlacement.Solve(small,At(new Vector3(1.125f,4,1.375f)),q*new Vector3(0,-1,1).normalized,a,session.Frame(a.OwnFrameId),yaw,0,default);
                Check(!BuildOccupancy.Overlaps(single,a.Origin,yaw,small,beamSnap.Origin,beamSnap.WorldYaw) &&
                    BuildGeometry.Connects(single,a.Origin,yaw,small,beamSnap.Origin,beamSnap.WorldYaw),"Interior beam slope socket penetrates or floats");
                var postOrigin=At(new Vector3(0,0,0));
                Check(BuildGeometry.Connects(single,a.Origin,yaw,catalog.presets[3],postOrigin,yaw),"Post bearing absent");
                var proxyObject=new GameObject("Roof pooled proxy test");var proxy=proxyObject.AddComponent<BuildGameplayProxy>();
                try
                {
                    proxy.Bind(a);Check(proxy.ActiveShape is MeshCollider,"Pooled roof collider");Physics.SyncTransforms();
                    var body=new GameObject("Attic body");var box=body.AddComponent<BoxCollider>();box.size=new Vector3(.2f,.2f,.2f);
                    try
                    {
                        body.transform.SetPositionAndRotation(At(new Vector3(2,3.5f,1.5f)),q);Physics.SyncTransforms();
                        foreach(var c in proxy.GetComponentsInChildren<MeshCollider>())Check(!Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,box,box.transform.position,q,out _,out _),"Physical collider fills attic");
                        body.transform.position=At(new Vector3(2,4.5f,1.5f));Physics.SyncTransforms();bool hit=false;
                        foreach(var c in proxy.GetComponentsInChildren<MeshCollider>())hit|=Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,box,box.transform.position,q,out _,out _);
                        Check(hit,"Physical roof slope not solid");
                    }finally{Object.DestroyImmediate(body);}
                    proxy.Unbind();foreach(var c in proxy.GetComponentsInChildren<MeshCollider>(true))Check(!c.enabled,"Unbound collider still active");
                    proxy.Bind(wall);Check(proxy.Shape.enabled,"Proxy cannot return to wall");foreach(var c in proxy.GetComponentsInChildren<MeshCollider>(true))Check(!c.enabled,"Roof collider leaked into wall");
                }finally{Object.DestroyImmediate(proxyObject);}
            }finally{Object.DestroyImmediate(small);}
            var gable=GableFixture();
            try
            {
                var gp=BuildPlacement.Solve(gable,At(new Vector3(.05f,4,1.5f)),q*Vector3.left,a,session.Frame(a.OwnFrameId),yaw,0,default);
                Near(gp.Origin,At(new Vector3(.25f,2.75f,.25f)),"Shaped gable end socket");
                Check(!BuildOccupancy.Overlaps(single,a.Origin,yaw,gable,gp.Origin,gp.WorldYaw),"Shaped gable fills/penetrates roof");
                Check(BuildGeometry.Connects(single,a.Origin,yaw,gable,gp.Origin,gp.WorldYaw),"Gable slope contact absent");
                Check(!BuildOccupancy.Overlaps(single,b.Origin,b.WorldYawStep,gable,gp.Origin,gp.WorldYaw),"Gable intersects opposing slope");
            }
            finally{Object.DestroyImmediate(gable);}
            session.Remove(wall.Id);Check(!a.Supported && !b.Supported,"Roof retained support from empty bounding box");
            wall=session.Add(catalog.presets[0],frame,new Vector3Int(1,0,0),0,false);Check(a.Supported && b.Supported,"Support reconnection failed");
            session.Remove(foundation.Id);Check(!a.Supported && !b.Supported,"Detached roof cycle grounded itself");
            // 4x8 building along X: mirrored far-side variants are reversed.
            var longSession=new BuildSession();var longFrame=longSession.CreateFrame(frame.Origin,yaw);
            longSession.Add(catalog.presets[2],longFrame,default,0,true);
            longSession.Add(catalog.presets[0],longFrame,new Vector3Int(1,0,0),0,false);
            var pieces=new[]{longSession.Add(left,longFrame,new Vector3Int(0,11,0),0,false),
                longSession.Add(right,longFrame,new Vector3Int(16,11,0),0,false),
                longSession.Add(right,longFrame,new Vector3Int(16,11,16),4,false),
                longSession.Add(left,longFrame,new Vector3Int(32,11,16),4,false)};
            foreach(var p in pieces)Check(p.Supported,"4x8 repeated roof unsupported");
            for(int i=0;i<pieces.Length;i++)for(int j=i+1;j<pieces.Length;j++)
                Check(!BuildOccupancy.Overlaps(pieces[i].Definition,pieces[i].Origin,pieces[i].WorldYawStep,pieces[j].Definition,pieces[j].Origin,pieces[j].WorldYawStep),"4x8 occupied joint overlaps");
            var next=BuildPlacement.Solve(right,At(new Vector3(3.9f,3.5f,.8f)),q*Vector3.right,pieces[0],longSession.Frame(pieces[0].OwnFrameId),yaw,0,default);
            Near(next.Origin,At(new Vector3(4,2.75f,0)),"Repeat socket changed grid");
        }
        ValidatePlacementBlockers(catalog,single);
    }
    private static BuildDefinition GableFixture()
    {
        var d=ScriptableObject.CreateInstance<BuildDefinition>();d.kind=BuildPartKind.Wall;d.sizeUnits=new Vector3Int(14,8,1);d.wallEndInsetUnits=1;d.roofAttachment=RoofAttachmentMode.Gable;
        // Inset beside the existing quarter-metre bay posts/eave rails. The
        // top follows the true slope; the lower corners leave rail clearance.
        var v=new List<Vector3>();
        foreach(float z in new[]{0f,.25f}) foreach(var p in new[]{new Vector2(0,0),new Vector2(0,.25f),new Vector2(1.75f,2),new Vector2(3.5f,.25f),new Vector2(3.5f,0)})v.Add(new Vector3(p.x,p.y,z));
        d.occupiedVolumes=new[]{new BuildConvexVolume{vertices=v.ToArray(),bounds=d.LocalBounds,faceAxes=new[]{Vector3.forward,Vector3.right,Vector3.up,new Vector3(1,1,0).normalized,new Vector3(-1,1,0).normalized},
            edgeAxes=new[]{Vector3.forward,Vector3.up,Vector3.right,new Vector3(1,1,0).normalized,new Vector3(-1,1,0).normalized}}};
        return d;
    }
    private static void ValidatePlacementBlockers(BuildCatalog catalog,BuildDefinition roof)
    {
        var host=new GameObject("Roof placement world");var world=host.AddComponent<BuildWorld>();
        if(world.Session==null) typeof(BuildWorld).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(world,null);
        var frame=world.Session.CreateFrame(new Vector3(30,10,30),0);
        world.Session.Add(catalog.presets[2],frame,default,0,true);
        world.Session.Add(catalog.presets[0],frame,new Vector3Int(1,0,0),0,false);
        var obstacle=new GameObject("Roof external blocker"){layer=GameplayLayers.Player};var box=obstacle.AddComponent<BoxCollider>();box.size=new Vector3(.4f,.4f,.4f);
        var preview=new BuildPreview{Definition=roof,Frame=frame,Anchor=new Vector3Int(0,11,0),Origin=frame.Origin+new Vector3(0,2.75f,0),WorldYaw=0};
        try
        {
            obstacle.transform.position=frame.Origin+new Vector3(2,3.5f,1.5f);Physics.SyncTransforms();world.Validate(ref preview);
            Check(preview.Valid,"Real player/body in empty attic blocks roof: "+preview.Message);
            obstacle.transform.position=frame.Origin+new Vector3(2,4.5f,1.5f);Physics.SyncTransforms();world.Validate(ref preview);
            if(preview.Valid)
            {
                var nearby=Physics.OverlapBox(preview.Origin+BuildOccupancy.Bounds(roof).center,BuildOccupancy.Bounds(roof).extents,Quaternion.identity,
                    GameplayLayers.SolidSurfaceMask|(1<<GameplayLayers.Player),QueryTriggerInteraction.Ignore);
                Debug.Log($"Roof blocker diagnostics: external enabled={box.enabled}, active={box.gameObject.activeInHierarchy}, layer={box.gameObject.layer}, bounds={box.bounds}, broadPhase={nearby.Length}");
            }
            Check(!preview.Valid && preview.Message=="Move clear of the preview","Real slope/player penetration missed: valid="+preview.Valid+", "+preview.Message);
            box.enabled=false;Physics.SyncTransforms();
            var placed=world.Commit(preview);Check(placed!=null,"Clear roof placement commit failed");
            var extension=new BuildPreview{Definition=roof,Frame=frame,Anchor=new Vector3Int(16,11,0),Origin=frame.Origin+new Vector3(4,2.75f,0),WorldYaw=0};
            world.Validate(ref extension);Check(extension.Valid,"The single prefab cannot repeat: "+extension.Message);
            var uphill=BuildPlacement.Solve(roof,placed.Origin+new Vector3(2,2.3f,1.9f),Vector3.up,placed,world.Session.Frame(placed.OwnFrameId),0,0,default);
            world.Validate(ref uphill);Check(uphill.Valid && uphill.RoofContinuesFromBelow,"Actual uphill preview rejected: "+uphill.Message);
            Check(uphill.VisualMesh==roof.roofContinuationMesh,"Ghost still overlaps the lower roof eave");
            var upper=world.Commit(uphill);Check(upper!=null && upper.Supported,"Actual uphill commit failed");
            Check(BuildRoof.VisualMesh(upper,world.Session)==roof.roofContinuationMesh,"Placed upper roof still overlaps");
            world.Remove(placed.Id);Check(!upper.Supported,"Upper roof did not lose support");
            Check(BuildRoof.VisualMesh(upper,world.Session)==roof.mesh,"Removing lower neighbor did not restore eave");
            world.Session.Add(roof,frame,new Vector3Int(0,11,0),0,false);
            Check(upper.Supported && BuildRoof.VisualMesh(upper,world.Session)==roof.roofContinuationMesh,"Reconnected roof did not restore join/support");

        }
        finally
        {
            Object.DestroyImmediate(obstacle);
            typeof(BuildWorld).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(world,null);
            Object.DestroyImmediate(host);
        }
    }
    private static void ValidateSurfaceUVs(BuildDefinition d)
    {
        var v=d.mesh.vertices;var uv=d.mesh.uv;var t=d.mesh.triangles;
        for(int i=0;i<t.Length;i+=3)
        {
            Vector2 a=uv[t[i]],b=uv[t[i+1]],c=uv[t[i+2]];
            Check(Mathf.Abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>.000001f,"Collapsed face UVs");
            Check(Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).sqrMagnitude>.0000001f,"Degenerate geometry");
        }
        // Rays from all six directions must hit the opaque, closed thatch shell.
        var obj=new GameObject("Closed thatch face test");var collider=obj.AddComponent<MeshCollider>();collider.sharedMesh=d.mesh;
        try
        {
            Vector3[] starts={new(-1,1.4f,1),new(5,1.4f,1),new(2,.1f,-1),new(2,2.35f,3),new(2,4,1),new(2,-1,1)};
            Vector3[] directions={Vector3.right,Vector3.left,Vector3.forward,Vector3.back,Vector3.down,Vector3.up};
            for(int i=0;i<starts.Length;i++)Check(collider.Raycast(new Ray(starts[i],directions[i]),out _,6),"Missing closed thatch face "+i);
        }
        finally{Object.DestroyImmediate(obj);}
    }
    private static void Render(BuildCatalog catalog)
    {
        Directory.CreateDirectory(".utmp/building-prototype");
        var session=new BuildSession();var frame=session.CreateFrame(Vector3.zero,0);
        var l=catalog.Find(ThatchRoofSetup.ContentId);var r=l;
        session.Add(l,frame,new Vector3Int(0,11,0),0,false);session.Add(r,frame,new Vector3Int(16,11,0),0,false);
        session.Add(r,frame,new Vector3Int(16,11,16),4,false);session.Add(l,frame,new Vector3Int(32,11,16),4,false);
        var root=new GameObject("Roof render fixture");var cameraObject=new GameObject("Roof camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        var lightObject=new GameObject("Roof sun");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.shadows=LightShadows.Soft;lightObject.transform.rotation=Quaternion.Euler(45,-35,0);
        var target=new RenderTexture(1200,800,24,RenderTextureFormat.ARGB32);target.Create();
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;var previous=RenderTexture.active;
        Action<ScriptableRenderContext,Camera> submit=null;
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.19f,.2f);camera.orthographic=true;camera.aspect=1.5f;camera.farClipPlane=4000;
            var renderer=new BuildRenderer(session);
            submit=(_,c)=>{if(c==camera)renderer.Draw(camera,3000,140);};RenderPipelineManager.beginCameraRendering+=submit;
            void Capture(string name,Vector3 position,Vector3 look,float scale)
            {
                camera.transform.position=position;camera.transform.LookAt(look);camera.orthographicSize=scale;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;var pixels=new Texture2D(1200,800,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1200,800),0,0);pixels.Apply();
                File.WriteAllBytes(".utmp/building-prototype/"+name+".png",pixels.EncodeToPNG());Object.DestroyImmediate(pixels);
                Check(renderer.DrawCalls>0,"Actual roof instancing absent");
            }
            Capture("roof-4x8-instanced",new Vector3(12,10,-10),new Vector3(4,3.6f,2),5.5f);
            Capture("roof-join-close",new Vector3(6,6,-2),new Vector3(4,4,1),1.2f);
            Capture("roof-underside",new Vector3(4,2.5f,-2),new Vector3(4,4,2),3.2f);
            Capture("roof-ridge",new Vector3(12,7,2),new Vector3(4,4.7f,2),2.8f);
            Capture("roof-distant",new Vector3(35,22,-32),new Vector3(4,3.5f,2),16);
            foreach(var id in new List<ulong>(session.Pieces.Keys))session.Remove(id);
            session.Add(l,frame,new Vector3Int(0,11,0),0,false);
            session.Add(l,frame,new Vector3Int(0,19,8),0,false);
            session.Add(l,frame,new Vector3Int(0,27,16),0,false);
            Capture("roof-uphill-chain",new Vector3(9,12,-8),new Vector3(2,6,3),5.5f);
            Capture("roof-uphill-join",new Vector3(6,7,-1),new Vector3(2,5.1f,2),1.2f);
        }
        finally
        {
            if(submit!=null)RenderPipelineManager.beginCameraRendering-=submit;
            GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;RenderTexture.active=previous;
            target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);
        }
    }
}
