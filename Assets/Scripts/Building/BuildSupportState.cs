using System.Collections.Generic;
using UnityEngine;

public static class BuildSupportState
{
    public static HashSet<ulong> Resolve(IReadOnlyList<BuildPieceRecord> pieces,Dictionary<ulong,BuildResolvedState> states,System.Func<BuildPieceRecord,IReadOnlyList<BuildPieceRecord>> nearby=null)
    {
        var links=new Dictionary<ulong,List<ulong>>();var supported=new HashSet<ulong>();var queue=new Queue<ulong>();
        foreach(var p in pieces){links[p.Id]=new List<ulong>();if(p.Grounded){supported.Add(p.Id);queue.Enqueue(p.Id);}}
        foreach(var a in pieces)foreach(var b in nearby?.Invoke(a)??pieces)
        {
            if(a.Id>=b.Id)continue;Bounds area=a.WorldBounds;area.Expand(.04f);
            if(!area.Intersects(b.WorldBounds))continue;
            if(BuildResolvedGeometry.Connects(a,states[a.Id],b,states[b.Id])){links[a.Id].Add(b.Id);links[b.Id].Add(a.Id);}
        }
        while(queue.Count>0)foreach(var id in links[queue.Dequeue()])if(supported.Add(id))queue.Enqueue(id);
        return supported;
    }
}
