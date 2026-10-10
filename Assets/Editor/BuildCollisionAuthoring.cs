using System.Collections.Generic;
using UnityEngine;

public static class BuildCollisionAuthoring
{
    public static BuildConvexVolume Volume(Mesh mesh)
    {
        var v=mesh.vertices;var t=mesh.triangles;var faces=new List<Vector3>();var edges=new List<Vector3>();
        void Unique(List<Vector3> list,Vector3 direction)
        {
            if(direction.sqrMagnitude<.000001f)return;direction.Normalize();
            foreach(var e in list)if(Mathf.Abs(Vector3.Dot(e,direction))>.9999f)return;list.Add(direction);
        }
        for(int i=0;i<t.Length;i+=3)
        {
            Unique(faces,Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]));
            Unique(edges,v[t[i+1]]-v[t[i]]);Unique(edges,v[t[i+2]]-v[t[i+1]]);Unique(edges,v[t[i]]-v[t[i+2]]);
        }
        return new BuildConvexVolume{mesh=mesh,vertices=v,faceAxes=faces.ToArray(),edgeAxes=edges.ToArray(),bounds=mesh.bounds};
    }
}
