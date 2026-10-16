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
        bool fullBearing=false;foreach(var p in pieces)fullBearing|=p.Definition.requiresFullBearing;
        if(!fullBearing)
        {
            while(queue.Count>0)foreach(var id in links[queue.Dequeue()])if(supported.Add(id))queue.Enqueue(id);
            Furnishings();return supported;
        }
        var byId=new Dictionary<ulong,BuildPieceRecord>();foreach(var p in pieces)byId.Add(p.Id,p);
        bool added;
        do
        {
            while(queue.Count>0)foreach(var id in links[queue.Dequeue()])
                if(!byId[id].Definition.requiresFullBearing && supported.Add(id))queue.Enqueue(id);
            added=false;
            foreach(var p in pieces)
                if(p.Definition.requiresFullBearing && !supported.Contains(p.Id) && FullBearing(p,nearby?.Invoke(p)??pieces,states,supported))
                {supported.Add(p.Id);queue.Enqueue(p.Id);added=true;}
        }while(added);
        Furnishings();return supported;
        void Furnishings()
        {
            foreach(var p in pieces)
                if(p.Definition.kind==BuildPartKind.Furniture && !supported.Contains(p.Id) &&
                    BuildFurnishing.Supported(p,nearby?.Invoke(p)??pieces,states,supported))supported.Add(p.Id);
        }
    }
    private static bool FullBearing(BuildPieceRecord floor,IReadOnlyList<BuildPieceRecord> pieces,Dictionary<ulong,BuildResolvedState> states,HashSet<ulong> supported)
    {
        var coverage=new List<Bounds>();var bounds=floor.Definition.LocalBounds;
        foreach(var other in pieces)
        {
            if(other.Id==floor.Id || !supported.Contains(other.Id) || !BuildResolution.Aligned(floor,other))continue;
            // Existing timber floors are framed structures, not solid bedding for stone paving.
            if(other.Definition.kind!=BuildPartKind.Foundation && !other.Definition.requiresFullBearing)continue;
            foreach(var box in states[other.Id].boxes)
            {
                var relative=BuildResolution.Relative(box,other,floor);
                if(Mathf.Abs(relative.max.y-bounds.min.y)<.012f)coverage.Add(relative);
            }
        }
        return BuildResolution.Subtract(new[]{bounds},coverage).Count==0;
    }
}
