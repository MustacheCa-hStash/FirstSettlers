using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Regression for the pictured wide gable gap, including independent asymmetric filling.</summary>
public static class SquareInfillValidation
{
    public static void Assets(BuildCatalog c,Action<bool,string> check)
    {
        var d=c.Find(SquareInfillSetup.Id);check(d!=null,"Square infill missing from catalog");
        check(d.IsWallInfill && d.sizeUnits==new Vector3Int(8,8,1) && d.stackRiseUnits==8 && d.wallEndInsetUnits==0,"Square infill structural dimensions");
        check(d.jointSockets.Length==9 && d.jointCoverVariants.Length==1,"Square infill junction data");
        check(d.material==SplitPlankWallSetup.TrimMaterial,"Square infill did not retain the current wood trim material");
        check((d.mesh.bounds.size-new Vector3(2,2,.25f)).sqrMagnitude<.000001f,"Square imported mesh bounds");
        check(Mathf.Abs(d.jointCoverVariants[0].bounds.size.y-2.003f)<.00001f,"Square generated post is not two metres tall");
        var colliders=d.authoringPrefab.GetComponentsInChildren<Collider>();
        check(colliders.Length==1 && colliders[0] is BoxCollider,"Square needs one box collider");
        var box=(BoxCollider)colliders[0];check(box.size==d.LocalBounds.size && box.center==d.LocalBounds.center,"Square authored collider differs from bounds");
        foreach(var path in new[]{SquareInfillSetup.Model,SquareInfillSetup.PostModel})
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            check(importer.bakeAxisConversion && !importer.isReadable && importer.materialImportMode==ModelImporterMaterialImportMode.None,"Square import settings");
        }
        check(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Square actual-mesh menu thumbnail missing");
    }
    public static void Runtime(BuildCatalog c,Action<bool,string> check)
    {
        for(int yaw=0;yaw<8;yaw++)
        {
            var host=new GameObject("Square infill wide-gable fixture");host.transform.position=new Vector3(52000+yaw*40,52000,52000);
            var w=host.AddComponent<BuildWorld>();w.enabled=false;w.Focus=host.transform;var s=w.Session;var f=s.CreateFrame(host.transform.position,yaw);var q=BuildGeometry.Rotation(yaw);
            var d=c.Find(SquareInfillSetup.Id);var rise=c.Find(OpeningWallSetup.GableId);var fall=rise.flipVariant;
            BuildPreview At(BuildDefinition part,Vector3 local,int turn=0)
            {var p=f.Origin+q*local;int heading=BuildGeometry.Turn(yaw+turn);return new BuildPreview{Definition=part,Origin=p,WorldYaw=heading,Frame=new BuildGridFrame{Origin=p,YawStep=(byte)heading}};}
            BuildPreview Aim(BuildDefinition part,BuildPieceRecord target,Vector3 hit,Vector3 normal,int turn=0)
                =>BuildPlacement.Solve(part,target.Origin+q*hit,q*normal,target,s.Frame(target.OwnFrameId),yaw,turn,default);
            void Near(Vector3 a,Vector3 b,string msg)=>check((a-b).sqrMagnitude<.000001f,"Square "+msg+": "+a+" != "+b);
            try
            {
                s.Add(c.presets[2],f,default,0,true);s.Add(c.presets[2],f,new Vector3Int(16,0,0),0,true);
                var wallA=w.Commit(At(c.presets[0],Vector3.zero));var wallB=w.Commit(At(c.presets[0],new Vector3(4,0,0)));
                var left=w.Commit(Aim(rise,wallA,new Vector3(1,2.9f,0),Vector3.forward));check(left!=null,"Square adjacent gable rejected");
                var side=Aim(d,left,new Vector3(1.99f,1.6f,0),Vector3.right);
                Near(side.Origin,f.Origin+q*new Vector3(2,3,0),"panel beside rising triangle");
                var a=w.Commit(side);check(a!=null && a.Supported,"Asymmetric single square rejected");
                check(a.Resolved.mesh==d.mesh && a.Resolved.boxes.Length==1,"Square authored body/collider changed");
                var otherHalf=Aim(d,wallA,new Vector3(2.5f,3,0),Vector3.up);
                Near(otherHalf.Origin,a.Origin,"right half of four-metre wall top");
                var firstHalf=Aim(d,wallA,new Vector3(.5f,3,0),Vector3.up);
                Near(firstHalf.Origin,f.Origin+Vector3.up*3,"left half of four-metre wall top");
                var b=w.Commit(Aim(d,wallB,new Vector3(.5f,3,0),Vector3.up));check(b!=null,"Second central square rejected");
                Near(b.Origin,f.Origin+q*new Vector3(4,3,0),"second square datum");
                var right=w.Commit(Aim(fall,wallB,new Vector3(3,2.9f,0),Vector3.forward));check(right!=null,"Right side triangle rejected");
                var upperA=w.Commit(Aim(rise,a,new Vector3(1,2,0),Vector3.up));var upperB=w.Commit(Aim(fall,b,new Vector3(1,2,0),Vector3.up));
                check(upperA!=null && upperB!=null,"Gables did not stack on square tops");
                Near(upperA.Origin,f.Origin+q*new Vector3(2,5,0),"gable above square uses 2m rise");
                Near(upperA.Origin+q*BuildWallShape.Peak(rise),upperB.Origin+q*BuildWallShape.Peak(fall),"upper gable common ridge");
                Near(Aim(d,upperA,new Vector3(1,0,0),Vector3.down).Origin,a.Origin,"infill under gable");
                var stacked=Aim(d,a,new Vector3(1,2,0),Vector3.up);Near(stacked.Origin,a.Origin+Vector3.up*2,"square stack height");
                Near(Aim(d,a,new Vector3(1,0,0),Vector3.down).Origin,a.Origin-Vector3.up*2,"square underside");
                var sideways=Aim(d,a,new Vector3(1.99f,.4f,0),Vector3.right);Near(sideways.Origin,b.Origin,"square sideways continuation");
                typeof(BuildWorld).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,null);Physics.SyncTransforms();
                foreach(var piece in new[]{a,b})
                {
                    check(w.TryGetCollisionProxy(piece.Id,out var proxy),"Square collision proxy missing");var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);
                    check(colliders.Count==1 && colliders[0] is BoxCollider,"Square runtime collider is not one box");
                    foreach(float y in new[]{.1f,1f,1.9f})foreach(bool reverse in new[]{false,true})
                        check(Physics.Raycast(piece.Origin+q*new Vector3(1,y,reverse?1:-1),q*(reverse?Vector3.back:Vector3.forward),2,1<<GameplayLayers.WorldSolid),"Square collision misses front/back of rectangular gap");
                }
                var duplicate=side;w.Validate(ref duplicate);check(!duplicate.Valid && duplicate.Failure==BuildPlacementFailure.Solid,"Duplicate square accepted");
                w.Remove(b.Id);check(a.Resolved.mesh==d.mesh,"Removing adjacent square changed retained body");
                check(w.Commit(At(d,new Vector3(4,3,0)))!=null,"Square could not be restored independently");
                // Isolated short corner: automatic cover must not project a third metre.
                using var cornerSession=new BuildSession();var cf=cornerSession.CreateFrame(f.Origin+Vector3.up*20,yaw);
                cornerSession.Add(d,cf,default,0,true);cornerSession.Add(d,cf,new Vector3Int(8,0,0),2,false);
                int coverCount=0;foreach(var piece in cornerSession.Pieces.Values)foreach(var attachment in piece.Resolved.attachments)
                {coverCount++;check(attachment.mesh.bounds.size.y<2.01f,"Short square corner gained full-storey framing");}
                check(coverCount==1,"Square corner missing shared cover");
            }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
        }
        Debug.Log("SQUARE INFILL PASS: eight headings, 8m wide gable with independently placed central squares, half-bay top snaps, side/underside joins, 2m stacks, native body retention, single-box physics, duplicate rejection and short junction covers.");
    }
}
