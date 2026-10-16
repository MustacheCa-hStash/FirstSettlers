using System;
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>A merged XZ footprint with authoritative edited bounds. Never persisted as mesh data.</summary>
public sealed class TerrainSculptGroup
{
    public TerrainSculptFootprint Footprint { get; internal set; }
    public double3 Minimum { get; internal set; }
    public double3 Maximum { get; internal set; }
    public bool Render { get; internal set; }
    public bool Collision { get; internal set; }
    public bool Prepared { get; internal set; }
    public bool Retained { get; internal set; }
    internal long ContentRevision;
    internal int VisualClass;
}

internal sealed class TerrainSculptChunkBuild
{
    internal TerrainSculptMeshData Render, Collision;
}

internal sealed class TerrainSculptBatch : IDisposable
{
    internal long Epoch, Request, Coverage, BaseRevision, Charge;
    internal double WorkerMs;
    internal double3 Anchor;
    internal TerrainDensityEdit Edit;
    internal List<TerrainSculptGroup> Groups;
    internal readonly Dictionary<ChunkCoord, TerrainSculptChunkBuild> Chunks = new Dictionary<ChunkCoord, TerrainSculptChunkBuild>();
    internal readonly HashSet<ChunkCoord> Reuse = new HashSet<ChunkCoord>();
    internal Exception Error;
    internal TerrainSculptMeshCache MeshCache;
    internal TerrainSculptSuppression Suppression;
    internal int ExtractedBricks,ReusedBricks;
    internal double ExtractMs,AssemblyMs;
    public void Dispose() { Edit?.Dispose(); Edit = null; Chunks.Clear(); Reuse.Clear(); Groups = null; MeshCache=null;Suppression=null; }
}

/// <summary>Producer-thread batch planning and extraction. No Unity objects are read here.</summary>
internal static class TerrainSculptBatchBuilder
{
    private static bool Overlap(TerrainSculptFootprint a, TerrainSculptFootprint b) =>
        math.all(a.Min <= b.Max) && math.all(b.Min <= a.Max);

    internal static List<TerrainSculptGroup> Group(TerrainDensitySnapshot snapshot)
    {
        var result = new List<TerrainSculptGroup>();
        var keys = new List<int3>(snapshot.Bricks.Keys);
        keys.Sort((a,b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z != b.z ? a.z.CompareTo(b.z) : a.y.CompareTo(b.y));
        foreach (var key in keys)
        {
            int3 origin = TerrainDensityCoordinates.Origin(key);
            var group = new TerrainSculptGroup
            {
                Footprint = new TerrainSculptFootprint(origin.xz - 16, origin.xz + 32),
                Minimum = (double3)origin * snapshot.Profile.Spacing,
                Maximum = (double3)(origin + 16) * snapshot.Profile.Spacing
            };
            // A union can intersect a previously skipped rectangle; restart after every merge.
            for (int i = 0; i < result.Count;)
            {
                var old = result[i];
                if (!Overlap(group.Footprint,old.Footprint)) { i++; continue; }
                group.Footprint = new TerrainSculptFootprint(math.min(group.Footprint.Min,old.Footprint.Min),math.max(group.Footprint.Max,old.Footprint.Max));
                group.Minimum = math.min(group.Minimum,old.Minimum); group.Maximum = math.max(group.Maximum,old.Maximum);
                result.RemoveAt(i); i = 0;
            }
            result.Add(group);
        }
        result.Sort((a,b) => a.Footprint.Min.x != b.Footprint.Min.x ? a.Footprint.Min.x.CompareTo(b.Footprint.Min.x) : a.Footprint.Min.y.CompareTo(b.Footprint.Min.y));
        return result;
    }

    internal static void Build(TerrainSculptBatch batch, TerrainDensitySnapshot snapshot, Func<double,double,double> height,
        int chunkSize, double3 focus, IReadOnlyList<TerrainSculptGroup> previous, IReadOnlyList<TerrainSculptPin> pins,
        TerrainSculptBuildBudget budget, double3 previousAnchor, long previousDensityRevision, ISet<ChunkCoord> previousChunks,
        TerrainSculptMeshCache previousCache=null,long cacheLimit=16L*1024*1024,bool prioritizeCollision=false)
    {
        var policy = new TerrainSculptCoveragePolicy(batch.Anchor);
        var visual = new TerrainSculptVisualField(snapshot,policy);
        long assemblyStarted=System.Diagnostics.Stopwatch.GetTimestamp();
        var cache=new TerrainSculptMeshCache.Builder(previousCache,snapshot,budget,cacheLimit);
        var heightCache=new HeightCache(height,snapshot.Profile.Spacing,budget);height=heightCache.Read;
        batch.Groups = Group(snapshot);
        var chunkCoords = new HashSet<ChunkCoord>();
        double s = snapshot.Profile.Spacing;
        foreach (var group in batch.Groups)
        {
            budget.Check();
            bool wasPrepared = false, wasCollision = false;
            TerrainSculptGroup exactOld=null;
            if (previous != null) foreach (var old in previous) if (Overlap(group.Footprint,old.Footprint))
            {
                wasPrepared |= old.Retained; wasCollision |= old.Collision;
                if(SameFootprint(group,old))exactOld=old;
            }
            bool changed=snapshot.Revision!=previousDensityRevision||exactOld==null;
            if(batch.Edit!=null)foreach(var key in batch.Edit.ChangedBricks)
            {
                int2 p=TerrainDensityCoordinates.Origin(key).xz;
                if(math.all(p<group.Footprint.Max)&&math.all(p+16>group.Footprint.Min))changed=true;
            }
            group.ContentRevision=changed?batch.Request:exactOld.ContentRevision;
            group.Prepared = policy.Prepare(group.Minimum,group.Maximum);
            group.Retained = group.Prepared || (wasPrepared && policy.Retain(group.Minimum,group.Maximum));
            group.Render = group.Retained && policy.Visible(group.Minimum,group.Maximum);
            double distance = TerrainSculptCoveragePolicy.Distance(focus,group.Minimum,group.Maximum);
            group.Collision = distance <= TerrainSculptCoveragePolicy.CollisionPrepare ||
                (wasCollision && distance <= TerrainSculptCoveragePolicy.CollisionRelease);
            if (pins != null) foreach (var pin in pins)
                if (math.all(pin.Minimum <= group.Maximum) && math.all(pin.Maximum >= group.Minimum)) group.Collision = true;
            // Under successor-memory pressure, prepare collision first and retain edited
            // visuals within 16 m of the player/target or any occupied/support pin.
            bool pressureRender=distance<=TerrainSculptCoveragePolicy.PressureRenderRadius;
            if(prioritizeCollision&&pins!=null)foreach(var pin in pins)
                pressureRender|=math.all(pin.Minimum<=group.Maximum)&&math.all(pin.Maximum>=group.Minimum);
            group.Render &= !prioritizeCollision||pressureRender;
            double2 furthest=math.max(math.abs(group.Minimum.xz-policy.Anchor.xz),math.abs(group.Maximum.xz-policy.Anchor.xz));
            double vertical=Math.Max(Math.Abs(group.Minimum.y-policy.Anchor.y),Math.Abs(group.Maximum.y-policy.Anchor.y));
            group.VisualClass=!group.Render?0:math.length(furthest)<=TerrainSculptCoveragePolicy.FullRadius&&vertical<=TerrainSculptCoveragePolicy.FullVertical?1:2;
            if (!group.Render && !group.Collision)
            {
                if(group.Prepared)
                {
                    // Authoritative overrides are already resident in P3. Warm the canonical
                    // height dependencies before the footprint reaches the visible annulus.
                    var prefetch=group.Footprint;
                    for(int z=prefetch.Min.y;z<=prefetch.Max.y;z+=16)for(int x=prefetch.Min.x;x<=prefetch.Max.x;x+=16)
                    {budget.Check();height(x*s,z*s);}
                }
                continue;
            }
            var f = group.Footprint;
            int2 first = new int2(TerrainDensityCoordinates.FloorDiv(f.Min.x,chunkSize),TerrainDensityCoordinates.FloorDiv(f.Min.y,chunkSize));
            int2 last = new int2(TerrainDensityCoordinates.FloorDiv(f.Max.x-1,chunkSize),TerrainDensityCoordinates.FloorDiv(f.Max.y-1,chunkSize));
            for (int z = first.y; z <= last.y; z++) for (int x = first.x; x <= last.x; x++) chunkCoords.Add(new ChunkCoord(x,z));
        }
        foreach(var coord in chunkCoords)
        {
            if(previousChunks==null||!previousChunks.Contains(coord))continue;
            bool same=snapshot.Revision==previousDensityRevision;
            // Density owners cover 16^3 samples. Include the two-cell influence of
            // interpolation and central-difference normals across source-chunk borders.
            if(batch.Edit!=null)foreach(var key in batch.Edit.ChangedBricks)
            {
                int2 p=TerrainDensityCoordinates.Origin(key).xz;
                if(math.all(p-2<new int2(coord.x+1,coord.z+1)*chunkSize)&&
                    math.all(p+17>new int2(coord.x,coord.z)*chunkSize)){same=false;break;}
            }
            if(previous!=null)foreach(var old in previous)
            {
                if(!same)break;
                if(!IntersectsChunk(old,coord,chunkSize)||(!old.Render&&!old.Collision))continue;
                TerrainSculptGroup match=null;
                foreach(var group in batch.Groups)if(EquivalentInChunk(group,old,coord,chunkSize,batch.Anchor,previousAnchor)){match=group;break;}
                if(match==null){same=false;break;}
            }
            if(same)foreach(var group in batch.Groups)
            {
                if(!IntersectsChunk(group,coord,chunkSize)||(!group.Render&&!group.Collision))continue;
                TerrainSculptGroup match=null;
                if(previous!=null)foreach(var old in previous)if(EquivalentInChunk(group,old,coord,chunkSize,batch.Anchor,previousAnchor)){match=old;break;}
                if(match==null){same=false;break;}
            }
            if(same)batch.Reuse.Add(coord);
        }
        var ordered = new List<ChunkCoord>(chunkCoords);
        ordered.Sort((a,b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
        foreach (var coord in ordered)
        {
            if(batch.Reuse.Contains(coord))continue;
            budget.Check();
            long chunkStart=budget.ChargedBytes,chunkPersistent=budget.PersistentBytes;
            var domain = new TerrainSculptFootprint(new int2(coord.x,coord.z)*chunkSize,new int2(coord.x+1,coord.z+1)*chunkSize);
            var render=new Dictionary<TerrainSculptGroup,TerrainSculptMeshData>();
            var collision=new Dictionary<TerrainSculptGroup,TerrainSculptMeshData>();
            foreach(var group in batch.Groups)
            {
                if((!group.Render&&!group.Collision)||!IntersectsChunk(group,coord,chunkSize))continue;
                var f=group.Footprint;
                int2 lo=math.max(f.Min,domain.Min),hi=math.min(f.Max,domain.Max);
                double low=group.Minimum.y,high=group.Maximum.y;
                // Local height envelope; an uphill section far along the cave must not
                // widen the volume sampled for this source chunk.
                for(int z=lo.y;z<=hi.y;z++)for(int x=lo.x;x<=hi.x;x++)
                {
                    budget.Check();double h=height(x*s,z*s);
                    if(!math.isfinite(h))throw new ArgumentException("Nonfinite baseline height.");
                    low=Math.Min(low,h-2*s);high=Math.Max(high,h+2*s);
                }
                int minY=TerrainDensityCoordinates.FloorIndex(low/s)-2,maxY=checked(TerrainDensityCoordinates.FloorIndex(high/s)+3);
                if(group.Render)render.Add(group,TerrainSculptPatchBuilder.BuildPatchCached(group.VisualClass==1?(ITerrainDensityField)snapshot:visual,f,minY,maxY,height,1,budget,cache,domain));
                if(group.Collision)collision.Add(group,TerrainSculptPatchBuilder.BuildPatchCached(snapshot,f,minY,maxY,height,8,budget,cache,domain));
            }
            var output = new TerrainSculptChunkBuild();
            output.Render = BuildChunk(snapshot.Profile,domain,height,render,1,budget);
            output.Collision = BuildChunk(snapshot.Profile,domain,height,collision,8,budget);
            batch.Chunks.Add(coord,output);
            budget.CompleteScope(chunkStart,((output.Render?.PayloadBytes??0)+(output.Collision?.PayloadBytes??0))*2,chunkPersistent);
        }
        batch.MeshCache=chunkCoords.Count==0?null:cache.Finish();batch.ExtractedBricks=cache.Built;batch.ReusedBricks=cache.Reused;batch.ExtractMs=cache.ExtractMs;
        batch.AssemblyMs=(System.Diagnostics.Stopwatch.GetTimestamp()-assemblyStarted)*1000d/System.Diagnostics.Stopwatch.Frequency-cache.ExtractMs;
        batch.Charge = budget.PeakBytes;
    }

    private static bool SameFootprint(TerrainSculptGroup a,TerrainSculptGroup b) => math.all(a.Footprint.Min==b.Footprint.Min)&&math.all(a.Footprint.Max==b.Footprint.Max);
    private static bool IntersectsChunk(TerrainSculptGroup g,ChunkCoord c,int size) =>
        math.all(g.Footprint.Min<new int2(c.x+1,c.z+1)*size)&&math.all(g.Footprint.Max>new int2(c.x,c.z)*size);
    private static bool EquivalentInChunk(TerrainSculptGroup a,TerrainSculptGroup b,ChunkCoord coord,int size,double3 anchor,double3 oldAnchor)
    {
        if(a.Render!=b.Render||a.Collision!=b.Collision||a.VisualClass!=b.VisualClass||
            a.VisualClass==2&&!math.all(anchor==oldAnchor))return false;
        int2 lo=new int2(coord.x,coord.z)*size,hi=lo+size;
        if(!math.all(math.max(a.Footprint.Min,lo)==math.max(b.Footprint.Min,lo))||
            !math.all(math.min(a.Footprint.Max,hi)==math.min(b.Footprint.Max,hi)))return false;
        // An outside seam becoming an interior chunk border changes its collar topology.
        return math.all((a.Footprint.Min>=lo)==(b.Footprint.Min>=lo))&&
            math.all((a.Footprint.Max<=hi)==(b.Footprint.Max<=hi));
    }

    private static TerrainSculptMeshData BuildChunk(TerrainDensityProfile profile,TerrainSculptFootprint domain,
        Func<double,double,double> height,Dictionary<TerrainSculptGroup,TerrainSculptMeshData> pieces,int step,TerrainSculptBuildBudget budget)
    {
        long scopeStart=budget.ChargedBytes;
        long persistentStart=budget.PersistentBytes;
        var holes = new List<TerrainSculptFootprint>();
        foreach (var pair in pieces)
        {
            var f = pair.Key.Footprint;
            if (math.all(f.Min < domain.Max) && math.all(f.Max > domain.Min))
                holes.Add(new TerrainSculptFootprint(math.max(f.Min,domain.Min),math.min(f.Max,domain.Max)));
        }
        if (holes.Count == 0) return null;
        var baseline = TerrainSculptPatchBuilder.BuildBaseline(profile,domain,height,step,holes,budget);
        int vertexCount=baseline.Positions.Length,indexCount=baseline.Triangles.Length;
        foreach(var piece in pieces.Values)
        {vertexCount=checked(vertexCount+piece.Positions.Length);indexCount=checked(indexCount+piece.Triangles.Length);}
        if(vertexCount>500000||indexCount>3000000)throw new TerrainDensityCapacityException();
        // The source window is already clipped and counts are known: exact concatenation
        // avoids growing/copying large intermediate lists and normalizing every vertex again.
        long bytes=(long)vertexCount*(step==1?44:36)+(long)indexCount*4;budget.Charge(bytes*2);
        var positions=new double3[vertexCount];var normals=new float3[vertexCount];var triangles=new int[indexCount];
        int vertex=0,index=0;
        void Append(TerrainSculptMeshData piece)
        {
            Array.Copy(piece.Positions,0,positions,vertex,piece.Positions.Length);
            Array.Copy(piece.Normals,0,normals,vertex,piece.Normals.Length);
            foreach(int i in piece.Triangles)triangles[index++]=i+vertex;
            vertex+=piece.Positions.Length;
        }
        Append(baseline);foreach(var piece in pieces.Values)Append(piece);
        float2[] surfaceData=null;
        if(step==1)
        {
            surfaceData=new float2[vertexCount];
            for(int i=0;i<vertexCount;i++)surfaceData[i]=new float2(TerrainSculptSurfaceSettings.Exposure(height(positions[i].x,positions[i].z)-positions[i].y,profile.Spacing),0);
        }
        var result=new TerrainSculptMeshData(positions,normals,triangles,0,0,surfaceData);
        budget.CompleteScope(scopeStart,result.PayloadBytes*2,persistentStart);return result;
    }

    private sealed class HeightCache
    {
        private readonly Func<double,double,double> source;
        private readonly double spacing;
        private readonly TerrainSculptBuildBudget budget;
        private readonly Dictionary<int2,double> values=new Dictionary<int2,double>();
        internal HeightCache(Func<double,double,double> source,double spacing,TerrainSculptBuildBudget budget)
        {this.source=source;this.spacing=spacing;this.budget=budget;}
        internal double Read(double x,double z)
        {
            double gx=x/spacing,gz=z/spacing;
            int ix=TerrainDensityCoordinates.FloorIndex(gx),iz=TerrainDensityCoordinates.FloorIndex(gz);
            double fx=Math.Clamp(gx-ix,0,1),fz=Math.Clamp(gz-iz,0,1);
            double a=Sample(ix,iz);
            if(fx==0&&fz==0)return a;
            if(fx==0)return a+fz*(Sample(ix,iz+1)-a);
            if(fz==0)return a+fx*(Sample(ix+1,iz)-a);
            double c=Sample(ix+1,iz+1);
            return fx>=fz?a+fx*(Sample(ix+1,iz)-a)+fz*(c-Sample(ix+1,iz)):
                a+fx*(c-Sample(ix,iz+1))+fz*(Sample(ix,iz+1)-a);
        }
        private double Sample(int x,int z)
        {
            var key=new int2(x,z);
            if(values.TryGetValue(key,out double value))return value;
            budget.ChargePersistent(64);value=source(key.x*spacing,key.y*spacing);values.Add(key,value);return value;
        }
    }

}

public readonly struct TerrainSculptPin
{
    public readonly double3 Minimum, Maximum;
    public TerrainSculptPin(double3 min,double3 max)
    {
        if(!math.all(math.isfinite(min))||!math.all(math.isfinite(max))||math.any(max<min))throw new ArgumentException("Invalid collision pin bounds.");
        Minimum=min;Maximum=max;
    }
}
