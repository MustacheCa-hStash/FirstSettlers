using System;
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>Connected planar clustering with locked tile seams, normal/material guards and
/// flip rejection. Render and collision consume the same result; density remains unchanged.</summary>
internal static class TerrainCaveMeshSimplifier
{
    private readonly struct Key:IEquatable<Key>
    {
        private readonly int3 cell,normal;
        private readonly int surface;
        internal Key(double3 p,float3 n,float material)
        {cell=(int3)math.floor(p/1.2);normal=(int3)math.floor(n*4);surface=(int)Math.Round(material*8);}
        public bool Equals(Key other)=>math.all(cell==other.cell)&&math.all(normal==other.normal)&&surface==other.surface;
        public override bool Equals(object o)=>o is Key key&&Equals(key);
        public override int GetHashCode()=>HashCode.Combine(cell,normal,surface);
    }
    internal static TerrainSculptMeshData Simplify(TerrainSculptMeshData source,TerrainDensitySnapshot snapshot,TerrainSculptBuildBudget budget)
    {
        int count=source.Positions.Length;if(count<100)return source;
        long start=budget.ChargedBytes,persistent=budget.PersistentBytes;
        budget.Charge((long)count*160+(long)source.Triangles.Length*4);
        var parent=new int[count];var key=new Key[count];var locked=new bool[count];
        // Collars and concatenated source pieces have shared geometric positions
        // but separate vertex indices. Their open topology must survive clustering
        // on both sides, including collar planes one sample inside a tile boundary.
        var boundary=new HashSet<ulong>();int boundaryCapacity=0;
        void Edge(int a,int b)
        {
            ulong edge=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
            if(boundary.Remove(edge))return;
            if(boundary.Count>=boundaryCapacity){budget.Charge(48*1024);boundaryCapacity+=1024;}
            boundary.Add(edge);
        }
        for(int i=0;i<source.Triangles.Length;i+=3)
        {Edge(source.Triangles[i],source.Triangles[i+1]);Edge(source.Triangles[i+1],source.Triangles[i+2]);Edge(source.Triangles[i+2],source.Triangles[i]);}
        foreach(ulong edge in boundary){locked[(int)(edge>>32)]=true;locked[(int)(uint)edge]=true;}
        var pins=new List<TerrainDensityCellBounds>();foreach(var brick in snapshot.Bricks.Keys)
        {int3 p=TerrainDensityCoordinates.Origin(brick);pins.Add(new TerrainDensityCellBounds(p-3,p+19));}
        double spacing=snapshot.Profile.Spacing,tile=TerrainCaveBatchBuilder.TileMetres;
        for(int i=0;i<count;i++)
        {
            var p=source.Positions[i];parent[i]=i;key[i]=new Key(p,source.Normals[i],source.SurfaceData?[i].x??0);
            double3 grid=p/tile;
            locked[i]|=math.cmin(math.abs(grid-math.round(grid)))<1e-7;
            if(source.SurfaceData!=null&&source.SurfaceData[i].x>.001f&&source.SurfaceData[i].x<.999f)locked[i]=true;
            foreach(var pin in pins)if(math.all(p>=(double3)pin.Min*spacing)&&math.all(p<=(double3)pin.Max*spacing)){locked[i]=true;break;}
        }
        int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
        void Join(int a,int b)
        {
            if(locked[a]||locked[b]||!key[a].Equals(key[b])||math.dot(source.Normals[a],source.Normals[b])<.995f)return;
            // Limit normal displacement even on inclined floors and curved rock faces.
            double3 delta=source.Positions[a]-source.Positions[b];
            if(Math.Abs(math.dot(delta,(double3)source.Normals[a]))>.02||Math.Abs(math.dot(delta,(double3)source.Normals[b]))>.02)return;
            int ra=Root(a),rb=Root(b);if(ra!=rb)parent[Math.Max(ra,rb)]=Math.Min(ra,rb);
        }
        for(int i=0;i<source.Triangles.Length;i+=3)
        {if((i&4095)==0)budget.Check();int a=source.Triangles[i],b=source.Triangles[i+1],c=source.Triangles[i+2];Join(a,b);Join(b,c);Join(c,a);}
        var sums=new double3[count];var normals=new float3[count];var counts=new int[count];
        for(int i=0;i<count;i++){int r=Root(i);sums[r]+=source.Positions[i];normals[r]+=source.Normals[i];counts[r]++;}
        for(int i=0;i<count;i++)if(counts[i]>0){sums[i]/=counts[i];normals[i]=math.normalizesafe(normals[i],source.Normals[i]);}
        var restore=new bool[count];
        for(int i=0;i<count;i++)
        {
            int r=Root(i);
            if(Math.Abs(math.dot(sums[r]-source.Positions[i],(double3)source.Normals[i]))>.025)restore[r]=true;
        }
        double3 Position(int i)=>restore[Root(i)]?source.Positions[i]:sums[Root(i)];
        int VertexId(int i)=>restore[Root(i)]?i:Root(i);
        for(int pass=0;pass<4;pass++)
        {
            bool changed=false;
            for(int i=0;i<source.Triangles.Length;i+=3)
            {
                int a=source.Triangles[i],b=source.Triangles[i+1],c=source.Triangles[i+2];
                if(VertexId(a)==VertexId(b)||VertexId(a)==VertexId(c)||VertexId(b)==VertexId(c))continue;
                double3 before=math.cross(source.Positions[b]-source.Positions[a],source.Positions[c]-source.Positions[a]);
                double3 after=math.cross(Position(b)-Position(a),Position(c)-Position(a));
                if(math.lengthsq(after)>1e-22&&math.dot(math.normalizesafe(before),math.normalizesafe(after))>.9)continue;
                foreach(int v in new[]{a,b,c}){int r=Root(v);if(!restore[r]){restore[r]=true;changed=true;}}
            }
            if(!changed)break;
            if(pass==3){budget.CompleteScope(start,0,persistent);return source;}
        }
        var output=new TerrainSculptMeshBuffer(budget:budget);var attributes=new List<float2>();var ids=new Dictionary<int,int>();
        int Vertex(int i)
        {
            int id=VertexId(i);if(ids.TryGetValue(id,out int index))return index;
            index=output.AddStored(Position(i),restore[Root(i)]?source.Normals[i]:normals[Root(i)]);ids.Add(id,index);attributes.Add(source.SurfaceData?[i]??default);return index;
        }
        for(int i=0;i<source.Triangles.Length;i+=3)
        {
            int a=source.Triangles[i],b=source.Triangles[i+1],c=source.Triangles[i+2];
            if(VertexId(a)==VertexId(b)||VertexId(a)==VertexId(c)||VertexId(b)==VertexId(c))continue;
            output.TriangleOriented(Vertex(a),Vertex(b),Vertex(c));
        }
        var result=output.Finish(source.SampledBricks,source.SkippedBricks,surfaceData:source.SurfaceData!=null?attributes.ToArray():null);
        budget.CompleteScope(start,result.PayloadBytes*2,persistent);return result;
    }
}
