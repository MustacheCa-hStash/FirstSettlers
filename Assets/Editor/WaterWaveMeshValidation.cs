using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WaterWaveMeshValidation
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    [MenuItem("Tools/Terrain/Validate Wave Water Mesh")]
    public static void Run()
    {
        foreach (int size in new[] { 19, 32, 128 }) foreach (float scale in new[] { .3f, 1.1f })
        {
            Mesh Make(int origin)
            {
                int width = size+3;
                var values = new WaterState[width*width];
                var terrain = new float[width*width];
                for (int x=0;x<width;x++) for(int z=0;z<width;z++)
                { values[x*width+z]=WaterState.Deep; terrain[x*width+z]=(14.4f-1.2f-.03f*(origin+x-1)-.01f*(z-1))/(200*scale); }
                using var states = new NativeArray<WaterState>(values, Allocator.TempJob);
                using var heights = new NativeArray<float>(terrain, Allocator.TempJob);
                return WaterMeshGenerator.GenerateWaveMesh(states,heights,width,scale,200,14.4f).CreateMesh();
            }
            Mesh a=Make(0), b=Make(size);
            try
            {
                var data=new List<Vector4>(); a.GetUVs(1,data);
                var other=new List<Vector4>(); b.GetUVs(1,other);
                Check(a.vertexCount>4 && data.Count==a.vertexCount,"Wave grid/data missing");
                Check(a.bounds.min.y<=14.4f-.299f && a.bounds.max.y>=14.4f+.299f,"Wave culling bounds too small");
                var unique=new HashSet<Vector3>(); var av=a.vertices; var bv=b.vertices;
                foreach(var v in av) Check(unique.Add(v) && v.y==14.4f,"Duplicate vertices or CPU-animated mesh");
                foreach(var d in data) Check(d.w==1 && Mathf.Abs(d.y-.03f/scale)<.0001f &&
                    Mathf.Abs(d.z-.01f/scale)<.0001f,"Incorrect world-space shore gradient or wave support");
                int shared=0;
                for(int i=0;i<av.Length;i++) if(Mathf.Abs(av[i].x-size*scale*.5f)<.0001f)
                {
                    bool found=false;
                    for(int j=0;j<bv.Length;j++) if(Mathf.Abs(bv[j].x+size*scale*.5f)<.0001f && Mathf.Abs(av[i].z-bv[j].z)<.0001f)
                    { Check((data[i]-other[j]).sqrMagnitude<.000001f,"Depth/gradient differs at chunk seam"); found=true; shared++; break; }
                    Check(found,"Nonconforming grid edge");
                }
                Check(shared>2,"Insufficient shared boundary samples");
                int[] triangles=a.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    Vector3 p=av[triangles[i]],q=av[triangles[i+1]],r=av[triangles[i+2]];
                    Check(Vector3.Cross(q-p,r-p).y>0,"Inverted wave triangle");
                    Check(Mathf.Max(p.x,q.x,r.x)-Mathf.Min(p.x,q.x,r.x)<=.601f &&
                        Mathf.Max(p.z,q.z,r.z)-Mathf.Min(p.z,q.z,r.z)<=.601f,"Wave cell exceeds vertex spacing");
                }
                Debug.Log($"WAVE GRID size={size}, scale={scale}: vertices={a.vertexCount}, triangles={triangles.Length/3}, shared boundary={shared}");
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
        using(var dry=new NativeArray<WaterState>(19*19,Allocator.TempJob))
        using(var heights=new NativeArray<float>(19*19,Allocator.TempJob))
            Check(WaterMeshGenerator.GenerateWaveMesh(dry,heights,19,.3f,200,14.4f).VertexCount==0,"Dry chunk emitted wave water");
        ValidateSmallPond();
        Debug.Log("WAVE MESH PASS: shared vertices, <=0.6m spacing, winding, padded shore depths/gradients, conforming chunk edges, bounded culling and dry coverage.");
    }

    static void ValidateSmallPond()
    {
        const int size=32,width=size+3;
        var values=new WaterState[width*width]; var terrain=new float[values.Length];
        for(int x=0;x<width;x++) for(int z=0;z<width;z++)
        {
            float depth=.8f*Mathf.Exp(-((x-14)*(x-14)+(z-19)*(z-19))/4f)-.35f;
            terrain[x*width+z]=(14.4f-depth)/60;
            values[x*width+z]=depth>=0?WaterState.Shallow:WaterState.Dry;
        }
        using var states=new NativeArray<WaterState>(values,Allocator.TempJob);
        using var heights=new NativeArray<float>(terrain,Allocator.TempJob);
        Mesh mesh=WaterMeshGenerator.GenerateWaveMesh(states,heights,width,.3f,200,14.4f).CreateMesh();
        try
        {
            var vertices=mesh.vertices; var triangles=mesh.triangles;
            Check(vertices.Length>0 && vertices.Length<100,"Small pond allocated an entire chunk grid");
            for(int x=1;x<=size+1;x++) for(int z=1;z<=size+1;z++) if(values[x*width+z]==WaterState.Shallow)
            {
                Vector2 p=new Vector2((x-1-size*.5f)*.3f,(z-1-size*.5f)*.3f); bool covered=false;
                float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    Vector3 aa=vertices[triangles[i]],bb=vertices[triangles[i+1]],cc=vertices[triangles[i+2]];
                    Vector2 a=new Vector2(aa.x,aa.z),b=new Vector2(bb.x,bb.z),c=new Vector2(cc.x,cc.z);
                    if(Cross(b-a,p-a)<=.00001f && Cross(c-b,p-b)<=.00001f && Cross(a-c,p-c)<=.00001f) {covered=true;break;}
                }
                Check(covered,"A submerged pond sample has no water geometry");
            }
        }
        finally {Object.DestroyImmediate(mesh);}
    }
}
