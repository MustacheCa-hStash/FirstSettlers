using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>Reversible SS23 tail pockets in compatible solid masonry. Canonical assets stay intact.</summary>
public static class BuildCorbelPocket
{
    public const string ShelfId = "build.furniture.ss23-sandstone-corbel-shelf";
    public static readonly Bounds[] Tails = {
        new(new Vector3(.25f,.1875f,-.125f),new Vector3(.25f,.375f,.25f)),
        new(new Vector3(.75f,.1875f,-.125f),new Vector3(.25f,.375f,.25f))
    };

    public static List<Bounds> Cuts(BuildPieceRecord wall, IReadOnlyList<BuildPieceRecord> pieces, Bounds visualBounds)
    {
        var cuts = new List<Bounds>(); var d = wall.Definition;
        // Rectangular, solid masonry only. Excludes thin niche backs, openings, gables and wood.
        if (d.kind != BuildPartKind.Wall || !d.preserveAuthoredMesh || d.IsHalfGable ||
            d.material == null || d.material.shader.name != "Custom/BakedStoneWallMatte" ||
            d.occupiedVolumes?.Length > 0 || d.solidBoxes?.Length != 1 ||
            (d.solidBoxes[0].size-d.LocalBounds.size).sqrMagnitude > .000001f || d.LocalBounds.size.z < .49f) return cuts;
        var inverse = Quaternion.Inverse(BuildGeometry.Rotation(wall.WorldYawStep));
        foreach (var shelf in pieces)
        {
            if (shelf.Definition.contentId != ShelfId) continue;
            int turn = BuildGeometry.Turn(shelf.WorldYawStep - wall.WorldYawStep);
            if (turn != 0 && turn != 4) continue;
            var offset = inverse * (shelf.Origin - wall.Origin);
            float face = turn == 0 ? d.LocalBounds.max.z : d.LocalBounds.min.z;
            if (Mathf.Abs(offset.z - face) > .001f) continue;
            var q = BuildGeometry.Rotation(turn); var pending = new List<Bounds>(); bool valid = true;
            foreach (var tail in Tails)
            {
                var cut = BuildRenderer.TransformBounds(tail, Matrix4x4.TRS(offset,q,Vector3.one));
                var inner = visualBounds; inner.Expand(.002f);
                if (!inner.Contains(cut.min) || !inner.Contains(cut.max)) { valid = false; break; }
                pending.Add(cut);
            }
            if (valid) foreach (var cut in pending)
                if (!cuts.Exists(b => (b.center-cut.center).sqrMagnitude < .000001f && (b.size-cut.size).sqrMagnitude < .000001f)) cuts.Add(cut);
        }
        cuts.Sort((a,b) => { for(int i=0;i<3;i++){int n=a.min[i].CompareTo(b.min[i]);if(n!=0)return n;}return 0; });
        return cuts;
    }

    public static Mesh Apply(BuildPieceRecord wall, IReadOnlyList<BuildPieceRecord> pieces, Mesh source,
        List<Bounds> boxes, Dictionary<string,Mesh> cache)
    {
        if(wall.Definition.kind!=BuildPartKind.Wall || wall.Definition.material==null || wall.Definition.material.shader.name!="Custom/BakedStoneWallMatte")return source;
        var cuts = Cuts(wall,pieces,source.bounds); if(cuts.Count==0)return source;
        var key = new StringBuilder("corbel:").Append(source.GetInstanceID());
        foreach(var cut in cuts) for(int i=0;i<3;i++)
            key.Append(':').Append(cut.min[i].ToString("F4",CultureInfo.InvariantCulture)).Append('/').Append(cut.max[i].ToString("F4",CultureInfo.InvariantCulture));
        string name=key.ToString();
        if(!cache.TryGetValue(name,out var fitted)){fitted=Clip(source,cuts);cache.Add(name,fitted);}
        var current=new List<Bounds>(boxes);
        foreach(var cut in cuts)
        {
            var next=new List<Bounds>();
            foreach(var b in current)
            {
                Vector3 lo=Vector3.Max(b.min,cut.min),hi=Vector3.Min(b.max,cut.max);
                if(hi.x-lo.x<.0001f||hi.y-lo.y<.0001f||hi.z-lo.z<.0001f){next.Add(b);continue;}
                void Add(Vector3 a,Vector3 z){if(z.x-a.x>.0001f&&z.y-a.y>.0001f&&z.z-a.z>.0001f)next.Add(new Bounds((a+z)*.5f,z-a));}
                Add(b.min,new Vector3(lo.x,b.max.y,b.max.z));Add(new Vector3(hi.x,b.min.y,b.min.z),b.max);
                Add(new Vector3(lo.x,b.min.y,b.min.z),new Vector3(hi.x,lo.y,b.max.z));
                Add(new Vector3(lo.x,hi.y,b.min.z),new Vector3(hi.x,b.max.y,b.max.z));
                Add(new Vector3(lo.x,lo.y,b.min.z),new Vector3(hi.x,hi.y,lo.z));
                Add(new Vector3(lo.x,lo.y,hi.z),new Vector3(hi.x,hi.y,b.max.z));
            }
            current=next;
        }
        boxes.Clear();boxes.AddRange(current);return fitted;
    }

    private struct Vertex
    {
        public Vector3 p,n; public Vector2 uv;
        public static Vertex Lerp(Vertex a,Vertex b,float t)=>new(){p=Vector3.Lerp(a.p,b.p,t),n=Vector3.Lerp(a.n,b.n,t),uv=Vector2.Lerp(a.uv,b.uv,t)};
    }
    // Subtract an axis-aligned box from each source triangle, interpolating its original UVs.
    // Tail solids fill the pockets completely, so hidden cavity caps would be duplicate interfaces.
    public static Mesh Clip(Mesh source,IReadOnlyList<Bounds> cuts)
    {
        var vs=source.vertices;var ns=source.normals;var us=source.uv;var indices=source.triangles;
        var polygons=new List<List<Vertex>>();
        for(int i=0;i<indices.Length;i+=3)
        {var poly=new List<Vertex>();for(int j=0;j<3;j++){int k=indices[i+j];poly.Add(new Vertex{p=vs[k],n=ns[k],uv=us[k]});}polygons.Add(poly);}
        foreach(var originalCut in cuts)
        {
            var cut=originalCut;cut.Expand(.00002f);var remaining=new List<List<Vertex>>();
            foreach(var poly in polygons)
            {
                var inside=poly;
                for(int plane=0;plane<6&&inside.Count>=3;plane++)
                {
                    int axis=plane/2;bool lower=plane%2==0;float level=lower?cut.min[axis]:cut.max[axis];
                    var within=new List<Vertex>();var outside=new List<Vertex>();
                    for(int i=0;i<inside.Count;i++)
                    {
                        var a=inside[i];var b=inside[(i+1)%inside.Count];float da=lower?a.p[axis]-level:level-a.p[axis];float db=lower?b.p[axis]-level:level-b.p[axis];
                        bool ia=da>=0,ib=db>=0;(ia?within:outside).Add(a);
                        if(ia!=ib){var edge=Vertex.Lerp(a,b,da/(da-db));within.Add(edge);outside.Add(edge);}
                    }
                    if(outside.Count>=3)remaining.Add(outside);inside=within;
                }
            }
            polygons=remaining;
        }
        var points=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        foreach(var poly in polygons)for(int j=1;j+1<poly.Count;j++)
        {
            if(Vector3.Cross(poly[j].p-poly[0].p,poly[j+1].p-poly[0].p).sqrMagnitude<1e-14f)continue;
            foreach(var v in new[]{poly[0],poly[j],poly[j+1]}){triangles.Add(points.Count);points.Add(v.p);normals.Add(v.n.normalized);uv.Add(v.uv);}
        }
        var result=new Mesh{name=source.name+" fitted corbel pockets"};result.SetVertices(points);result.SetNormals(normals);result.SetUVs(0,uv);result.SetTriangles(triangles,0);result.RecalculateBounds();result.RecalculateTangents();return result;
    }
}
