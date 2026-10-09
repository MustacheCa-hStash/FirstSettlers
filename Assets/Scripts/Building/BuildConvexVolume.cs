using System;
using UnityEngine;

/// <summary>Authored convex solid. Mesh and SAT data describe the same physical volume.</summary>
[Serializable]
public sealed class BuildConvexVolume
{
    public Mesh mesh;
    public Vector3[] vertices, faceAxes, edgeAxes;
    public Bounds bounds;
}

public static class BuildOccupancy
{
    public static bool Custom(BuildDefinition d) => d.occupiedVolumes != null && d.occupiedVolumes.Length > 0;
    public static Bounds Bounds(BuildDefinition d)
    {
        if (!Custom(d)) return d.LocalBounds;
        var bounds = d.occupiedVolumes[0].bounds;
        foreach (var volume in d.occupiedVolumes) bounds.Encapsulate(volume.bounds);
        return bounds;
    }
    public static bool Overlaps(BuildDefinition a, Vector3 ao, int ay, BuildDefinition b, Vector3 bo, int by, float minimum = .005f)
    {
        if (!Custom(a) && !Custom(b)) return BuildGeometry.Overlaps(a.LocalBounds,ao,ay,b.LocalBounds,bo,by,minimum);
        var aq = BuildGeometry.Rotation(ay); var bq = BuildGeometry.Rotation(by);
        int an = Custom(a) ? a.occupiedVolumes.Length : 1, bn = Custom(b) ? b.occupiedVolumes.Length : 1;
        for (int i=0; i<an; i++) for (int j=0; j<bn; j++)
            if (SolidOverlap(Custom(a) ? a.occupiedVolumes[i] : null,a.LocalBounds,ao,aq,
                Custom(b) ? b.occupiedVolumes[j] : null,b.LocalBounds,bo,bq,minimum)) return true;
        return false;
    }
    // SAT for convex polyhedra: both face normals and all edge cross products.
    public static bool SolidOverlap(BuildConvexVolume a, Bounds ab, Vector3 ao, Quaternion aq,
        BuildConvexVolume b, Bounds bb, Vector3 bo, Quaternion bq, float minimum)
    {
        Vector3[] aa=a?.faceAxes ?? BoxAxes, ba=b?.faceAxes ?? BoxAxes;
        foreach(var axis in aa) if (Separated(a,ab,ao,aq,b,bb,bo,bq,aq*axis,minimum)) return false;
        foreach(var axis in ba) if (Separated(a,ab,ao,aq,b,bb,bo,bq,bq*axis,minimum)) return false;
        foreach(var x in a?.edgeAxes ?? BoxAxes) foreach(var y in b?.edgeAxes ?? BoxAxes)
        {
            Vector3 axis=Vector3.Cross(aq*x,bq*y);
            if (axis.sqrMagnitude>.000001f && Separated(a,ab,ao,aq,b,bb,bo,bq,axis.normalized,minimum)) return false;
        }
        return true;
    }
    private static readonly Vector3[] BoxAxes={Vector3.right,Vector3.up,Vector3.forward};
    private static bool Separated(BuildConvexVolume a, Bounds ab, Vector3 ao, Quaternion aq,
        BuildConvexVolume b, Bounds bb, Vector3 bo, Quaternion bq, Vector3 axis,float minimum)
    {
        Project(a,ab,ao,aq,axis,out float amin,out float amax);
        Project(b,bb,bo,bq,axis,out float bmin,out float bmax);
        return Mathf.Min(amax,bmax)-Mathf.Max(amin,bmin)<=minimum;
    }
    private static void Project(BuildConvexVolume v,Bounds bounds,Vector3 origin,Quaternion q,Vector3 axis,out float min,out float max)
    {
        if(v==null)
        {
            float c=Vector3.Dot(origin+q*bounds.center,axis);
            float r=Mathf.Abs(Vector3.Dot(q*Vector3.right,axis))*bounds.extents.x+
                Mathf.Abs(Vector3.Dot(q*Vector3.up,axis))*bounds.extents.y+Mathf.Abs(Vector3.Dot(q*Vector3.forward,axis))*bounds.extents.z;
            min=c-r;max=c+r;return;
        }
        min=float.PositiveInfinity;max=float.NegativeInfinity;
        foreach(var point in v.vertices) { float p=Vector3.Dot(origin+q*point,axis);min=Mathf.Min(min,p);max=Mathf.Max(max,p); }
    }
}
