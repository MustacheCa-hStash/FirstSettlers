using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

/// <summary>P6 surface/vegetation, building consumers and cached cave ambient.</summary>
public static class TerrainSculptingWorldValidation
{
    private static int checks;
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static T Field<T>(object target,string field)=>(T)target.GetType().GetField(field,Hidden).GetValue(target);
    private static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Hidden).Invoke(target,args);
    [MenuItem("Tools/Terrain/Validate Sculpting Surfaces and Vegetation")]
    public static void Run()
    {
        checks=0;
        string path=Path.Combine(TerrainSculptingValidation.SourceRoot(),".utmp","sculpt-p6",Guid.NewGuid().ToString("N"));
        var identity=TerrainEditIdentity.Create("p6-fixture",WorldGenerationConfiguration.Default,TerrainDensityProfile.Default);
        TerrainSculptSuppression saved;
        using(var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10,store:new TerrainEditStore(path,identity),saveClock:()=>0))
        {
            f.Viewer.transform.position=new Vector3(-.15f,7,-.15f);f.Ready();
            Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(-.15,7,-.15),1)),"Buried P6 excavation rejected.");
            Check(f.Runtime.Suppression==null,"Uncommitted cut suppressed vegetation.");f.Ready();
            saved=f.Runtime.Suppression;
            Check(saved!=null&&saved.TileCount==0&&!saved.Contains(-.15,-.15),"A sealed underground excavation removed foliage from its intact roof.");
            Check(!saved.Contains(4,4),"Suppression escaped its local footprint.");
            Check(saved.DensityRevision==f.World.Revision,"Mask revision is not the committed density revision.");
            Check(Physics.Raycast(new Vector3(-.15f,12,-.15f),Vector3.down,out var roof,3,GameplayLayers.SolidSurfaceMask)&&Math.Abs(roof.point.y-10)<.001,"Foliage suppression removed the retained solid roof.");
            ValidateSurfaceData(f);ValidateIntactRoofVegetation(f,saved);
            Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(-.15,9.8,-.15),1)),"Surface breakthrough rejected.");f.Ready();
            Check(saved.TileCount==0&&!saved.Contains(-.15,-.15),"Surface breakthrough mutated the old intact-roof mask.");
            saved=f.Runtime.Suppression;
            Check(saved.Contains(-.15,-.15)&&!saved.Contains(1.25,-.15),"Surface opening is unmasked or its grass border is too wide.");
            ValidateVegetation(f,saved);
            Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(1.5,7,0),new double3(2,1,1))),"Underground tunnel extension rejected.");f.Ready();
            Check(!f.Runtime.Suppression.Contains(3,0)&&!f.Runtime.Suppression.ChangedIn(saved,-5,-5,8,5),"Extending a tunnel below its intact roof enlarged the entrance's grass clearing.");
            saved=f.Runtime.Suppression;
            f.SourceUnavailable=true;
            Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,9.8,0),1)),"Second P6 excavation rejected.");
            var clock=Stopwatch.StartNew();while(f.Runtime.Status!="Waiting for source terrain"&&clock.ElapsedMilliseconds<10000)f.Tick();
            Check(f.Runtime.Status=="Waiting for source terrain"&&ReferenceEquals(saved,f.Runtime.Suppression)&&!saved.Contains(12,0),"A pending source handoff published a vegetation footprint.");
            f.SourceUnavailable=false;f.Ready();
            Check(!saved.Contains(12,0)&&f.Runtime.Suppression.Contains(12,0),"Mask mutation leaked into an older immutable revision.");
            saved=f.Runtime.Suppression;
        } // Real orderly shutdown flush.
        using(var restored=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10,store:new TerrainEditStore(path,identity)))
        {
            restored.Viewer.transform.position=new Vector3(-.15f,7,-.15f);restored.Ready();
            Check(restored.Runtime.Suppression!=null,"Reload did not reconstruct foliage suppression.");
            for(int z=-20;z<20;z++)for(int x=-20;x<60;x++)
                Check(saved.Contains(x*.3,z*.3)==restored.Runtime.Suppression.Contains(x*.3,z*.3),"Reload changed the conservative footprint.");
            ValidateSurfaceData(restored);ValidateVegetation(restored,restored.Runtime.Suppression);
            var old=restored.Runtime.Suppression;
            restored.Viewer.transform.position=new Vector3(380,7,0);restored.Ready();
            Check(ReferenceEquals(old,restored.Runtime.Suppression)&&old.Contains(-.15,-.15),"Streaming retirement lost the persistent suppression footprint.");
        }
        ValidateSurfaceContact();ValidateBuildingIntegration();TerrainSkyAccessValidation.Run();
        Debug.Log($"TERRAIN SCULPTING P6 CONSUMERS PASS: {checks} checks; surface contact/retained roofs, tight borders, atomic publication, resident/late vegetation, CPU grass/fern caches, immutable masks, shutdown/reload, permissive buried builds, fresh cave footing, retained grounding, structural collapse and streaming retention. Cached sky access validated separately above.");
    }
    private static BuildPreview At(BuildDefinition definition,Vector3 origin)=>new BuildPreview
    {Definition=definition,Frame=new BuildGridFrame{Origin=origin},Origin=origin,Hint="Ground grid"};
    private static BuildDefinition Building(BuildPartKind kind,Vector3Int size,Vector3Int minimum)
    {
        var d=ScriptableObject.CreateInstance<BuildDefinition>();
        d.contentId="p6.fixture."+kind;d.displayName=d.contentId;d.kind=kind;d.sizeUnits=size;d.minimumUnits=minimum;d.preserveAuthoredMesh=true;
        d.mesh=BuildTimberMesh.Create(d.contentId,new[]{new BuildTimberPart{bounds=d.LocalBounds,grainAxis=1}});
        return d;
    }
    private static void ValidateBuildingIntegration()
    {
        using var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10);
        f.Viewer.transform.position=new Vector3(12,7,12);f.Ready();
        Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(12,7,12),new double3(4,2,4))),"Building cave fixture rejected.");f.Ready();
        var host=new GameObject("P6 building integration");
        var foundation=Building(BuildPartKind.Foundation,new Vector3Int(16,2,16),new Vector3Int(0,-2,0));
        var wall=Building(BuildPartKind.Wall,new Vector3Int(4,8,1),default);
        var w=host.AddComponent<BuildWorld>();w.enabled=false;if(w.Session==null)Call(w,"Awake");w.TerrainGuard=f.Runtime;
        try
        {
            var starter=At(foundation,new Vector3(13,5.5f,11));w.Validate(ref starter);
            Check(starter.Valid&&starter.Grounded,"Foundation did not find the actual carved cave floor: "+starter.Message);
            var anchor=w.Commit(starter);
            Check(anchor!=null&&anchor.Grounded&&anchor.Supported,"Cave foundation did not latch its initial footing.");
            var hidden=At(wall,new Vector3(16.25f,5.5f,12));
            var piece=w.Commit(hidden);
            Check(piece!=null&&piece.Supported&&!piece.Grounded,"Fully buried wall did not retain structural attachment.");
            Check(piece.Resolved.mesh==wall.mesh&&piece.Resolved.cuts.Length==0&&piece.Resolved.boxes.Length==1&&piece.Resolved.boxes[0]==wall.LocalBounds,"Terrain placement clipped the authored building geometry.");
            var duplicate=hidden;w.Validate(ref duplicate);
            Check(!duplicate.Valid&&duplicate.Failure==BuildPlacementFailure.Solid,"Terrain overlap bypassed duplicate prevention.");
            var unsupported=At(wall,new Vector3(12,7,12));w.Validate(ref unsupported);
            Check(!unsupported.Valid&&unsupported.Failure==BuildPlacementFailure.Support&&!unsupported.Grounded,"A wall acquired its own terrain anchor.");
            var buriedFoundation=At(foundation,new Vector3(24,5.5f,24));w.Validate(ref buriedFoundation);
            Check(!buriedFoundation.Valid&&!buriedFoundation.Grounded,"Solid burial was mistaken for initial foundation footing.");
            var cached=At(foundation,new Vector3(9,5.5f,9));w.Validate(ref cached);
            Check(cached.Valid&&cached.Grounded,"Pre-excavation grounding cache fixture failed.");
            // Simulate a committed density revision whose replacement collision has not
            // published yet. New placement must not use the old floor during this gap.
            using(var edit=f.World.StageExcavation(TerrainDensityBrush.Box(new double3(12,4.5,12),new double3(6,1,6))))
                Check(f.World.TryCommit(edit),"Building footing removal failed.");
            var waiting=cached;w.Validate(ref waiting);
            Check(!waiting.Valid&&waiting.Failure==BuildPlacementFailure.Terrain&&w.Commit(cached)==null,"Unpublished edited collision accepted a new building.");
            Check(anchor.Grounded&&anchor.Supported,"Pending terrain publication revoked an existing anchor.");f.Ready();
            w.Validate(ref cached);
            Check(!cached.Grounded,"A cached new preview retained footing after excavation removed it.");
            var lower=At(foundation,new Vector3(9,4,9));w.Validate(ref lower);
            Check(lower.Valid&&lower.Grounded,"New foundation did not find the newly lowered cave floor: "+lower.Message);
            w.Session.RecomputeSupport();
            Check(anchor.Grounded&&anchor.Supported&&piece.Supported,"Excavation revoked placement-latched support.");
            // A new structural revision must retain grounding too, without re-probing
            // the now empty space beneath the original foundation.
            var upper=w.Commit(At(wall,new Vector3(16.25f,7.5f,12)));
            Check(upper!=null&&upper.Supported&&anchor.Grounded,"An unrelated structural rebuild revoked the excavated anchor.");
            var gameplay=Field<BuildGameplay>(w,"gameplay");gameplay.Update(f.Viewer.transform.position,32,40,8);
            Check(w.TryGetCollisionProxy(piece.Id,out var proxy)&&proxy.Shape!=null&&proxy.Shape.enabled&&proxy.Shape.size==wall.LocalBounds.size,"Buried wall lost or clipped its physical collider.");
            f.Viewer.transform.position=new Vector3(400,7,12);f.Ready();w.Session.RecomputeSupport();
            Check(anchor.Grounded&&anchor.Supported&&piece.Supported&&upper.Supported,"Terrain streaming revoked a logical building anchor.");
            // Structural destruction remains authoritative; the existing grace period
            // still applies even though terrain changes never trigger collapse.
            Check(w.Damage(anchor.Id,foundation.maxHealth)&&!w.Session.TryGet(anchor.Id,out _),"Foundation destruction failed.");
            Check(!piece.Supported&&!upper.Supported,"Destroyed structural anchor retained dependent support.");
            Call(w,"UpdateCollapse");Check(w.Session.TryGet(piece.Id,out _),"Structural grace period disappeared.");
            var deadlines=Field<Dictionary<ulong,float>>(w,"collapseAt");
            foreach(var id in new List<ulong>(deadlines.Keys))deadlines[id]=Time.time-1;
            Call(w,"UpdateCollapse");Check(w.Session.Pieces.Count==0,"Unsupported pieces did not collapse after their grace period.");
        }
        finally
        {
            Object.DestroyImmediate(host);Object.DestroyImmediate(foundation.mesh);Object.DestroyImmediate(wall.mesh);
            Object.DestroyImmediate(foundation);Object.DestroyImmediate(wall);Physics.SyncTransforms();
        }
    }
    private static void ValidateSurfaceContact()
    {
        using var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10);
        f.Viewer.transform.position=new Vector3(0,8,0);f.Ready();
        Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(0,8.8,0),new double3(.6))),"Thin-roof fixture rejected.");f.Ready();
        Check(f.Runtime.Suppression.TileCount==0,"A roof with 0.6 m of remaining stone lost grass.");
        Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(0,9.7,0),new double3(.6))),"Box surface fixture rejected.");f.Ready();
        var mask=f.Runtime.Suppression;
        Check(mask.Contains(.65,.05)&&mask.Contains(-.65,-.05),"Tight padding omitted the immediate edge of the opening.");
        // Density interpolation bevels the nominal box's lip beyond its analytic 0.6 m
        // extent. Check outside the reconstructed lip plus the new rasterized margin.
        Check(!mask.Contains(.95,.05)&&!mask.Contains(-.95,-.05),"Grass clearing still extends beyond the reconstructed box lip and tight margin.");
        Check(!mask.Contains(.8,.8),"Square diagonal dilation left an oversized corner clearing.");
        using var slope=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10+.4*x+.2*z);
        slope.Viewer.transform.position=new Vector3(12,16,12);slope.Ready();
        Check(slope.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,14,12),.7)),"Sloping buried cavity rejected.");slope.Ready();
        Check(slope.Runtime.Suppression.TileCount==0,"A sloping intact roof lost foliage.");
        Check(slope.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,17,12),.7)),"Sloping surface scoop rejected.");slope.Ready();
        Check(slope.Runtime.Suppression.Contains(12,12),"A sloping surface breakthrough retained floating grass.");
    }
    private static void ValidateIntactRoofVegetation(TerrainSculptingIntegrationValidation.Fixture f,TerrainSculptSuppression mask)
    {
        var root=new GameObject("P6 intact roof plants");
        var mesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.up,Vector3.right},triangles=new[]{0,1,2}};
        try
        {
            var matrices=new[]{Matrix4x4.Translate(new Vector3(-.15f,10,-.15f))};
            var runtime=new ChunkFoliageRuntime{root=root.transform};
            Call(runtime,"PublishFlowers",new List<FlowerRenderBatch>{new FlowerRenderBatch(matrices,new[]{Vector4.one})});
            var bush=new GameObject("P6 bush on intact roof");bush.transform.SetParent(root.transform);bush.transform.position=new Vector3(-.15f,10,-.15f);
            Field<List<GameObject>>(runtime,"bushGameObjects").Add(bush);runtime.ApplySuppression(mask,38.4f);
            Check(runtime.GpuFlowerInstanceCount==1&&bush!=null&&bush.activeSelf,"Intact-roof flowers/bushes were removed.");
            var grass=new List<FoliageInstanceData>{new FoliageInstanceData(new Vector3(-.15f,10,-.15f),Quaternion.identity,Vector3.one,1,0)};
            using var renderer=new ResidentGrassRenderer(2,8,2);
            renderer.Upload(0,grass,Matrix4x4.identity,mesh,mesh);renderer.ApplySuppression(mask);renderer.Upload(1,grass,Matrix4x4.identity,mesh,mesh);
            Check(Field<GrassRenderUtility.Instance[][]>(renderer,"cpuSlots")[0].Length==1&&Field<GrassRenderUtility.Instance[][]>(renderer,"cpuSlots")[1].Length==1,"Resident/late grass was removed above a sealed cave.");
            var fern=new LeafClusterGeneration(f.Find(new ChunkCoord(0,0)).ChunkRecord,new FernSettings(),1,128,.3f,60);
            fern.Instances.Add(new LeafClusterInstance{position=new Vector3(-.15f,10,-.15f),rotation=Quaternion.identity,scale=1});Call(fern,"ApplySuppression",mask);
            Check(fern.Instances.Count==1&&fern.RepresentationRevision==0,"Intact-roof fern representation was invalidated.");
        }
        finally{Object.DestroyImmediate(mesh);Object.DestroyImmediate(root);}
    }
    private static void ValidateSurfaceData(TerrainSculptingIntegrationValidation.Fixture f)
    {
        bool stone=false,skin=false;
        foreach(var surface in Field<Dictionary<ChunkCoord,TerrainSculptChunkSurface>>(f.Runtime,"live").Values)
        {
            var mesh=(Mesh)surface.GetType().GetProperty("RenderMesh",Hidden).GetValue(surface);
            if(mesh==null)continue;
            var uv=mesh.uv2;Check(uv.Length==mesh.vertexCount,"Exposure channel was not uploaded.");
            var vertices=mesh.vertices;
            for(int i=0;i<uv.Length;i++)
            {
                Check(uv[i].x>=0&&uv[i].x<=1,"Exposure escaped its range.");
                if(vertices[i].y<9.5){Check(uv[i].x>.999,"Buried wall/floor/ceiling inherited the landscape material.");stone=true;}
                if(Math.Abs(vertices[i].y-10)<.00001){Check(uv[i].x==0,"Untouched exterior skin changed material.");skin=true;}
            }
            var renderer=mesh!=null?Field<GameObject>(surface,"root").GetComponent<MeshRenderer>():null;
            Check(renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.TwoSided,"Inward-facing ceilings cannot cast shadows.");
        }
        Check(stone&&skin,"Fixture did not cover both underground stone and untouched surface.");
    }
    private static void ValidateVegetation(TerrainSculptingIntegrationValidation.Fixture f,TerrainSculptSuppression mask)
    {
        var root=new GameObject("P6 small decorations");
        var mesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.up,Vector3.right},triangles=new[]{0,1,2}};
        try
        {
            var runtime=new ChunkFoliageRuntime{root=root.transform};
            var matrices=new[]{Matrix4x4.Translate(new Vector3(-.15f,10,-.15f)),Matrix4x4.Translate(new Vector3(5,10,5))};
            var data=new[]{Vector4.one*.2f,Vector4.one*.8f};
            Call(runtime,"PublishFlowers",new List<FlowerRenderBatch>{new FlowerRenderBatch(matrices,data)});
            Call(runtime,"PublishClover",new List<CloverRenderBatch>{new CloverRenderBatch(0,matrices,data)});
            Call(runtime,"PublishDandelions",new List<GrassRenderBatch>{new GrassRenderBatch(matrices,data)});
            Call(runtime,"PublishWaterPlants",false,new List<Matrix4x4[]>{matrices});
            var hiddenBush=new GameObject("P6 resident bush over excavation");hiddenBush.transform.SetParent(root.transform);hiddenBush.transform.position=new Vector3(-.15f,10,-.15f);hiddenBush.AddComponent<BoxCollider>();
            var keptBush=new GameObject("P6 unrelated bush");keptBush.transform.SetParent(root.transform);keptBush.transform.position=new Vector3(5,10,5);
            var bushes=Field<List<GameObject>>(runtime,"bushGameObjects");bushes.Add(hiddenBush);bushes.Add(keptBush);
            var tree=new GameObject("P6 retained large prop");tree.transform.SetParent(root.transform);tree.transform.position=new Vector3(-.15f,10,-.15f);
            runtime.ApplySuppression(mask,38.4f);
            Check(hiddenBush==null&&bushes.Count==1&&keptBush.activeSelf&&tree.activeSelf,"Small-bush suppression changed an unrelated bush/large prop or kept an undermined collider.");
            var stagedBush=new GameObject("P6 pending bush");stagedBush.transform.SetParent(root.transform);stagedBush.transform.position=new Vector3(-.15f,10,-.15f);
            var pending=new List<GameObject>{stagedBush};Call(runtime,"SuppressBushes",pending);
            Check(stagedBush==null&&pending.Count==0,"A staged small bush bypassed the latest suppression footprint.");
            Check(runtime.GpuFlowerInstanceCount==1&&runtime.GpuCloverInstanceCount==1&&runtime.GpuDandelionInstanceCount==1&&runtime.GpuLilyPadInstanceCount==1,"Resident small foliage was not filtered.");
            Call(runtime,"PublishFlowers",new List<FlowerRenderBatch>{new FlowerRenderBatch(matrices,data)});
            Check(runtime.GpuFlowerInstanceCount==1&&matrices.Length==2&&data.Length==2,"A late publication restored suppressed flowers or changed candidate storage.");
            var remaining=Field<List<FlowerRenderBatch>>(runtime,"flowerRenderBatches")[0];
            Check(remaining.matrices[0].m03==5&&remaining.petalColors[0].x==.8f,"Filtering changed an unrelated flower's transform/tint.");
            var grass=new List<FoliageInstanceData>{new FoliageInstanceData(new Vector3(-.15f,10,-.15f),Quaternion.identity,Vector3.one,1,0),new FoliageInstanceData(new Vector3(5,10,5),Quaternion.identity,Vector3.one,2,1)};
            using var renderer=new ResidentGrassRenderer(2,8,2);
            renderer.Upload(0,grass,Matrix4x4.identity,mesh,mesh);renderer.ApplySuppression(mask);
            Check(Field<GrassRenderUtility.Instance[][]>(renderer,"cpuSlots")[0].Length==1,"Resident CPU grass survived suppression.");
            renderer.Upload(1,grass,Matrix4x4.identity,mesh,mesh);
            Check(Field<GrassRenderUtility.Instance[][]>(renderer,"cpuSlots")[1].Length==1&&grass.Count==2,"Late grass upload restored removed grass or mutated cached candidates.");
            var leaf=new LeafClusterGeneration(f.Find(new ChunkCoord(0,0)).ChunkRecord,new FernSettings(),1,128,.3f,60);
            leaf.Instances.Add(new LeafClusterInstance{position=new Vector3(-.15f,10,-.15f),rotation=Quaternion.identity,scale=1});
            leaf.Instances.Add(new LeafClusterInstance{position=new Vector3(5,10,5),rotation=Quaternion.identity,scale=1});
            Call(leaf,"ApplySuppression",mask);
            Check(leaf.Instances.Count==1&&leaf.RepresentationRevision==1,"Fern generation did not invalidate its resident representation.");
            leaf.Instances.Add(new LeafClusterInstance{position=new Vector3(-.15f,10,-.15f)});Call(leaf,"ApplySuppression",mask);
            Check(leaf.Instances.Count==1,"A newly generated fern bypassed an unchanged footprint.");
        }
        finally{Object.DestroyImmediate(mesh);Object.DestroyImmediate(root);}
    }
    public static void RunBatch()
    {
        try{Run();EditorApplication.Exit(0);}
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
