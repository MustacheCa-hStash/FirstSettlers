using System.Collections.Generic;
using UnityEngine;

/// <summary>One derived visual per junction/storey. Explicit posts suppress it; physics remains unchanged.</summary>
public static class BuildJointCovers
{
    private sealed class Group
    {
        public Vector3 position,local;
        public BuildPieceRecord owner;
    }
    public static void Resolve(IReadOnlyList<BuildPieceRecord> pieces,Dictionary<ulong,BuildResolvedState> states,System.Func<BuildPieceRecord,IReadOnlyList<BuildPieceRecord>> nearby=null)
    {
        var bins=new Dictionary<Vector3Int,List<Group>>();var groups=new List<Group>();
        foreach(var p in pieces)
        {
            if(p.Definition.jointCoverVariants==null || p.Definition.jointCoverVariants.Length==0)continue;
            foreach(var joint in states[p.Id].joints)
            {
                var cell=Cell(joint.Position);Group found=null;
                for(int x=-1;x<=1 && found==null;x++)for(int y=-1;y<=1 && found==null;y++)for(int z=-1;z<=1 && found==null;z++)
                    if(bins.TryGetValue(cell+new Vector3Int(x,y,z),out var entries))foreach(var g in entries)
                        if((g.position-joint.Position).sqrMagnitude<.000144f){found=g;break;}
                if(found==null)
                {
                    found=new Group{position=joint.Position,owner=p,local=joint.LocalPosition};groups.Add(found);
                    if(!bins.TryGetValue(cell,out var list))bins[cell]=list=new List<Group>();list.Add(found);
                }
                else if(BuildJointPlanning.ComparePose(p,found.owner)<0){found.owner=p;found.local=joint.LocalPosition;}
            }
        }
        groups.Sort((a,b)=>{for(int i=0;i<3;i++){int c=a.position[i].CompareTo(b.position[i]);if(c!=0)return c;}return 0;});
        var outputs=new Dictionary<ulong,List<BuildRenderAttachment>>();
        foreach(var g in groups)
        {
            var p=g.owner;Vector3 position=p.Origin+BuildGeometry.Rotation(p.WorldYawStep)*g.local;bool supplied=false;
            foreach(var other in nearby?.Invoke(p)??pieces)
            {
                if(other.Definition.kind!=BuildPartKind.Corner)continue;
                var centre=other.Origin+BuildGeometry.Rotation(other.WorldYawStep)*new Vector3(other.Definition.LocalBounds.center.x,other.Definition.LocalBounds.min.y,other.Definition.LocalBounds.center.z);
                Vector3 delta=centre-position;delta.y=0;
                if(delta.sqrMagnitude<.000144f && centre.y<=position.y+.012f && other.Origin.y+other.Definition.LocalBounds.max.y>=position.y+p.Definition.LocalBounds.size.y-.012f){supplied=true;break;}
            }
            if(supplied)continue;
            var variants=p.Definition.jointCoverVariants;
            var mesh=variants[BuildTrimUV.Variation(position,p.WorldYawStep,variants.Length)];
            if(!outputs.TryGetValue(p.Id,out var list))outputs[p.Id]=list=new List<BuildRenderAttachment>();
            list.Add(new BuildRenderAttachment{mesh=mesh,material=p.Definition.jointCoverMaterial??p.Definition.material,localMatrix=Matrix4x4.Translate(g.local)});
        }
        foreach(var pair in outputs)states[pair.Key].attachments=pair.Value.ToArray();
    }
    private static Vector3Int Cell(Vector3 p)=>new(Mathf.FloorToInt(p.x/.25f),Mathf.FloorToInt(p.y/.25f),Mathf.FloorToInt(p.z/.25f));
}
