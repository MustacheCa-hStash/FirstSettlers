using System;
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>Deterministic six-tetrahedra extraction; surfaces face from negative stone to nonnegative air.</summary>
public static class TerrainDensityMesher
{
    private static readonly int3[] Corners = { new int3(0,0,0), new int3(1,0,0), new int3(0,1,0), new int3(1,1,0),
        new int3(0,0,1), new int3(1,0,1), new int3(0,1,1), new int3(1,1,1) };
    // The x/z face diagonal is (0,0)-(1,1), matching the existing heightfield.
    private static readonly int[,] Tetrahedra = { {0,1,3,7}, {0,3,2,7}, {0,2,6,7}, {0,6,4,7}, {0,4,5,7}, {0,5,1,7} };

    private readonly struct Edge : IEquatable<Edge>
    {
        internal readonly int3 A, B;
        internal Edge(int3 a, int3 b, short va, short vb)
        {
            // All edges ending on an exact zero share one canonical lattice-point vertex.
            if (va == 0) b = a;
            else if (vb == 0) a = b;
            if (Compare(a, b) > 0) { int3 swap = a; a = b; b = swap; }
            A = a; B = b;
        }
        public bool Equals(Edge other) => math.all(A == other.A) && math.all(B == other.B);
        public override bool Equals(object obj) => obj is Edge other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(A.GetHashCode(), B.GetHashCode());
    }

    private static int Compare(int3 a, int3 b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z);

    public static TerrainSculptMeshData Build(ITerrainDensityField snapshot, TerrainDensityCellBounds bounds,
        long maximumCells = 2000000, TerrainSculptBuildBudget budget = null)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        snapshot.RequireAlive();
        if (bounds.CellCount <= 0 || bounds.CellCount > maximumCells) throw new TerrainDensityCapacityException();
        using var scratch = snapshot.ReserveScratch(19 * 19 * 19 * sizeof(short));
        long scopeStart=budget?.ChargedBytes??0;
        long persistentStart=budget?.PersistentBytes??0;
        var output = new TerrainSculptMeshBuffer(budget: budget);
        budget?.Charge(19 * 19 * 19 * sizeof(short) + 2048);
        var edges = new Dictionary<Edge, int>();
        var input = new short[19 * 19 * 19];
        var values = new short[8]; var positions = new int3[8]; var local = new int3[8];
        var stone = new int[4]; var air = new int[4];
        int sampled=0,skipped=0;
        int3 first = TerrainDensityCoordinates.BrickOf(bounds.Min), last = TerrainDensityCoordinates.BrickOf(bounds.Max - 1);
        for (int bz = first.z; bz <= last.z; bz++) for (int by = first.y; by <= last.y; by++) for (int bx = first.x; bx <= last.x; bx++)
        {
            budget?.Check();
            int3 origin = TerrainDensityCoordinates.Origin(new int3(bx, by, bz));
            int3 lo = math.max(bounds.Min - origin, 0), hi = math.min(bounds.Max - origin, 16);
            if(snapshot.IsSurfaceFree(new TerrainDensityCellBounds(origin+lo,origin+hi))){skipped++;continue;}
            sampled++;
            // Reconstruct one leased snapshot's halo. No reads from a changing live world.
            snapshot.FillMeshHalo(origin-1,input);
            for (int z = lo.z; z < hi.z; z++) for (int y = lo.y; y < hi.y; y++) for (int x = lo.x; x < hi.x; x++)
            {
                var cell = new int3(x,y,z); int negative = 0;
                for (int i = 0; i < 8; i++)
                {
                    local[i] = cell + Corners[i] + 1; positions[i] = origin + cell + Corners[i];
                    values[i] = input[At(local[i])]; if (values[i] < 0) negative++;
                }
                if (negative == 0 || negative == 8) continue;
                for (int t = 0; t < 6; t++)
                {
                    int ns = 0, na = 0; double3 sc = 0, ac = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        int i = Tetrahedra[t,j];
                        if (values[i] < 0) { stone[ns++] = i; sc += (double3)positions[i]; }
                        else { air[na++] = i; ac += (double3)positions[i]; }
                    }
                    if (ns == 0 || na == 0) continue;
                    double3 toward = ac / na - sc / ns;
                    int Vertex(int a, int b)
                    {
                        var key = new Edge(positions[a], positions[b], values[a], values[b]);
                        if (edges.TryGetValue(key, out int index)) return index;
                        // Canonical endpoint order makes interpolation bit-identical in other bricks.
                        if (Compare(positions[a], positions[b]) > 0) { int swap = a; a = b; b = swap; }
                        double fraction = (double)values[a] / ((int)values[a] - values[b]);
                        double3 p = ((double3)positions[a] + (double3)(positions[b] - positions[a]) * fraction) * snapshot.Profile.Spacing;
                        double3 normal = math.lerp(Gradient(input, local[a]), Gradient(input, local[b]), fraction);
                        index = output.Add(p, normal); edges.Add(key, index); return index;
                    }
                    if (ns == 1) output.Triangle(Vertex(stone[0],air[0]),Vertex(stone[0],air[1]),Vertex(stone[0],air[2]),toward);
                    else if (na == 1) output.Triangle(Vertex(air[0],stone[0]),Vertex(air[0],stone[1]),Vertex(air[0],stone[2]),toward);
                    else
                    {
                        int a = Vertex(stone[0],air[0]), b = Vertex(stone[0],air[1]);
                        int c = Vertex(stone[1],air[0]), d = Vertex(stone[1],air[1]);
                        output.Triangle(a,b,d,toward); output.Triangle(a,d,c,toward);
                    }
                }
            }
        }
        var result=output.Finish(sampled,skipped);
        budget?.CompleteScope(scopeStart,result.PayloadBytes*2,persistentStart);
        return result;
    }

    private static int At(int3 p) => p.x + 19 * (p.y + 19 * p.z);
    private static double3 Gradient(short[] v, int3 p) => new double3(
        (int)v[At(p + new int3(1,0,0))] - v[At(p - new int3(1,0,0))],
        (int)v[At(p + new int3(0,1,0))] - v[At(p - new int3(0,1,0))],
        (int)v[At(p + new int3(0,0,1))] - v[At(p - new int3(0,0,1))]);

    private readonly struct CachedVertex:IEquatable<CachedVertex>
    {
        private readonly double3 position;
        private readonly float3 normal;
        internal CachedVertex(double3 p,float3 n){position=p;normal=n;}
        public bool Equals(CachedVertex other)=>math.all(position==other.position)&&math.all(normal==other.normal);
        public override bool Equals(object obj)=>obj is CachedVertex vertex&&Equals(vertex);
        public override int GetHashCode()=>HashCode.Combine(position.GetHashCode(),normal.GetHashCode());
    }

    internal static TerrainSculptMeshData BuildCached(ITerrainDensityField field,TerrainDensityCellBounds bounds,
        TerrainSculptBuildBudget budget,TerrainSculptMeshCache.Builder cache)
    {
        if(cache==null)return Build(field,bounds,budget:budget);
        long start=budget.ChargedBytes,persistent=budget.PersistentBytes;
        var output=new TerrainSculptMeshBuffer(budget:budget);
        var vertices=new Dictionary<CachedVertex,int>();
        int3 first=TerrainDensityCoordinates.BrickOf(bounds.Min),last=TerrainDensityCoordinates.BrickOf(bounds.Max-1);
        int sampled=0,skipped=0;
        for(int z=first.z;z<=last.z;z++)for(int y=first.y;y<=last.y;y++)for(int x=first.x;x<=last.x;x++)
        {
            budget.Check();int3 origin=TerrainDensityCoordinates.Origin(new int3(x,y,z));
            var piece=cache.Get(field,new TerrainDensityCellBounds(math.max(bounds.Min,origin),math.min(bounds.Max,origin+16)));
            sampled+=piece.SampledBricks;skipped+=piece.SkippedBricks;
            budget.Charge((long)piece.Positions.Length*sizeof(int));
            var remap=new int[piece.Positions.Length];
            for(int i=0;i<remap.Length;i++)
            {
                var key=new CachedVertex(piece.Positions[i],piece.Normals[i]);
                if(!vertices.TryGetValue(key,out int index))
                {index=output.AddStored(piece.Positions[i],piece.Normals[i]);vertices.Add(key,index);}
                remap[i]=index;
            }
            budget.Charge((long)piece.Triangles.Length*22);
            foreach(int index in piece.Triangles)output.Triangles.Add(remap[index]);
        }
        var result=output.Finish(sampled,skipped);budget.CompleteScope(start,result.PayloadBytes*2,persistent);return result;
    }
}
