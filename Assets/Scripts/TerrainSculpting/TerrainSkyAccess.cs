using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

/// <summary>Sparse, derived ambient visibility. Shares sculpt mesh-worker admission, never
/// blocks terrain publication, and never becomes density/save/collision authority.</summary>
public sealed class TerrainSkyAccess:IDisposable
{
    public const int Spacing=2,Cells=16,Width=17,TileMetres=Spacing*Cells,Capacity=128;
    public const int AtlasColumns=16,AtlasWidth=Width*AtlasColumns,AtlasHeight=Width*Width*(Capacity/AtlasColumns);
    public static readonly int3 TableSize=new int3(16,8,16);
    public const long ResidentCharge=24L*1024*1024; // CPU arrays, readable Unity copies, GPU and bounded worker scratch.
    private static readonly ProfilerMarker TraceMarker=new ProfilerMarker("FS.Streaming.Sculpt.SkyTrace");
    private static readonly ProfilerMarker UploadMarker=new ProfilerMarker("FS.Streaming.Sculpt.SkyUpload");
    private static TerrainSkyAccess active;
    private readonly TerrainDensityWorld world;
    private readonly Func<double,double,double> height;
    private readonly TerrainRequestManager requests;
    private readonly TerrainSkyAccessSettings settings;
    private readonly object gate=new object();
    private readonly ManualResetEventSlim drained=new ManualResetEventSlim(true);
    private readonly Dictionary<int3,Page> pages=new Dictionary<int3,Page>();
    private readonly List<int3> wanted=new List<int3>();
    private readonly Stack<int> free=new Stack<int>();
    private readonly float[] times=new float[Capacity];
    private readonly Color32[] table=new Color32[16*8*16];
    private Color32[] sky,visibility,heights;
    private Texture2D skyTexture,visibilityTexture,heightTexture,tableTexture;
    private CancellationTokenSource cancellation;
    private Result completed;
    private int3 tableOrigin,lastFocus=new int3(int.MinValue);
    private long revision=-1;
    private bool running,disposed;
    public bool Busy { get { lock(gate)return running||completed!=null||Pending>0; } }
    public int Pending { get { int n=0;foreach(var key in wanted)if(!pages.TryGetValue(key,out var p)||p.Revision!=revision)n++;return n; } }
    public int ResidentPages=>pages.Count;
    public int VisiblePages=>wanted.Count;
    public int OmittedPages { get; private set; }
    public int StaleResults { get; private set; }
    public double LastTraceMs { get; private set; }
    public double LastUploadMs { get; private set; }
    public string Status { get; private set; }="No excavations";
    public long DensityRevision=>revision;
    public bool Allocated=>sky!=null;
    private sealed class Page { public int Slot;public long Revision; }
    private sealed class Result
    {
        public int3 Key;public long Revision;public Color32[] Sky,Visibility,Height;
        public double Ms;public Exception Error;
    }
    public TerrainSkyAccess(TerrainDensityWorld world,Func<double,double,double> height,TerrainRequestManager requests,TerrainSkyAccessSettings settings)
    {this.world=world;this.height=height;this.requests=requests;this.settings=settings;for(int i=Capacity-1;i>=0;i--)free.Push(i);}
    private static int Index(int x,int y,int z)=>x+Width*(y+Width*z);
    private static int AtlasIndex(int slot,int x,int y,int z)=>(slot%AtlasColumns)*Width+x+AtlasWidth*((slot/AtlasColumns)*Width*Width+y+Width*z);
    private static int HeightIndex(int slot,int x,int z)=>(slot%AtlasColumns)*Width+x+AtlasWidth*((slot/AtlasColumns)*Width+z);
    private static bool InTable(int3 key,int3 origin)=>math.all(key>=origin)&&math.all(key<origin+TableSize);
    private static int TableIndex(int3 local)=>local.x+16*(local.y+8*local.z);
    private static int3 Tile(double3 p)=>(int3)math.floor(p/TileMetres);
    private static Texture2D Texture(string name,int width,int height,FilterMode filter)=>new Texture2D(width,height,TextureFormat.RGBA32,false,true)
    {name=name,filterMode=filter,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
    private void Allocate()
    {
        if(Allocated)return;
        sky=new Color32[AtlasWidth*AtlasHeight];visibility=new Color32[sky.Length];heights=new Color32[AtlasWidth*Width*(Capacity/AtlasColumns)];
        skyTexture=Texture("Cave sky probes",AtlasWidth,AtlasHeight,FilterMode.Point);
        visibilityTexture=Texture("Cave probe visibility",AtlasWidth,AtlasHeight,FilterMode.Point);
        heightTexture=Texture("Cave original surface",AtlasWidth,Width*(Capacity/AtlasColumns),FilterMode.Bilinear);
        tableTexture=Texture("Cave sky page table",16,8*16,FilterMode.Point);
    }
    private void Cancel(){cancellation?.Cancel();}
    /// <summary>Do not let optional lighting snapshots consume an edit's density
    /// headroom, even when the host admits several mesh workers concurrently.</summary>
    public bool YieldToTerrain(){Cancel();lock(gate)return !running;}
    public void Update(double3 focus,long publishedRevision,bool terrainBusy,bool canPublish=true)
    {
        if(disposed)return;
        if(settings==null||!settings.enabled){Cancel();Disable();Status="Disabled";return;}
        if(terrainBusy||publishedRevision!=world.Revision){Cancel();Bind();Status="Waiting for terrain";return;}
        if(!canPublish){Bind();return;}
        int3 focusTile=Tile(focus);
        if(revision!=publishedRevision||!math.all(focusTile==lastFocus))
        {
            if(!YieldToTerrain()){Bind();Status="Waiting for sky worker to yield";return;}
            try
            {
                using var snapshot=world.CaptureSnapshot();
                var candidates=new HashSet<int3>();tableOrigin=focusTile-TableSize/2;
                foreach(var key in snapshot.Bricks.Keys)
                {
                    double3 minimum=(double3)TerrainDensityCoordinates.Origin(key)*snapshot.Profile.Spacing-Spacing;
                    double3 maximum=minimum+snapshot.Profile.Spacing*16+Spacing*2;
                    int3 lo=Tile(minimum),hi=Tile(maximum);
                    for(int z=lo.z;z<=hi.z;z++)for(int y=lo.y;y<=hi.y;y++)for(int x=lo.x;x<=hi.x;x++)
                    {var p=new int3(x,y,z);if(InTable(p,tableOrigin))candidates.Add(p);}
                }
                wanted.Clear();wanted.AddRange(candidates);
                wanted.Sort((a,b)=>math.distancesq((double3)a*TileMetres+TileMetres*.5,focus).CompareTo(math.distancesq((double3)b*TileMetres+TileMetres*.5,focus)));
                OmittedPages=Math.Max(0,wanted.Count-Capacity);if(wanted.Count>Capacity)wanted.RemoveRange(Capacity,wanted.Count-Capacity);
                var keep=new HashSet<int3>(wanted);var remove=new List<int3>();
                foreach(var pair in pages)if(!keep.Contains(pair.Key))remove.Add(pair.Key);
                foreach(var key in remove){free.Push(pages[key].Slot);pages.Remove(key);}
                revision=publishedRevision;lastFocus=focusTile;
                if(wanted.Count>0)Allocate();
                RebuildTable();
            }
            catch(TerrainDensityCapacityException){Status="Waiting for density snapshot budget";return;}
        }
        Result result;
        lock(gate){result=completed;completed=null;}
        if(result!=null)
        {
            if(result.Error is OperationCanceledException||result.Revision!=revision||!wanted.Contains(result.Key))StaleResults++;
            else if(result.Error!=null){Status="Sky trace failed: "+result.Error.Message;Cancel();Bind();return;}
            else Publish(result);
        }
        Bind();
        lock(gate)if(running)return;
        foreach(var key in wanted)
        {
            if(pages.TryGetValue(key,out var page)&&page.Revision==revision)continue;
            cancellation?.Dispose();cancellation=new CancellationTokenSource();var token=cancellation.Token;long version=revision;
            lock(gate){running=true;drained.Reset();}
            bool admitted=requests.TryRunSculpt(()=>
            {
                var output=new Result{Key=key,Revision=version};var clock=Stopwatch.StartNew();
                try
                {
                    using var marker=TraceMarker.Auto();using var snapshot=world.CaptureSnapshot();
                    if(snapshot.Revision!=version)throw new OperationCanceledException();
                    Build(output,snapshot,height,token);
                }
                catch(Exception ex){output.Error=ex;}
                finally{output.Ms=clock.Elapsed.TotalMilliseconds;lock(gate){completed=disposed?null:output;running=false;drained.Set();}}
            });
            if(!admitted){lock(gate){running=false;drained.Set();}Status="Waiting for shared mesh worker";return;}
            Status="Tracing sky access";return;
        }
        Status=wanted.Count==0?"No nearby excavations":OmittedPages>0?"Ready (nearest 128 tiles; capacity reached)":"Ready";
    }
    private void Publish(Result result)
    {
        using var marker=UploadMarker.Auto();var clock=Stopwatch.StartNew();
        bool exists=pages.TryGetValue(result.Key,out var page);
        if(!exists){page=new Page{Slot=free.Pop()};pages.Add(result.Key,page);}
        int slot=page.Slot;float now=Time.realtimeSinceStartup;
        float blend=settings.blendSeconds<=0?1:Mathf.Clamp01((now-times[slot])/settings.blendSeconds);
        for(int z=0;z<Width;z++)for(int y=0;y<Width;y++)for(int x=0;x<Width;x++)
        {
            int a=AtlasIndex(slot,x,y,z),b=Index(x,y,z);var value=result.Sky[b];var old=sky[a];
            value.r=exists?(byte)Mathf.RoundToInt(Mathf.Lerp(old.r,old.g,blend)):(byte)255;
            sky[a]=value;visibility[a]=result.Visibility[b];result.Sky[b]=value;
        }
        for(int z=0;z<Width;z++)for(int x=0;x<Width;x++)heights[HeightIndex(slot,x,z)]=result.Height[x+Width*z];
        // Rows are y + 17*z, so each 3D tile is one bounded 17 x 289 atlas rectangle.
        skyTexture.SetPixels32(slot%16*Width,slot/16*Width*Width,Width,Width*Width,result.Sky);
        visibilityTexture.SetPixels32(slot%16*Width,slot/16*Width*Width,Width,Width*Width,result.Visibility);
        heightTexture.SetPixels32(slot%16*Width,slot/16*Width,Width,Width,result.Height);
        skyTexture.Apply(false,false);visibilityTexture.Apply(false,false);heightTexture.Apply(false,false);
        page.Revision=result.Revision;times[slot]=now;RebuildTable();LastTraceMs=result.Ms;LastUploadMs=clock.Elapsed.TotalMilliseconds;
    }
    private void RebuildTable()
    {
        if(!Allocated)return;Array.Clear(table,0,table.Length);
        foreach(var pair in pages)if(InTable(pair.Key,tableOrigin))table[TableIndex(pair.Key-tableOrigin)]=new Color32((byte)(pair.Value.Slot+1),0,0,255);
        tableTexture.SetPixels32(table);tableTexture.Apply(false,false);
    }
    private void Bind()
    {
        active=this;
        Shader.SetGlobalFloat("_TerrainSkyEnabled",Allocated&&pages.Count>0&&settings.enabled?1:0);
        if(!Allocated)return;
        Shader.SetGlobalTexture("_TerrainSkyData",skyTexture);Shader.SetGlobalTexture("_TerrainSkyVisibility",visibilityTexture);
        Shader.SetGlobalTexture("_TerrainSkyHeight",heightTexture);Shader.SetGlobalTexture("_TerrainSkyTable",tableTexture);
        Shader.SetGlobalVector("_TerrainSkyOrigin",new Vector4(tableOrigin.x,tableOrigin.y,tableOrigin.z,0));
        Shader.SetGlobalVector("_TerrainSkyControls",new Vector4(Mathf.Clamp01(settings.caveAmbientMultiplier),Mathf.Clamp(settings.skyResponse,.25f,4),Mathf.Max(0,settings.blendSeconds),Time.realtimeSinceStartup));
        Shader.SetGlobalFloatArray("_TerrainSkyTimes",times);
    }
    private void Disable(){if(active==this){Shader.SetGlobalFloat("_TerrainSkyEnabled",0);active=null;}}
    /// <summary>CPU diagnostic matching the shader lookup; does not trace or access physics.</summary>
    public float SampleExposure(double3 position,bool targetValue=false)
    {
        int3 key=Tile(position);
        if(disposed||!Allocated||settings==null||!settings.enabled||!pages.TryGetValue(key,out var page))return 1;
        double3 grid=math.clamp((position-(double3)key*TileMetres)/Spacing,0,15.9999);
        int3 cell=(int3)math.floor(grid);double3 f=grid-cell;
        double Height(int x,int z)
        {var h=heights[HeightIndex(page.Slot,x,z)];return (h.r+h.g*256)/65535d*256-128+key.y*TileMetres;}
        double surface=math.lerp(math.lerp(Height(cell.x,cell.z),Height(cell.x+1,cell.z),f.x),math.lerp(Height(cell.x,cell.z+1),Height(cell.x+1,cell.z+1),f.x),f.z);
        if(position.y>=surface-.025)return 1;
        double blend=targetValue||settings.blendSeconds<=0?1:math.saturate((Time.realtimeSinceStartup-times[page.Slot])/settings.blendSeconds);
        double total=0,access=0;
        for(int z=0;z<2;z++)for(int y=0;y<2;y++)for(int x=0;x<2;x++)
        {
            int3 c=cell+new int3(x,y,z);var d=sky[AtlasIndex(page.Slot,c.x,c.y,c.z)];var v=visibility[AtlasIndex(page.Slot,c.x,c.y,c.z)];
            double3 negative=new double3(v.r,d.b,v.b)*(4d/255),positive=new double3(v.g,d.a,v.a)*(4d/255);
            if(math.csum(negative+positive)<.001||key.y*TileMetres+c.y*Spacing>=Height(c.x,c.z)-.025)continue;
            double3 delta=(grid-c)*Spacing,clearance=math.min(delta+negative,positive-delta);
            double3 weights=math.lerp(1-f,f,new double3(x,y,z));
            double weight=weights.x*weights.y*weights.z*math.saturate((math.cmin(clearance)+.035)/.1);
            total+=weight;access+=math.lerp(d.r,d.g,blend)/255*weight;
        }
        return total>.0001?(float)math.saturate(access/total):0;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;Cancel();drained.Wait();Disable();
        foreach(var texture in new[]{skyTexture,visibilityTexture,heightTexture,tableTexture})
            if(texture!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(texture);else UnityEngine.Object.DestroyImmediate(texture);}
        sky=null;visibility=null;heights=null;pages.Clear();wanted.Clear();completed=null;cancellation?.Dispose();drained.Dispose();
    }
    private static void Build(Result result,TerrainDensitySnapshot snapshot,Func<double,double,double> height,CancellationToken token)
    {
        result.Sky=new Color32[Width*Width*Width];result.Visibility=new Color32[result.Sky.Length];result.Height=new Color32[Width*Width];
        var sample=new Sampler(snapshot);double3 origin=(double3)result.Key*TileMetres;
        for(int z=0;z<Width;z++)for(int x=0;x<Width;x++)
        {
            token.ThrowIfCancellationRequested();double h=height(origin.x+x*Spacing,origin.z+z*Spacing);
            int encoded=(int)Math.Round(math.clamp((h-origin.y+128)/256,0,1)*65535);
            result.Height[x+Width*z]=new Color32((byte)(encoded&255),(byte)(encoded>>8),0,255);
            for(int y=0;y<Width;y++)
            {
                token.ThrowIfCancellationRequested();double3 p=origin+new double3(x,y,z)*Spacing;int i=Index(x,y,z);
                if(sample.Density(p)<.001)continue;
                if(p.y>=h){result.Sky[i]=new Color32(255,255,255,255);result.Visibility[i]=new Color32(255,255,255,255);continue;}
                int visible=TraceSky(sample,height,p,new double3(0,1,0),token)?2:0;
                for(int direction=0;direction<8;direction++)
                {
                    double angle=direction*Math.PI*.25;
                    if(TraceSky(sample,height,p,new double3(Math.Cos(angle)*.8660254,.5,Math.Sin(angle)*.8660254),token))visible++;
                }
                byte access=(byte)(visible*255/10);
                result.Sky[i]=new Color32(access,access,Clear(sample,p,new double3(0,-1,0)),Clear(sample,p,new double3(0,1,0)));
                result.Visibility[i]=new Color32(Clear(sample,p,new double3(-1,0,0)),Clear(sample,p,new double3(1,0,0)),Clear(sample,p,new double3(0,0,-1)),Clear(sample,p,new double3(0,0,1)));
            }
        }
    }
    private static bool TraceSky(Sampler sample,Func<double,double,double> height,double3 p,double3 direction,CancellationToken token)
    {
        for(double distance=.15;distance<=128;distance+=.15)
        {
            if(((int)(distance/.15)&31)==0)token.ThrowIfCancellationRequested();
            double3 point=p+direction*distance;
            if(sample.Density(point)<0)return false;
            // The present baseline is solid below its height surface. Reaching its
            // exterior certifies outdoor escape; exhausting a finite trace never does.
            if(point.y>height(point.x,point.z)+.05)return true;
        }
        return false;
    }
    private static byte Clear(Sampler sample,double3 p,double3 direction)
    {
        double old=sample.Density(p);
        for(double distance=.15;distance<4;distance+=.15)
        {
            double d=sample.Density(p+direction*distance);
            if(d<0){double hit=distance-.15+.15*old/(old-d);return (byte)math.clamp(Math.Floor(hit/4*255),0,255);}
            old=d;
        }
        return 255;
    }
    private sealed class Sampler
    {
        private readonly TerrainDensitySnapshot snapshot;
        private readonly Dictionary<int3,short> samples=new Dictionary<int3,short>();
        internal Sampler(TerrainDensitySnapshot snapshot){this.snapshot=snapshot;}
        private short Read(int3 p)
        {
            if(samples.TryGetValue(p,out short d))return d;
            if(samples.Count>=8192)samples.Clear();d=snapshot.ReadEncoded(p);samples.Add(p,d);return d;
        }
        internal double Density(double3 p)
        {
            double3 grid=p/snapshot.Profile.Spacing;int3 cell=(int3)math.floor(grid);double3 f=grid-cell;
            int a=0,b=1,c=2;if(f[a]<f[b]){int t=a;a=b;b=t;}if(f[b]<f[c]){int t=b;b=c;c=t;}if(f[a]<f[b]){int t=a;a=b;b=t;}
            int3 first=cell;first[a]++;int3 second=first;second[b]++;
            return Read(cell)*(1-f[a])+Read(first)*(f[a]-f[b])+Read(second)*(f[b]-f[c])+Read(cell+1)*f[c];
        }
    }
}
