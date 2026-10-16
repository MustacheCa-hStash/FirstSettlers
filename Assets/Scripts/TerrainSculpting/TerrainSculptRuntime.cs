using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>One bounded asynchronous transaction/coverage batch. Publication and all Unity
/// objects belong to the calling main thread. No stroke queue or additional worker pool.</summary>
public sealed class TerrainSculptRuntime : IDisposable, ITerrainMovementGuard
{
    private static long nextEpoch;
    private static readonly ProfilerMarker MeshMarker = new ProfilerMarker("FS.Streaming.Sculpt.Mesh");
    private static readonly ProfilerMarker StageMarker = new ProfilerMarker("FS.Streaming.Sculpt.Stage");
    private static readonly ProfilerMarker CookMarker = new ProfilerMarker("FS.Streaming.Sculpt.Cook");
    private static readonly ProfilerMarker CommitMarker = new ProfilerMarker("FS.Streaming.Sculpt.Commit");
    private static readonly ProfilerMarker EditMarker = new ProfilerMarker("FS.Streaming.Sculpt.Edit");
    private static readonly ProfilerMarker CoverageMarker = new ProfilerMarker("FS.Streaming.Sculpt.Coverage");
    private static readonly ProfilerMarker SuppressionMarker=new ProfilerMarker("FS.Streaming.Sculpt.FoliageMask");
    private readonly long epoch = Interlocked.Increment(ref nextEpoch);
    private readonly TerrainDensityWorld world;
    private readonly Func<double,double,double> height;
    private readonly int chunkSize;
    private readonly TerrainRequestManager requests;
    private readonly Func<ChunkCoord,ChunkRuntime> findRuntime;
    private readonly Func<double3,bool> destinationReady;
    private readonly Func<bool> hostReady;
    private readonly Transform viewer;
    private readonly long derivedLimit;
    private readonly long consumerCacheLimit;
    private readonly double publicationMs;
    private readonly bool ownsWorld;
    private readonly TerrainSculptSurfaceSettings surfaceAppearance;
    private readonly object resultGate = new object();
    private readonly ManualResetEventSlim drained = new ManualResetEventSlim(true);
    private readonly List<TerrainSculptPin> pins = new List<TerrainSculptPin>();
    private readonly Dictionary<ChunkCoord,TerrainSculptChunkSurface> live = new Dictionary<ChunkCoord,TerrainSculptChunkSurface>();
    private readonly Dictionary<ChunkCoord,TerrainSculptChunkSurface> staged = new Dictionary<ChunkCoord,TerrainSculptChunkSurface>();
    private readonly List<ChunkCoord> stagingOrder = new List<ChunkCoord>();
    private List<TerrainSculptGroup> groups = new List<TerrainSculptGroup>();
    private TerrainSculptMeshCache meshCache;
    private TerrainSculptBatch completed, preparing;
    private CancellationTokenSource cancellation;
    private TerrainDensityBrush? pendingBrush;
    private double3 anchor;
    private double3 publishedAnchor;
    private bool hasAnchor, disposed, dirty = true, workerRunning, buildWithoutCache;
    private long coverageRevision, requestIdentity, liveCharge, reservedCharge;
    private long failedDensityRevision=-1,failedCoverageRevision=-1;
    private long editStarted;
    private int stagingCursor;
    private double3? destination;
    public TerrainDensityWorld Density => world;
    public TerrainSkyAccess SkyAccess { get; }
    public TerrainSculptSuppression Suppression { get; private set; }
    public long SuppressionBytes=>Suppression?.PayloadCharge??0;
    public bool IsDisposed => disposed;
    public TerrainEditPersistence Persistence { get; }
    public long PublishedDensityRevision { get; private set; }
    public long PublishedCoverageRevision { get; private set; }
    public int StaleDiscards { get; private set; }
    public int CommittedEdits { get; private set; }
    public int LiveChunkCount => live.Count;
    public int ActiveJobs { get { lock(resultGate)return workerRunning?1:0; } }
    public long DerivedReservedBytes => reservedCharge+(SkyAccess!=null&&!SkyAccess.Allocated?TerrainSkyAccess.ResidentCharge:0);
    public long DerivedLiveBytes => liveCharge+(SkyAccess?.Allocated==true?TerrainSkyAccess.ResidentCharge:0);
    public int PreparedGroupCount { get; private set; }
    public int RetainedGroupCount { get; private set; }
    public int LiveRenderTriangles { get; private set; }
    public int LiveCollisionTriangles { get; private set; }
    public double LastWorkerMs { get; private set; }
    public double LastMeshStageMs { get; private set; }
    public double LastCookMs { get; private set; }
    public double LastCommitMs { get; private set; }
    public double LastEditMs { get; private set; }
    public int LastExtractedBricks { get; private set; }
    public int LastReusedBricks { get; private set; }
    public int CachePressureRetries { get; private set; }
    public double LastExtractMs { get; private set; }
    public double LastAssemblyMs { get; private set; }
    public string Status { get; private set; } = "Ready";
    private bool HasOperation
    { get { lock(resultGate)return pendingBrush.HasValue||workerRunning||completed!=null||preparing!=null||destination.HasValue; } }
    public bool Busy => !disposed && (Persistence != null && !Persistence.Ready && !Persistence.FailedToLoad || dirty || HasOperation);
    public double3? PendingDestination => destination;
    public double3 CoverageAnchor => anchor;
    public IEnumerable<ChunkCoord> PendingSourceChunks => preparing==null ? Array.Empty<ChunkCoord>() : (IEnumerable<ChunkCoord>)preparing.Chunks.Keys;
    public IEnumerable<ChunkCoord> PendingRenderChunks
    { get { if(preparing!=null)foreach(var pair in preparing.Chunks)if(pair.Value.Render!=null)yield return pair.Key; } }
    public IEnumerable<ChunkCoord> RenderChunks
    { get { foreach(var pair in live)if(pair.Value.HasRender)yield return pair.Key; } }
    public IReadOnlyList<TerrainSculptGroup> Groups => groups.AsReadOnly();
    public event Action<long,TerrainDensityBrush> TerrainEditCommitted;

    public TerrainSculptRuntime(TerrainDensityWorld world, Func<double,double,double> baselineHeight, int chunkSize,
        TerrainRequestManager requests, Func<ChunkCoord,ChunkRuntime> findRuntime, Transform viewer,
        TerrainSculptConfiguration configuration, bool ownsWorld = false, Func<double3,bool> destinationReady = null, Func<bool> hostReady = null,
        TerrainEditPersistence persistence = null)
    {
        this.world=world??throw new ArgumentNullException(nameof(world));height=baselineHeight??throw new ArgumentNullException(nameof(baselineHeight));
        if(chunkSize<=0||chunkSize%16!=0)throw new ArgumentException("Source chunks must align to 16-cell columns.");
        this.chunkSize=chunkSize;this.requests=requests??throw new ArgumentNullException(nameof(requests));
        this.findRuntime=findRuntime??throw new ArgumentNullException(nameof(findRuntime));this.viewer=viewer;
        if(viewer==null)throw new ArgumentNullException(nameof(viewer));
        if(configuration.DerivedBytes<=0||!float.IsFinite(configuration.PublicationMs)||configuration.PublicationMs<0)
            throw new ArgumentException("Positive derived budget and finite publication allowance required.");
        derivedLimit=configuration.DerivedBytes;consumerCacheLimit=configuration.DerivedBytes/8;publicationMs=configuration.PublicationMs;this.ownsWorld=ownsWorld;
        this.destinationReady=destinationReady;
        this.hostReady=hostReady;
        Persistence=persistence;
        surfaceAppearance=configuration.SurfaceAppearance!=null?configuration.SurfaceAppearance:Resources.Load<TerrainSculptSurfaceSettings>("TerrainSculpting/ExcavatedStone");
        var skySettings=configuration.SkyAccess!=null?configuration.SkyAccess:Resources.Load<TerrainSkyAccessSettings>("TerrainSculpting/CaveLighting");
        // Reserve lighting inside the existing derived cap, including readable Unity
        // copies/GPU storage and bounded tracing scratch. Tiny synthetic hosts opt out.
        if(skySettings!=null&&derivedLimit>=TerrainSkyAccess.ResidentCharge+8L*1024*1024)
        {SkyAccess=new TerrainSkyAccess(world,height,requests,skySettings);derivedLimit-=TerrainSkyAccess.ResidentCharge;}
        PublishedDensityRevision=world.Revision;PublishedCoverageRevision=-1;
    }

    public bool TryExcavate(TerrainDensityBrush brush)
    {
        if(disposed||HasOperation||Persistence!=null&&!Persistence.CanExcavate)return false;
        brush.Validate();pendingBrush=brush;dirty=true;editStarted=Stopwatch.GetTimestamp();Status="Preparing excavation";return true;
    }

    /// <summary>Prepares destination terrain before moving the local viewer in the final swap.
    /// Direct transform teleports bypass this contract and must not be used by gameplay.</summary>
    public bool RequestTeleport(double3 target)
    {
        if(disposed||HasOperation||Persistence!=null&&!Persistence.Ready||!math.all(math.isfinite(target)))return false;
        destination=target;InvalidateCoverage();Status="Preparing destination";return true;
    }
    public void CancelTeleport()
    { if(!destination.HasValue)return;destination=null;InvalidateCoverage(); }

    public IDisposable PinCollision(TerrainSculptPin pin)
    {
        if(disposed)throw new ObjectDisposedException(nameof(TerrainSculptRuntime));
        pins.Add(pin);InvalidateCoverage();return new PinLease(this,pin);
    }
    private sealed class PinLease : IDisposable
    {
        private TerrainSculptRuntime owner; private readonly TerrainSculptPin pin;
        internal PinLease(TerrainSculptRuntime owner,TerrainSculptPin pin){this.owner=owner;this.pin=pin;}
        public void Dispose(){var old=Interlocked.Exchange(ref owner,null);if(old==null||old.disposed)return;old.pins.Remove(pin);old.InvalidateCoverage();}
    }
    private void InvalidateCoverage()
    { coverageRevision++;dirty=true;cancellation?.Cancel(); }
    public bool OwnsRuntime(ChunkCoord coord) => live.ContainsKey(coord) || (preparing!=null&&preparing.Chunks.ContainsKey(coord));
    public bool OwnsRenderIn(FarTerrainPatchKey patch)
    {
        int x0=patch.Origin.x*patch.SizeInChunks,z0=patch.Origin.z*patch.SizeInChunks;
        foreach(var pair in live)if(pair.Value.HasRender&&pair.Key.x>=x0&&pair.Key.x<x0+patch.SizeInChunks&&pair.Key.z>=z0&&pair.Key.z<z0+patch.SizeInChunks)return true;
        return false;
    }
    public void RejectPreparation(string reason)
    {
        if(preparing==null)return;
        DiscardPrepared();pendingBrush=null;destination=null;dirty=false;Status=reason;
        failedDensityRevision=world.Revision;failedCoverageRevision=coverageRevision;
    }

    public void Update(WorldStreamingBudget shared = null)
    {
        UpdateTerrain(shared);
        if(!disposed)SkyAccess?.Update((float3)viewer.position,PublishedDensityRevision,Busy,shared==null||shared.HasRemaining);
    }
    private void UpdateTerrain(WorldStreamingBudget shared)
    {
        if(disposed)return;
        Persistence?.Update();
        if(Persistence!=null&&!Persistence.Ready){dirty=false;Status=Persistence.Status;return;}
        double3 occupied=(float3)viewer.position,focus=destination??occupied;
        if(!hasAnchor||TerrainSculptCoveragePolicy.MoveAnchor(focus,anchor,world.Profile))
        { anchor=TerrainSculptCoveragePolicy.AnchorFor(focus,world.Profile);hasAnchor=true;InvalidateCoverage(); }
        if(world.Revision!=PublishedDensityRevision&&(failedDensityRevision!=world.Revision||failedCoverageRevision!=coverageRevision))dirty=true;
        long started=Stopwatch.GetTimestamp();
        bool madeProgress=false;
        bool CanWork() => (shared==null||shared.HasRemaining) &&
            (!madeProgress||publicationMs<=0||(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency<publicationMs);
        if(!CanWork())return;
        using var scope=StageMarker.Auto();
        TerrainSculptBatch result=null;
        lock(resultGate)if(!workerRunning&&completed!=null){result=completed;completed=null;}
        if(result!=null)
        {
            reservedCharge=0;
            if(!Current(result)) { StaleDiscards++;result.Dispose();dirty=true; }
            else if(result.Error!=null)
            {
                bool cancelled=result.Error is OperationCanceledException;
                bool reclaimCache=result.Error is TerrainDensityCapacityException&&!buildWithoutCache;
                Status=cancelled?"Coverage changed; retrying":result.Error.Message;
                result.Dispose();dirty=cancelled||reclaimCache;
                if(reclaimCache)
                {
                    // Cached CPU extraction is optional. Release it only after the failed
                    // worker has drained; keep the published render/collision and pending
                    // request intact. The bounded retry prioritizes collision coverage over
                    // distant render-only edits and does not retain a successor cache.
                    liveCharge-=meshCache?.Charge??0;meshCache=null;buildWithoutCache=true;CachePressureRetries++;
                    Status="Reclaiming terrain cache; prioritizing nearby collision";
                }
                else if(!cancelled){pendingBrush=null;destination=null;failedDensityRevision=world.Revision;failedCoverageRevision=coverageRevision;}
            }
            else
            {
                LastWorkerMs=result.WorkerMs;LastMeshStageMs=0;LastCookMs=0;
                LastExtractedBricks=result.ExtractedBricks;LastReusedBricks=result.ReusedBricks;
                LastExtractMs=result.ExtractMs;LastAssemblyMs=result.AssemblyMs;
                preparing=result;reservedCharge=result.Charge;stagingCursor=0;stagingOrder.Clear();
                stagingOrder.AddRange(result.Chunks.Keys);Status="Staging terrain";
            }
        }
        if(preparing!=null&&!Current(preparing)) { DiscardPrepared();StaleDiscards++;dirty=true; }
        if(preparing!=null)
        {
            // No replacement assets are allocated until every source runtime exists.
            foreach(var coord in stagingOrder)
            {
                var runtime=findRuntime(coord);
                if(runtime==null||runtime.Root==null||!runtime.ChunkRecord.HasTerrainData)
                { Status="Waiting for source terrain";return; }
            }
            // At least one indivisible operation per admitted frame; later work honours the soft allowance.
            bool didWork=false;
            try
            {
                while(stagingCursor<stagingOrder.Count&&(!didWork||CanWork()))
                {
                    var coord=stagingOrder[stagingCursor];
                    if(!staged.TryGetValue(coord,out var surface))
                    {
                        long operationStarted=Stopwatch.GetTimestamp();
                        surface=new TerrainSculptChunkSurface(coord,preparing.Chunks[coord],world.Profile,chunkSize,surfaceAppearance);
                        LastMeshStageMs+=(Stopwatch.GetTimestamp()-operationStarted)*1000d/Stopwatch.Frequency;
                        staged.Add(coord,surface);didWork=true;madeProgress=true;
                        if(!CanWork())break;
                    }
                    if(!surface.CollisionPrepared)
                    {
                        using var admission=requests.TryAcquireSculptCooking();
                        if(admission==null)return;
                        long operationStarted=Stopwatch.GetTimestamp();
                        using(CookMarker.Auto())surface.PrepareCollision();didWork=true;madeProgress=true;
                        LastCookMs+=(Stopwatch.GetTimestamp()-operationStarted)*1000d/Stopwatch.Frequency;
                    }
                    stagingCursor++;
                }
                if(stagingCursor==stagingOrder.Count&&(!didWork||CanWork()))
                {
                    if(hostReady!=null&&!hostReady()){Status="Waiting for complete terrain handoff";return;}
                    if(destination.HasValue&&destinationReady!=null&&!destinationReady(destination.Value))
                    {Status="Waiting for destination collision";return;}
                    Publish();
                }
            }
            catch(Exception ex)
            {
                Status="Terrain staging failed: "+ex.Message;DiscardPrepared();pendingBrush=null;destination=null;dirty=false;
                failedDensityRevision=world.Revision;failedCoverageRevision=coverageRevision;
                Debug.LogException(ex);
            }
            return;
        }
        if(!dirty||ActiveJobs!=0||!CanWork())return;
        if(world.ModifiedBrickCount==0&&!pendingBrush.HasValue&&!destination.HasValue)
        { dirty=false;PublishedDensityRevision=world.Revision;PublishedCoverageRevision=coverageRevision;Status="Ready";return; }
        if(SkyAccess!=null&&!SkyAccess.YieldToTerrain()){Status="Waiting for sky worker to yield";return;}
        Dispatch(focus,occupied);
    }

    private bool Current(TerrainSculptBatch batch) => !disposed&&batch.Epoch==epoch&&batch.Request==requestIdentity&&
        batch.Coverage==coverageRevision&&batch.BaseRevision==world.Revision;

    private void Dispatch(double3 focus,double3 occupied)
    {
        long capacity=derivedLimit-liveCharge;
        if(capacity<=0){Status="Derived terrain budget is full";dirty=false;pendingBrush=null;destination=null;return;}
        cancellation?.Dispose();cancellation=new CancellationTokenSource();
        var token=cancellation.Token;var brush=pendingBrush;
        var batch=new TerrainSculptBatch{Epoch=epoch,Request=++requestIdentity,Coverage=coverageRevision,BaseRevision=world.Revision,Anchor=anchor};
        var capturedPins=new List<TerrainSculptPin>(pins)
        { new TerrainSculptPin(occupied-new double3(1,1,1),occupied+new double3(1,3,1)) };
        var oldGroups=new List<TerrainSculptGroup>(groups);
        var oldChunks=new HashSet<ChunkCoord>(live.Keys);
        double3 oldAnchor=publishedAnchor;long oldDensityRevision=PublishedDensityRevision;
        var oldCache=meshCache;
        long cacheLimit=buildWithoutCache?0:consumerCacheLimit;
        bool prioritizeCollision=buildWithoutCache;
        var oldSuppression=Suppression;
        lock(resultGate){workerRunning=true;drained.Reset();}
        reservedCharge=capacity;
        bool submitted;
        try
        {
            submitted=requests.TryRunSculpt(()=>
            {
                long workerStarted=Stopwatch.GetTimestamp();
                try
                {
                    using var marker=MeshMarker.Auto();token.ThrowIfCancellationRequested();
                    if(brush.HasValue)using(EditMarker.Auto())batch.Edit=world.StageExcavation(brush.Value);
                    using var snapshot=batch.Edit!=null?world.CaptureMeshingSnapshot(batch.Edit):world.CaptureSnapshot();
                    var budget=new TerrainSculptBuildBudget(capacity,token);
                    using(SuppressionMarker.Auto())batch.Suppression=TerrainSculptSuppression.Build(snapshot,batch.Edit,oldSuppression,budget,consumerCacheLimit,height);
                    using(CoverageMarker.Auto())TerrainSculptBatchBuilder.Build(batch,snapshot,height,chunkSize,focus,oldGroups,capturedPins,budget,oldAnchor,oldDensityRevision,oldChunks,oldCache,cacheLimit,prioritizeCollision);
                    token.ThrowIfCancellationRequested();
                }
                catch(Exception ex){batch.Error=ex;}
                finally
                {
                    batch.WorkerMs=(Stopwatch.GetTimestamp()-workerStarted)*1000d/Stopwatch.Frequency;
                    lock(resultGate)
                    {
                        if(disposed)batch.Dispose();else completed=batch;
                        workerRunning=false;drained.Set();
                    }
                }
            });
        }
        catch { lock(resultGate){workerRunning=false;drained.Set();}reservedCharge=0;batch.Dispose();throw; }
        if(!submitted)
        { lock(resultGate){workerRunning=false;drained.Set();}reservedCharge=0;batch.Dispose();Status="Waiting for shared mesh worker";return; }
        dirty=false;
    }

    private void Publish()
    {
        if(!Current(preparing))return;
        long commitStarted=Stopwatch.GetTimestamp();
        using var scope=CommitMarker.Auto();
        var touched=new Dictionary<ChunkRuntime,TerrainSculptChunkSurface>();
        foreach(var pair in live) if(!preparing.Reuse.Contains(pair.Key)&&pair.Value.BoundRuntime!=null)touched[pair.Value.BoundRuntime]=pair.Value;
        foreach(var coord in stagingOrder)
        {
            var runtime=findRuntime(coord);
            if(runtime==null)return;
            if(!touched.ContainsKey(runtime))touched.Add(runtime,null);
        }
        // Unity attachment can fail. Density is committed only after every attachment succeeds;
        // rollback restores the complete old coverage without yielding a render/physics frame.
        try
        {
            foreach(var pair in touched)pair.Key.SetSculptSurface(null);
            foreach(var pair in staged)findRuntime(pair.Key).SetSculptSurface(pair.Value);
            if(preparing.Edit!=null&&!world.TryCommit(preparing.Edit))throw new InvalidOperationException("Density changed during terrain publication.");
        }
        catch
        {
            foreach(var pair in touched)pair.Key.SetSculptSurface(pair.Value);
            throw;
        }
        bool edited=preparing.Edit!=null;TerrainDensityBrush brush=edited?preparing.Edit.Brush:default;
        var retired=new List<ChunkCoord>();
        foreach(var pair in live)if(!preparing.Reuse.Contains(pair.Key)){pair.Value.Dispose();retired.Add(pair.Key);}
        foreach(var coord in retired)live.Remove(coord);
        foreach(var pair in staged)live.Add(pair.Key,pair.Value);staged.Clear();
        groups=preparing.Groups;meshCache=preparing.MeshCache;buildWithoutCache=false;Suppression=preparing.Suppression;liveCharge=(meshCache?.Charge??0)+SuppressionBytes;
        LiveRenderTriangles=0;LiveCollisionTriangles=0;
        foreach(var surface in live.Values)
        {liveCharge+=surface.PayloadBytes*2+4096;LiveRenderTriangles+=surface.RenderTriangles;LiveCollisionTriangles+=surface.CollisionTriangles;}
        PreparedGroupCount=0;RetainedGroupCount=0;
        foreach(var group in groups){if(group.Prepared)PreparedGroupCount++;if(group.Retained)RetainedGroupCount++;}
        PublishedDensityRevision=world.Revision;PublishedCoverageRevision=preparing.Coverage;
        publishedAnchor=preparing.Anchor;
        // Release caller-owned CPU arrays once Unity has copied/cooked them.
        preparing.Groups=null;preparing.Dispose();preparing=null;reservedCharge=0;pendingBrush=null;dirty=false;Status="Ready";
        if(destination.HasValue)
        {
            var controller=viewer.GetComponent<CharacterController>();bool enabled=controller!=null&&controller.enabled;
            if(enabled)controller.enabled=false;
            viewer.position=(Vector3)(float3)destination.Value;
            if(enabled)controller.enabled=true;
            destination=null;
            InvalidateCoverage(); // Re-evaluate the old occupied collision pin after the move.
        }
        Physics.SyncTransforms();
        LastCommitMs=(Stopwatch.GetTimestamp()-commitStarted)*1000d/Stopwatch.Frequency;
        if(edited)
        {
            LastEditMs=(Stopwatch.GetTimestamp()-editStarted)*1000d/Stopwatch.Frequency;
            CommittedEdits++;
            // Consumer failure cannot undo terrain already committed. P6 adds subscribers here.
            if(TerrainEditCommitted!=null)foreach(Action<long,TerrainDensityBrush> subscriber in TerrainEditCommitted.GetInvocationList())
                try{subscriber(PublishedDensityRevision,brush);}catch(Exception ex){Debug.LogException(ex);}
        }
    }

    private void DiscardPrepared()
    {
        foreach(var surface in staged.Values)surface.Dispose();staged.Clear();
        preparing?.Dispose();preparing=null;stagingOrder.Clear();stagingCursor=0;reservedCharge=0;
    }

    public bool CanMove(double3 from,double3 to,double radius,double height)
    {
        if(disposed)return false;
        if(Persistence!=null&&!Persistence.Ready)return false;
        if(world.Revision!=PublishedDensityRevision)return false;
        if(!math.all(math.isfinite(from))||!math.all(math.isfinite(to)))return false;
        double3 min=math.min(from,to)-new double3(radius,1,radius);
        double3 max=math.max(from,to)+new double3(radius,height,radius);
        var visual=new TerrainSculptCoveragePolicy(publishedAnchor);
        foreach(var group in groups)
        {
            if(math.length(math.max(math.max(group.Minimum-max,min-group.Maximum),0))>TerrainSculptCoveragePolicy.CollisionRadius)continue;
            if(visual.Weight(to)<1||visual.Weight(to+new double3(0,height,0))<1)return false;
            if(!group.Collision||PublishedDensityRevision!=world.Revision)return false;
            // Pressure recovery may leave distant edits on the baseline renderer. Allow
            // approach through air, but never enter the edited volume before its visible
            // representation is ready. The occupied pin then forces that render on retry.
            if(!group.Render&&math.all(min<=group.Maximum)&&math.all(max>=group.Minimum))return false;
            int x0=TerrainDensityCoordinates.FloorDiv(TerrainDensityCoordinates.FloorIndex(group.Footprint.Min.x),chunkSize);
            int z0=TerrainDensityCoordinates.FloorDiv(TerrainDensityCoordinates.FloorIndex(group.Footprint.Min.y),chunkSize);
            int x1=TerrainDensityCoordinates.FloorDiv(group.Footprint.Max.x-1,chunkSize),z1=TerrainDensityCoordinates.FloorDiv(group.Footprint.Max.y-1,chunkSize);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                if(!live.TryGetValue(new ChunkCoord(x,z),out var surface)||!surface.HasCollision||surface.BoundRuntime==null)return false;
        }
        // Before the first reconstruction of restored data, all modified destinations are unready.
        if(world.ModifiedBrickCount>0&&PublishedCoverageRevision<0)return false;
        return true;
    }

    public void Dispose()
    {
        lock(resultGate){if(disposed)return;disposed=true;}
        cancellation?.Cancel();drained.Wait();
        SkyAccess?.Dispose();
        lock(resultGate){completed?.Dispose();completed=null;}
        DiscardPrepared();foreach(var surface in live.Values)surface.Dispose();live.Clear();groups.Clear();pins.Clear();meshCache=null;
        Suppression=null;liveCharge=reservedCharge=0;cancellation?.Dispose();drained.Dispose();
        // Pending batches have been cancelled; only the last published density may flush.
        Persistence?.Dispose();
        if(ownsWorld)world.Dispose();
    }
}
