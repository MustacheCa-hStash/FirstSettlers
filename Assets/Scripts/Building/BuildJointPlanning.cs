using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct BuildJointSocket
{
    public Vector3 localPosition;
    public Vector3 outward;
    public bool interior;
}
public enum BuildJointKind { Butt, Corner, Angled, Tee, Cross }
public readonly struct BuildJointContact
{
    public readonly ulong OtherPieceId;
    public readonly Vector3 Position;
    public readonly BuildJointKind Kind;
    public readonly bool PrimaryOwner;
    public readonly Vector3 LocalPosition;
    public BuildJointContact(ulong other,Vector3 position,BuildJointKind kind,bool primary,Vector3 localPosition)
    {OtherPieceId=other;Position=position;Kind=kind;PrimaryOwner=primary;LocalPosition=localPosition;}
}

/// <summary>Declared wall endpoint contacts for future covers/trims. No meshes, colliders or posts are spawned.</summary>
public static class BuildJointPlanning
{
    public static BuildJointContact[] Contacts(BuildPieceRecord piece,IReadOnlyList<BuildPieceRecord> neighbours)
    {
        var sockets=piece.Definition.jointSockets;
        if(piece.Definition.kind!=BuildPartKind.Wall || sockets==null || sockets.Length==0)return Array.Empty<BuildJointContact>();
        var result=new List<BuildJointContact>();var rotation=BuildGeometry.Rotation(piece.WorldYawStep);
        foreach(var other in neighbours)
        {
            if(other.Id==piece.Id || other.Definition.kind!=BuildPartKind.Wall || other.Definition.jointSockets==null)continue;
            var oq=BuildGeometry.Rotation(other.WorldYawStep);
            foreach(var a in sockets)foreach(var b in other.Definition.jointSockets)
            {
                Vector3 ap=piece.Origin+rotation*a.localPosition,bp=other.Origin+oq*b.localPosition;
                if((ap-bp).sqrMagnitude>.000144f || a.outward.sqrMagnitude<.001f || b.outward.sqrMagnitude<.001f)continue;
                float dot=Mathf.Abs(Vector3.Dot(rotation*a.outward.normalized,oq*b.outward.normalized));
                if(dot>.999f && (a.interior || b.interior))continue;
                var kind=dot>.999f?BuildJointKind.Butt:dot<.001f?BuildJointKind.Corner:BuildJointKind.Angled;
                if(dot<.001f && (a.interior || b.interior))kind=a.interior && b.interior?BuildJointKind.Cross:BuildJointKind.Tee;
                result.Add(new BuildJointContact(other.Id,ap,kind,ComparePose(piece,other)<0,a.localPosition));
            }
        }
        return result.ToArray();
    }
    public static int ComparePose(BuildPieceRecord a,BuildPieceRecord b)
    {
        int value=a.WorldYawStep.CompareTo(b.WorldYawStep);if(value!=0)return value;
        for(int i=0;i<3;i++){value=a.Origin[i].CompareTo(b.Origin[i]);if(value!=0)return value;}
        return string.CompareOrdinal(a.Definition.contentId,b.Definition.contentId);
    }
}
