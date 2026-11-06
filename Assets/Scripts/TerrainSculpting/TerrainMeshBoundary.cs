using System;
using System.Collections.Generic;
using Unity.Mathematics;

// Geometry identity is the global lattice edge, never a shading vertex or rounded position.
internal readonly struct TerrainSurfaceVertexId : IEquatable<TerrainSurfaceVertexId>, IComparable<TerrainSurfaceVertexId>
{
    internal readonly int3 A, B;
    internal TerrainSurfaceVertexId(int3 a, int3 b) { A=a; B=b; }
    private static int Compare(int3 a,int3 b)=>a.x!=b.x?a.x.CompareTo(b.x):a.y!=b.y?a.y.CompareTo(b.y):a.z.CompareTo(b.z);
    public int CompareTo(TerrainSurfaceVertexId other) { int c=Compare(A,other.A);return c!=0?c:Compare(B,other.B); }
    public bool Equals(TerrainSurfaceVertexId other)=>math.all(A==other.A)&&math.all(B==other.B);
    public override bool Equals(object obj)=>obj is TerrainSurfaceVertexId id&&Equals(id);
    public override int GetHashCode()=>HashCode.Combine(A.GetHashCode(),B.GetHashCode());
    internal int Faces(TerrainDensityCellBounds b)
    {
        int mask=0;
        for(int axis=0;axis<3;axis++)
        {
            if(A[axis]==b.Min[axis]&&B[axis]==b.Min[axis])mask|=1<<(axis*2);
            if(A[axis]==b.Max[axis]&&B[axis]==b.Max[axis])mask|=2<<(axis*2);
        }
        return mask;
    }
}

internal readonly struct TerrainBoundaryEdge
{
    internal readonly TerrainSurfaceVertexId A,B;
    internal readonly int VertexA,VertexB,Faces;
    internal TerrainBoundaryEdge(TerrainSurfaceVertexId a,TerrainSurfaceVertexId b,int va,int vb,int faces)
    { A=a;B=b;VertexA=va;VertexB=vb;Faces=faces; }
}

internal sealed class TerrainMeshBoundary
{
    internal readonly TerrainBoundaryEdge[] Edges;
    internal long PayloadBytes=>(long)Edges.Length*64;
    internal TerrainMeshBoundary(TerrainBoundaryEdge[] edges) { Edges=edges; }
    internal sealed class Builder
    {
        private readonly struct Key:IEquatable<Key>
        {
            private readonly TerrainSurfaceVertexId a,b;
            internal Key(TerrainBoundaryEdge e) { bool order=e.A.CompareTo(e.B)<0;a=order?e.A:e.B;b=order?e.B:e.A; }
            public bool Equals(Key other)=>a.Equals(other.a)&&b.Equals(other.b);
            public override bool Equals(object obj)=>obj is Key k&&Equals(k);
            public override int GetHashCode()=>HashCode.Combine(a.GetHashCode(),b.GetHashCode());
        }
        private readonly Dictionary<Key,TerrainBoundaryEdge> open=new Dictionary<Key,TerrainBoundaryEdge>();
        private readonly HashSet<Key> closed=new HashSet<Key>();
        private readonly TerrainSculptBuildBudget budget;
        internal Builder(TerrainSculptBuildBudget budget) { this.budget=budget; }
        internal void Add(TerrainBoundaryEdge edge)
        {
            if(edge.Faces==0||edge.A.Equals(edge.B))return;
            var key=new Key(edge);
            if(closed.Contains(key))throw new TerrainSculptTopologyException("More than two triangles own a boundary edge.");
            if(open.TryGetValue(key,out var old))
            {
                if(!old.A.Equals(edge.B)||!old.B.Equals(edge.A))throw new TerrainSculptTopologyException("Boundary triangles have inconsistent orientation.");
                open.Remove(key);closed.Add(key);return;
            }
            budget?.Charge(256);open.Add(key,edge);
        }
        internal TerrainMeshBoundary Finish()
        {
            var edges=new List<TerrainBoundaryEdge>(open.Values);
            edges.Sort((a,b)=> { int c=a.A.CompareTo(b.A);return c!=0?c:a.B.CompareTo(b.B); });
            return new TerrainMeshBoundary(edges.ToArray());
        }
    }
    internal static TerrainMeshBoundary Extract(TerrainSculptMeshBuffer mesh,Dictionary<int,TerrainSurfaceVertexId> ids,
        TerrainDensityCellBounds bounds,TerrainSculptBuildBudget budget)
    {
        var boundary=new Builder(budget);
        for(int i=0;i<mesh.Triangles.Count;i+=3)for(int j=0;j<3;j++)
        {
            int va=mesh.Triangles[i+j],vb=mesh.Triangles[i+(j+1)%3];
            if(ids.TryGetValue(va,out var a)&&ids.TryGetValue(vb,out var b))
                boundary.Add(new TerrainBoundaryEdge(a,b,va,vb,a.Faces(bounds)&b.Faces(bounds)));
        }
        return boundary.Finish();
    }
}
