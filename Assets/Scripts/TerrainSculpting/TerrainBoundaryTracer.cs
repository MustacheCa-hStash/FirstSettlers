using System;
using System.Collections.Generic;

public enum TerrainSculptBuildOutcome { Complete, NeedsHorizontalGuard, NeedsVerticalGuard, InvalidBoundaryTopology, UnsupportedSourceLayout }

public sealed class TerrainSculptTopologyException:InvalidOperationException
{
    public TerrainSculptBuildOutcome Outcome=>TerrainSculptBuildOutcome.InvalidBoundaryTopology;
    public TerrainSculptTopologyException(string message):base(message) { }
}

internal sealed class TerrainBoundaryChain
{
    internal readonly List<int> Vertices=new List<int>();
    // Does the volume half-edge run in the same direction as this ascending walk?
    internal bool Forward;
}

internal static class TerrainBoundaryTracer
{
    internal static TerrainBoundaryChain Trace(TerrainSculptMeshData mesh,int face,int along,double from,double to,
        TerrainSculptBuildBudget budget)
    {
        if(mesh.Boundary==null)throw new TerrainSculptTopologyException("Volume boundary provenance is missing.");
        var edges=new List<TerrainBoundaryEdge>();
        var adjacent=new Dictionary<TerrainSurfaceVertexId,List<int>>();
        TerrainSculptTopologyException Failure(string message)=>new TerrainSculptTopologyException($"{message} Face {face}; {edges.Count} edges, {adjacent.Count} geometric vertices.");
        foreach(var edge in mesh.Boundary.Edges)if((edge.Faces&(1<<face))!=0)
        {
            budget?.Charge(128);int i=edges.Count;edges.Add(edge);Link(edge.A,i);Link(edge.B,i);
        }
        void Link(TerrainSurfaceVertexId id,int i)
        {
            if(!adjacent.TryGetValue(id,out var list)){budget?.Charge(192);list=new List<int>(2);adjacent.Add(id,list);}
            list.Add(i);if(list.Count>2)throw Failure("The external collar boundary branches.");
        }
        var endpoints=new List<TerrainSurfaceVertexId>();
        foreach(var pair in adjacent)if(pair.Value.Count==1)endpoints.Add(pair.Key);
        if(endpoints.Count!=2)throw Failure($"The external collar needs one open connected contour; found {endpoints.Count} endpoints.");
        int Vertex(TerrainSurfaceVertexId id)
        { var e=edges[adjacent[id][0]];return e.A.Equals(id)?e.VertexA:e.VertexB; }
        endpoints.Sort((a,b)=> { int c=mesh.Positions[Vertex(a)][along].CompareTo(mesh.Positions[Vertex(b)][along]);return c!=0?c:a.CompareTo(b); });
        var chain=new TerrainBoundaryChain();var current=endpoints[0];int previous=-1,visited=0;
        chain.Vertices.Add(Vertex(current));
        while(true)
        {
            budget?.Check();var links=adjacent[current];int next=links[0]==previous?(links.Count==2?links[1]:-1):links[0];
            if(next<0)break;
            var edge=edges[next];bool forward=edge.A.Equals(current);
            if(visited==0)chain.Forward=forward;
            else if(chain.Forward!=forward)throw Failure("The collar contour has inconsistent winding.");
            current=forward?edge.B:edge.A;chain.Vertices.Add(forward?edge.VertexB:edge.VertexA);
            previous=next;if(++visited>edges.Count)throw Failure("The collar contour loops.");
        }
        const double epsilon=1e-8;
        if(visited!=edges.Count||Math.Abs(mesh.Positions[chain.Vertices[0]][along]-from)>epsilon||
            Math.Abs(mesh.Positions[chain.Vertices[chain.Vertices.Count-1]][along]-to)>epsilon)
            throw Failure($"The external collar is disconnected or incomplete; visited {visited} edges.");
        for(int i=1;i<chain.Vertices.Count;i++)
            if(mesh.Positions[chain.Vertices[i]][along]<mesh.Positions[chain.Vertices[i-1]][along]-epsilon)
                throw Failure("The external collar folds horizontally.");
        return chain;
    }
}
