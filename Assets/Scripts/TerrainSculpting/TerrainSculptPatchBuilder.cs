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
/// collider collars consume the actual source layout; no skirt, overlap, or shader-only hole.
/// </summary>
public static class TerrainSculptPatchBuilder
{
    private static readonly Unity.Profiling.ProfilerMarker SeamMarker=new Unity.Profiling.ProfilerMarker("FS.Streaming.Sculpt.Seams");

    public static TerrainSculptMeshData BuildPatch(ITerrainDensityField snapshot,
        TerrainSculptFootprint footprint, int minY, int maxY, Func<double,double,double> baselineHeight, int outerStep = 1,
        TerrainSculptBuildBudget budget = null)
        => BuildPatchCached(snapshot,footprint,minY,maxY,baselineHeight,outerStep,budget,null);

    internal static TerrainSculptMeshData BuildPatchCached(ITerrainDensityField snapshot,
        TerrainSculptFootprint footprint,int minY,int maxY,Func<double,double,double> baselineHeight,int outerStep,
        TerrainSculptBuildBudget budget,TerrainSculptMeshCache.Builder cache,TerrainSculptFootprint? window=null,
        TerrainSourceSurfaceLayout? sourceLayout=null)
    {
        if (snapshot == null || baselineHeight == null) throw new ArgumentNullException(nameof(snapshot));
        snapshot.RequireAlive(); ValidateGrid(footprint, outerStep);
        var layout=sourceLayout??new TerrainSourceSurfaceLayout(snapshot.Profile.Spacing,outerStep,outerStep);
        if(layout.Spacing!=snapshot.Profile.Spacing||layout.BoundaryStep!=outerStep)throw new ArgumentException("Source layout does not match the density lattice.");
        int collar=layout.CollarCells;
        long scopeStart=budget?.ChargedBytes??0;
        long persistentStart=budget?.PersistentBytes??0;
        if (math.any(footprint.Max - footprint.Min <= collar * 2)) throw new ArgumentException("Patch needs an interior and an unchanged collar.");
        var global = new TerrainDensityCellBounds(new int3(footprint.Min.x + collar, minY, footprint.Min.y + collar),
            new int3(footprint.Max.x - collar, maxY, footprint.Max.y - collar));
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
        double spacing = snapshot.Profile.Spacing;
        double ix0 = bounds.Min.x * spacing, ix1 = bounds.Max.x * spacing;
        double iz0 = bounds.Min.z * spacing, iz1 = bounds.Max.z * spacing;
        bool natural=snapshot is TerrainDensitySnapshot naturalSnapshot&&naturalSnapshot.Generator is TerrainCaveDensityGenerator;
        // Interior source-chunk boundaries are ordinary shared volume faces, including cave
        // openings. Only the excavation group's actual outside edges get baseline collars.
        var west = bounds.Min.x==global.Min.x?TerrainBoundaryTracer.Trace(interior,0,2,iz0,iz1,budget,natural):null;
        var east = bounds.Max.x==global.Max.x?TerrainBoundaryTracer.Trace(interior,1,2,iz0,iz1,budget,natural):null;
        var south = bounds.Min.z==global.Min.z?TerrainBoundaryTracer.Trace(interior,4,0,ix0,ix1,budget,natural):null;
        var north = bounds.Max.z==global.Max.z?TerrainBoundaryTracer.Trace(interior,5,0,ix0,ix1,budget,natural):null;
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
        if(west!=null)TerrainSeamStitcher.Join(output,west,OuterLine(true,footprint.Min.x,bounds.Min.z,bounds.Max.z),2);
        if(east!=null)TerrainSeamStitcher.Join(output,east,OuterLine(true,footprint.Max.x,bounds.Min.z,bounds.Max.z),2);
        if(south!=null)TerrainSeamStitcher.Join(output,south,OuterLine(false,footprint.Min.y,bounds.Min.x,bounds.Max.x),0);
        if(north!=null)TerrainSeamStitcher.Join(output,north,OuterLine(false,footprint.Max.y,bounds.Min.x,bounds.Max.x),0);
        void Corner(int x, int z, int ix, int iz, int vertex)
        {
            int a = Outer(x,z), b = Outer(x,iz), d = Outer(ix,z);
            output.Triangle(a,b,vertex,new double3(0,1,0)); output.Triangle(a,vertex,d,new double3(0,1,0));
        }
        var w=west?.Vertices;var e=east?.Vertices;var so=south?.Vertices;var n=north?.Vertices;
        if(w!=null&&so!=null){RequireSameCorner(interior,w[0],so[0]);Corner(footprint.Min.x,footprint.Min.y,bounds.Min.x,bounds.Min.z,w[0]);}
        if(w!=null&&n!=null){RequireSameCorner(interior,w[w.Count-1],n[0]);Corner(footprint.Min.x,footprint.Max.y,bounds.Min.x,bounds.Max.z,w[w.Count-1]);}
        if(e!=null&&so!=null){RequireSameCorner(interior,e[0],so[so.Count-1]);Corner(footprint.Max.x,footprint.Min.y,bounds.Max.x,bounds.Min.z,e[0]);}
        if(e!=null&&n!=null){RequireSameCorner(interior,e[e.Count-1],n[n.Count-1]);Corner(footprint.Max.x,footprint.Max.y,bounds.Max.x,bounds.Max.z,e[e.Count-1]);}
        budget?.Charge((long)output.Positions.Count*16);
        var surfaceData=new float2[output.Positions.Count];Array.Copy(interior.SurfaceData,surfaceData,interior.SurfaceData.Length);
        if(natural)
        {
            // Preserve underground surfaces through the one-cell exterior collar. Only
            // unchanged original skin is replaced by the explicit stitched surface strip.
            var ring=new List<TerrainDensityCellBounds>();
            if(west!=null)ring.Add(new TerrainDensityCellBounds(new int3(domain.Min.x,minY,domain.Min.y),new int3(bounds.Min.x,maxY,domain.Max.y)));
            if(east!=null)ring.Add(new TerrainDensityCellBounds(new int3(bounds.Max.x,minY,domain.Min.y),new int3(domain.Max.x,maxY,domain.Max.y)));
            if(south!=null)ring.Add(new TerrainDensityCellBounds(new int3(bounds.Min.x,minY,domain.Min.y),new int3(bounds.Max.x,maxY,bounds.Min.z)));
            if(north!=null)ring.Add(new TerrainDensityCellBounds(new int3(bounds.Min.x,minY,bounds.Max.z),new int3(bounds.Max.x,maxY,domain.Max.y)));
            var allSurface=new List<float2>(surfaceData);
            foreach(var cells in ring)
            {
                if(cells.CellCount<=0)continue;
                var piece=TerrainDensityMesher.BuildCached(snapshot,cells,budget,cache);
                var remap=new Dictionary<int,int>();
                int Vertex(int source)
                {
                    if(remap.TryGetValue(source,out int index))return index;
                    index=output.AddStored(piece.Positions[source],piece.Normals[source]);allSurface.Add(piece.SurfaceData[source]);remap.Add(source,index);return index;
                }
                for(int i=0;i<piece.Triangles.Length;i+=3)
                {
                    int a=piece.Triangles[i],b=piece.Triangles[i+1],c=piece.Triangles[i+2];
                    if(Math.Max(piece.SurfaceData[a].x,Math.Max(piece.SurfaceData[b].x,piece.SurfaceData[c].x))<.999f)continue;
                    output.TriangleOriented(Vertex(a),Vertex(b),Vertex(c));
                }
            }
            surfaceData=allSurface.ToArray();
        }
        var result=output.Finish(interior.SampledBricks,interior.SkippedBricks,surfaceData:surfaceData);
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

    internal static void ValidateEnvelope(ITerrainDensityField snapshot,TerrainSculptFootprint outer,
        TerrainDensityCellBounds inner,Func<double,double,double> height,TerrainDensityCellBounds collarInner)
    {
        long columns=checked(((long)outer.Max.x-outer.Min.x+1)*((long)outer.Max.y-outer.Min.y+1));
        if(columns>2000000)throw new TerrainDensityCapacityException();
        double s=snapshot.Profile.Spacing;
        for(int z=outer.Min.y;z<=outer.Max.y;z++)for(int x=outer.Min.x;x<=outer.Max.x;x++)
        {
            if(snapshot.ReadEncoded(new int3(x,inner.Min.y,z))>=0 || snapshot.ReadEncoded(new int3(x,inner.Max.y,z))<0)
                throw new TerrainSculptGuardException(TerrainSculptBuildOutcome.NeedsVerticalGuard,"Vertical envelope must have solid lower and air upper guards.");
            if(x>collarInner.Min.x && x<collarInner.Max.x && z>collarInner.Min.z && z<collarInner.Max.z)continue;
            if(snapshot is TerrainDensitySnapshot natural&&natural.Generator is TerrainCaveDensityGenerator)
            {
                double surface=height(x*s,z*s);
                int near=TerrainDensityCoordinates.FloorIndex(surface/s);
                for(int y=near-2;y<=near+2;y++)
                    if(snapshot.ReadEncoded(new int3(x,y,z))!=snapshot.Profile.Encode(y*s-surface))
                        throw new TerrainSculptGuardException(TerrainSculptBuildOutcome.NeedsHorizontalGuard,"Natural opening reaches the exterior collar.");
                continue;
            }
            if(!snapshot.HasOverrides(new int3(x,inner.Min.y,z),new int3(x,inner.Max.y,z)))continue;
            double h=height(x*s,z*s);
            for(int y=inner.Min.y;y<=inner.Max.y;y++)
                if(snapshot.ReadEncoded(new int3(x,y,z))!=snapshot.Profile.Encode(y*s-h))
                    throw new TerrainSculptGuardException(TerrainSculptBuildOutcome.NeedsHorizontalGuard,"Excavation reaches the seam collar.");
        }
    }

    private static void RequireSameCorner(TerrainSculptMeshData mesh,int a,int b)
    { if(!math.all(mesh.Positions[a]==mesh.Positions[b]))throw new InvalidOperationException("Collar corners do not share a canonical vertex."); }
}
