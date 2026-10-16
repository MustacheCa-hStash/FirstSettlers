using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;

/// <summary>Real files, interrupted commits, bounded decode, restart meshes/colliders and IO lifecycle.</summary>
public static class TerrainSculptingPersistenceValidation
{
    private static int checks;
    private static string root;
    private static readonly TerrainDensityProfile P=TerrainDensityProfile.Default;
    private static TerrainEditIdentity Identity => TerrainEditIdentity.Create("validation",WorldGenerationConfiguration.Default,P);
    private static void Check(bool condition,string message){checks++;if(!condition)throw new InvalidOperationException(message);}
    private static void Throws<T>(Action action,string message) where T:Exception
    {try{action();}catch(T){checks++;return;}throw new InvalidOperationException(message);}
    private static TerrainDensityWorld World()=>new TerrainDensityWorld(P,new TerrainSculptingIntegrationValidation.Height((x,z)=>100));
    private static string Folder(string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
    private static TerrainEditStore Store(string path,Action<string> fault=null)=>new TerrainEditStore(path,Identity,TerrainDensityWorld.DefaultBudgetBytes,fault);
    private static void Cut(TerrainDensityWorld world,TerrainDensityBrush brush)
    {using var edit=world.StageExcavation(brush);Check(world.TryCommit(edit),"Fixture cut did not commit.");}
    private static void Save(TerrainEditStore store,TerrainDensityWorld world){using var snapshot=world.CaptureSnapshot();store.Save(snapshot);}
    private static int3[] Keys(TerrainDensitySnapshot snapshot)
    {
        var values=(IDictionary)typeof(TerrainDensitySnapshot).GetProperty("Bricks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(snapshot);
        return values.Keys.Cast<int3>().OrderBy(p=>p.x).ThenBy(p=>p.y).ThenBy(p=>p.z).ToArray();
    }
    private static void Equal(TerrainDensitySnapshot a,TerrainDensitySnapshot b)
    {
        Check(a.Revision==b.Revision&&a.ModifiedBrickCount==b.ModifiedBrickCount&&a.UniformBrickCount==b.UniformBrickCount,"Reload changed owner/revision/encoding counts.");
        int3[] ak=Keys(a),bk=Keys(b);Check(ak.Length==bk.Length,"Reload changed canonical owner count.");
        for(int k=0;k<ak.Length;k++)
        {
            Check(math.all(ak[k]==bk[k]),"Reload changed canonical owner coordinates.");
            short[] av=a.CopyBrickSamples(ak[k]),bv=b.CopyBrickSamples(bk[k]);
            for(int i=0;i<av.Length;i++)Check(av[i]==bv[i],"Reload changed an encoded density sample.");
        }
        Check(a.ReadEncoded(new int3(-1000))==b.ReadEncoded(new int3(-1000)),"Absent owners stopped using baseline.");
    }
    private static void EqualMesh(TerrainSculptMeshData a,TerrainSculptMeshData b)
    {
        Check(a.Positions.Length==b.Positions.Length&&a.Triangles.Length==b.Triangles.Length,"Restart changed mesh counts.");
        for(int i=0;i<a.Positions.Length;i++)Check(math.all(a.Positions[i]==b.Positions[i])&&math.all(a.Normals[i]==b.Normals[i]),"Restart changed a boundary vertex/normal.");
        for(int i=0;i<a.Triangles.Length;i++)Check(a.Triangles[i]==b.Triangles[i],"Restart changed triangle connectivity.");
    }

    [MenuItem("Tools/Terrain/Validate Sculpting Persistence and Recovery")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run outside Play Mode.");
        checks=0;root=Path.Combine(TerrainSculptingValidation.SourceRoot(),".utmp","sculpt-p4",Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(root);
        ValidateIdentity();ValidateRoundTrip();ValidateInterruptedWrites();ValidateCorruption();ValidateLimitsAndOwnership();ValidateDebounceAndShutdown();ValidateRuntimeRestart();ValidateProductionRestart();
        Debug.Log($"TERRAIN SCULPTING P4 COMPLETE: {checks} assertions; real-file round trips, identical boundary geometry, atomic cross-brick recovery, corruption/mismatch blocking, resident dirty data, background debounce, shutdown and restored runtime collision passed. Isolated files: {root}");
    }

    private static void ValidateIdentity()
    {
        var g=WorldGenerationConfiguration.Default;var original=TerrainEditIdentity.Create("a",g,P);
        Check(original.Fingerprint==TerrainEditIdentity.Create("a",g,P).Fingerprint,"Fingerprint is unstable.");
        var otherSlot=TerrainEditIdentity.Create("b",g,P);
        Check(original.WorldId!=otherSlot.WorldId&&original.Fingerprint==otherSlot.Fingerprint,"Slot identity is coupled to generation.");
        foreach(string name in new[]{"Seed","ChunkSize","Octaves","SampleScale","WorldScale","Persistence","Lacunarity","MeshHeightMultiplier","MountainHorizontalScale","MountainSnowRenderCoverageGamma"})
        {
            object changed=g;var field=typeof(WorldGenerationConfiguration).GetField(name);
            field.SetValue(changed,field.FieldType==typeof(int)?(object)((int)field.GetValue(changed)+1):(float)field.GetValue(changed)+.125f);
            Check(original.Fingerprint!=TerrainEditIdentity.Create("a",(WorldGenerationConfiguration)changed,P).Fingerprint,"Generation field missing from fingerprint: "+name);
        }
        foreach(var field in typeof(WorldErosionSettings).GetFields(BindingFlags.Instance|BindingFlags.Public))
        {
            object erosion=g.Erosion;object value=field.GetValue(erosion);
            if(value is float f)value=f+.125f;else if(value is int n)value=n+1;else if(value is bool b)value=!b;
            else if(value is Vector2 v)value=v+Vector2.one;else if(field.FieldType.IsEnum)value=ErosionFadeTarget.Altitude;else throw new Exception("Uncovered erosion field.");
            field.SetValue(erosion,value);var changed=g;changed.Erosion=(WorldErosionSettings)erosion;
            Check(original.Fingerprint!=TerrainEditIdentity.Create("a",changed,P).Fingerprint,"Erosion field missing from fingerprint: "+field.Name);
        }
        var water=g;water.Water=new TerrainWaterSettings(g.Water.SurfaceY+1,g.MeshHeightMultiplier,g.WorldScale);
        Check(original.Fingerprint!=TerrainEditIdentity.Create("a",water,P).Fingerprint,"Water settings missing from fingerprint.");
        Check(original.Fingerprint!=TerrainEditIdentity.Create("a",g,new TerrainDensityProfile(.5,1.5)).Fingerprint,"Density profile missing from fingerprint.");
        Throws<ArgumentException>(()=>new TerrainEditStorageConfiguration(root,"../escape"),"Unsafe slot path accepted.");
    }

    private static void ValidateRoundTrip()
    {
        string path=Folder("roundtrip");using var world=World();
        using(var store=Store(path))
        {
            store.Load(world);
            Cut(world,TerrainDensityBrush.Box(new double3(2.25),new double3(4.5)));
            Save(store,world);long initialBytes=store.LastWrittenBytes;
            Check(File.Exists(Path.Combine(path,"manifest.bin")),"First manifest missing.");
            var oldRecords=Directory.GetFiles(Path.Combine(path,"bricks"),"*.brick");
            Cut(world,TerrainDensityBrush.Sphere(new double3(-12,5,-12),.5));Save(store,world);
            Check(store.LastWrittenBytes<initialBytes,"Unchanged owners were rewritten instead of reusing immutable records.");
            Check(oldRecords.All(File.Exists)&&File.Exists(Path.Combine(path,"manifest.previous.bin")),"Save destroyed predecessor records/manifest.");
            using var snapshot=world.CaptureSnapshot();Check(snapshot.UniformBrickCount>0,"Uniform-air override fixture failed.");
            using var pending=world.StageExcavation(TerrainDensityBrush.Sphere(new double3(20,5,20),.5));
            using var overlay=world.CaptureMeshingSnapshot(pending);
            Throws<InvalidOperationException>(()=>store.Save(overlay),"Pending mesh overlay was persisted.");
            Debug.Log($"P4 storage fixture: {snapshot.ModifiedBrickCount} owners ({snapshot.UniformBrickCount} uniform); resident {world.ResidentPayloadBytes} bytes; first write {initialBytes}, incremental {store.LastWrittenBytes} bytes.");
        }
        using var restored=World();using var reader=Store(path);reader.Load(restored);
        using var before=world.CaptureSnapshot();using var after=restored.CaptureSnapshot();Equal(before,after);
        var bounds=new TerrainDensityCellBounds(new int3(-16),new int3(32));
        EqualMesh(TerrainDensityMesher.Build(before,bounds),TerrainDensityMesher.Build(after,bounds));
        using var ah=world.CaptureMeshInput(new int3(-1));using var bh=restored.CaptureMeshInput(new int3(-1));
        for(int z=0;z<19;z++)for(int y=0;y<19;y++)for(int x=0;x<19;x++)Check(ah.Read(x,y,z)==bh.Read(x,y,z),"Restart changed reconstructed shared halo.");
        Check(!reader.ReadOnly&&reader.SavedRevision==world.Revision,"Healthy reload is not writable/current.");
    }

    private static void ValidateInterruptedWrites()
    {
        foreach(string boundary in new[]{"RecordWritten","ManifestPrepared","ManifestPublished"})
        {
            string path=Folder("crash-"+boundary);using var world=World();bool armed=false;
            using(var store=Store(path,stage=>{if(armed&&stage==boundary)throw new IOException("simulated process interruption");}))
            {
                store.Load(world);Cut(world,TerrainDensityBrush.Sphere(new double3(-.1,5,-.1),.7));Save(store,world);
                Cut(world,TerrainDensityBrush.Box(new double3(12,5,12),new double3(.6)));armed=true;
                Throws<IOException>(()=>Save(store,world),"Fault injection did not interrupt save at "+boundary);
            }
            using var restored=World();using var reader=Store(path);reader.Load(restored);
            bool published=boundary=="ManifestPublished";
            Check(restored.Revision==(published?2:1),"Interrupted write selected a partial revision at "+boundary);
            int3 first=TerrainDensityCoordinates.FloorSample(new double3(-.1,5,-.1),P);
            int3 second=TerrainDensityCoordinates.FloorSample(new double3(12,5,12),P);
            Check(restored.ReadEncoded(first)>0,"Interrupted write lost an older cut.");
            Check((restored.ReadEncoded(second)>0)==published,"Interrupted cross-brick cut loaded only part of the new manifest.");
            Check(restored.ModifiedBrickCount==(published?world.ModifiedBrickCount:KeysFromPrevious(path)),"Interrupted write mixed owner sets.");
        }
        string firstPath=Folder("first-crash");using(var world=World())using(var store=Store(firstPath,s=>{if(s=="ManifestPrepared")throw new IOException("first save crash");}))
        {store.Load(world);Cut(world,TerrainDensityBrush.Sphere(new double3(0,5,0),.6));Throws<IOException>(()=>Save(store,world),"First-write fault not reached.");}
        using(var world=World())using(var store=Store(firstPath)){store.Load(world);Check(world.ModifiedBrickCount==0&&world.Revision==0,"Uncommitted first-save orphans were treated as a saved world.");}
    }
    private static int KeysFromPrevious(string path)
    {
        // Interrupted-before-publish cases still name only the first cross-boundary sphere.
        using var expected=World();Cut(expected,TerrainDensityBrush.Sphere(new double3(-.1,5,-.1),.7));return expected.ModifiedBrickCount;
    }
    private static void Flip(string path){byte[] bytes=File.ReadAllBytes(path);bytes[bytes.Length-1]^=0x40;File.WriteAllBytes(path,bytes);}
    private static string Seed(string name,bool twice=false)
    {
        string path=Folder(name);using var world=World();using var store=Store(path);store.Load(world);
        Cut(world,TerrainDensityBrush.Sphere(new double3(0,5,0),.6));Save(store,world);
        if(twice){Cut(world,TerrainDensityBrush.Sphere(new double3(12,5,12),.6));Save(store,world);}return path;
    }
    private static Dictionary<string,byte[]> Files(string path)=>Directory.GetFiles(path,"*",SearchOption.AllDirectories)
        .Where(p=>!p.EndsWith("writer.lock",StringComparison.Ordinal)).ToDictionary(p=>p,File.ReadAllBytes);
    private static void Unchanged(Dictionary<string,byte[]> before)
    {foreach(var pair in before)Check(File.Exists(pair.Key)&&pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)),"Rejected load overwrote a saved file.");}

    private static void ValidateCorruption()
    {
        string bad=Seed("corrupt-only");Flip(Directory.GetFiles(Path.Combine(bad,"bricks"),"*.brick")[0]);var preserved=Files(bad);
        using(var world=World())using(var store=Store(bad))
        {Throws<IOException>(()=>store.Load(world),"Corrupt only copy silently loaded baseline.");Check(world.Revision==0&&world.ModifiedBrickCount==0,"Partial corrupt load became authoritative.");}Unchanged(preserved);
        string recover=Seed("recover-manifest",true);Flip(Path.Combine(recover,"manifest.bin"));preserved=Files(recover);
        using(var world=World())using(var store=Store(recover))
        {store.Load(world);Check(store.ReadOnly&&store.Warning!=null&&world.Revision==1,"Last-good manifest did not recover read-only.");using var snapshot=world.CaptureSnapshot();Throws<InvalidOperationException>(()=>store.Save(snapshot),"Recovery overwrote damaged successor.");}Unchanged(preserved);
        string recoverBrick=Seed("recover-brick");var old=Directory.GetFiles(Path.Combine(recoverBrick,"bricks"),"*.brick").ToHashSet();
        using(var world=World())using(var store=Store(recoverBrick)){store.Load(world);Cut(world,TerrainDensityBrush.Sphere(new double3(12,5,12),.6));Save(store,world);}
        Flip(Directory.GetFiles(Path.Combine(recoverBrick,"bricks"),"*.brick").First(p=>!old.Contains(p)));
        using(var world=World())using(var store=Store(recoverBrick)){store.Load(world);Check(store.ReadOnly&&world.Revision==1,"Corrupt latest brick did not recover a complete predecessor.");}
        string mismatch=Seed("mismatch",true);preserved=Files(mismatch);
        var changed=WorldGenerationConfiguration.Default;changed.Seed++;
        using(var world=World())using(var store=new TerrainEditStore(mismatch,TerrainEditIdentity.Create("validation",changed,P)))
        {Throws<TerrainEditCompatibilityException>(()=>store.Load(world),"Generation mismatch loaded edits or fell back silently.");Check(world.ModifiedBrickCount==0,"Mismatched load modified density.");}Unchanged(preserved);
        string unknown=Seed("unknown-format");string manifest=Path.Combine(unknown,"manifest.bin");byte[] file=File.ReadAllBytes(manifest);
        file[4]=99;RehashManifest(file);File.WriteAllBytes(manifest,file);preserved=Files(unknown);
        using(var world=World())using(var store=Store(unknown))Throws<TerrainEditCompatibilityException>(()=>store.Load(world),"Unknown future format was accepted.");Unchanged(preserved);
        string truncated=Seed("truncated");File.WriteAllBytes(Path.Combine(truncated,"manifest.bin"),new byte[9]);
        using(var world=World())using(var store=Store(truncated))Throws<IOException>(()=>store.Load(world),"Truncated manifest was accepted.");
    }
    private static void RehashManifest(byte[] file)
    {
        using var sha=System.Security.Cryptography.SHA256.Create();byte[] hash=sha.ComputeHash(file,0,file.Length-64);
        byte[] hex=System.Text.Encoding.ASCII.GetBytes(BitConverter.ToString(hash).Replace("-","").ToLowerInvariant());Buffer.BlockCopy(hex,0,file,file.Length-64,64);
    }

    private static void ValidateLimitsAndOwnership()
    {
        string path=Seed("bounded");
        using(var world=World())using(var store=new TerrainEditStore(path,Identity,64*1024))
        {Throws<TerrainDensityCapacityException>(()=>store.Load(world),"Oversized saved world bypassed density admission.");Check(world.ModifiedBrickCount==0,"Budget failure published a partial load.");}
        using(var world=World())using(var first=Store(path))
        {
            first.Load(world);using var secondWorld=World();using var second=Store(path);
            Throws<IOException>(()=>second.Load(secondWorld),"Concurrent writer acquired the same slot.");
        }
        // Corrupt reused records must prevent a successor manifest from committing.
        using(var world=World())using(var store=Store(path))
        {
            store.Load(world);byte[] manifest=File.ReadAllBytes(Path.Combine(path,"manifest.bin"));
            Flip(Directory.GetFiles(Path.Combine(path,"bricks"),"*.brick")[0]);
            Cut(world,TerrainDensityBrush.Sphere(new double3(12,5,12),.6));
            Throws<IOException>(()=>Save(store,world),"Corrupt reused brick was published into a successor.");
            Check(manifest.SequenceEqual(File.ReadAllBytes(Path.Combine(path,"manifest.bin"))),"Failed reuse changed the committed manifest.");
        }
        string headroom=Folder("save-headroom");const long smallBudget=512*1024;
        using(var world=new TerrainDensityWorld(P,new TerrainSculptingIntegrationValidation.Height((x,z)=>100),smallBudget))
        using(var persistence=new TerrainEditPersistence(world,new TerrainEditStore(headroom,Identity,smallBudget)))
        {
            Pump(persistence,()=>persistence.Ready);bool refused=false;
            for(int i=0;i<40;i++)
            {
                try{Cut(world,TerrainDensityBrush.Sphere(new double3(i*20+3,5,3),.5));}
                catch(TerrainDensityCapacityException){refused=true;break;}
            }
            Check(refused&&world.ModifiedBrickCount>0&&world.ResidentPayloadBytes<=(smallBudget-64*1024)/2,"Persistent edits consumed their mandatory save headroom.");
            Check(persistence.Flush()&&persistence.SavedRevision==world.Revision&&world.ReservedPayloadBytes==0,"A capacity-limited world could no longer flush its accepted edits.");
        }
        string external=Seed("manifest-changed");
        using(var world=World())using(var store=Store(external))
        {
            store.Load(world);Flip(Path.Combine(external,"manifest.bin"));var preserved=Files(external);
            Cut(world,TerrainDensityBrush.Sphere(new double3(12,5,12),.6));
            Throws<IOException>(()=>Save(store,world),"External manifest corruption was silently overwritten.");Unchanged(preserved);
        }
    }
    private static void Pump(TerrainEditPersistence persistence,Func<bool> done)
    {
        var timer=Stopwatch.StartNew();
        do{persistence.Update();if(done())return;Thread.Sleep(1);}while(timer.ElapsedMilliseconds<10000);
        throw new TimeoutException("Persistence fixture did not converge: "+persistence.Status);
    }
    private static void ValidateDebounceAndShutdown()
    {
        string path=Folder("debounce");double now=10;using var world=World();
        using(var persistence=new TerrainEditPersistence(world,Store(path),null,()=>now))
        {
            Check(!persistence.Ready&&!persistence.CanExcavate,"Startup allowed edits before validation.");
            Pump(persistence,()=>persistence.Ready);Cut(world,TerrainDensityBrush.Sphere(new double3(0,5,0),.6));persistence.Update();
            Check(persistence.Dirty&&persistence.SavedRevision==0,"Committed-in-memory revision was reported durable.");
            now=11.9;persistence.Update();Check(!File.Exists(Path.Combine(path,"manifest.bin")),"Save ignored the two-second debounce.");
            now=12.1;Pump(persistence,()=>persistence.SavedRevision==1);
            Check(!persistence.Dirty&&world.ReservedPayloadBytes==0,"Save leaked its snapshot lease.");
            Cut(world,TerrainDensityBrush.Sphere(new double3(12,5,12),.6));persistence.Update();
            Check(persistence.RequestSave(),"Explicit save was rejected.");Pump(persistence,()=>persistence.SavedRevision==2);
            Cut(world,TerrainDensityBrush.Sphere(new double3(24,5,24),.6));
            Check(persistence.Dirty,"Unsaved density was discarded before shutdown.");
        }
        using(var restored=World())using(var store=Store(path)){store.Load(restored);Check(restored.Revision==3,"Orderly shutdown did not flush the latest committed revision.");}

        // A blocked in-flight save owns an immutable revision while newer edits commit.
        string concurrent=Folder("inflight");using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        using(var w=World())using(var persistence=new TerrainEditPersistence(w,Store(concurrent,s=>{if(s=="ManifestPrepared"){entered.Set();if(!release.Wait(10000))throw new TimeoutException();}})))
        {
            try
            {
                Pump(persistence,()=>persistence.Ready);Cut(w,TerrainDensityBrush.Sphere(new double3(0,5,0),.6));
                persistence.RequestSave();persistence.Update();Check(entered.Wait(10000),"Background save did not start.");
                Cut(w,TerrainDensityBrush.Sphere(new double3(12,5,12),.6));
                Check(w.ReservedPayloadBytes>0&&persistence.Dirty,"In-flight save lost its lease or newer dirty revision.");
            }
            finally{release.Set();}
            Check(persistence.Flush()&&persistence.SavedRevision==2&&w.ReservedPayloadBytes==0,"Flush did not catch a newer revision behind an in-flight save.");
        }
        string fail=Folder("save-failure");bool inject=true;
        using(var w=World())using(var persistence=new TerrainEditPersistence(w,Store(fail,s=>{if(inject&&s=="RecordWritten")throw new IOException("simulated disk full");})))
        {
            Pump(persistence,()=>persistence.Ready);Cut(w,TerrainDensityBrush.Sphere(new double3(0,5,0),.6));long payload=w.ResidentPayloadBytes;
            persistence.RequestSave();Pump(persistence,()=>persistence.LastError!=null);
            Check(persistence.Dirty&&!persistence.CanExcavate&&w.ResidentPayloadBytes==payload&&w.ReservedPayloadBytes==0,"Failed save discarded unsaved density or admitted more edits.");
            inject=false;persistence.RequestSave();Pump(persistence,()=>!persistence.Dirty);
            Check(persistence.CanExcavate,"Successful explicit retry did not release save backpressure.");
        }
    }

    private static void ValidateRuntimeRestart()
    {
        string path=Folder("runtime-restart");TerrainSculptMeshData before;
        var brush=TerrainDensityBrush.Box(new double3(12,2.4,12),new double3(1.2,1,1.2));
        using(var fixture=new TerrainSculptingIntegrationValidation.Fixture(store:Store(path)))
        {
            Check(!fixture.Runtime.TryExcavate(brush)&&!fixture.Runtime.CanMove(new double3(12,1.4,12),new double3(12,1.4,12),.3,1.8),"Runtime bypassed startup readiness.");
            fixture.Ready();Check(fixture.Runtime.TryExcavate(brush),"Ready persistent runtime refused excavation.");fixture.Ready();
            using var snapshot=fixture.World.CaptureSnapshot();before=TerrainDensityMesher.Build(snapshot,new TerrainDensityCellBounds(new int3(32,0,32),new int3(48,24,48)));
            Check(fixture.Runtime.Persistence.Dirty,"Runtime reported an unflushed edit saved.");
            Check(fixture.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(24,2.4,12),.5)),"Could not queue the shutdown-cancellation fixture.");
            Check(fixture.World.Revision==1,"Queued shutdown brush published density before staging.");
        }
        using(var fixture=new TerrainSculptingIntegrationValidation.Fixture(store:Store(path)))
        {
            Check(!fixture.Runtime.CanMove(new double3(12,1.4,12),new double3(12,1.4,12),.3,1.8),"Restart allowed movement before edited collision.");
            fixture.Ready();Check(fixture.World.Revision==1&&fixture.Runtime.PublishedDensityRevision==1&&fixture.Runtime.LiveChunkCount>0,"Runtime did not reconstruct restored coverage.");
            using var snapshot=fixture.World.CaptureSnapshot();EqualMesh(before,TerrainDensityMesher.Build(snapshot,new TerrainDensityCellBounds(new int3(32,0,32),new int3(48,24,48))));
            Physics.SyncTransforms();
            Check(Physics.Raycast(new Vector3(12,2.4f,12),Vector3.down,out var hit,3)&&Math.Abs(hit.point.y-1.4)<.06,"Restarted tunnel floor collider does not match the saved excavation.");
            Check(fixture.Runtime.CanMove(new double3(12,1.4,12),new double3(12,1.4,12),.3,1.8),"Restored collision did not release movement guard.");
            // Retiring derived geometry leaves saved authoritative owners intact.
            int count=fixture.World.ModifiedBrickCount;fixture.Viewer.transform.position+=Vector3.right*400;fixture.Ready();
            Check(fixture.World.ModifiedBrickCount==count&&fixture.Runtime.LiveChunkCount==0,"Derived retirement lost saved density.");
            fixture.Viewer.transform.position=new Vector3(12,1.4f,12);fixture.Ready();
            Check(fixture.Runtime.LiveChunkCount>0,"Return to saved excavation did not reconstruct it.");
        }
        string failed=Seed("runtime-corrupt");Flip(Path.Combine(failed,"manifest.bin"));var messages=new List<string>();
        using(var fixture=new TerrainSculptingIntegrationValidation.Fixture(store:Store(failed),report:messages.Add))
        {
            var timer=Stopwatch.StartNew();while(!fixture.Runtime.Persistence.FailedToLoad&&timer.ElapsedMilliseconds<10000)fixture.Tick();
            Check(fixture.Runtime.Persistence.FailedToLoad&&messages.Count==1,"Corrupt startup did not report a stable failure.");
            Check(!fixture.Runtime.TryExcavate(brush)&&!fixture.Runtime.CanMove(new double3(12),new double3(12),.3,1.8),"Corrupt startup silently enabled baseline gameplay/overwrite.");
        }
    }

    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    private static void ValidateProductionRestart()
    {
        var generation=TerrainSculptingValidation.ReadSceneGeneration(Path.Combine(TerrainSculptingValidation.SourceRoot(),"Assets/Scenes/SmearScene.unity"));
        var baseline=new TerrainBaselineSampler(generation,8);double x=12,z=12,h=baseline.SampleHeight(x,z);
        for(int attempt=0;h<generation.Water.SurfaceY+1.2&&attempt<16;attempt++){x=6+attempt%4*8;z=6+attempt/4*8;h=baseline.SampleHeight(x,z);}
        Check(h>generation.Water.SurfaceY+1,"No dry native restart fixture surface.");
        var viewer=new GameObject("P4 native restart viewer");var material=new Material(Shader.Find("Hidden/InternalErrorShader"));
        var storage=new TerrainEditStorageConfiguration(Folder("native-host"),"native-validation");
        var config=new WorldConfiguration{Generation=generation,TerrainEdits=storage,Scene=new WorldSceneReferences{Viewer=viewer.transform},
            Rendering=new WorldRenderingConfiguration{TerrainMaterial=material,WaterMaterial=material,HorizonShadows=new TerrainHorizonShadowSettings{enabled=false}}};
        var coverage=TerrainCoverageConfiguration.Default;coverage.ViewDistance=1;coverage.ColliderDistance=1;coverage.EnableFarTerrain=false;config.Coverage=coverage;
        var sculpt=TerrainSculptConfiguration.Default;sculpt.Enabled=true;config.Sculpting=sculpt;
        var coord=new ChunkCoord((int)Math.Floor(x/(128*P.Spacing)),(int)Math.Floor(z/(128*P.Spacing)));
        Vector3[] vertices=null;int[] triangles=null;float originalFloor=0;ChunkManager manager=null;int frame=0;
        try
        {
            for(int session=0;session<2;session++)
            {
                viewer.transform.position=new Vector3((float)x,(float)h+1,(float)z);manager=new ChunkManager(config,()=>++frame);
                var loaded=Field<Dictionary<ChunkCoord,ChunkRuntime>>(manager,"loadedChunks");
                void Ready()
                {
                    var watch=Stopwatch.StartNew();
                    do
                    {
                        manager.UpdateActiveChunks();
                        if(!manager.Sculpting.Busy&&manager.Sculpting.Persistence.Ready&&loaded.TryGetValue(coord,out var source)&&source.HasCollider()&&source.HasTerrainMesh)return;
                        if(manager.Sculpting.Persistence.FailedToLoad)throw new Exception(manager.Sculpting.Persistence.Status);
                        Thread.Sleep(2);
                    }while(watch.ElapsedMilliseconds<30000);
                    throw new TimeoutException("Native restart did not converge: "+manager.Sculpting.Status);
                }
                Ready();
                if(session==0){Check(manager.Sculpting.TryExcavate(TerrainDensityBrush.Sphere(new double3(x,h-.2,z),.5)),"Native persistent scoop refused.");Ready();}
                Check(manager.Sculpting.PublishedDensityRevision==1&&loaded[coord].HasSculptSurface,
                    $"Native session {session} failed to publish the saved patch: {manager.Sculpting.Status}; density={manager.Sculpting.Density.Revision}, published={manager.Sculpting.PublishedDensityRevision}; save={manager.Sculpting.Persistence.Status}.");
                var surface=Field<TerrainSculptChunkSurface>(loaded[coord],"sculptSurface");var mesh=Field<Mesh>(surface,"renderMesh");
                if(session==0){vertices=mesh.vertices;triangles=mesh.triangles;}
                else
                {
                    var actual=mesh.vertices;var indices=mesh.triangles;Check(actual.Length==vertices.Length&&indices.Length==triangles.Length,"Native restart changed source patch counts.");
                    for(int i=0;i<actual.Length;i++)Check(actual[i].Equals(vertices[i]),"Native restart changed a published Unity vertex.");
                    for(int i=0;i<indices.Length;i++)Check(indices[i]==triangles[i],"Native restart changed published topology.");
                }
                Physics.SyncTransforms();Check(Physics.Raycast(new Vector3((float)x,(float)h+2,(float)z),Vector3.down,out var hit,5,GameplayLayers.SolidSurfaceMask)&&hit.point.y<h-.3,"Native restart restored the original blocking collider.");
                if(session==0)originalFloor=hit.point.y;else Check(hit.point.y.Equals(originalFloor),"Native restart changed the physical scoop floor.");
                manager.Dispose();
                Check(manager.Sculpting.Persistence.SavedRevision==1&&manager.Sculpting.Persistence.LastError==null,"Native shutdown did not finish its terrain save: "+manager.Sculpting.Persistence.Status);
                manager=null;
            }
            var savedFiles=Files(storage.Root);sculpt.Enabled=false;config.Sculpting=sculpt;
            manager=new ChunkManager(config,()=>++frame);for(int i=0;i<3;i++)manager.UpdateActiveChunks();
            Check(manager.Sculpting==null,"Disabled feature unexpectedly loaded terrain edits.");manager.Dispose();manager=null;Unchanged(savedFiles);
            Debug.Log("SCULPT P4 NATIVE RESTART PASS: actual ChunkManager and SmearScene generator, shutdown flush, fresh host rebuild, identical published Unity geometry and collider hit; disabled feature preserved sidecar files.");
        }
        finally{manager?.Dispose();UnityEngine.Object.DestroyImmediate(viewer);UnityEngine.Object.DestroyImmediate(material);}
    }
}
