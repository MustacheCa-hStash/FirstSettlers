using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class BuildFramingValidation
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.000001f,message+$" {a} != {b}");
    public static void RunBatch()
    {
        try
        {
            BuildingPrototypeSetup.CreateAssets();var c=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            Assets(c);Layers(c);Openings(c);Placement(c);StairwellWalks(c);Render(c);
            Debug.Log("BUILD FRAMING PASS: "+checks+" checks; authored closed timber meshes/UVs, 3m stack, reversible floor/rim/foundation ownership, stair passage, pooled collider refresh, eight rotations, preview/commit and instanced views.");
            EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static void Assets(BuildCatalog c)
    {
        Check(c.presets.Length==8,"Catalog option count changed");
        Check(c.presets[1].material==c.presets[0].material,"Floor lost matte atlas");
        Check(c.presets[1].mesh.triangles.Length/3==324,"Authored floor cost/mesh changed");
        Check(c.presets[1].floorOpeningMesh.triangles.Length/3==480,"Authored stairwell mesh missing");
        foreach(var mesh in new[]{c.presets[1].mesh,c.presets[1].floorOpeningMesh,AssetDatabase.LoadAssetAtPath<Mesh>(BuildingPrototypeSetup.Folder+"/rim-span-mesh.asset"),AssetDatabase.LoadAssetAtPath<Mesh>(BuildingPrototypeSetup.Folder+"/bearing-cap-mesh.asset")})
        {
            Check(mesh.subMeshCount==1 && mesh.uv.Length==mesh.vertexCount,"Timber UVs/submesh missing");
            var uv=mesh.uv;var t=mesh.triangles;var v=mesh.vertices;var n=mesh.normals;
            for(int i=0;i<t.Length;i+=3)
            {
                Vector2 a=uv[t[i]],b=uv[t[i+1]],d=uv[t[i+2]];
                Check(Mathf.Abs((b.x-a.x)*(d.y-a.y)-(b.y-a.y)*(d.x-a.x))>.000001f,"Collapsed timber face UV");
                Check(Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).sqrMagnitude>.000001f,"Degenerate timber face");
            }
            foreach(var normal in n)Check(Mathf.Abs(normal.magnitude-1)<.001f,"Invalid timber normals");
        }
    }
    private static void Layers(BuildCatalog c)
    {
        for(int yaw=0;yaw<8;yaw++)
        {
            using var s=new BuildSession();var f=s.CreateFrame(new Vector3(20,7,-12),yaw);var q=BuildGeometry.Rotation(yaw);
            var basePiece=s.Add(c.presets[2],f,default,0,true);
            var wall=s.Add(c.presets[0],f,new Vector3Int(1,0,0),0,false);
            var snap=BuildPlacement.Solve(c.presets[0],wall.Origin+q*new Vector3(1,2.75f,.1f),Vector3.up,wall,s.Frame(wall.OwnFrameId),yaw,0,default);
            Near(snap.Origin,wall.Origin+Vector3.up*3,"Upper wall not on 3m level");
            var upper=s.Add(c.presets[0],f,new Vector3Int(1,12,0),0,false);
            Check(upper.Supported && upper.Resolved.boxes.Length==2,"Implicit bearing gap/support");
            var post=s.Add(c.presets[3],f,default,0,false);
            var postSnap=BuildPlacement.Solve(c.presets[3],post.Origin+Vector3.up*2.75f,Vector3.up,post,s.Frame(post.OwnFrameId),yaw,0,default);
            Near(postSnap.Origin,post.Origin+Vector3.up*3,"Post stack does not reserve band");
            var upperPost=s.Add(c.presets[3],f,new Vector3Int(0,12,0),0,false);
            Check(upperPost.Supported && upperPost.Resolved.boxes.Length==2,"Post bearing cap missing");
            var floor=s.Add(c.presets[1],f,new Vector3Int(0,12,0),0,false);
            Check(floor.Supported && upper.Supported && upper.Resolved.boxes.Length==1,"Floor did not replace duplicate bearing");
            Check(upperPost.Supported && upperPost.Resolved.boxes.Length==1,"Floor did not replace corner cap");
            Vector3 upperOrigin=upper.Origin;s.Remove(floor.Id);
            Near(upper.Origin,upperOrigin,"Removing floor moved wall");Check(upper.Supported && upper.Resolved.boxes.Length==2,"Bearing not restored");
            var roof=s.Add(c.Find(ThatchRoofSetup.ContentId),f,new Vector3Int(0,24,0),0,false);
            Check(roof.Supported && roof.Resolved.boxes.Length>0,"Roof above second storey has no bearing");
            var second=s.Add(c.presets[1],f,new Vector3Int(0,24,0),0,false);
            Check(second.Supported && roof.Supported && roof.Resolved.boxes.Length==0,"Second floor/roof ownership");
            s.Remove(second.Id);Check(roof.Resolved.boxes.Length>0 && roof.Supported,"Second roof bearing not restored");
            var finish=s.Add(c.presets[1],f,default,0,false);
            Check(finish.Supported && basePiece.Resolved.boxes.Length==1,"Foundation cap finish/support");
            Near(basePiece.Resolved.boxes[0].max,new Vector3(4,-.25f,4),"Foundation cap not deferred");
            s.Remove(finish.Id);Near(basePiece.Resolved.boxes[0].max,new Vector3(4,0,4),"Foundation cap not restored");
            s.Remove(basePiece.Id);Check(!wall.Supported && !upper.Supported && !roof.Supported,"Detached structure grounded itself");
        }
    }
    private static void Openings(BuildCatalog c)
    {
        for(int yaw=0;yaw<8;yaw++)
        {
            using var s=new BuildSession();var f=s.CreateFrame(new Vector3(-30,11,18),yaw);
            s.Add(c.presets[2],f,default,0,true);var wall=s.Add(c.presets[0],f,new Vector3Int(1,0,15),0,false);
            var floor=s.Add(c.presets[1],f,new Vector3Int(0,12,0),0,false);
            var first=s.Add(c.Find(W21StairSetup.ContentId),f,new Vector3Int(5,0,0),0,false);
            var second=s.Add(c.Find(W21StairSetup.ContentId),f,new Vector3Int(5,6,8),0,false);
            Check(floor.Resolved.cuts.Length==1 && floor.Resolved.mesh==floor.Definition.floorOpeningMesh,"Full flight did not select authored stairwell");
            foreach(var box in floor.Resolved.boxes)Check(!(box.Contains(new Vector3(1.8f,-.1f,2.5f))),"Floor fills stairwell");
            var obj=new GameObject("Resolved floor collider fixture");var proxy=obj.AddComponent<BuildGameplayProxy>();
            try
            {
                proxy.Bind(floor);Physics.SyncTransforms();
                Vector3 point=floor.Origin+BuildGeometry.Rotation(yaw)*new Vector3(1.8f,1,2.5f);
                bool hit=false;foreach(var collider in obj.GetComponentsInChildren<BoxCollider>())hit|=collider.Raycast(new Ray(point,Vector3.down),out _,2);
                Check(!hit,"Physical floor collider fills stairwell");
                s.Remove(first.Id);Check(floor.Resolved.cuts.Length>0,"Removing one stair removed another stair's opening");
                s.Remove(second.Id);Check(floor.Resolved.cuts.Length==0,"Removed stair opening did not restore");
                proxy.Bind(floor);Physics.SyncTransforms();hit=false;foreach(var collider in obj.GetComponentsInChildren<BoxCollider>())hit|=collider.Raycast(new Ray(point,Vector3.down),out _,2);
                Check(hit,"Restored floor is not physically solid");
            }finally{Object.DestroyImmediate(obj);}
            // A middle-level deck fits to a full-height protected wall.
            var mid=s.Add(c.presets[1],f,new Vector3Int(0,6,0),0,false);
            Check(mid.Resolved.cuts.Length>0 && !BuildResolvedGeometry.Overlaps(mid,mid.Resolved,wall,wall.Resolved),"Floor did not defer to wall");
            Check(wall.Resolved.mesh==wall.Definition.mesh,"Floor modified protected wall appearance");
            s.Remove(wall.Id);Check(mid.Resolved.cuts.Length==0,"Wall-edge floor cut not restored");
        }
    }
    private static void Placement(BuildCatalog c)
    {
        var host=new GameObject("Framing placement fixture");var w=host.AddComponent<BuildWorld>();
        if(w.Session==null)typeof(BuildWorld).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(w,null);
        try
        {
            var f=w.Session.CreateFrame(new Vector3(50,20,50),0);var foundation=w.Session.Add(c.presets[2],f,default,0,true);
            BuildPreview At(BuildDefinition d,Vector3Int a)=>new(){Definition=d,Frame=f,Anchor=a,Origin=BuildGeometry.WorldPoint(f,a),WorldYaw=0};
            var wall=w.Commit(At(c.presets[0],new Vector3Int(1,0,0)));Check(wall!=null,"Wall fixture rejected");
            var roof=w.Commit(At(c.Find(ThatchRoofSetup.ContentId),new Vector3Int(0,12,0)));Check(roof!=null,"Roof with automatic bearing rejected");
            var floor=w.Commit(At(c.presets[1],new Vector3Int(0,12,0)));Check(floor!=null && roof.Supported,"Floor-after-roof rejected");
            Check(roof.Resolved.boxes.Length==0,"Committed roof still overlaps floor");
            w.Remove(roof.Id);roof=w.Commit(At(c.Find(ThatchRoofSetup.ContentId),new Vector3Int(0,12,0)));Check(roof!=null,"Roof-after-floor rejected");
            var dup=At(c.presets[1],new Vector3Int(0,12,0));w.Validate(ref dup);Check(!dup.Valid,"Duplicate floor accepted");
            var finish=w.Commit(At(c.presets[1],default));Check(finish!=null,"Flush foundation finish rejected");
            var bad=At(c.Find(W21StairSetup.ContentId),new Vector3Int(1,0,0));w.Validate(ref bad);Check(bad.Valid && bad.Warning==BuildPlacementWarning.BuildOverlap,"Stair/wall overlap did not use permissive policy");
        }finally{Object.DestroyImmediate(host);}
    }
    private static void Render(BuildCatalog c)
    {
        Directory.CreateDirectory(".utmp/framing");using var s=new BuildSession();var f=s.CreateFrame(Vector3.zero,0);
        s.Add(c.presets[2],f,default,0,true);s.Add(c.presets[0],f,new Vector3Int(1,0,0),0,false);
        s.Add(c.presets[0],f,new Vector3Int(1,12,0),0,false);s.Add(c.presets[1],f,new Vector3Int(0,12,0),0,false);
        s.Add(c.Find(W21StairSetup.ContentId),f,new Vector3Int(5,0,0),0,false);s.Add(c.Find(W21StairSetup.ContentId),f,new Vector3Int(5,6,8),0,false);
        var camObj=new GameObject("Framing camera");var cam=camObj.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.aspect=1.5f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.16f,.19f,.2f);
        var lightObj=new GameObject("Framing light");var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.shadows=LightShadows.Soft;lightObj.transform.rotation=Quaternion.Euler(40,-35,0);
        var target=new RenderTexture(1200,800,24);target.Create();var old=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;var active=RenderTexture.active;
        var renderer=new BuildRenderer(s);Action<ScriptableRenderContext,Camera> submit=(_,camera)=>{if(camera==cam)renderer.Draw(cam,3000,140);};
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
            RenderPipelineManager.beginCameraRendering+=submit;
            void Capture(string name,Vector3 pos,Vector3 look,float size)
            {
                cam.transform.position=pos;cam.transform.LookAt(look);cam.orthographicSize=size;
                for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;var pixels=new Texture2D(1200,800,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1200,800),0,0);pixels.Apply();File.WriteAllBytes(".utmp/framing/"+name+".png",pixels.EncodeToPNG());Object.DestroyImmediate(pixels);
                Check(renderer.DrawCalls>0,"Instanced framing not submitted");
            }
            Capture("two-storey-stairwell",new Vector3(9,9,-10),new Vector3(2,2.5f,2),4.8f);
            Capture("stairwell-top",new Vector3(7,10,7),new Vector3(2,3,2),3.4f);
            Capture("stairwell-underside",new Vector3(7,1,6),new Vector3(2,2,2),3.5f);
        }
        finally{RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=old;QualitySettings.renderPipeline=quality;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(camObj);Object.DestroyImmediate(lightObj);}
    }
    private static void StairwellWalks(BuildCatalog c)
    {
        foreach(int yaw in new[]{0,1,3,7})foreach(bool down in new[]{false,true})foreach(bool floorFirst in new[]{false,true})
        {
            var host=new GameObject("Actual stairwell placement");var w=host.AddComponent<BuildWorld>();
            if(w.Session==null)typeof(BuildWorld).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(w,null);
            var origin=new Vector3(200+yaw*20,50,200);var f=w.Session.CreateFrame(origin,yaw);var q=BuildGeometry.Rotation(yaw);
            BuildPreview At(BuildDefinition d,Vector3Int a,int turn=0)=>new(){Definition=d,Frame=f,Anchor=a,YawStep=turn,Origin=BuildGeometry.WorldPoint(f,a),WorldYaw=BuildGeometry.Turn(yaw+turn)};
            w.Session.Add(c.presets[2],f,default,0,true);w.Session.Add(c.presets[2],f,new Vector3Int(0,0,-16),0,true);
            var rig=new GameObject("Framed stairwell motor");
            BuildGameplay gameplay=null;
            try
            {
                Check(w.Commit(At(c.presets[0],new Vector3Int(1,0,1),6))!=null,"Stairwell side wall commit failed");
                if(floorFirst)
                {
                    Check(w.Commit(At(c.presets[1],new Vector3Int(0,12,0)))!=null,"Upper floor commit failed");
                    Check(w.Commit(At(c.presets[1],new Vector3Int(0,12,16)))!=null,"Landing commit failed");
                }
                var stair=c.Find(W21StairSetup.ContentId);
                Check(w.Commit(At(stair,new Vector3Int(5,0,0)))!=null,"First stair under existing floor rejected");
                Check(w.Commit(At(stair,new Vector3Int(5,6,8)))!=null,"Second stair under existing floor rejected");
                if(!floorFirst)
                {
                    Check(w.Commit(At(c.presets[1],new Vector3Int(0,12,0)))!=null,"Floor around existing stairs rejected");
                    Check(w.Commit(At(c.presets[1],new Vector3Int(0,12,16)))!=null,"Late landing commit failed");
                }
                gameplay=new BuildGameplay(w.Session);gameplay.Update(origin,32,40,16);Physics.SyncTransforms();
                rig.layer=GameplayLayers.Player;var cc=rig.AddComponent<CharacterController>();cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.slopeLimit=50;cc.stepOffset=.3f;cc.skinWidth=.08f;
                rig.transform.SetPositionAndRotation(origin+q*new Vector3(1.875f,down?3.08f:.08f,down?4.8f:-.8f),q);
                var motor=rig.AddComponent<CharacterMotor>();typeof(CharacterMotor).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(motor,null);Physics.SyncTransforms();
                for(int i=0;i<30;i++)motor.Simulate(new CharacterMoveCommand(Vector2.zero,false,false,false),1f/60);
                bool finished=false;float previous=down?4.8f:-.8f;int stalled=0;
                for(int i=0;i<420;i++)
                {
                    motor.Simulate(new CharacterMoveCommand(new Vector2(0,down?-1:1),false,false,false),1f/60);
                    var local=Quaternion.Inverse(q)*(rig.transform.position-origin);
                    if(i>12 && Mathf.Abs(local.z-previous)<.001f)stalled++;else stalled=0;
                    Check(stalled<25,$"Stairwell motor blocked at yaw={yaw}, down={down}, {local}");
                    Check(local.y>-.2f && local.y<3.3f,"Motor left the intended stair/floor surfaces");
                    if(!down && local.z>4.7f || down && local.z<-.65f)
                    {Check(Mathf.Abs(local.y-(down?0:3))<.12f,"Stairwell landing height mismatch");finished=true;break;}
                    previous=local.z;
                }
                Check(finished,"Actual framed stairwell traversal timed out");
            }
            finally{gameplay?.Dispose();Object.DestroyImmediate(rig);Object.DestroyImmediate(host);Physics.SyncTransforms();}
        }
    }
}
