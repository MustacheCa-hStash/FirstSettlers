using System;
using System.Collections.Generic;
using Unity.Mathematics;

public readonly struct TerrainSculptFootprint
{
    public readonly int2 Min, Max; // Source-lattice cell coordinates, max exclusive.
    public TerrainSculptFootprint(int2 min, int2 max)
    {
        if (math.any(max <= min)) throw new ArgumentOutOfRangeException(nameof(max));
        Min = min; Max = max;
    }
    internal bool Contains(int x, int z, int step) => x >= Min.x && z >= Min.y && x + step <= Max.x && z + step <= Max.y;
    internal bool Intersects(int x, int z, int step) => x < Max.x && z < Max.y && x + step > Min.x && z + step > Min.y;
}

/// <summary>
/// Reconstructs a rectangular replacement and an explicit unchanged outer collar. Render and
/// collider collars use step 1 and step 8 respectively; no skirt, overlap, or shader-only hole.
/// </summary>
public static class TerrainSculptPatchBuilder
{
    // A packed pair with the default ulong hash folds its halves using XOR. Sequential
    // vertex pairs then collapse into very few buckets and seam discovery becomes quadratic.
    private sealed class EdgeComparer:IEqualityComparer<ulong>
    {
        internal static readonly EdgeComparer Instance=new EdgeComparer();
        public bool Equals(ulong a,ulong b)=>a==b;
        public int GetHashCode(ulong edge)=>HashCode.Combine((uint)(edge>>32),(uint)edge);
    }
    private static readonly Unity.Profiling.ProfilerMarker SeamMarker=new Unity.Profiling.ProfilerMarker("FS.Streaming.Sculpt.Seams");
    private static ulong Edge(int a, int b) => ((ulong)(uint)Math.Min(a,b) << 32) | (uint)Math.Max(a,b);

    public static TerrainSculptMeshData BuildPatch(ITerrainDensityField snapshot,
        TerrainSculptFootprint footprint, int minY, int maxY, Func<double,double,double> baselineHeight, int outerStep = 1,
        TerrainSculptBuildBudget budget = null)
        => BuildPatchCached(snapshot,footprint,minY,maxY,baselineHeight,outerStep,budget,null);

    internal static TerrainSculptMeshData BuildPatchCached(ITerrainDensityField snapshot,
        TerrainSculptFootprint footprint,int minY,int maxY,Func<double,double,double> baselineHeight,int outerStep,
        TerrainSculptBuildBudget budget,TerrainSculptMeshCache.Builder cache,TerrainSculptFootprint? window=null)
    {
        if (snapshot == null || baselineHeight == null) throw new ArgumentNullException(nameof(snapshot));
        snapshot.RequireAlive(); ValidateGrid(footprint, outerStep);
        long scopeStart=budget?.ChargedBytes??0;
        long persistentStart=budget?.PersistentBytes??0;
        if (math.any(footprint.Max - footprint.Min <= outerStep * 2)) throw new ArgumentException("Patch needs an interior and an unchanged collar.");
        var global = new TerrainDensityCellBounds(new int3(footprint.Min.x + outerStep, minY, footprint.Min.y + outerStep),
            new int3(footprint.Max.x - outerStep, maxY, footprint.Max.y - outerStep));
        var domain=window.HasValue?new TerrainSculptFootprint(math.max(footprint.Min,window.Value.Min),math.min(footprint.Max,window.Value.Max)):footprint;
        var bounds=new TerrainDensityCellBounds(new int3(Math.Max(global.Min.x,domain.Min.x),minY,Math.Max(global.Min.z,domain.Min.y)),
            new int3(Math.Min(global.Max.x,domain.Max.x),maxY,Math.Min(global.Max.z,domain.Max.y)));
        ValidateEnvelope(snapshot,domain,bounds,baselineHeight,global);
        var interior = TerrainDensityMesher.BuildCached(snapshot,bounds,budget,cache);
        var output = new TerrainSculptMeshBuffer(budget: budget);
        for (int i = 0; i < interior.Positions.Length; i++) output.Add(interior.Positions[i], interior.Normals[i]);
        budget?.Charge((long)interior.Triangles.Length * 22);
        output.Triangles.AddRange(interior.Triangles);
        using var seamMarker=SeamMarker.Auto();
        var boundary = BoundaryEdges(interior);
        double spacing = snapshot.Profile.Spacing;
        double ix0 = bounds.Min.x * spacing, ix1 = bounds.Max.x * spacing;
        double iz0 = bounds.Min.z * spacing, iz1 = bounds.Max.z * spacing;
        // Interior source-chunk boundaries are ordinary shared volume faces, including cave
        // openings. Only the excavation group's actual outside edges get baseline collars.
        var west = bounds.Min.x==global.Min.x?Curve(interior,boundary,0,ix0,2,iz0,iz1):null;
        var east = bounds.Max.x==global.Max.x?Curve(interior,boundary,0,ix1,2,iz0,iz1):null;
        var south = bounds.Min.z==global.Min.z?Curve(interior,boundary,2,iz0,0,ix0,ix1):null;
        var north = bounds.Max.z==global.Max.z?Curve(interior,boundary,2,iz1,0,ix0,ix1):null;
        var outerVertices = new Dictionary<int2,int>();
        int Outer(int x, int z)
        {
            var key = new int2(x,z); if (outerVertices.TryGetValue(key, out int index)) return index;
            double wx = x * spacing, wz = z * spacing;
            double3 normal = new double3(baselineHeight(wx-spacing,wz)-baselineHeight(wx+spacing,wz),
                2*spacing, baselineHeight(wx,wz-spacing)-baselineHeight(wx,wz+spacing));
            index = output.Add(new double3(wx,baselineHeight(wx,wz),wz),normal); outerVertices.Add(key,index); return index;
        }
        List<int> OuterLine(bool xFixed, int fixedCoordinate, int from, int to)
        {
            var result = new List<int>(); for (int t = from; t <= to; t += outerStep)
                result.Add(xFixed ? Outer(fixedCoordinate,t) : Outer(t,fixedCoordinate));
            return result;
        }
        if(west!=null)Join(output,west,OuterLine(true,footprint.Min.x,bounds.Min.z,bounds.Max.z),2);
        if(east!=null)Join(output,east,OuterLine(true,footprint.Max.x,bounds.Min.z,bounds.Max.z),2);
        if(south!=null)Join(output,south,OuterLine(false,footprint.Min.y,bounds.Min.x,bounds.Max.x),0);
        if(north!=null)Join(output,north,OuterLine(false,footprint.Max.y,bounds.Min.x,bounds.Max.x),0);
        void Corner(int x, int z, int ix, int iz, int vertex)
        {
            int a = Outer(x,z), b = Outer(x,iz), d = Outer(ix,z);
            output.Triangle(a,b,vertex,new double3(0,1,0)); output.Triangle(a,vertex,d,new double3(0,1,0));
        }
        if(west!=null&&south!=null){RequireSameCorner(interior,west[0],south[0]);Corner(footprint.Min.x,footprint.Min.y,bounds.Min.x,bounds.Min.z,west[0]);}
        if(west!=null&&north!=null){RequireSameCorner(interior,west[west.Count-1],north[0]);Corner(footprint.Min.x,footprint.Max.y,bounds.Min.x,bounds.Max.z,west[west.Count-1]);}
        if(east!=null&&south!=null){RequireSameCorner(interior,east[0],south[south.Count-1]);Corner(footprint.Max.x,footprint.Min.y,bounds.Max.x,bounds.Min.z,east[0]);}
        if(east!=null&&north!=null){RequireSameCorner(interior,east[east.Count-1],north[north.Count-1]);Corner(footprint.Max.x,footprint.Max.y,bounds.Max.x,bounds.Max.z,east[east.Count-1]);}
        var result=output.Finish(interior.SampledBricks,interior.SkippedBricks);
        budget?.CompleteScope(scopeStart,result.PayloadBytes*2,persistentStart);
        return result;
    }

    public static TerrainSculptMeshData BuildBaseline(TerrainDensityProfile profile, TerrainSculptFootprint domain,
        Func<double,double,double> height, int step = 1, IReadOnlyList<TerrainSculptFootprint> holes = null,
        TerrainSculptBuildBudget budget = null)
    {
        profile.Validate(); if (height == null) throw new ArgumentNullException(nameof(height)); ValidateGrid(domain,step);
        long scopeStart=budget?.ChargedBytes??0;
        long persistentStart=budget?.PersistentBytes??0;
        if (holes != null) foreach (var hole in holes) ValidateGrid(hole,step);
        long cells = checked(((long)domain.Max.x-domain.Min.x)/step * (((long)domain.Max.y-domain.Min.y)/step));
        if (cells > 2000000) throw new TerrainDensityCapacityException();
        var output = new TerrainSculptMeshBuffer(budget: budget); var vertices = new Dictionary<int2,int>();
        int Vertex(int x,int z)
        {
            var key = new int2(x,z); if (vertices.TryGetValue(key,out int index)) return index;
            double wx=x*profile.Spacing,wz=z*profile.Spacing,s=profile.Spacing;
            index=output.Add(new double3(wx,height(wx,wz),wz),new double3(height(wx-s,wz)-height(wx+s,wz),2*s,height(wx,wz-s)-height(wx,wz+s)));
            vertices.Add(key,index); return index;
        }
        for(int z=domain.Min.y;z<domain.Max.y;z+=step)for(int x=domain.Min.x;x<domain.Max.x;x+=step)
        {
            bool masked=false;
            if(holes!=null)foreach(var hole in holes)if(hole.Intersects(x,z,step))
            { if(!hole.Contains(x,z,step))throw new ArgumentException("Mask cuts through a source cell; align it to the source grid.");masked=true;break; }
            if(masked)continue;
            int a=Vertex(x,z),b=Vertex(x,z+step),c=Vertex(x+step,z+step),d=Vertex(x+step,z);
            output.Triangle(a,b,c,new double3(0,1,0));output.Triangle(a,c,d,new double3(0,1,0));
        }
        var result=output.Finish();budget?.CompleteScope(scopeStart,result.PayloadBytes*2,persistentStart);return result;
    }

    private static void ValidateGrid(TerrainSculptFootprint footprint,int step)
    {
        if(step<=0 || step>16 || math.any(footprint.Max<=footprint.Min) || math.any(footprint.Min%step!=0) || math.any(footprint.Max%step!=0))
            throw new ArgumentException("Footprint must align to its positive source-grid step.");
        if(math.any(footprint.Min<int.MinValue+32)||math.any(footprint.Max>int.MaxValue-32))throw new ArgumentOutOfRangeException(nameof(footprint));
    }

    private static void ValidateEnvelope(ITerrainDensityField snapshot,TerrainSculptFootprint outer,
        TerrainDensityCellBounds inner,Func<double,double,double> height,TerrainDensityCellBounds collarInner)
    {
        long columns=checked(((long)outer.Max.x-outer.Min.x+1)*((long)outer.Max.y-outer.Min.y+1));
        if(columns>2000000 || inner.CellCount>2000000)throw new TerrainDensityCapacityException();
        double s=snapshot.Profile.Spacing;
        for(int z=outer.Min.y;z<=outer.Max.y;z++)for(int x=outer.Min.x;x<=outer.Max.x;x++)
        {
            if(snapshot.ReadEncoded(new int3(x,inner.Min.y,z))>=0 || snapshot.ReadEncoded(new int3(x,inner.Max.y,z))<0)
                throw new ArgumentException("Vertical envelope must have solid lower and air upper guards; expand it before publishing.");
            if(x>collarInner.Min.x && x<collarInner.Max.x && z>collarInner.Min.z && z<collarInner.Max.z)continue;
            if(!snapshot.HasOverrides(new int3(x,inner.Min.y,z),new int3(x,inner.Max.y,z)))continue;
            double h=height(x*s,z*s);
            for(int y=inner.Min.y;y<=inner.Max.y;y++)
                if(snapshot.ReadEncoded(new int3(x,y,z))!=snapshot.Profile.Encode(y*s-h))
                    throw new ArgumentException("Excavation reaches the seam collar; expand the footprint before publishing.");
        }
    }

    private static Dictionary<ulong,int> BoundaryEdges(TerrainSculptMeshData mesh)
    {
        var result=new Dictionary<ulong,int>(EdgeComparer.Instance);
        for(int i=0;i<mesh.Triangles.Length;i+=3)for(int j=0;j<3;j++)
        { ulong edge=Edge(mesh.Triangles[i+j],mesh.Triangles[i+(j+1)%3]);result.TryGetValue(edge,out int count);result[edge]=count+1; }
        foreach(int count in result.Values)if(count>2)throw new InvalidOperationException("Non-manifold volume mesh cannot be collared.");
        return result;
    }

    private static List<int> Curve(TerrainSculptMeshData mesh,Dictionary<ulong,int> edges,int fixedAxis,double plane,
        int along,double from,double to)
    {
        const double epsilon=1e-8;
        var found=new HashSet<int>();
        foreach(var pair in edges)if(pair.Value==1)
        {
            int a=(int)(pair.Key>>32),b=(int)(pair.Key&uint.MaxValue);
            if(Math.Abs(mesh.Positions[a][fixedAxis]-plane)<epsilon && Math.Abs(mesh.Positions[b][fixedAxis]-plane)<epsilon)
            {found.Add(a);found.Add(b);}
        }
        var curve=new List<int>(found);curve.Sort((a,b)=>mesh.Positions[a][along].CompareTo(mesh.Positions[b][along]));
        if(curve.Count<2 || Math.Abs(mesh.Positions[curve[0]][along]-from)>epsilon || Math.Abs(mesh.Positions[curve[curve.Count-1]][along]-to)>epsilon)
            throw new InvalidOperationException("Baseline seam contour is incomplete.");
        for(int i=1;i<curve.Count;i++)
            if(mesh.Positions[curve[i]][along]-mesh.Positions[curve[i-1]][along]<=0 ||
                !edges.TryGetValue(Edge(curve[i-1],curve[i]),out int count)||count!=1)
                throw new InvalidOperationException("Seam requires one connected heightfield contour, not multiple floors or disconnected edges.");
        return curve;
    }

    private static void Join(TerrainSculptMeshBuffer output,List<int> inner,List<int> outer,int axis)
    {
        int i=0,j=0;
        while(i+1<inner.Count||j+1<outer.Count)
        {
            bool nextInner=j+1==outer.Count||(i+1<inner.Count&&output.Positions[inner[i+1]][axis]<=output.Positions[outer[j+1]][axis]);
            if(nextInner){output.Triangle(inner[i],outer[j],inner[i+1],new double3(0,1,0));i++;}
            else{output.Triangle(inner[i],outer[j],outer[j+1],new double3(0,1,0));j++;}
        }
    }
    private static void RequireSameCorner(TerrainSculptMeshData mesh,int a,int b)
    { if(a!=b || math.distance(mesh.Positions[a],mesh.Positions[b])>1e-10)throw new InvalidOperationException("Collar corners do not share a canonical vertex."); }
}
