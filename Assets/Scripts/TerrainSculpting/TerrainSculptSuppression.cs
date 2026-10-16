using System;
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>Immutable sparse mask of lost support at the original landscape surface.
/// Retained roofs keep their foliage. Never stored separately or applied to ecology maps.</summary>
public sealed class TerrainSculptSuppression
{
    public const double BorderPadding=.15, SupportDepth=.05;
    private const long TileCharge=384;
    private readonly Dictionary<int2,ulong[]> tiles;
    private readonly double spacing;
    public long DensityRevision { get; }
    public long PayloadCharge=>(tiles.Count+1)*TileCharge;
    public int TileCount=>tiles.Count;
    private TerrainSculptSuppression(Dictionary<int2,ulong[]> tiles,double spacing,long revision)
    {this.tiles=tiles;this.spacing=spacing;DensityRevision=revision;}
    public bool Contains(double x,double z)
    {
        int sx=TerrainDensityCoordinates.FloorIndex(x/spacing),sz=TerrainDensityCoordinates.FloorIndex(z/spacing);
        int bx=TerrainDensityCoordinates.FloorDiv(sx,16),bz=TerrainDensityCoordinates.FloorDiv(sz,16);
        if(!tiles.TryGetValue(new int2(bx,bz),out var bits))return false;
        int index=sx-bx*16+16*(sz-bz*16);return (bits[index>>6]&(1UL<<(index&63)))!=0;
    }
    public bool ChangedIn(TerrainSculptSuppression previous,double x0,double z0,double x1,double z1)
    {
        if(ReferenceEquals(this,previous))return false;
        if(tiles.Count==0&&(previous==null||previous.tiles.Count==0))return false;
        int bx0=TerrainDensityCoordinates.FloorDiv(TerrainDensityCoordinates.FloorIndex(x0/spacing),16);
        int bz0=TerrainDensityCoordinates.FloorDiv(TerrainDensityCoordinates.FloorIndex(z0/spacing),16);
        int bx1=TerrainDensityCoordinates.FloorDiv(TerrainDensityCoordinates.FloorIndex(x1/spacing),16);
        int bz1=TerrainDensityCoordinates.FloorDiv(TerrainDensityCoordinates.FloorIndex(z1/spacing),16);
        for(int z=bz0;z<=bz1;z++)for(int x=bx0;x<=bx1;x++)
        {
            var key=new int2(x,z);tiles.TryGetValue(key,out var current);
            ulong[] old=null;previous?.tiles.TryGetValue(key,out old);
            if(!ReferenceEquals(current,old))return true;
        }
        return false;
    }
    internal static TerrainSculptSuppression Build(TerrainDensitySnapshot snapshot,TerrainDensityEdit edit,
        TerrainSculptSuppression previous,TerrainSculptBuildBudget budget,long maximumBytes,Func<double,double,double> surfaceHeight)
    {
        if(surfaceHeight==null)throw new ArgumentNullException(nameof(surfaceHeight));
        bool incremental=previous!=null&&previous.spacing==snapshot.Profile.Spacing*.5&&previous.DensityRevision==snapshot.Revision;
        if(incremental&&(edit==null||edit.ChangedSamples==0))return previous;
        long revision=snapshot.Revision+(edit!=null&&edit.ChangedSamples>0?1:0);
        long scopeStart=budget.ChargedBytes,persistentStart=budget.PersistentBytes;
        var old=incremental?previous.tiles:null;
        budget.ChargePersistent(((old?.Count??0)+1)*TileCharge);
        var result=old!=null?new Dictionary<int2,ulong[]>(old):new Dictionary<int2,ulong[]>();
        var writable=new HashSet<int2>();
        double s=snapshot.Profile.Spacing;
        double maskSpacing=s*.5;
        int margin=(int)Math.Ceiling(BorderPadding/maskSpacing);
        var visited=new HashSet<int2>();
        var samples=new Dictionary<int3,int2>();
        var heights=new Dictionary<int2,double>();
        double Height(int x,int z)
        {
            var key=new int2(x,z);
            if(heights.TryGetValue(key,out double h))return h;
            budget.Charge(64);h=surfaceHeight(x*s,z*s);heights.Add(key,h);return h;
        }
        int2 Sample(int3 sample)
        {
            if(samples.TryGetValue(sample,out var values))return values;
            budget.Charge(96);
            double3 p=TerrainDensityCoordinates.ToWorld(sample,snapshot.Profile);
            short original=snapshot.Profile.Encode(snapshot.Generator is ITerrainHeightDensityGenerator?
                p.y-Height(sample.x,sample.z):snapshot.Generator.SampleDensity(p));
            short edited=snapshot.Bricks.TryGetValue(TerrainDensityCoordinates.BrickOf(sample),out var brick)?
                brick.Read(TerrainDensityCoordinates.Index(TerrainDensityCoordinates.LocalOf(sample))):original;
            values=new int2(original,edited);samples.Add(sample,values);return values;
        }
        bool RemovedSurface(double x,double z)
        {
            // Probe just below each original root. A cavity below an intact roof does
            // not change these samples, regardless of how much stone was removed below.
            double3 grid=new double3(x,surfaceHeight(x,z)-SupportDepth,z)/s;
            int3 cell=new int3(TerrainDensityCoordinates.FloorIndex(grid.x),TerrainDensityCoordinates.FloorIndex(grid.y),TerrainDensityCoordinates.FloorIndex(grid.z));
            double3 f=grid-cell;
            // The mesher's six tetrahedra share the 000 -> 111 diagonal. Sorting the
            // fractional axes selects that same tetrahedron and its barycentric weights.
            int a=0,b=1,c=2;
            if(f[a]<f[b]){int swap=a;a=b;b=swap;}
            if(f[b]<f[c]){int swap=b;b=c;c=swap;}
            if(f[a]<f[b]){int swap=a;a=b;b=swap;}
            int3 first=cell;first[a]++;
            int3 second=first;second[b]++;
            double2 density=(double2)Sample(cell)*(1-f[a])+(double2)Sample(first)*(f[a]-f[b])+
                (double2)Sample(second)*(f[b]-f[c])+(double2)Sample(cell+1)*f[c];
            return density.x<0&&density.y>=0;
        }
        void Mark(int x,int z)
        {
            int bx=TerrainDensityCoordinates.FloorDiv(x,16),bz=TerrainDensityCoordinates.FloorDiv(z,16);
            var key=new int2(bx,bz);int i=x-bx*16+16*(z-bz*16);ulong bit=1UL<<(i&63);
            if(result.TryGetValue(key,out var bits)&&(bits[i>>6]&bit)!=0)return;
            if(!writable.Contains(key))
            {
                if(bits==null)
                {
                    if((result.Count+2)*TileCharge>maximumBytes)throw new TerrainDensityCapacityException("Terrain foliage mask budget is full.");
                    budget.ChargePersistent(TileCharge);
                }
                bits=bits!=null?(ulong[])bits.Clone():new ulong[4];result[key]=bits;writable.Add(key);
            }
            bits[i>>6]|=bit;
        }
        IEnumerable<int3> keys=incremental?(IEnumerable<int3>)edit.ChangedBricks:snapshot.Bricks.Keys;
        foreach(var key in keys)
        {
            budget.Check();
            int3 origin=TerrainDensityCoordinates.Origin(key);
            // Changed corners influence the adjacent cells on both sides of an owner
            // boundary. Visit each XZ cell only once across all vertical density owners.
            for(int z=-1;z<16;z++)for(int x=-1;x<16;x++)
            {
                var column=origin.xz+new int2(x,z);
                if(visited.Contains(column))continue;
                budget.Charge(64);visited.Add(column);
                for(int sz=0;sz<2;sz++)for(int sx=0;sx<2;sx++)
                {
                    budget.Check();int mx=column.x*2+sx,mz=column.y*2+sz;
                    if(!RemovedSurface((mx+.5)*maskSpacing,(mz+.5)*maskSpacing))continue;
                    // A small circular lip replaces the old full-cell expansion and
                    // square 0.6 m dilation. The separate 0.15 m mask avoids a wide halo.
                    for(int dz=-margin;dz<=margin;dz++)for(int dx=-margin;dx<=margin;dx++)
                        if((dx*dx+dz*dz)*maskSpacing*maskSpacing<=BorderPadding*BorderPadding+1e-8)Mark(mx+dx,mz+dz);
                }
            }
        }
        if((result.Count+1)*TileCharge>maximumBytes)throw new TerrainDensityCapacityException("Terrain foliage mask budget is full.");
        budget.CompleteScope(scopeStart,0,persistentStart);
        return new TerrainSculptSuppression(result,maskSpacing,revision);
    }
}
