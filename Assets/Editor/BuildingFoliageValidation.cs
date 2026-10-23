using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class BuildingFoliageValidation
{
    private static int checks;
    private static readonly List<string> errors=new();
    private static void Log(string message,string stack,LogType kind) { if(kind is LogType.Error or LogType.Assert or LogType.Exception)errors.Add(message); }
    private static readonly List<double> stressFrames=new();
    private static void Check(bool value,string message) { checks++;if(!value)throw new Exception(message); }
    private static BuildPieceRecord Box(ulong id,Vector3 origin,Bounds bounds,int yaw=0)=>new()
    {
        Id=id,Origin=origin,WorldYawStep=(byte)yaw,Resolved=new BuildResolvedState { boxes=new[]{bounds} }
    };
    private static void Pump(BuildFoliageExclusion world,bool measure=false)
    {
        var elapsed=Stopwatch.StartNew();
        do
        {
            world.Tick();
            Check(world.RunningWorkers<=BuildFoliageExclusion.MaxWorkers,"Worker admission exceeded");
            Check(world.LastSteps<=BuildFoliageExclusion.MaxStepsPerFrame,"Step cap exceeded");
            Check(world.LastPublications<=BuildFoliageExclusion.MaxPublicationsPerFrame,"Upload cap exceeded");
            if(measure)stressFrames.Add(world.LastFrameMs);
            if(world.IsIdle)return;
            if(elapsed.Elapsed.TotalSeconds>40)throw new Exception("Foliage did not converge");
            Thread.Sleep(1); // Fixture only. Runtime never waits for workers.
        }while(true);
    }
    public static void RunBatch()
    {
        try
        {
            checks=0;errors.Clear();Application.logMessageReceived+=Log; GeometryAndOwnership(); ReversibleBatches(); ResidentGrass(); ForestScatter(); FreeBuilding(); NotificationEdits(); Stress(); ResidentGrassCullingValidation.Run();
            Check(errors.Count==0,"Native validation logged errors: "+string.Join(" | ",errors));
            Application.logMessageReceived-=Log;
            stressFrames.Sort();
            string result=$"BUILDING FOLIAGE PASS: {checks} checks; overlap/removal/height/rotated convex/late arrival/stale workers/grass CPU+GPU/free building. "+
                $"131072-instance stress: frames={stressFrames.Count}, p95={stressFrames[(int)(stressFrames.Count*.95)]:F3} ms, "+
                $"p99={stressFrames[(int)(stressFrames.Count*.99)]:F3} ms, max={stressFrames[stressFrames.Count-1]:F3} ms. "+
                $"Budget {BuildFoliageExclusion.FrameBudgetMs} ms; at most 2 workers, 128 plants x 16 shapes/packet, 4 publications/frame.";
            Directory.CreateDirectory(".utmp");File.WriteAllText(".utmp/building-foliage-result.txt",result);
            UnityEngine.Debug.Log(result);EditorApplication.Exit(0);
        }
        catch(Exception e) { Application.logMessageReceived-=Log;UnityEngine.Debug.LogException(e);EditorApplication.Exit(1); }
    }
    private static void GeometryAndOwnership()
    {
        using var world=new BuildFoliageExclusion();
        var hidden=new bool[4];int calls=0;
        var positions=new[]{Vector3.zero,new Vector3(5,0,0),new Vector3(0,4,0),new Vector3(0,-5,0)};
        using var filter=world.Register(4,i=>new BuildFoliageExclusion.Probe(positions[i],.5f),(start,bits,count)=>
        { Check(count<=128,"Unbounded publication");Array.Copy(bits,0,hidden,start,count);calls++; });
        Pump(world);Check(calls==0,"Empty world uploaded unchanged foliage");
        var box=Box(1,Vector3.zero,new Bounds(Vector3.zero,new Vector3(4,1,4)),1);world.Set(box);Pump(world);
        Check(hidden[0]&&!hidden[1]&&!hidden[2]&&!hidden[3],"Height/rotated ground contact incorrect");
        var second=Box(2,Vector3.zero,new Bounds(Vector3.zero,new Vector3(2,1,2)));world.Set(second);Pump(world);
        world.Remove(1);Pump(world);Check(hidden[0],"Removing one owner revealed overlapping coverage");
        world.Remove(2);Pump(world);foreach(bool value in hidden)Check(!value,"Demolition failed to restore foliage");
        world.Set(box);world.Tick();world.Remove(1);Pump(world);Check(!hidden[0],"Stale worker resurrected an exclusion");
        // A triangular prism: its enclosing box must not clear the empty half.
        var v=new BuildConvexVolume { vertices=new[]{new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(0,2,0),new Vector3(0,0,2),new Vector3(2,0,2),new Vector3(0,2,2)},
            faceAxes=new[]{Vector3.right,Vector3.up,Vector3.forward,new Vector3(1,1,0).normalized},bounds=new Bounds(Vector3.one,Vector3.one*2) };
        var triangle=Box(3,Vector3.zero,default);triangle.Resolved.boxes=Array.Empty<Bounds>();triangle.Resolved.volumes=new[]{v};world.Set(triangle);
        bool[] wedge=new bool[2];using var wedgeFilter=world.Register(2,i=>new BuildFoliageExclusion.Probe(i==0?new Vector3(.3f,.3f,1):new Vector3(1.8f,1.8f,1),.1f),
            (start,bits,count)=>Array.Copy(bits,0,wedge,start,count));Pump(world);Check(wedge[0]&&!wedge[1],"Convex shape used its broad bounds");
        bool late=false;using var arrival=world.Register(1,_=>new BuildFoliageExclusion.Probe(new Vector3(.3f,.3f,1),.1f),(_,bits,__)=>late=bits[0]);
        Pump(world);Check(late,"Streamed-in foliage ignored existing builds");
        int previous=calls;for(int i=0;i<30;i++)world.Tick();Check(calls==previous&&world.LastSteps==0,"Settled foliage performed repeated work");
    }
    private static void ReversibleBatches()
    {
        using var world=new BuildFoliageExclusion();world.Activate();
        var matrices=new Matrix4x4[1023];var colors=new Vector4[1023];
        for(int i=0;i<matrices.Length;i++) { matrices[i]=Matrix4x4.Translate(new Vector3(i%2==0?0:8,0,0));colors[i]=new Vector4(i,0,0,0); }
        using var batch=new BuildingFoliageBatch(matrices,colors,.5f);Pump(world);
        world.Set(Box(1,Vector3.zero,new Bounds(Vector3.zero,new Vector3(2,1,2))));Pump(world);
        Check(batch.Count==511,"Small-plant compaction count incorrect");
        for(int i=0;i<batch.Count;i++)Check(batch.Values[i].x==i*2+1,"Instance attributes lost alignment");
        world.Remove(1);Pump(world);Check(batch.Count==1023,"Small plants did not restore");
        for(int i=0;i<batch.Count;i++)Check(batch.Values[i]==colors[i]&&batch.Matrices[i]==matrices[i],"Original batch lost during restoration");
        world.Set(Box(2,Vector3.zero,new Bounds(Vector3.zero,new Vector3(2,1,2))));world.Tick();
        batch.Dispose();world.Remove(2);Pump(world);Check(world.FilterCount==0&&world.SpatialCellCount==0,"Retired batch retained a spatial subscription");
    }
    private static void ResidentGrass()
    {
        using var world=new BuildFoliageExclusion();world.Activate();
        var mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};mesh.RecalculateBounds();
        var settings=new GrassSettings { grassCompactShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/GrassCompact.compute") };
        var shader=Shader.Find("Custom/GrassInstancedTerrainTint");
        var material=shader!=null?new Material(shader){enableInstancing=true}:null;
        try
        {
            var candidates=new List<FoliageInstanceData>();
            for(int i=0;i<256;i++)candidates.Add(new FoliageInstanceData(new Vector3(i%2==0?0:8,0,0),Quaternion.identity,Vector3.one,(uint)(i*1234567),0));
            using var renderer=new ResidentGrassRenderer(1,32,1);renderer.Upload(0,candidates,Matrix4x4.identity,mesh,mesh);Pump(world);
            var original=renderer.ReadSlotForValidation(0);
            world.Set(Box(1,Vector3.zero,new Bounds(Vector3.zero,new Vector3(2,1,2))));Pump(world);
            var hidden=renderer.ReadSlotForValidation(0);int suppressed=0;
            foreach(var item in hidden)if(item.Data.z>1)suppressed++;
            Check(suppressed==128&&hidden.Length==256,"Grass discarded originals or used wrong coverage");
            for(int i=0;i<hidden.Length;i++)Check((GrassStreamingPolicy.Select(hidden[i].Data.z,hidden[i].Data.w,0,3,Vector4.one,1,0,100,10)<0)==(i%2==0),"CPU fallback visibility disagrees");
            if(material!=null && GrassRenderUtility.IsSupported(settings.grassCompactShader,material))
            {
                renderer.CullAllForValidation(settings,new[]{mesh,mesh,null,null},new[]{material,material,null,null},null,Vector3.zero,3,100,10,Vector4.one,1);
                Check(renderer.ReadVisibleForValidation(0).Length+renderer.ReadVisibleForValidation(1).Length==128,"GPU grass ignored CPU exclusion");
            }
            else throw new Exception("Native GPU grass validation unavailable");
            world.Remove(1);Pump(world);
            var restored=renderer.ReadSlotForValidation(0);
            for(int i=0;i<restored.Length;i++)Check(restored[i].Data==original[i].Data,"Grass selection changed after restoration");
            renderer.CullAllForValidation(settings,new[]{mesh,mesh,null,null},new[]{material,material,null,null},null,Vector3.zero,3,100,10,Vector4.one,1);
            Check(renderer.ReadVisibleForValidation(0).Length+renderer.ReadVisibleForValidation(1).Length==256,"GPU restoration lost grass");
            world.Set(Box(2,Vector3.zero,new Bounds(Vector3.zero,new Vector3(2,1,2))));Pump(world);
            var tiles=new Dictionary<Unity.Mathematics.int2,ulong[]> { [new Unity.Mathematics.int2(0,0)]=new[]{1UL<<8,0UL,0UL,0UL} };
            var mask=(TerrainSculptSuppression)Activator.CreateInstance(typeof(TerrainSculptSuppression),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,
                null,new object[]{tiles,1d,1L},null);
            renderer.ApplySuppression(mask);Pump(world);world.Remove(2);Pump(world);
            var composed=renderer.ReadSlotForValidation(0);Check(composed.Length==128,"Building restoration undid terrain suppression");
            for(int i=0;i<composed.Length;i++)Check(composed[i].Data==original[i*2].Data,"Terrain/build filtering lost original selection ranks");
        }
        finally { Object.DestroyImmediate(mesh);if(material!=null)Object.DestroyImmediate(material); }
    }
    private static void FreeBuilding()
    {
        var go=new GameObject("Free building validation");var definition=ScriptableObject.CreateInstance<BuildDefinition>();
        definition.contentId="validation.floating";definition.kind=BuildPartKind.Foundation;definition.sizeUnits=new Vector3Int(8,1,8);
        definition.preserveAuthoredMesh=true;definition.requiresFullBearing=true;
        try
        {
            var world=go.AddComponent<BuildWorld>();if(world.Session==null)typeof(BuildWorld).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(world,null);
            var p=new BuildPreview { Definition=definition,Origin=new Vector3(9000,100,9000),Frame=new BuildGridFrame { Origin=new Vector3(9000,100,9000) } };
            world.Validate(ref p);Check(p.Valid&&!world.PreviewUsedFullSupport,"Floating foundation rejected: "+p.Message);
            var first=world.Commit(p);Check(first!=null,"Floating placement commit failed");
            p.Origin+=Vector3.up*10;p.Frame=new BuildGridFrame { Origin=p.Origin };var second=world.Commit(p);
            Check(second!=null,"Independent elevated placement failed");world.Remove(first.Id);
            Check(world.Session.TryGet(second.Id,out _)&&second.Supported,"Removing neighbour invalidated floating piece");
            Check(world.Damage(second.Id,100)&&!world.Session.TryGet(second.Id,out _),"Explicit damage/removal no longer works");
            definition.kind=BuildPartKind.Stair;p.Origin+=Vector3.up*10;p.Frame=new BuildGridFrame { Origin=p.Origin };p.Hint="Ground grid";
            world.Validate(ref p);Check(p.Valid,"Unattached stair rejected: "+p.Message);
            definition.kind=BuildPartKind.Furniture;definition.furnitureWallMounted=true;p.InvalidAttachment=true;
            world.Validate(ref p);Check(p.Valid,"Floating furnishing rejected: "+p.Message);
        }
        finally { Object.DestroyImmediate(go);Object.DestroyImmediate(definition); }
    }
    private static void ForestScatter()
    {
        foreach(bool fern in new[]{false,true})
        {
            using var world=new BuildFoliageExclusion();world.Activate();
            using var record=new ChunkRecord(new ChunkCoord(0,0));
            // Completed empty terrain input, then explicit deterministic scatter candidates.
            var generation=new LeafClusterGeneration(record,fern?(LeafClusterSettings)new FernSettings():new LeafClusterSettings(),123,8,1,1);
            generation.Instances.Add(new LeafClusterInstance { position=Vector3.zero,scale=1,rotation=Quaternion.identity,rank=123 });
            generation.Instances.Add(new LeafClusterInstance { position=Vector3.right*8,scale=1,rotation=Quaternion.identity,rank=456 });
            var update=typeof(LeafClusterGeneration).GetMethod("UpdateBuildingExclusion",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var hidden=typeof(LeafClusterGeneration).GetMethod("IsBuildingHidden",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var dispose=typeof(LeafClusterGeneration).GetMethod("DisposeBuildingExclusion",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            update.Invoke(generation,new object[]{fern?.8f:.15f,.2f});
            var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Resources/Foliage/ForestScatterCompact.compute");
            using var renderer=new ForestScatterGpuRenderer(compute,fern?3:2);
            try
            {
                renderer.Sync(record.ChunkCoord,generation,true,.4f,fern);Pump(world);
                world.Set(Box(1,Vector3.zero,new Bounds(Vector3.zero,new Vector3(2,1,2))));Pump(world);
                Check((bool)hidden.Invoke(generation,new object[]{0})&&!(bool)hidden.Invoke(generation,new object[]{1}),"Forest CPU fallback flags incorrect");
                var sourceField=typeof(ForestScatterGpuRenderer).GetField("sources",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var gpu=(ComputeBuffer)sourceField.GetValue(renderer);var sources=new ForestScatterGpuRenderer.Source[2];gpu.GetData(sources,0,0,2);
                Check(sources[0].Selection.z==2&&sources[1].Selection.z<1,"Forest GPU visibility upload incorrect");
                world.Remove(1);Pump(world);gpu.GetData(sources,0,0,2);
                Check(sources[0].Selection.z==LeafClusterGeneration.Unit(123,277),"Forest GPU density rank failed to restore");
                Check(!(bool)hidden.Invoke(generation,new object[]{0})&&generation.Instances.Count==2,"Forest CPU restoration discarded candidates");
                renderer.Remove(record.ChunkCoord);world.Set(Box(2,Vector3.zero,new Bounds(Vector3.zero,new Vector3(2,1,2))));Pump(world);
                renderer.Sync(record.ChunkCoord,generation,true,.4f,fern);
                Check(renderer.ReadSource(0).Selection.z==2,"Recreated forest GPU tile ignored cached visibility");
            }
            finally { dispose.Invoke(generation,null); }
        }
    }
    private static void NotificationEdits()
    {
        using var world=new BuildFoliageExclusion();var hidden=new bool[160];
        for(int i=0;i<hidden.Length;i++) { int index=i;world.Register(1,_=>new BuildFoliageExclusion.Probe(Vector3.zero,.5f),(_,bits,__)=>hidden[index]=bits[0]); }
        Pump(world);var box=Box(1,Vector3.zero,new Bounds(Vector3.zero,new Vector3(2,1,2)));
        world.Set(box);world.Tick();world.Tick();world.Remove(1);world.Tick();world.Set(box);Pump(world);
        foreach(bool value in hidden)Check(value,"A rapid edit was lost during notification fan-out");
        world.Remove(1);Pump(world);foreach(bool value in hidden)Check(!value,"Notification restart lost demolition");
    }
    private static void Stress()
    {
        using var world=new BuildFoliageExclusion();world.Activate();
        var mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};mesh.RecalculateBounds();
        try
        {
            using var renderer=new ResidentGrassRenderer(256,368,16);
            var plants=new List<FoliageInstanceData>();
            for(int i=0;i<512;i++)plants.Add(new FoliageInstanceData(new Vector3((i%16)*.2f,0,(i/16)*.1f),Quaternion.identity,Vector3.one,(uint)(i*1234567),0));
            for(int tile=0;tile<256;tile++)renderer.Upload(tile,plants,Matrix4x4.Translate(new Vector3((tile%32)*8,0,(tile/32)*8)),mesh,mesh);
            // Allocate/replay GPU storage before timing local changes, as in resident gameplay.
            renderer.ReadSlotForValidation(0);Pump(world);
            for(int i=0;i<256;i++)world.Set(Box((ulong)i+1,new Vector3((i%32)*8,0,(i/32)*8),new Bounds(new Vector3(1.5f,0,1.5f),new Vector3(4,1,4))));
            Pump(world,true);
            for(int tile=0;tile<256;tile++)foreach(var plant in renderer.ReadSlotForValidation(tile))Check(plant.Data.z>1,"Stress grass coverage missing");
            for(int i=0;i<256;i++)world.Remove((ulong)i+1);Pump(world,true);
            for(int tile=0;tile<256;tile++)
            {
                var restored=renderer.ReadSlotForValidation(tile);
                for(int i=0;i<restored.Length;i++)Check(restored[i].Data.z==GrassStreamingPolicy.UnitRank(plants[i].selectionRank),"Stress grass restoration changed rank");
            }
            stressFrames.Sort();Check(stressFrames[(int)(stressFrames.Count*.99)]<3,"Foliage p99 exceeded 3 ms");
            Check(stressFrames[stressFrames.Count-1]<20,"Foliage stress produced a >20 ms spike");
        }
        finally { Object.DestroyImmediate(mesh); }
    }
}
