using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Reversible top-course fitting. The wall's canonical pose and storey sockets never move.</summary>
public static class BuildTopCourse
{
    public static bool Compatible(BuildDefinition wall,BuildDefinition course)
    {
        var b=wall.LocalBounds;var c=course.LocalBounds;
        if(course.topCourseDepth<=0 || wall.kind!=BuildPartKind.Wall || wall.IsHalfGable || !wall.preserveAuthoredMesh || wall.topCourseDepth>0 ||
            Mathf.Abs(b.size.x-c.size.x)>.001f || Mathf.Abs(b.size.y-3)>.001f || Mathf.Abs(b.size.z-(c.size.z-.25f))>.001f)return false;
        float level=b.max.y-course.topCourseDepth;
        foreach(var opening in wall.wallOpenings??Array.Empty<Bounds>())if(opening.max.y>level+.001f)return false;
        // The upper course must be solid across the complete bay, not a gable or open frame.
        for(int x=0;x<9;x++)for(int z=0;z<3;z++)
        {
            var point=new Vector3(Mathf.Lerp(b.min.x+.002f,b.max.x-.002f,x/8f),level+.01f,Mathf.Lerp(b.min.z+.002f,b.max.z-.002f,z/2f));bool solid=false;
            if(BuildOccupancy.Custom(wall)){foreach(var v in wall.occupiedVolumes)solid|=BuildOccupancy.Contains(v,point);}
            else if(wall.solidBoxes?.Length>0){foreach(var box in wall.solidBoxes)solid|=box.Contains(point);}
            else solid=b.Contains(point);
            if(!solid)return false;
        }
        return true;
    }
    public static bool Fits(BuildPieceRecord wall,BuildPieceRecord course)
    {
        if(!Compatible(wall.Definition,course.Definition) || BuildGeometry.Turn(wall.WorldYawStep-course.WorldYawStep)!=0)return false;
        var b=wall.Definition.LocalBounds;var c=course.Definition.LocalBounds;
        var expected=wall.Origin+BuildGeometry.Rotation(wall.WorldYawStep)*new Vector3(b.min.x-c.min.x,b.max.y-course.Definition.topCourseDepth-c.min.y,b.center.z-c.center.z);
        return (expected-course.Origin).sqrMagnitude<.000001f;
    }
    public static bool Solve(BuildDefinition d,BuildPieceRecord target,Vector3Int nudge,int turn,out BuildPreview preview,int? lockedWorldYaw=null)
    {
        preview=default;if(d.topCourseDepth<=0)return false;
        bool valid=target!=null&&Compatible(target.Definition,d);
        int yaw=lockedWorldYaw??target?.WorldYawStep??0;var b=target?.Definition.LocalBounds??default;var c=d.LocalBounds;
        var origin=(target?.Origin??Vector3.zero)+BuildGeometry.Rotation(yaw)*(new Vector3(b.min.x-c.min.x,b.max.y-d.topCourseDepth-c.min.y,b.center.z-c.center.z)+(Vector3)nudge*BuildGeometry.Unit);
        preview=new BuildPreview{Definition=d,Origin=origin,WorldYaw=yaw,Frame=new BuildGridFrame{Origin=origin,YawStep=(byte)yaw},InvalidAttachment=!valid,Hint="Replace wall top course"};return true;
    }
    // Clip triangles at a horizontal plane, retaining the authored UVs and normals.
    // Collision inputs are convex; their new cap is a sorted convex polygon.
    public static Mesh Clip(Mesh source,float level,bool convex)
    {
        var vertices=source.vertices;var normals=source.normals;var uv=source.uv;var indices=source.triangles;
        var output=new List<Vector3>();var ns=new List<Vector3>();var tex=new List<Vector2>();var triangles=new List<int>();var cuts=new List<Vector3>();
        (Vector3 p,Vector3 n,Vector2 uv) Vertex(int i)=>(vertices[i],normals.Length==vertices.Length?normals[i]:Vector3.up,uv.Length==vertices.Length?uv[i]:Vector2.zero);
        void Triangle((Vector3 p,Vector3 n,Vector2 uv) a,(Vector3 p,Vector3 n,Vector2 uv) b,(Vector3 p,Vector3 n,Vector2 uv) c)
        {
            if(Vector3.Cross(b.p-a.p,c.p-a.p).sqrMagnitude<1e-14f)return;
            foreach(var v in new[]{a,b,c}){triangles.Add(output.Count);output.Add(v.p);ns.Add(v.n.normalized);tex.Add(v.uv);}
        }
        for(int i=0;i<indices.Length;i+=3)
        {
            var polygon=new[]{Vertex(indices[i]),Vertex(indices[i+1]),Vertex(indices[i+2])};var clipped=new List<(Vector3 p,Vector3 n,Vector2 uv)>();
            for(int j=0;j<3;j++)
            {
                var a=polygon[j];var b=polygon[(j+1)%3];bool inside=a.p.y<=level,other=b.p.y<=level;
                if(inside)clipped.Add(a);
                if(inside!=other){float t=(level-a.p.y)/(b.p.y-a.p.y);var point=Vector3.Lerp(a.p,b.p,t);clipped.Add((point,Vector3.Lerp(a.n,b.n,t),Vector2.Lerp(a.uv,b.uv,t)));if(!cuts.Exists(p=>(p-point).sqrMagnitude<1e-10f))cuts.Add(point);}
            }
            for(int j=1;j+1<clipped.Count;j++)Triangle(clipped[0],clipped[j],clipped[j+1]);
        }
        if(cuts.Count>=3)
        {
            Vector3 centre=Vector3.zero;foreach(var p in cuts)centre+=p/cuts.Count;
            if(!convex)
            {
                // Compatible walls have a continuous rectangular upper band.
                var b=source.bounds;cuts=new List<Vector3>{new(b.min.x,level,b.min.z),new(b.min.x,level,b.max.z),new(b.max.x,level,b.max.z),new(b.max.x,level,b.min.z)};
            }
            else cuts.Sort((a,b)=>Mathf.Atan2(a.x-centre.x,a.z-centre.z).CompareTo(Mathf.Atan2(b.x-centre.x,b.z-centre.z)));
            for(int j=0;j<cuts.Count;j++)Triangle((centre,Vector3.up,new Vector2(.5f,.5f)),(cuts[j],Vector3.up,new Vector2(.5f+cuts[j].x*.001f,.5f+cuts[j].z*.001f)),(cuts[(j+1)%cuts.Count],Vector3.up,new Vector2(.5f+cuts[(j+1)%cuts.Count].x*.001f,.5f+cuts[(j+1)%cuts.Count].z*.001f)));
        }
        var mesh=new Mesh{name=source.name+" fitted top course"};mesh.SetVertices(output);mesh.SetNormals(ns);mesh.SetUVs(0,tex);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
    }
    public static BuildConvexVolume Volume(Mesh mesh)
    {
        var v=mesh.vertices;var t=mesh.triangles;var axes=new List<Vector3>();var edges=new List<Vector3>();
        void Add(List<Vector3> list,Vector3 axis){if(axis.sqrMagnitude<1e-8f)return;axis.Normalize();foreach(var a in list)if(Mathf.Abs(Vector3.Dot(a,axis))>.9999f)return;list.Add(axis);}
        for(int i=0;i<t.Length;i+=3){Add(axes,Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]));for(int j=0;j<3;j++)Add(edges,v[t[i+(j+1)%3]]-v[t[i+j]]);}
        return new BuildConvexVolume{mesh=mesh,vertices=v,faceAxes=axes.ToArray(),edgeAxes=edges.ToArray(),bounds=mesh.bounds};
    }
}
