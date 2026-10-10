using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Closed boxes with longitudinal wood UVs and capped end-grain. No mesh boolean operations.</summary>
public static class BuildTimberMesh
{
    public static Mesh Create(string name,IReadOnlyList<BuildTimberPart> parts,bool roofAtlas=false,bool plain=false)
    {
        var v=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
        foreach(var part in parts)Append(part,v,n,uv,t,roofAtlas,plain);
        var mesh=new Mesh{name=name,indexFormat=v.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
        mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateBounds();return mesh;
    }
    private static void Append(BuildTimberPart part,List<Vector3> vertices,List<Vector3> normals,List<Vector2> uv,List<int> triangles,bool roof,bool plain)
    {
        Bounds b=part.bounds;Vector3 lo=b.min,hi=b.max;
        Vector3[] v={new(lo.x,lo.y,lo.z),new(hi.x,lo.y,lo.z),new(lo.x,hi.y,lo.z),new(hi.x,hi.y,lo.z),
            new(lo.x,lo.y,hi.z),new(hi.x,lo.y,hi.z),new(lo.x,hi.y,hi.z),new(hi.x,hi.y,hi.z)};
        int[][] faces={new[]{0,2,3,1},new[]{4,5,7,6},new[]{0,1,5,4},new[]{2,6,7,3},new[]{0,4,6,2},new[]{1,3,7,5}};
        foreach(var f in faces)
        {
            Vector3 normal=Vector3.Cross(v[f[1]]-v[f[0]],v[f[2]]-v[f[0]]).normalized;
            Bounds reference=part.hasUvBounds?part.uvBounds:b;
            int seed=part.uvSeed;
            if(seed==0){var pos=BuildGeometry.Ticks(reference.center);unchecked{seed=pos.x*73856093^pos.y*19349663^pos.z*83492791^part.grainAxis*31;}}
            int start=vertices.Count;
            foreach(int index in f)
            {
                Vector3 p=v[index];Vector2 tex=BuildTrimUV.Sample(p,normal,reference,part.grainAxis,seed);
                if(plain)tex=Vector2.zero;
                if(roof)tex=new Vector2(tex.x*.5f,.5f+tex.y*.5f);
                vertices.Add(p);normals.Add(normal);uv.Add(tex);
            }
            triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start);triangles.Add(start+2);triangles.Add(start+3);
        }
    }
}
