using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct TerrainCaveSettings
{
    public bool enabled;
    [Range(512,2048),Tooltip("World metres per cave planning cell. One independently seeded system can occupy each eligible cell.")] public int cellSize;
    [Range(0,1)] public float probability;
    [Range(240,600),Tooltip("Approximate main route length in metres. Side branches and loops add walking distance.")] public float systemLength;
    [Range(6,12)] public float passageWidth;
    [Range(5,9)] public float passageHeight;
    [Range(20,72)] public float chamberWidth;
    [Range(5,20)] public float minimumCover;
    [Range(128,512),Tooltip("Derived terrain cap in MiB when caves are enabled, including old/new geometry and sky-cache reservation. Edit density retains its separate cap.")] public int derivedMemoryMiB;
    public static TerrainCaveSettings Default=>new TerrainCaveSettings{enabled=false,cellSize=768,probability=.65f,systemLength=420,passageWidth=8,passageHeight=6,chamberWidth=36,minimumCover=8,derivedMemoryMiB=256};
    public TerrainCaveSettings Sanitized()
    {
        var s=this;if(s.cellSize==0){bool on=s.enabled;s=Default;s.enabled=on;}
        s.cellSize=math.clamp(s.cellSize,512,2048);s.probability=math.isfinite(s.probability)?math.saturate(s.probability):.65f;
        s.systemLength=math.clamp(math.isfinite(s.systemLength)?s.systemLength:420,240,Math.Min(600,s.cellSize-128));
        s.passageWidth=math.clamp(math.isfinite(s.passageWidth)?s.passageWidth:8,6,12);s.passageHeight=math.clamp(math.isfinite(s.passageHeight)?s.passageHeight:6,5,9);
        s.chamberWidth=math.clamp(math.isfinite(s.chamberWidth)?s.chamberWidth:36,20,72);s.minimumCover=math.clamp(math.isfinite(s.minimumCover)?s.minimumCover:8,5,20);
        s.derivedMemoryMiB=math.clamp(s.derivedMemoryMiB,128,512);return s;
    }
}

/// <summary>Bounded immutable world-cell plans. Cold work belongs on a producer, never in a
/// movement query. Point density reads reuse the owning cell without list allocation.</summary>
public sealed class TerrainCavePlanner
{
    public const int Schema=2,Capacity=64;
    private sealed class Entry
    {
        internal readonly TerrainCaveLayout Cave;
        internal readonly TerrainCaveLayout[] Items;
        internal Entry(TerrainCaveLayout cave){Cave=cave;Items=cave==null?Array.Empty<TerrainCaveLayout>():new[]{cave};}
    }
    private readonly ConcurrentDictionary<int2,Lazy<Entry>> entries=new ConcurrentDictionary<int2,Lazy<Entry>>();
    private readonly Queue<int2> order=new Queue<int2>();
    private readonly object gate=new object();
    private readonly int seed;
    private readonly double water;
    private readonly TerrainCaveSettings settings;
    private readonly Func<IReadOnlyList<double2>,double[]> sample;
    private readonly Func<double,double,double> canonicalHeight;
    private readonly Func<double2,WorldRockClass> geology;
    private readonly ITerrainDensityBounds canonicalBounds;
    public int CachedCells=>entries.Count;
    public TerrainCavePlanner(int seed,double water,TerrainCaveSettings settings,Func<IReadOnlyList<double2>,double[]> sample,
        Func<double,double,double> canonicalHeight,Func<double2,WorldRockClass> geology=null)
    {this.seed=seed;this.water=water;this.settings=settings.Sanitized();this.sample=sample??throw new ArgumentNullException(nameof(sample));this.canonicalHeight=canonicalHeight??throw new ArgumentNullException(nameof(canonicalHeight));this.geology=geology;}
    public TerrainCavePlanner(WorldGenerationConfiguration generation,TerrainBaselineSampler surface)
        :this(generation.Seed,generation.Water.SurfaceY,generation.Caves,new NativeSamples(generation).Read,surface.SampleHeight,
            p=>WorldLandforms.RockClass((float2)(p/generation.WorldScale),generation.Seed,generation.SampleScale,generation.Erosion.landforms)){canonicalBounds=surface;}
    private int2 Cell(double2 p)=>new int2(TerrainDensityCoordinates.FloorIndex(p.x/settings.cellSize),TerrainDensityCoordinates.FloorIndex(p.y/settings.cellSize));
    private Entry Get(int2 cell)
    {
        if(!entries.TryGetValue(cell,out var lazy))
        {
            var candidate=new Lazy<Entry>(()=>new Entry(Plan(cell)),LazyThreadSafetyMode.ExecutionAndPublication);
            lazy=entries.GetOrAdd(cell,candidate);
            if(ReferenceEquals(lazy,candidate))lock(gate)
            {
                order.Enqueue(cell);
                while(entries.Count>Capacity&&order.Count>0){var old=order.Dequeue();if(!math.all(old==cell))entries.TryRemove(old,out _);}
            }
        }
        return lazy.Value;
    }
    public IReadOnlyList<TerrainCaveLayout> Query(double3 min,double3 max)
    {
        if(!settings.enabled)return Array.Empty<TerrainCaveLayout>();
        int2 lo=Cell(min.xz),hi=Cell(max.xz);
        if(math.all(lo==hi))return Get(lo).Items;
        if((long)(hi.x-lo.x+1)*(hi.y-lo.y+1)>64)throw new ArgumentException("Cave planning query exceeds 64 cells.");
        var result=new List<TerrainCaveLayout>();
        for(int z=lo.y;z<=hi.y;z++)for(int x=lo.x;x<=hi.x;x++)
        {var cave=Get(new int2(x,z)).Cave;if(cave!=null&&cave.Intersects(min,max))result.Add(cave);}
        return result;
    }
    public double Air(double3 p)
    {
        if(!settings.enabled)return double.NegativeInfinity;
        var cave=Get(Cell(p.xz)).Cave;
        return cave!=null&&cave.Intersects(p-new double3(1.3),p+new double3(1.3))?cave.Air(p):double.NegativeInfinity;
    }
    public bool TryGetCachedEntrance(double3 focus,out double3 entrance)
    {
        entrance=default;double distance=double.PositiveInfinity;
        foreach(var item in entries.Values)
        {
            if(!item.IsValueCreated||item.Value.Cave==null)continue;
            foreach(var p in item.Value.Cave.Entrances){double d=math.distancesq(p,focus);if(d>=distance)continue;entrance=p;distance=d;}
        }
        return double.IsFinite(distance);
    }
    public bool FindNearest(double3 focus,out double3 entrance,CancellationToken cancellation=default)
    {
        entrance=default;double best=double.PositiveInfinity;int2 centre=Cell(focus.xz);var cells=new List<int2>();
        if(!settings.enabled)return false;
        for(int z=-3;z<=3;z++)for(int x=-3;x<=3;x++)cells.Add(centre+new int2(x,z));
        double Lower(int2 cell)
        {double2 min=(double2)cell*settings.cellSize,max=min+settings.cellSize;return math.lengthsq(math.max(math.max(min-focus.xz,focus.xz-max),0));}
        cells.Sort((a,b)=>{int c=Lower(a).CompareTo(Lower(b));return c!=0?c:a.x!=b.x?a.x.CompareTo(b.x):a.y.CompareTo(b.y);});
        foreach(var cell in cells)
        {
            cancellation.ThrowIfCancellationRequested();if(Lower(cell)>best)break;
            var cave=Get(cell).Cave;if(cave==null)continue;
            foreach(var p in cave.Entrances){double d=math.distancesq(p,focus);if(d<best){best=d;entrance=p;}}
        }
        return double.IsFinite(best);
    }
    private static double Unit(uint h)=>(h&0x00ffffffu)/16777216d;
    private TerrainCaveLayout Plan(int2 cell)
    {
        uint hash=WorldLandforms.Hash(cell.x,cell.y,seed+934871);
        if(Unit(hash)>settings.probability)return null;
        double2 cellMin=(double2)cell*settings.cellSize,centre=cellMin+settings.cellSize*.5;
        double length=settings.systemLength*(.88+.24*Unit(WorldLandforms.Hash(cell.x,cell.y,seed+934873)));
        length=Math.Min(settings.cellSize-160,length);
        bool preferEnclosed=(hash%3)==0;
        for(int attempt=0;attempt<8;attempt++)
        {
            double angle=Unit(WorldLandforms.Hash(cell.x,cell.y,seed+934879+attempt))*Math.PI*2;
            double2 direction=new double2(Math.Cos(angle),Math.Sin(angle)),side=new double2(-direction.y,direction.x);
            WorldRockClass rock=geology?.Invoke(centre)??WorldRockClass.Limestone;
            int styleSeed=(unchecked((int)hash)+attempt*97)&0x3fffffff;
            if(rock==WorldRockClass.Granite)styleSeed|=0x40000000;
            double Random(int n)=>Unit(WorldLandforms.Hash(cell.x+n,cell.y-n,styleSeed));
            var nodes=new List<double2>();
            var offsets=new[]{new double2(-.42,.04),new double2(-.22,-.13),new double2(0,.04),new double2(.22,.14),new double2(.42,-.04),new double2(-.08,-.34),new double2(.36,.32)};
            for(int i=0;i<offsets.Length;i++)
            {
                var p=offsets[i]+new double2(Random(i+210)-.5,Random(i+220)-.5)*.08;
                nodes.Add(centre+length*(direction*p.x+side*p.y));
            }
            var routes=new List<(List<double2> points,double width)>();
            List<double2> Curve(double2 a,double2 b,int id)
            {
                double2 delta=b-a,normal=math.normalizesafe(new double2(-delta.y,delta.x));
                double2 control=(a+b)*.5+normal*math.length(delta)*(.12+.12*Random(id))*(Random(id+30)<.5?-1:1);
                int steps=Math.Max(3,(int)Math.Ceiling(math.length(delta)/18));var path=new List<double2>();
                for(int i=0;i<=steps;i++){double t=i/(double)steps;path.Add((1-t)*(1-t)*a+2*t*(1-t)*control+t*t*b);}return path;
            }
            void Link(int a,int b,int id,double width)=>routes.Add((Curve(nodes[a],nodes[b],id),width));
            for(int i=0;i<4;i++)Link(i,i+1,i,settings.passageWidth*(.9+.15*Random(i+50)));
            Link(1,5,5,6);Link(5,2,6,6);Link((hash&32)==0?3:2,6,7,5);
            if((hash&64)!=0)Link(6,4,8,5);
            // Probe the complete route and chamber shoulders before selecting the
            // underground datum. Neither endpoint is forced onto the heightfield.
            double roomWidth=settings.chamberWidth*(rock==WorldRockClass.Granite?.8:1);
            var chamberNodes=(hash&128)==0?new[]{1,3}:new[]{1,2};
            var probes=new List<double2>();
            foreach(var route in routes)foreach(var p in route.points)
                for(int j=-1;j<=1;j++)probes.Add(p+side*(j*route.width*.7));
            foreach(int index in chamberNodes)for(int j=0;j<12;j++)
            {double a=j*Math.PI/6;probes.Add(nodes[index]+new double2(Math.Cos(a),Math.Sin(a))*roomWidth*.65);}
            var heights=sample(probes);double low=double.PositiveInfinity;foreach(double h in heights)low=Math.Min(low,h);
            double datum=low-settings.minimumCover-27-(preferEnclosed?8+Random(90)*16:0);
            if(datum<water+4)continue;
            double Floor(double2 p)=>datum+math.dot(p-centre,direction)*.018;
            var spans=new List<TerrainCaveSpan>();
            foreach(var route in routes)for(int i=1;i<route.points.Count;i++)
            {
                double2 a=route.points[i-1],b=route.points[i];
                spans.Add(new TerrainCaveSpan(new double3(a.x,Floor(a),a.y),new double3(b.x,Floor(b),b.y),route.width,settings.passageHeight,styleSeed,false,true));
            }
            // Overlapping offset lobes make asymmetric rooms rather than a single
            // enlarged corridor. Their floors share the connected route datum.
            foreach(int index in chamberNodes)for(int lobe=0;lobe<3;lobe++)
            {
                double a=angle+lobe*2.2+Random(index+70),w=roomWidth*(.6+.18*Random(index+lobe+80));
                double2 axis=new double2(Math.Cos(a),Math.Sin(a));
                double2 p=nodes[index]+axis*(lobe==0?-5:5),q=p+axis*10;
                spans.Add(new TerrainCaveSpan(new double3(p.x,Floor(nodes[index]),p.y),new double3(q.x,Floor(nodes[index]),q.y),w,12+Random(index+95)*5,styleSeed,false,true));
            }
            var entrances=new List<double3>();
            if(!preferEnclosed)
            {
                // Seek a nearby hillside intersection. A failed search leaves a
                // sealed cave; it never manufactures a long surface-to-surface tube.
                foreach(int node in new[]{0,4})
                {
                    var candidates=new List<double2>();
                    for(int radius=24;radius<=96;radius+=24)for(int j=-3;j<=3;j++)
                    {
                        double a=angle+(node==0?Math.PI:0)+j*.3;
                        candidates.Add(nodes[node]+new double2(Math.Cos(a),Math.Sin(a))*radius);
                    }
                    var levels=sample(candidates);double best=double.PositiveInfinity;double2 mouth=default;double mouthFloor=0;
                    for(int i=0;i<candidates.Count;i++)
                    {
                        double d=math.distance(candidates[i],nodes[node]),rise=Math.Abs(levels[i]-Floor(nodes[node]));
                        if(rise>d*.145||levels[i]<water+2||d+rise*2>=best)continue;
                        if(math.any(candidates[i]<cellMin+32)||math.any(candidates[i]>cellMin+settings.cellSize-32))continue;
                        best=d+rise*2;mouth=candidates[i];mouthFloor=levels[i]-.08;
                    }
                    if(!double.IsFinite(best))continue;
                    mouthFloor=canonicalHeight(mouth.x,mouth.y)-.08;
                    var path=Curve(nodes[node],mouth,node+100);double total=0;for(int i=1;i<path.Count;i++)total+=math.distance(path[i-1],path[i]);
                    if(Math.Abs(mouthFloor-Floor(nodes[node]))>total*.16)continue;
                    double travelled=0;
                    for(int i=1;i<path.Count;i++)
                    {
                        double previous=travelled;travelled+=math.distance(path[i-1],path[i]);var a=path[i-1];var b=path[i];
                        spans.Add(new TerrainCaveSpan(new double3(a.x,math.lerp(Floor(nodes[node]),mouthFloor,previous/total),a.y),new double3(b.x,math.lerp(Floor(nodes[node]),mouthFloor,travelled/total),b.y),settings.passageWidth*(.85+.1*Random(node+110)),settings.passageHeight,styleSeed,true,true));
                    }
                    entrances.Add(new double3(mouth.x,mouthFloor+.2,mouth.y));
                }
            }
            if(entrances.Count==0&&canonicalBounds!=null)
            {
                double lowerBy=0;
                foreach(var span in spans)
                    if(canonicalBounds.TryGetDensityBounds(new double3(span.Minimum.x,0,span.Minimum.z),new double3(span.Maximum.x,0,span.Maximum.z),out _,out double high))
                        lowerBy=Math.Max(lowerBy,span.Maximum.y+settings.minimumCover+1+high);
                if(lowerBy>0)for(int i=0;i<spans.Count;i++)
                {var s=spans[i];spans[i]=new TerrainCaveSpan(s.Start-new double3(0,lowerBy,0),s.End-new double3(0,lowerBy,0),s.Width,s.Height,s.Seed,false,true);}
            }
            bool dry=true;foreach(var span in spans)dry&=span.Minimum.y>water+.5;if(!dry)continue;
            var anchor=spans[0].Start+new double3(0,.25,0);
            var cave=new TerrainCaveLayout(cell,spans,entrances.Count>0?entrances[0]:anchor,entrances.Count>0?entrances[entrances.Count-1]:anchor,entrances);
            // Entire influence stays in its owning cell, including the density band. Point
            // queries therefore never depend on neighboring cell residency/order.
            if(math.any(cave.Minimum.xz<cellMin+4)||math.any(cave.Maximum.xz>cellMin+settings.cellSize-4))continue;
            var formations=new List<TerrainCaveFormation>();
            double offset=spans[0].Start.y-Floor(spans[0].Start.xz);
            foreach(int node in chamberNodes)for(int j=0;j<12;j++)
            {
                double a=angle+j*Math.PI/6+Random(node+140)*.4;
                double2 p=nodes[node]+new double2(Math.Cos(a),Math.Sin(a))*roomWidth*(.28+.12*Random(j+150));
                double radius=.45+.55*Random(j+160);bool clear=true;
                foreach(var span in spans)
                {
                    double2 delta=span.End.xz-span.Start.xz;double t=math.clamp(math.dot(p-span.Start.xz,delta)/math.lengthsq(delta),0,1);
                    if(math.distance(p,math.lerp(span.Start.xz,span.End.xz,t))<2.3+radius){clear=false;break;}
                }
                if(!clear)continue;
                double floor=Floor(nodes[node])+offset-1;
                for(int step=0;step<30&&cave.Air(new double3(p.x,floor,p.y))<.1;step++)floor+=.1;
                if(cave.Air(new double3(p.x,floor+.5,p.y))<radius*.6)continue;
                double roof=floor+1;while(roof<floor+24&&cave.Air(new double3(p.x,roof,p.y))>.05)roof+=.2;
                double height=roof-floor;if(height<3)continue;
                if(rock==WorldRockClass.Limestone&&(j%3)!=0)
                {
                    double size=Math.Min(height*.28,1.2+Random(j+170)*2.1);
                    formations.Add(new TerrainCaveFormation(new double3(p.x,floor-.25,p.y),new double3(p.x,floor+size,p.y),radius));
                    if((j&1)==0)formations.Add(new TerrainCaveFormation(new double3(p.x,roof+.25,p.y),new double3(p.x,roof-Math.Min(height*.25,2.5),p.y),radius*.7));
                }
                else formations.Add(new TerrainCaveFormation(new double3(p.x,floor-.25,p.y),new double3(p.x,floor+.6+Random(j+180),p.y),radius,true));
            }
            return new TerrainCaveLayout(cell,spans,cave.Entrance,cave.Destination,entrances,formations);
        }
        return null;
    }
    private sealed class NativeSamples
    {
        private readonly WorldGenerationConfiguration generation;
        internal NativeSamples(WorldGenerationConfiguration generation){this.generation=generation;}
        internal double[] Read(IReadOnlyList<double2> points)
        {
            var input=new NativeArray<float2>(points.Count,Allocator.Persistent);var output=new NativeArray<float>(points.Count,Allocator.Persistent);
            try
            {
                for(int i=0;i<points.Count;i++)input[i]=(float2)(points[i]/generation.WorldScale);
                new Heights{Positions=input,Output=output,Scale=generation.SampleScale,Seed=generation.Seed,Water=generation.Water.WaterLevel,Coverage=generation.MountainHorizontalScale,Erosion=generation.Erosion}.Run();
                var values=new double[points.Count];for(int i=0;i<values.Length;i++)values[i]=output[i]*generation.MeshHeightMultiplier*generation.WorldScale;return values;
            }
            finally{input.Dispose();output.Dispose();}
        }
    }
    [BurstCompile(FloatMode=FloatMode.Strict,FloatPrecision=FloatPrecision.Standard)]
    private struct Heights:IJob
    {
        [ReadOnly]public NativeArray<float2> Positions;
        public NativeArray<float> Output;
        public float Scale,Water,Coverage;
        public int Seed;
        public WorldErosionSettings Erosion;
        public void Execute(){for(int i=0;i<Positions.Length;i++)Output[i]=HeightMapGenerator.SampleTerrainHeightNative(Positions[i].x,Positions[i].y,Scale,Seed+60000,Water,Coverage,Erosion).Height;}
    }
}
