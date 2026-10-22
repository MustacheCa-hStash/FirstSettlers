using System.Collections.Generic;
using UnityEngine;

public static class BuildSupportState
{
    public static bool CandidateSupported(BuildPieceRecord candidate,IReadOnlyList<BuildPieceRecord> neighbours,Dictionary<ulong,BuildResolvedState> states)
    {
        if(candidate.Grounded)return true;
        var supported=new HashSet<ulong>();var local=new Dictionary<ulong,BuildResolvedState>(states);
        foreach(var p in neighbours){if(p.Supported)supported.Add(p.Id);if(!local.ContainsKey(p.Id))local[p.Id]=p.Resolved;}
        var d=candidate.Definition;
        if(d.kind==BuildPartKind.Furniture)return BuildFurnishing.Supported(candidate,neighbours,local,supported);
        if(d.topCourseDepth>0)return CourseBearing(candidate,neighbours,supported);
        if(d.requiresFullBearing)return FullBearing(candidate,neighbours,local,supported);
        foreach(var other in neighbours)if(other.Supported && BuildResolvedGeometry.Connects(candidate,local[candidate.Id],other,local[other.Id]))return true;
        return false;
    }
    public static HashSet<ulong> Resolve(IReadOnlyList<BuildPieceRecord> pieces,Dictionary<ulong,BuildResolvedState> states,System.Func<BuildPieceRecord,IReadOnlyList<BuildPieceRecord>> nearby=null,
        System.Func<BuildPieceRecord,IEnumerable<ulong>> connections=null)
    {
        var links=new Dictionary<ulong,List<ulong>>();var supported=new HashSet<ulong>();var queue=new Queue<ulong>();
        foreach(var p in pieces){links[p.Id]=new List<ulong>();if(p.Grounded){supported.Add(p.Id);queue.Enqueue(p.Id);}}
        if(connections!=null)foreach(var p in pieces)links[p.Id].AddRange(connections(p));
        else foreach(var a in pieces)foreach(var b in nearby?.Invoke(a)??pieces)
        {
            if(a.Id>=b.Id)continue;Bounds area=a.WorldBounds;area.Expand(.04f);
            if(!area.Intersects(b.WorldBounds))continue;
            if(BuildResolvedGeometry.Connects(a,states[a.Id],b,states[b.Id])){links[a.Id].Add(b.Id);links[b.Id].Add(a.Id);}
        }
        bool fullBearing=false;foreach(var p in pieces)fullBearing|=p.Definition.requiresFullBearing || p.Definition.topCourseDepth>0;
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
                if(!byId[id].Definition.requiresFullBearing && byId[id].Definition.topCourseDepth<=0 && supported.Add(id))queue.Enqueue(id);
            added=false;
            foreach(var p in pieces)
                if(!supported.Contains(p.Id) && (p.Definition.topCourseDepth>0 ? CourseBearing(p,nearby?.Invoke(p)??pieces,supported) :
                    p.Definition.requiresFullBearing && FullBearing(p,nearby?.Invoke(p)??pieces,states,supported)))
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
    private static bool CourseBearing(BuildPieceRecord course,IReadOnlyList<BuildPieceRecord> pieces,HashSet<ulong> supported)
    {
        foreach(var wall in pieces)if(supported.Contains(wall.Id) && BuildTopCourse.Fits(wall,course))return true;
        return false;
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
