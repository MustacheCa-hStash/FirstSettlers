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

/// <summary>Real asynchronous requests, replacement ownership, physics and movement; no saved scene mutation.</summary>
public static class TerrainSculptingIntegrationValidation
{
    private static int checks;
    private static readonly TerrainDensityProfile P=TerrainDensityProfile.Default;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
    private static void Cut(TerrainDensityWorld world,TerrainDensityBrush brush)
    {using var edit=world.StageExcavation(brush);Check(world.TryCommit(edit),"Fixture cut failed.");}

    [MenuItem("Tools/Terrain/Validate Sculpting Transactions and Streaming")]
    public static void Run()
    {
        checks=0;
        ValidateVisualField();ValidateBudgetAndAdmission();ValidateTransactionsAndMotor();ValidateStaleAndShutdown();ValidateCoverageAndReuse();ValidateMemoryFailure();ValidateTeleport();ValidateEditedArea();ValidateChunkSeams();ValidateSessionLifecycle();ValidateProductionHost();
        Debug.Log($"TERRAIN SCULPTING P3 COMPLETE: {checks} assertions; asynchronous transactions, stale rejection, real motor/collision, coverage thresholds, pins, reuse, budget failure, teleport and shutdown passed.");
    }

    internal sealed class Height : ITerrainHeightDensityGenerator,ITerrainDensityBounds
    {
        internal readonly Func<double,double,double> Function;
        internal readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(true);
        internal int BlockNext;
        internal bool Fail;
        internal int Calls;
        internal Height(Func<double,double,double> function){Function=function;}
        internal double Read(double x,double z)
        {
            Interlocked.Increment(ref Calls);
            if(Interlocked.Exchange(ref BlockNext,0)==1)
            {Entered.Set();if(!Release.Wait(10000))throw new TimeoutException("Fixture worker release timed out.");}
            if(Fail)throw new InvalidOperationException("injected sculpt generator failure");
            return Function(x,z);
        }
        public double SampleDensity(double3 p)=>p.y-Read(p.x,p.z);
        public double SampleHeight(double x,double z)=>Read(x,z);
        public bool TryGetDensityBounds(double3 min,double3 max,out double low,out double high)
        {
            double lo=double.PositiveInfinity,hi=double.NegativeInfinity;
            int x0=(int)Math.Floor(min.x/P.Spacing),x1=(int)Math.Ceiling(max.x/P.Spacing);
            int z0=(int)Math.Floor(min.z/P.Spacing),z1=(int)Math.Ceiling(max.z/P.Spacing);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){double h=Read(x*P.Spacing,z*P.Spacing);lo=Math.Min(lo,h);hi=Math.Max(hi,h);}
            low=min.y-hi;high=max.y-lo;return true;
        }
        internal void Block(){Entered.Reset();Release.Reset();BlockNext=1;}
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly GameObject Viewer=new GameObject("P3 viewer");
        internal readonly Height Baseline;
        internal readonly TerrainDensityWorld World;
        internal readonly TerrainRequestManager Requests;
        internal readonly Dictionary<ChunkCoord,ChunkRuntime> Runtimes=new Dictionary<ChunkCoord,ChunkRuntime>();
        internal readonly List<Mesh> SourceMeshes=new List<Mesh>();
        internal readonly List<ChunkRecord> Records=new List<ChunkRecord>();
        internal readonly Material Material=new Material(Shader.Find("Hidden/InternalErrorShader"));
        internal TerrainSculptRuntime Runtime;
        internal readonly WorldStreamingBudget Shared;
        internal bool SourceUnavailable,DestinationAvailable=true;
        private readonly bool receiveShadows;
        internal double MaxStreamingMs;
        internal Fixture(Func<double,double,double> height=null,long derivedBytes=128L*1024*1024,float publicationMs=.5f,
            TerrainEditStore store=null,Func<double> saveClock=null,Action<string> report=null,double minimumRemovalY=double.NegativeInfinity,
            Shader terrainShader=null,TerrainSculptSurfaceSettings appearance=null,bool receiveShadows=false,TerrainSkyAccessSettings skyAccess=null)
        {
            this.receiveShadows=receiveShadows;if(terrainShader!=null)Material.shader=terrainShader;
            Baseline=new Height(height??((x,z)=>4.2));
            World=new TerrainDensityWorld(P,Baseline,TerrainDensityWorld.DefaultBudgetBytes,minimumRemovalY);
            Requests=new TerrainRequestManager(1,1,1,1,new TerrainWaterSettings(-100,200,.3f));
            Viewer.transform.position=new Vector3(12,1.4f,12);
            var config=TerrainSculptConfiguration.Default;config.DerivedBytes=derivedBytes;config.PublicationMs=publicationMs;
            config.SurfaceAppearance=appearance;
            config.SkyAccess=skyAccess;
            var persistence=store==null?null:new TerrainEditPersistence(World,store,report,saveClock);
            Runtime=new TerrainSculptRuntime(World,Baseline.Read,128,Requests,Find,Viewer.transform,config,false,p=>DestinationAvailable,null,persistence);
            var shared=WorldStreamingConfiguration.Default;shared.SculptReserveMs=.5f;
            Shared=new WorldStreamingBudget(shared);
        }
        internal ChunkRuntime Find(ChunkCoord coord)
        {
            if(SourceUnavailable)return null;
            if(Runtimes.TryGetValue(coord,out var runtime))return runtime;
            const int size=128,width=size+3;
            var heights=new float[width,width];var maps=new float[width,width];
            for(int z=0;z<width;z++)for(int x=0;x<width;x++)heights[x,z]=(float)(Baseline.Function((coord.x*size+x-1)*P.Spacing,(coord.z*size+z-1)*P.Spacing)/60);
            var record=new ChunkRecord(coord);Records.Add(record);
            Check(record.TryCompleteTerrainDataRequest(record.BeginTerrainDataRequest(),heights,maps,maps,maps,
                new BiomeType[width,width],new SurfaceType[width,width],new WaterState[width,width],new GroundCoverType[width,width],
                new WorldFeaturePlan(width,width),maps,Array.Empty<Texture2D>()),"Source fixture data did not complete.");
            runtime=new ChunkRuntime(record,size,(float)P.Spacing,null,Material,Material,receiveShadows);Runtimes.Add(coord,runtime);
            var domain=new TerrainSculptFootprint(new int2(coord.x,coord.z)*size,new int2(coord.x+1,coord.z+1)*size);
            var render=TerrainSculptPatchBuilder.BuildBaseline(P,domain,Baseline.Function).CreateMesh((float3)runtime.RootTransform.position,"Original P3 render");
            var collider=TerrainSculptPatchBuilder.BuildBaseline(P,domain,Baseline.Function,8).CreateMesh((float3)runtime.RootTransform.position,"Original P3 collision");
            SourceMeshes.Add(render);SourceMeshes.Add(collider);runtime.SetMeshes(render,null,0);runtime.ApplyCollider(collider);runtime.SetVisible(true);Physics.SyncTransforms();return runtime;
        }
        internal void Tick(Action simulation=null)
        {
            Shared.BeginFrame();
            using(Shared.Measure(WorldStreamingPhase.Sculpt))Runtime.Update(Shared);
            Shared.EndFrame();MaxStreamingMs=Math.Max(MaxStreamingMs,Shared.LastFrameMs);
            Check(Requests.ActiveMeshJobCount<=1&&Runtime.ActiveJobs<=1,"Sculpt dispatch exceeded shared mesh admission.");
            Check(Runtime.DerivedLiveBytes+Runtime.DerivedReservedBytes<=128L*1024*1024,"Derived reservation exceeded the approved cap.");
            simulation?.Invoke();Thread.Sleep(1);
        }
        internal void Ready(Action simulation=null,int timeoutMs=20000)
        {
            var clock=Stopwatch.StartNew();
            do
            {
                Tick(simulation);
                if(!Runtime.Busy&&Runtime.PublishedCoverageRevision>=0&&Runtime.Status=="Ready")return;
                if(!Runtime.Busy&&Runtime.Status!="Ready"&&Runtime.Status!="Waiting for shared mesh worker")throw new Exception("P3 fixture stopped: "+Runtime.Status);
            }while(clock.ElapsedMilliseconds<timeoutMs);
            throw new TimeoutException("P3 fixture did not converge: "+Runtime.Status);
        }
        public void Dispose()
        {
            Baseline.Release.Set();Runtime?.Dispose();
            foreach(var runtime in Runtimes.Values)runtime.DestroyRuntime();
            foreach(var record in Records)record.Dispose();
            foreach(var mesh in SourceMeshes)Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(Material);Object.DestroyImmediate(Viewer);World.Dispose();Requests.Dispose();Requests.WaitForActiveRequestsToFinish();
            Baseline.Entered.Dispose();Baseline.Release.Dispose();
        }
    }

    private static void ValidateVisualField()
    {
        var height=new Height((x,z)=>100);using var world=new TerrainDensityWorld(P,height);
        Cut(world,TerrainDensityBrush.Sphere(new double3(0),1.2));using var snapshot=world.CaptureSnapshot();
        short original=world.ReadEncoded(new int3(0));
        foreach(var sample in new[]{new double3(0),new double3(-128*P.Spacing,0,0)})
        {
            int3 address=TerrainDensityCoordinates.FloorSample(sample,P);
            foreach(double horizontal in new[]{192,208,224,240,256,288,320})
            {
                var policy=new TerrainSculptCoveragePolicy(sample-new double3(horizontal,0,0));
                var field=new TerrainSculptVisualField(snapshot,policy);
                double weight=policy.Weight(sample);Check(weight>=0&&weight<=1,"Visual blend escaped its range.");
                short expected=(short)Math.Round(math.lerp((double)P.Encode(sample.y-100),snapshot.ReadEncoded(address),weight),MidpointRounding.AwayFromZero);
                Check(field.ReadEncoded(address)==expected,"Horizontal blend does not reconstruct the shared sample.");
            }
        }
        foreach(double vertical in new[]{64,80,96,112,128})
        {
            var policy=new TerrainSculptCoveragePolicy(new double3(0,vertical,0));
            var field=new TerrainSculptVisualField(snapshot,policy);
            if(vertical<=64)Check(field.ReadEncoded(new int3(0))==original,"Full vertical range altered the field.");
            if(vertical>=96)Check(field.ReadEncoded(new int3(0))==-32767,"Vertical fade did not return to baseline.");
        }
        Check(world.ReadEncoded(new int3(0))==original,"Rendering faded authoritative density.");
        foreach(double horizontal in new[]{0d,208d,224d,256d})
        {
            ITerrainDensityField field=new TerrainSculptVisualField(snapshot,new TerrainSculptCoveragePolicy(new double3(horizontal,0,0)));
            var values=new short[19*19*19];var first=new int3(-9);
            field.FillMeshHalo(first,values);
            for(int z=0;z<19;z++)for(int y=0;y<19;y++)for(int x=0;x<19;x++)
                Check(values[x+19*(y+19*z)]==field.ReadEncoded(first+new int3(x,y,z)),"Bulk halo differs from canonical individual reads.");
        }
    }

    private static void ValidateBudgetAndAdmission()
    {
        long time=0;var config=WorldStreamingConfiguration.Default;config.SculptReserveMs=.5f;
        var budget=new WorldStreamingBudget(config,()=>time,.01);budget.BeginFrame();
        using(budget.Measure(WorldStreamingPhase.Results)){Check(Math.Abs(budget.RemainingMs-.5)<1e-5,"Sculpt reservation was added outside the shared allowance.");time+=50;}
        using(budget.Measure(WorldStreamingPhase.Sculpt)){Check(Math.Abs(budget.RemainingMs-.5)<1e-5,"Sculpt reservation was unavailable.");time+=20;}
        budget.EndFrame();Check(Math.Abs(budget.LastFrameMs-.7)<1e-5,"Sculpt time was not measured in the shared total.");
        using var fixture=new Fixture();
        using var gate=new ManualResetEventSlim(false);using var entered=new ManualResetEventSlim(false);
        Check(fixture.Requests.TryRunSculpt(()=>{entered.Set();gate.Wait();}),"Could not occupy the shared worker.");
        try
        {
            Check(entered.Wait(2000),"Shared worker did not start.");
            Check(!fixture.Requests.TryRunSculpt(()=>{}),"Sculpt started a second worker outside the shared cap.");
            Check(fixture.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,3.8,12),.5)),"First brush was not accepted.");
            fixture.Tick();Check(fixture.Runtime.ActiveJobs==0&&fixture.World.Revision==0,"Busy admission exposed an edit.");
            Check(!fixture.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(13,3.8,12),.5)),"A second stroke was queued while busy.");
        }
        finally{gate.Set();}
        fixture.SourceUnavailable=true;
        var watch=Stopwatch.StartNew();while(fixture.Runtime.Status!="Waiting for source terrain"&&watch.ElapsedMilliseconds<10000)fixture.Tick();
        Check(fixture.World.Revision==0,"Source-runtime wait committed density early.");
        fixture.SourceUnavailable=false;fixture.Ready();Check(fixture.Runtime.CommittedEdits==1,"Deferred shared admission lost the brush.");
    }

    private static void ValidateTransactionsAndMotor()
    {
        double s=P.Spacing;
        using var f=new Fixture((x,z)=>s*math.clamp(.7*((x-12)/s+20),0,14));
        const double zLane=12;
        Cut(f.World,TerrainDensityBrush.Box(new double3(9,1.35,zLane),new double3(2.4,1.35,1.2)));
        Cut(f.World,TerrainDensityBrush.Box(new double3(15,1.35,zLane),new double3(2.4,1.35,1.2)));
        f.Viewer.transform.position=new Vector3(9,0,12);f.Ready();
        Check(Physics.Raycast(new Vector3(9,1.35f,12),Vector3.right,out var wall,10,GameplayLayers.SolidSurfaceMask)&&wall.point.x<13,"Separator was missing before the transaction.");
        long before=f.World.Revision;int events=0;
        f.Runtime.TerrainEditCommitted+=(revision,brush)=>{events++;Check(revision==f.World.Revision&&f.Runtime.PublishedDensityRevision==revision,"Commit callback observed partial density.");};
        var standing=new GameObject("P3 actor standing during commit");var standingController=standing.AddComponent<CharacterController>();
        standingController.height=1.8f;standingController.center=new Vector3(0,.9f,0);standingController.radius=.4f;standingController.skinWidth=.08f;
        standing.transform.position=new Vector3(9,.08f,12);var standingMotor=standing.AddComponent<CharacterMotor>();
        typeof(CharacterMotor).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(standingMotor,null);standingMotor.TerrainGuard=f.Runtime;
        Physics.SyncTransforms();
        Action simulate=()=>{standingMotor.Simulate(default,1/60f);Check(standing.transform.position.y>-.2f&&standing.transform.position.y<.2f,"Standing actor lost terrain during the transaction.");};
        try
        {
        using(var cook=f.Requests.TryAcquireSculptCooking())
        {
            Check(cook!=null,"Could not occupy collider admission.");
            Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(12,1.35,zLane),new double3(1.2,1.35,1.2))),"Breakthrough brush was not accepted.");
            var watch=Stopwatch.StartNew();while(f.Runtime.Status!="Staging terrain"&&watch.ElapsedMilliseconds<10000)f.Tick(simulate);
            for(int i=0;i<5;i++)f.Tick(simulate);
            Check(f.World.Revision==before&&events==0,"Density committed before collision cooking admission.");
            Check(Physics.Raycast(new Vector3(9,1.35f,12),Vector3.right,out wall,10,GameplayLayers.SolidSurfaceMask)&&wall.point.x<13,"Uncommitted geometry opened the separator.");
        }
        f.Ready(simulate);Check(events==1&&f.World.Revision==before+1,"Transaction committed more or less than once.");
        }
        finally{Object.DestroyImmediate(standing);}
        Check(Physics.Raycast(new Vector3(9,1.35f,12),Vector3.right,out var far,12,GameplayLayers.SolidSurfaceMask)&&far.point.x>17,"Original/separating collider still blocks breakthrough.");
        Check(Physics.Raycast(new Vector3(15,8,12),Vector3.down,out var roof,8,GameplayLayers.SolidSurfaceMask)&&roof.point.y>4.1,"Transaction lost the retained roof.");
        var runtime=f.Find(new ChunkCoord(0,0));
        runtime.SetMeshes(f.SourceMeshes[0],null,3);runtime.ApplyCollider(f.SourceMeshes[1]);
        runtime.SetRenderVisible(false);runtime.SetTerrainHandoffHidden(true);runtime.SetRenderVisible(true);runtime.SetTerrainHandoffHidden(false);
        Check(!runtime.Root.GetComponent<MeshRenderer>().enabled&&!runtime.Root.GetComponent<MeshCollider>().enabled,"Normal refresh restored duplicate terrain over a replacement.");
        Check(Physics.Raycast(new Vector3(9,1.35f,12),Vector3.right,out far,12,GameplayLayers.SolidSurfaceMask)&&far.point.x>17,"Normal collider publication reclosed the opening.");
        foreach(float dt in new[]{1/60f,1/120f})foreach(bool reverse in new[]{false,true})
        {
            var rig=new GameObject("P3 guarded actual motor");var cc=rig.AddComponent<CharacterController>();cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.stepOffset=.3f;cc.skinWidth=.08f;
            rig.transform.SetPositionAndRotation(new Vector3(reverse?16:2,.08f,12),Quaternion.Euler(0,reverse?-90:90,0));
            var motor=rig.AddComponent<CharacterMotor>();typeof(CharacterMotor).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(motor,null);motor.TerrainGuard=f.Runtime;Physics.SyncTransforms();
            try
            {
                bool arrived=false;
                for(int i=0;i<30;i++)motor.Simulate(default,dt);
                for(int i=0;i<(int)(8/dt);i++)
                {
                    motor.Simulate(new CharacterMoveCommand(new Vector2(0,1),false,false,false),dt);
                    Check(rig.transform.position.y>-.2f&&rig.transform.position.y<5,"Guarded motor fell through replacement terrain.");
                    if(reverse?rig.transform.position.x<2.2f:rig.transform.position.x>15.8f){arrived=true;break;}
                }
                Check(arrived,"Guarded motor stalled in the streamed tunnel.");
            }
            finally{Object.DestroyImmediate(rig);}
        }
        Debug.Log($"SCULPT P3 TRANSACTION PASS: roof, breakthrough, normal refresh ownership, four actual guarded motor routes; observed maximum isolated publication frame {f.MaxStreamingMs:F3} ms (soft allowance, not live-world FPS).");
    }

    private static void ValidateStaleAndShutdown()
    {
        using var f=new Fixture();Cut(f.World,TerrainDensityBrush.Sphere(new double3(12,3.8,12),.5));f.Ready();
        long revision=f.World.Revision;
        f.Baseline.Block();Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(14,3.8,12),.5)),"Stale fixture brush rejected.");
        f.Tick();Check(f.Baseline.Entered.Wait(2000),"Sculpt worker did not reach the blocking generator.");
        try
        {
            f.Viewer.transform.position+=Vector3.right*6;f.Tick();
            Check(f.World.Revision==revision,"Pending stale work altered committed density.");
        }
        finally{f.Baseline.Release.Set();}
        f.Ready();Check(f.Runtime.StaleDiscards>0&&f.World.Revision==revision+1&&f.Runtime.CommittedEdits==1,"Anchor invalidation lost or duplicated the excavation.");
        f.Baseline.Block();Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(16,3.8,12),.5)),"Shutdown fixture brush rejected.");
        f.Tick();Check(f.Baseline.Entered.Wait(2000),"Shutdown worker did not start.");
        ThreadPool.QueueUserWorkItem(_=>{Thread.Sleep(20);f.Baseline.Release.Set();});
        f.Runtime.Dispose();Check(f.World.Revision==revision+1&&f.World.ReservedPayloadBytes==0&&f.Runtime.DerivedReservedBytes==0,"Shutdown published work or leaked reservations.");
        foreach(var runtime in f.Runtimes.Values)Check(!runtime.HasSculptSurface,"Shutdown left a replacement attached.");
    }

    private static void ValidateCoverageAndReuse()
    {
        using var f=new Fixture(publicationMs:.001f);Cut(f.World,TerrainDensityBrush.Sphere(new double3(12,3.8,12),1));f.Ready();
        long revision=f.World.Revision;
        var surface=Field<Dictionary<ChunkCoord,TerrainSculptChunkSurface>>(f.Runtime,"live")[new ChunkCoord(0,0)];
        f.Viewer.transform.position+=Vector3.right*6;f.Ready();
        Check(ReferenceEquals(surface,Field<Dictionary<ChunkCoord,TerrainSculptChunkSurface>>(f.Runtime,"live")[new ChunkCoord(0,0)]),"Full-detail movement rebuilt an unchanged chunk.");
        using(var pin=f.Runtime.PinCollision(new TerrainSculptPin(new double3(10,0,10),new double3(14,6,14))))
        {
            f.Viewer.transform.position=new Vector3(100,4,12);f.Ready();
            Check(f.Runtime.Groups[0].Collision,"Support collision pin was retired outside 64 m.");
        }
        f.Ready();Check(!f.Runtime.Groups[0].Collision,"Released support pin kept remote collision indefinitely.");
        Check(!f.Runtime.CanMove(new double3(12,0,12),new double3(13,0,12),.4,1.8),"Guard admitted an edited destination without collision.");
        foreach(float x in new[]{200f,224f,260f,300f,340f,380f})
        {
            int beforePrefetch=f.Baseline.Calls;
            f.Viewer.transform.position=new Vector3(x,4,12);f.Ready();
            Check(f.World.Revision==revision,"Coverage movement modified authoritative terrain.");
            if(x==300)Check(f.Runtime.PreparedGroupCount>0&&f.Runtime.LiveChunkCount==0&&f.Baseline.Calls>beforePrefetch,"Prefetch range did not prepare dependencies or was confused with visible excavation.");
            if(x>=380)Check(f.Runtime.RetainedGroupCount==0&&f.Runtime.DerivedLiveBytes==f.Runtime.SuppressionBytes,"Release threshold retained derived meshes beyond the persistent foliage mask.");
        }
        f.Viewer.transform.position=new Vector3(12,84,12);f.Ready();Check(f.Runtime.Groups[0].Render,"Vertical transition discarded a still-relevant excavation.");
        f.Viewer.transform.position=new Vector3(12,112,12);f.Ready();Check(!f.Runtime.Groups[0].Render,"Vertical fade failed to return rendering to baseline.");
        f.Viewer.transform.position=new Vector3(12,1.4f,12);f.Ready();
        Check(f.Runtime.CanMove(new double3(12,0,12),new double3(13,0,12),.4,1.8),"Return movement did not reconstruct local collision.");
        Check(f.World.ReservedPayloadBytes==0,"Coverage jobs leaked density/halo reservations.");
        Debug.Log("SCULPT P3 COVERAGE PASS: 192/256 render fade, 288/320 preparation/retention, 64/96 vertical fade, collision pins/retirement, unchanged-chunk reuse and low publication allowance convergence.");
    }

    private static void ValidateMemoryFailure()
    {
        using var f=new Fixture(derivedBytes:64*1024);
        Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,3.8,12),1)),"Budget fixture brush rejected before its worker.");
        var clock=Stopwatch.StartNew();do{f.Tick();}while(f.Runtime.Busy&&clock.ElapsedMilliseconds<10000);
        Check(!f.Runtime.Busy&&f.World.Revision==0&&f.Runtime.LiveChunkCount==0,"Failed derived admission published a partial edit.");
        Check(f.World.ReservedPayloadBytes==0&&f.Runtime.DerivedReservedBytes==0,"Failed admission leaked reservations.");

        using var pressure=new Fixture();
        Cut(pressure.World,TerrainDensityBrush.Sphere(new double3(12,3.8,12),1));pressure.Ready();
        long revision=pressure.World.Revision;
        // Inject an unsatisfiable successor allowance after a healthy published batch.
        // Recovery must try once, preserve authoritative terrain and keep its old collider.
        typeof(TerrainSculptRuntime).GetField("derivedLimit",BindingFlags.Instance|BindingFlags.NonPublic)
            .SetValue(pressure.Runtime,pressure.Runtime.DerivedLiveBytes+1);
        Check(pressure.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,3.5,12),1)),"Cache pressure edit refused before staging.");
        clock.Restart();do{pressure.Tick();}while(pressure.Runtime.Busy&&clock.ElapsedMilliseconds<10000);
        Check(!pressure.Runtime.Busy&&pressure.Runtime.CachePressureRetries==1,"Cache pressure failed to stop after its one cache-free retry.");
        Check(pressure.World.Revision==revision&&pressure.Runtime.CanMove(new double3(12,5,12),new double3(12,4.99,12),.4,1.8),"Failed recovery changed density or disabled the published collider.");
        Check(pressure.World.ReservedPayloadBytes==0&&pressure.Runtime.DerivedReservedBytes==0,"Cache recovery leaked reservations.");
    }

    private static void ValidateSessionLifecycle()
    {
        var root=new GameObject("Session terrain composition");root.SetActive(false);
        var host=root.AddComponent<WorldManager>();
        try
        {
            typeof(WorldManager).GetField("enableTerrainSculpting",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,true);
            typeof(WorldManager).GetField("worldSeed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,14567);
            var build=typeof(WorldManager).GetMethod("BuildConfiguration",BindingFlags.Instance|BindingFlags.NonPublic);
            var first=(WorldConfiguration)build.Invoke(host,null);
            Check(first.Sculpting.Enabled,"Session composition fixture failed to enable sculpting.");
            Check(first.TerrainEdits==null,"WorldManager still attaches a disk terrain store.");
            typeof(WorldManager).GetField("worldSeed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,24680);
            var second=(WorldConfiguration)build.Invoke(host,null);
            Check(second.Generation.Seed==24680&&second.TerrainEdits==null,"Changing seed reactivated old disk terrain.");
        }
        finally{Object.DestroyImmediate(root);}
        using(var session=new Fixture())
        {
            session.Ready();Check(session.Runtime.Persistence==null,"Session created a persistence service.");
            Check(session.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,3.8,12),1)),"Session excavation refused.");session.Ready();
            session.Viewer.transform.position+=Vector3.right*400;session.Ready();
            Check(session.World.ModifiedBrickCount>0&&session.Runtime.LiveChunkCount==0,"Streaming retirement discarded session density.");
            session.Viewer.transform.position=new Vector3(12,6,12);session.Ready();
            Check(session.Runtime.LiveChunkCount>0&&session.Runtime.Suppression!=null,"Returning within a session lost terrain/vegetation edits.");
        }
        using(var restarted=new Fixture())
        {
            restarted.Ready();
            Check(restarted.World.Revision==0&&restarted.World.ModifiedBrickCount==0&&restarted.Runtime.LiveChunkCount==0&&restarted.Runtime.Suppression==null,
                "Restart carried edits or suppression into the fresh session.");
            Check(restarted.Runtime.CanMove(new double3(12,100,12),new double3(12,99.99,12),.4,1.8),"Unedited elevated spawn is blocked by sculpt readiness.");
        }
        Debug.Log("SCULPT SESSION PASS: actual WorldManager composition ignores disk persistence across seed changes; edits survive streaming only within one session and restart with empty terrain/foliage overrides.");
    }

    private static void ValidateTeleport()
    {
        using var f=new Fixture();Cut(f.World,TerrainDensityBrush.Sphere(new double3(12,3.8,12),1));f.Ready();
        f.Viewer.transform.position=new Vector3(120,4,12);f.Ready();
        f.DestinationAvailable=false;
        Check(f.Runtime.RequestTeleport(new double3(12,1.4,12)),"Destination preparation was not accepted.");
        var watch=Stopwatch.StartNew();while(f.Runtime.Status!="Waiting for destination collision"&&watch.ElapsedMilliseconds<10000)f.Tick();
        Check(f.Viewer.transform.position.x==120&&f.Runtime.PendingDestination.HasValue,"Viewer moved before destination readiness.");
        f.DestinationAvailable=true;f.Ready();
        Check(Math.Abs(f.Viewer.transform.position.x-12)<.001&&f.Runtime.CanMove(new double3(12,1.4,12),new double3(12,1.3,12),.4,1.8),"Teleport completed without edited collision.");
        using var empty=new Fixture();Check(empty.Runtime.RequestTeleport(new double3(20,4,20)),"Unedited teleport rejected.");empty.Ready();
        Check(Math.Abs(empty.Viewer.transform.position.x-20)<.001,"Unedited destination never completed.");
    }

    private static void ValidateEditedArea()
    {
        using var f=new Fixture();var times=new List<double>();
        for(int z=-2;z<=2;z+=2)for(int x=-2;x<=2;x+=2)
        {
            var clock=Stopwatch.StartNew();
            Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12+x*128*P.Spacing,3.8,12+z*128*P.Spacing),.5)),"Edited-area brush was rejected.");
            f.Ready();times.Add(clock.Elapsed.TotalMilliseconds);
        }
        Check(f.Runtime.Groups.Count==9&&f.Runtime.CommittedEdits==9,"Edited-area fixture did not preserve all separated excavations.");
        long revision=f.World.Revision;
        foreach(float x in new[]{18f,90f,150f,230f,330f,12f})
        {f.Viewer.transform.position=new Vector3(x,4,12);f.Ready();Check(f.World.Revision==revision,"Edited-area sweep changed saved density.");}
        times.Sort();double p95=times[(int)Math.Ceiling(times.Count*.95)-1];
        Debug.Log($"SCULPT P3 EDITED AREA PASS: nine separated excavations inside the enlarged near radius; coverage sweep/return passed; isolated cold/warm mixed edit p95={p95:F3} ms, max publication frame={f.MaxStreamingMs:F3} ms, density={f.World.ResidentPayloadBytes} bytes, retained derived estimate={f.Runtime.DerivedLiveBytes} bytes. Not a live SmearScene frame-rate benchmark.");
    }

    private static void ValidateChunkSeams()
    {
        foreach(double center in new[]{0,128*P.Spacing,-128*P.Spacing})
        {
            using var f=new Fixture();
            Cut(f.World,TerrainDensityBrush.Box(new double3(center,1.35,0),new double3(1.8,1.35,1.2)));
            f.Viewer.transform.position=new Vector3((float)center,0,0);f.Ready();
            PublishedTopology(f.Runtime,false);PublishedTopology(f.Runtime,true);
            var rig=new GameObject("P3 chunk boundary motor");var cc=rig.AddComponent<CharacterController>();
            cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.stepOffset=.3f;cc.skinWidth=.08f;
            rig.transform.SetPositionAndRotation(new Vector3((float)center-1,.08f,0),Quaternion.Euler(0,90,0));
            var motor=rig.AddComponent<CharacterMotor>();typeof(CharacterMotor).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(motor,null);motor.TerrainGuard=f.Runtime;Physics.SyncTransforms();
            try
            {
                for(int i=0;i<30;i++)motor.Simulate(default,1/120f);
                bool arrived=false;
                for(int i=0;i<150;i++)
                {
                    motor.Simulate(new CharacterMoveCommand(new Vector2(0,1),false,false,false),1/120f);
                    Check(rig.transform.position.y>-.2f&&rig.transform.position.y<.2f,"Motor fell through a streamed source-chunk corner.");
                    if(rig.transform.position.x>center+.9){arrived=true;break;}
                }
                Check(arrived,"Motor could not cross the source-chunk seam.");
            }
            finally{Object.DestroyImmediate(rig);}
            int released=0;
            var policy=TerrainCoverageConfiguration.Default;policy.EnableFarTerrain=false;
            var handoff=new TerrainHandoffCoordinator(new TerrainCoveragePolicy(policy),f.Runtimes,new Dictionary<FarTerrainPatchKey,FarTerrainTileRuntime>(),
                (c,r)=>{released++;r.DestroyRuntime();},r=>{},c=>!f.Runtime.OwnsRuntime(c));
            handoff.Reconcile(new HashSet<ChunkCoord>());handoff.Update(default,new HashSet<ChunkCoord>(),new HashSet<FarTerrainPatchKey>());
            Check(released==0&&f.Runtime.LiveChunkCount>0,"Ordinary handoff retired active sculpt coverage.");
            f.Viewer.transform.position=new Vector3((float)center+400,4,0);f.Ready();
            handoff.Update(default,new HashSet<ChunkCoord>(),new HashSet<FarTerrainPatchKey>());
            Check(released>0&&f.Runtimes.Count==0,"Completed replacement retirement pinned source runtimes forever.");
        }
        Debug.Log("SCULPT P3 SEAMS PASS: published render/collision topology and actual motor at source-chunk corners 0/+128/-128; ordinary handoff ownership/retirement passed.");
    }

    private static void PublishedTopology(TerrainSculptRuntime runtime,bool collision)
    {
        var live=Field<Dictionary<ChunkCoord,TerrainSculptChunkSurface>>(runtime,"live");
        var present=new HashSet<ChunkCoord>();var positions=new List<Vector3>();var vertices=new Dictionary<Vector3,int>();var edges=new Dictionary<ulong,int>();
        foreach(var pair in live)
        {
            if(collision?!pair.Value.HasCollision:!pair.Value.HasRender)continue;
            present.Add(pair.Key);var root=Field<GameObject>(pair.Value,"root");
            Mesh mesh=collision?root.GetComponent<MeshCollider>().sharedMesh:root.GetComponent<MeshFilter>().sharedMesh;
            var source=mesh.vertices;var remap=new int[source.Length];
            for(int i=0;i<source.Length;i++)
            {
                var point=source[i];
                if(!vertices.TryGetValue(point,out int index)){index=positions.Count;vertices.Add(point,index);positions.Add(point);}remap[i]=index;
            }
            var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)
            {
                int a=remap[triangles[i+j]],b=remap[triangles[i+(j+1)%3]];
                Check(a!=b,"Unity float conversion collapsed an edge.");
                ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);edges.TryGetValue(key,out int n);edges[key]=n+1;
            }
        }
        double width=128*P.Spacing;
        foreach(var edge in edges)
        {
            if(edge.Value==2){checks++;continue;}
            Check(edge.Value==1,"Published meshes have a duplicate or non-manifold edge.");
            Vector3 a=positions[(int)(edge.Key>>32)],b=positions[(int)(edge.Key&uint.MaxValue)];bool exterior=false;
            foreach(int axis in new[]{0,2})
            {
                if(a[axis]!=b[axis])continue;
                int plane=(int)Math.Round(a[axis]/width);
                if(Math.Abs(a[axis]-plane*width)>1e-4)continue;
                int along=(int)Math.Floor(((double)a[axis==0?2:0]+b[axis==0?2:0])*.5/width);
                var left=axis==0?new ChunkCoord(plane-1,along):new ChunkCoord(along,plane-1);
                var right=axis==0?new ChunkCoord(plane,along):new ChunkCoord(along,plane);
                exterior|=!present.Contains(left)||!present.Contains(right);
            }
            Check(exterior,$"Published {(collision?"collision":"render")} has an internal crack: {a:R} to {b:R}.");
        }
    }

    private static void ValidateProductionHost()
    {
        var generation=TerrainSculptingValidation.ReadSceneGeneration(Path.Combine(TerrainSculptingValidation.SourceRoot(),"Assets/Scenes/SmearScene.unity"));
        var baseline=new TerrainBaselineSampler(generation,8);
        var viewer=new GameObject("P3 production ChunkManager viewer");var material=new Material(Shader.Find("Hidden/InternalErrorShader"));
        ChunkManager manager=null;
        try
        {
            double x=12,z=12,h=baseline.SampleHeight(x,z);
            for(int attempt=0;h<generation.Water.SurfaceY+1.2&&attempt<16;attempt++)
            {x=6+(attempt%4)*8;z=6+(attempt/4)*8;h=baseline.SampleHeight(x,z);}
            Check(h>generation.Water.SurfaceY+1,"Could not locate a dry native fixture surface.");
            viewer.transform.position=new Vector3((float)x,(float)h+1,(float)z);
            var config=new WorldConfiguration {Generation=generation,Scene=new WorldSceneReferences{Viewer=viewer.transform},
                Rendering=new WorldRenderingConfiguration{TerrainMaterial=material,WaterMaterial=material,HorizonShadows=new TerrainHorizonShadowSettings{enabled=false}}};
            var coverage=TerrainCoverageConfiguration.Default;coverage.ViewDistance=1;coverage.ColliderDistance=1;coverage.EnableFarTerrain=false;config.Coverage=coverage;
            var sculpt=TerrainSculptConfiguration.Default;sculpt.Enabled=true;config.Sculpting=sculpt;
            var streaming=WorldStreamingConfiguration.Default;streaming.SculptReserveMs=.5f;config.Streaming=streaming;
            int simulatedFrame=0;manager=new ChunkManager(config,()=>++simulatedFrame);
            var coord=new ChunkCoord((int)Math.Floor(x/(128*P.Spacing)),(int)Math.Floor(z/(128*P.Spacing)));
            var loaded=Field<Dictionary<ChunkCoord,ChunkRuntime>>(manager,"loadedChunks");
            var watch=Stopwatch.StartNew();
            while((!loaded.TryGetValue(coord,out var source)||!source.HasCollider()||!source.HasTerrainMesh||!source.IsShowingLOD(0))&&watch.ElapsedMilliseconds<30000)
            {manager.UpdateActiveChunks();Thread.Sleep(2);}
            Check(loaded.TryGetValue(coord,out var runtime)&&runtime.HasCollider()&&runtime.HasTerrainMesh,"Production streaming did not prepare the source terrain.");
            ValidateElevatedMotor(manager,viewer);
            var original=runtime.Root.GetComponent<MeshFilter>().sharedMesh;
            Check(manager.Sculpting.TryExcavate(TerrainDensityBrush.Sphere(new double3(x,h-.2,z),.5)),"Production host rejected the dry scoop.");
            watch.Restart();
            while(manager.Sculpting.Busy&&watch.ElapsedMilliseconds<30000){manager.UpdateActiveChunks();Thread.Sleep(2);}
            Check(manager.Sculpting.CommittedEdits==1&&runtime.HasSculptSurface,"Production ChunkManager did not publish the replacement.");
            Check(runtime.Root.GetComponent<MeshFilter>().sharedMesh==original,"Sculpting overwrote the cached ordinary LOD asset.");
            Physics.SyncTransforms();
            Check(Physics.Raycast(new Vector3((float)x,(float)h+2,(float)z),Vector3.down,out var hit,5,GameplayLayers.SolidSurfaceMask)&&hit.point.y<h-.3,
                "Production source collider still blocks the scoop.");
            Check(BuildWorld.IsGround(hit.collider),"Production patch lost its ground identity.");
            double firstMs=watch.Elapsed.TotalMilliseconds;var warm=new List<double>();
            for(int i=1;i<=5;i++)
            {
                double bx=x+i*.9,bz=z+(i%2)*.7,bh=baseline.SampleHeight(bx,bz);
                Check(manager.Sculpting.TryExcavate(TerrainDensityBrush.Sphere(new double3(bx,bh-.2,bz),.5)),"Warm native scoop was not accepted.");
                watch.Restart();while(manager.Sculpting.Busy&&watch.ElapsedMilliseconds<30000){manager.UpdateActiveChunks();Thread.Sleep(2);}
                Check(manager.Sculpting.CommittedEdits==i+1,"Warm native scoop failed to publish.");warm.Add(watch.Elapsed.TotalMilliseconds);
            }
            warm.Sort();
            Debug.Log($"SCULPT P3 HOST PASS: actual ChunkManager, native SmearScene generator/profile and shared streaming loop; first scoop {firstMs:F3} ms, five subsequent scoops {warm[0]:F3}-{warm[warm.Count-1]:F3} ms; last worker={manager.Sculpting.LastWorkerMs:F3}, mesh stage={manager.Sculpting.LastMeshStageMs:F3}, cooking={manager.Sculpting.LastCookMs:F3}, commit={manager.Sculpting.LastCommitMs:F3} ms; live render/collision={manager.Sculpting.LiveRenderTriangles}/{manager.Sculpting.LiveCollisionTriangles} triangles; original cached LOD asset retained. Fixture uses a small content-free coverage set; live scene activation/FPS gate remains outstanding.");
            // Simulate a still-visible outgoing far tile over a relocation destination.
            var farKey=new FarTerrainPatchKey(new ChunkCoord(1,0),1);
            var farRecord=new FarTerrainTileRecord(farKey);
            var farRuntime=new FarTerrainTileRuntime(farRecord,128,(float)P.Spacing,null,material,false,material);
            var farMesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.right,Vector3.forward},triangles=new[]{0,2,1}};
            try
            {
                farRuntime.SetMesh(farMesh);farRuntime.SetVisible(true);
                var far=Field<Dictionary<FarTerrainPatchKey,FarTerrainTileRuntime>>(manager,"loadedFarTerrainTiles");far.Add(farKey,farRuntime);
                Field<HashSet<FarTerrainPatchKey>>(manager,"activeFarTilesLastUpdate").Add(farKey);
                Vector3 oldPosition=viewer.transform.position;
                Check(manager.Sculpting.RequestTeleport(new double3(50,h+1,12)),"Production relocation request was not accepted.");
                watch.Restart();bool sawWait=false;
                while(manager.Sculpting.Busy&&watch.ElapsedMilliseconds<30000)
                {
                    manager.UpdateActiveChunks();
                    if(viewer.transform.position==oldPosition)
                    {sawWait=true;Check(far.ContainsKey(farKey),"Far tile disappeared before the relocation swap.");}
                    Thread.Sleep(2);
                }
                Check(sawWait&&Math.Abs(viewer.transform.position.x-50)<.001&&!far.ContainsKey(farKey),"Far/source relocation handoff did not complete atomically.");
                Check(farMesh.vertexCount==3&&farMesh.triangles.Length==3,"Handoff modified far mesh output data.");
                Debug.Log("SCULPT P3 FAR HANDOFF PASS: outgoing far tile retained until normal coverage/collision readiness, then retired in the relocation frame; far mesh data unchanged.");
            }
            finally{farRecord.Dispose();Object.DestroyImmediate(farMesh);}
        }
        finally{manager?.Dispose();Object.DestroyImmediate(viewer);Object.DestroyImmediate(material);}
    }

    private static void ValidateElevatedMotor(ChunkManager manager,GameObject viewer)
    {
        Vector3 original=viewer.transform.position;
        Check(manager.Sculpting.Persistence==null,"Native session host unexpectedly enabled disk persistence.");
        Check(Physics.Raycast(original+Vector3.up*8,Vector3.down,out var floor,40,GameplayLayers.SolidSurfaceMask),"No native spawn floor was ready.");
        var cc=viewer.AddComponent<CharacterController>();cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.skinWidth=.08f;
        viewer.transform.position=new Vector3(original.x,floor.point.y+8,original.z);
        var motor=viewer.AddComponent<CharacterMotor>();typeof(CharacterMotor).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,null);
        motor.TerrainGuard=manager.Sculpting;Physics.SyncTransforms();
        try
        {
            float spawnY=viewer.transform.position.y;
            motor.Simulate(default,1/60f);
            Check(viewer.transform.position.y<spawnY&&motor.ActualVelocity.y<0,"Elevated spawn gravity was blocked.");
            for(int i=0;i<180;i++){manager.UpdateActiveChunks();motor.Simulate(default,1/60f);}
            Check(cc.isGrounded&&Math.Abs(viewer.transform.position.y-floor.point.y)<.3,"Spawn did not land on the native terrain collider.");
            Vector3 landed=viewer.transform.position;
            for(int i=0;i<60;i++){manager.UpdateActiveChunks();motor.Simulate(new CharacterMoveCommand(Vector2.up,false,false,false),1/60f);}
            Check(math.length(((float3)(viewer.transform.position-landed)).xz)>.4f,"Native motor could not walk after landing.");
            Debug.Log($"SCULPT ELEVATED SPAWN PASS: actual native ChunkManager and CharacterMotor fall from 8 m above terrain, land and walk {Vector3.Distance(landed,viewer.transform.position):F2} m with session-only terrain.");
        }
        finally
        {
            cc.enabled=false;viewer.transform.position=original;Object.DestroyImmediate(motor);Object.DestroyImmediate(cc);Physics.SyncTransforms();
        }
    }
}
