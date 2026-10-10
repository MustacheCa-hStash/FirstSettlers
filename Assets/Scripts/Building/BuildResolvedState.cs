using System;
using UnityEngine;

[Serializable]
public struct BuildTimberPart
{
    public Bounds bounds;
    public int grainAxis;
    public int uvSeed;
    public bool hasUvBounds;
    public Bounds uvBounds;
}
public struct BuildRenderAttachment
{
    public Mesh mesh;
    public Material material;
    public Matrix4x4 localMatrix;
}

/// <summary>Derived, reversible representation. Canonical record pose/health never changes.</summary>
public sealed class BuildResolvedState
{
    public Mesh mesh;
    // Shares the piece pose/material, but never copies the authored core's CPU data.
    public Mesh auxiliaryMesh;
    public Bounds[] boxes=Array.Empty<Bounds>();
    public BuildConvexVolume[] volumes=Array.Empty<BuildConvexVolume>();
    public Bounds[] cuts=Array.Empty<Bounds>();
    public string key;
    public bool invalidJoint;
    public BuildJointContact[] joints=Array.Empty<BuildJointContact>();
    public BuildRenderAttachment[] attachments=Array.Empty<BuildRenderAttachment>();
    public bool SameVisuals(BuildResolvedState other)
    {
        if(other==null || mesh!=other.mesh || auxiliaryMesh!=other.auxiliaryMesh || attachments.Length!=other.attachments.Length)return false;
        for(int i=0;i<attachments.Length;i++)if(attachments[i].mesh!=other.attachments[i].mesh || attachments[i].material!=other.attachments[i].material || attachments[i].localMatrix!=other.attachments[i].localMatrix)return false;
        return true;
    }
    public Bounds Bounds
    {
        get
        {
            bool first=true;Bounds result=default;
            foreach(var b in boxes){if(first){result=b;first=false;}else result.Encapsulate(b);}
            foreach(var v in volumes){if(first){result=v.bounds;first=false;}else result.Encapsulate(v.bounds);}
            return result;
        }
    }
}
