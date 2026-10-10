using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Deterministic joint ownership, derived from canonical records. No permanent mesh edits.</summary>
public sealed class BuildResolution : IDisposable
{
    public const float Band=.25f, PassageHeight=2f, PassageMargin=.5f;
    private readonly Dictionary<string,Mesh> meshes=new();
    public Dictionary<ulong,BuildResolvedState> Resolve(IReadOnlyList<BuildPieceRecord> pieces,Func<BuildPieceRecord,IReadOnlyList<BuildPieceRecord>> nearby=null)
    {
        var result=new Dictionary<ulong,BuildResolvedState>();
        foreach(var p in pieces)if(p.Definition.kind==BuildPartKind.Floor)result[p.Id]=Floor(p,nearby?.Invoke(p)??pieces);
        foreach(var p in pieces)
        {
            if(p.Definition.kind==BuildPartKind.Floor)continue;
            if(p.Definition.kind==BuildPartKind.Foundation)result[p.Id]=Foundation(p,nearby?.Invoke(p)??pieces,result);
            else result[p.Id]=Ordinary(p,nearby?.Invoke(p)??pieces,result);
        }
        foreach(var p in pieces)
            if(p.Definition.jointSockets!=null && p.Definition.jointSockets.Length>0)
                result[p.Id].joints=BuildJointPlanning.Contacts(p,nearby?.Invoke(p)??pieces);
        BuildJointCovers.Resolve(pieces,result,nearby);
        return result;
    }
    private BuildResolvedState Floor(BuildPieceRecord p,IReadOnlyList<BuildPieceRecord> pieces)
    {
        var core=BuildTrimUV.Core(p);int variation=BuildTrimUV.Variation(p.Origin,p.WorldYawStep,p.Definition.uvVariants?.Length??0);
        var cuts=new List<Bounds>();bool invalid=false;
        foreach(var other in pieces)
        {
            if(other.Id==p.Id)continue;
            Bounds b=Relative(other.Definition.LocalBounds,other,p);
            if(other.Definition.kind is BuildPartKind.Wall or BuildPartKind.Corner)
            {
                if(Mathf.Min(b.max.y,p.Definition.LocalBounds.max.y)-Mathf.Max(b.min.y,p.Definition.LocalBounds.min.y)>.005f)
                {
                    if(!Aligned(p,other)){invalid=true;continue;}
                    AddCut(cuts,b,p.Definition.LocalBounds);
                }
            }
            else if(other.Definition.kind==BuildPartKind.Stair)
            {
                var passage=StairFloorCut(other,p);
                if(passage.HasValue){if(!Aligned(p,other))invalid=true;else AddCut(cuts,passage.Value,p.Definition.LocalBounds);}
            }
        }
        MergeCuts(cuts);Sort(cuts);var boxes=Subtract(new[]{p.Definition.LocalBounds},cuts);
        // Fitting is optional. An unsupported cut keeps the complete floor/collider and warns.
        if(invalid || boxes.Count==0)return new BuildResolvedState{mesh=core,boxes=new[]{p.Definition.LocalBounds},invalidJoint=true,key="unresolved-full:"+variation};
        var state=new BuildResolvedState{boxes=boxes.ToArray(),cuts=cuts.ToArray(),invalidJoint=invalid};
        if(cuts.Count==0){state.mesh=core;state.key="full:"+variation;return state;}
        if(cuts.Count==1 && p.Definition.floorOpeningMesh!=null &&
            (cuts[0].min-new Vector3(.75f,-.25f,.5f)).sqrMagnitude<.000001f &&
            (cuts[0].max-new Vector3(3,0,4)).sqrMagnitude<.000001f)
        {var variants=p.Definition.floorOpeningUvVariants;state.mesh=variants!=null && variants.Length>0?variants[variation%variants.Length]:p.Definition.floorOpeningMesh;state.key="authored-stairwell:"+variation;return state;}
        string key=Key(p.Definition,"floor",cuts)+":uv"+variation;state.key=key;
        if(!meshes.TryGetValue(key,out state.mesh))
        {
            var frames=new List<BuildTimberPart>();
            foreach(var cut in cuts)
            {
                // Opening edges only. No header across a tile-edge/stair exit.
                Bounds outer=p.Definition.LocalBounds;Vector3 lo=cut.min,hi=cut.max;
                if(lo.x>outer.min.x+.005f)frames.Add(Part(new Vector3(Mathf.Max(outer.min.x,lo.x-.125f),outer.min.y,lo.z),new Vector3(lo.x,outer.max.y,hi.z),2));
                if(hi.x<outer.max.x-.005f)frames.Add(Part(new Vector3(hi.x,outer.min.y,lo.z),new Vector3(Mathf.Min(outer.max.x,hi.x+.125f),outer.max.y,hi.z),2));
                if(lo.z>outer.min.z+.005f)frames.Add(Part(new Vector3(lo.x,outer.min.y,Mathf.Max(outer.min.z,lo.z-.125f)),new Vector3(hi.x,outer.max.y,lo.z),0));
                if(hi.z<outer.max.z-.005f)frames.Add(Part(new Vector3(lo.x,outer.min.y,hi.z),new Vector3(hi.x,outer.max.y,Mathf.Min(outer.max.z,hi.z+.125f)),0));
            }
            var parts=new List<BuildTimberPart>();var reserved=new List<Bounds>(cuts);
            foreach(var f in frames)reserved.Add(f.bounds);
            foreach(var timber in p.Definition.floorParts??Array.Empty<BuildTimberPart>())
                foreach(var box in Subtract(new[]{timber.bounds},reserved))
                {var fitted=timber;fitted.bounds=box;fitted.uvSeed+=variation*104729;fitted.hasUvBounds=true;fitted.uvBounds=timber.hasUvBounds?timber.uvBounds:timber.bounds;parts.Add(fitted);}
            // Resolve overlapping trimmers as a union, rather than layering coplanar faces.
            var occupied=new List<Bounds>(cuts);
            foreach(var f in frames)
            {
                foreach(var box in Subtract(new[]{f.bounds},occupied))parts.Add(new BuildTimberPart{bounds=box,grainAxis=f.grainAxis});
                occupied.Add(f.bounds);
            }
            if(parts.Count==0 && boxes.Count>0)foreach(var box in boxes)parts.Add(new BuildTimberPart{bounds=box,grainAxis=2});
            state.mesh=BuildTimberMesh.Create("Resolved framed floor",parts);meshes.Add(key,state.mesh);
        }
        return state;
    }
    private BuildResolvedState Foundation(BuildPieceRecord p,IReadOnlyList<BuildPieceRecord> pieces,Dictionary<ulong,BuildResolvedState> states)
    {
        var cuts=new List<Bounds>();Bounds b=p.Definition.LocalBounds;
        foreach(var floor in pieces)
            if(floor.Definition.kind==BuildPartKind.Floor && Aligned(p,floor) && Mathf.Abs(floor.Origin.y-p.Origin.y)<.012f)
                foreach(var solid in states[floor.Id].boxes)AddCut(cuts,Relative(solid,floor,p),b);
        if(cuts.Count==0)return new BuildResolvedState{mesh=p.Definition.mesh,boxes=new[]{b},key="full"};
        var boxes=new List<Bounds>{Box(b.min,new Vector3(b.max.x,b.max.y-Band,b.max.z))};
        boxes.AddRange(Subtract(new[]{Box(new Vector3(b.min.x,b.max.y-Band,b.min.z),b.max)},cuts));Sort(boxes);
        string key=Key(p.Definition,"foundation",boxes);
        if(!meshes.TryGetValue(key,out var mesh))
        {var parts=new List<BuildTimberPart>();foreach(var box in boxes)parts.Add(new BuildTimberPart{bounds=box,grainAxis=0});mesh=BuildTimberMesh.Create("Foundation with reversible finish cap",parts,plain:true);meshes.Add(key,mesh);}
        return new BuildResolvedState{mesh=mesh,boxes=boxes.ToArray(),key=key};
    }
    private BuildResolvedState Ordinary(BuildPieceRecord p,IReadOnlyList<BuildPieceRecord> pieces,Dictionary<ulong,BuildResolvedState> floors)
    {
        var d=p.Definition;Mesh core=BuildTrimUV.Core(p);
        if(d.kind==BuildPartKind.Roof && d.roofContinuationMesh!=null)
            foreach(var other in pieces)if(BuildRoof.IsBelow(d,p.Origin,p.WorldYawStep,other.Definition,other.Origin,other.WorldYawStep)){core=d.roofContinuationMesh;break;}
        var boxes=new List<Bounds>();var volumes=BuildOccupancy.Custom(d)?d.occupiedVolumes:Array.Empty<BuildConvexVolume>();
        if(volumes.Length==0)boxes.Add(d.LocalBounds);
        var headers=new List<Bounds>();
        if(d.kind is BuildPartKind.Wall or BuildPartKind.Corner or BuildPartKind.Roof)
        {
            Bounds foot=d.kind==BuildPartKind.Roof?new Bounds(new Vector3(2,-Band*.5f,.125f),new Vector3(4,Band,.25f)):
                Box(new Vector3(d.LocalBounds.min.x,-Band,d.LocalBounds.min.z),new Vector3(d.LocalBounds.max.x,0,d.LocalBounds.max.z));
            bool bearing=false;var cut=new List<Bounds>();
            foreach(var other in pieces)
            {
                if(other.Id==p.Id)continue;Bounds ob=Relative(other.Definition.LocalBounds,other,p);
                if(other.Definition.kind is BuildPartKind.Wall or BuildPartKind.Corner)
                    bearing|=Aligned(p,other) && Mathf.Abs(ob.max.y-foot.min.y)<.012f && PlanOverlap(ob,foot);
                if(other.Definition.kind==BuildPartKind.Floor && Mathf.Abs(ob.max.y)<.012f && Aligned(p,other))
                {
                    foreach(var box in floors[other.Id].boxes)AddCut(cut,Relative(box,other,p),foot);
                    // A stairwell stays empty even when duplicate bearing geometry is suppressed/restored.
                    foreach(var hole in floors[other.Id].cuts)AddCut(cut,Relative(hole,other,p),foot);
                }
                if(other.Definition.kind==BuildPartKind.Stair)
                {var hole=StairFloorCut(other,p);if(hole.HasValue && Aligned(p,other))AddCut(cut,hole.Value,foot);}
            }
            if(bearing)headers=Subtract(new[]{foot},cut);
        }
        boxes.AddRange(headers);Sort(headers);
        if(headers.Count==0)return new BuildResolvedState{mesh=core,boxes=boxes.ToArray(),volumes=volumes,key=core==d.mesh?"full":"joined"};
        string key=Key(d,core==d.mesh?"bearing":"joined-bearing",headers);
        if(!meshes.TryGetValue(key,out var visual))
        {
            var parts=new List<BuildTimberPart>();
            foreach(var h in headers)
            {
                if(d.kind==BuildPartKind.Roof)
                {
                    var spans=Subtract(new[]{h},new[]{Box(new Vector3(0,-.25f,0),new Vector3(.25f,0,.25f)),Box(new Vector3(3.75f,-.25f,0),new Vector3(4,0,.25f))});
                    foreach(var span in spans)parts.Add(new BuildTimberPart{bounds=span,grainAxis=0});
                    foreach(float x in new[]{0f,3.75f})
                    {
                        var cap=Box(new Vector3(x,-.25f,0),new Vector3(x+.25f,0,.25f));
                        Vector3 lo=Vector3.Max(cap.min,h.min),hi=Vector3.Min(cap.max,h.max);
                        if(hi.x-lo.x>.005f && hi.z-lo.z>.005f)parts.Add(Part(lo,hi,1));
                    }
                }
                else parts.Add(new BuildTimberPart{bounds=h,grainAxis=h.size.x>.3f?0:h.size.z>.3f?2:1});
            }
            visual=BuildTimberMesh.Create("Shared bearing only",parts,roofAtlas:d.kind==BuildPartKind.Roof);meshes.Add(key,visual);
        }
        return new BuildResolvedState{mesh=core,auxiliaryMesh=visual,boxes=boxes.ToArray(),volumes=volumes,key=key};
    }
    public static Bounds? StairFloorCut(BuildPieceRecord stair,BuildPieceRecord floor)
    {
        float level=floor.Origin.y-stair.Origin.y;
        if(level<=.01f)return null;
        Bounds sb=stair.Definition.LocalBounds;float slope=sb.size.y/sb.size.z;
        float start=Mathf.Max(0,(level-Band-PassageHeight)/slope-PassageMargin),end=Mathf.Min(sb.size.z,level/slope);
        if(level>sb.size.y+.01f && level-Band-sb.size.y<PassageHeight)end=sb.size.z+PassageMargin;
        if(end-start<=.005f)return null;
        Bounds cut=Box(new Vector3(sb.min.x-PassageMargin,level-Band,start),new Vector3(sb.max.x+PassageMargin,level,end));
        return Relative(cut,stair,floor);
    }
    public static bool Aligned(BuildPieceRecord a,BuildPieceRecord b)=>BuildGeometry.Turn(a.WorldYawStep-b.WorldYawStep)%2==0;
    public static Bounds Relative(Bounds b,BuildPieceRecord from,BuildPieceRecord to)
    {
        var q=Quaternion.Inverse(BuildGeometry.Rotation(to.WorldYawStep));return BuildGeometry.WorldBounds(b,q*(from.Origin-to.Origin),from.WorldYawStep-to.WorldYawStep);
    }
    public static Bounds Box(Vector3 lo,Vector3 hi)=>new((lo+hi)*.5f,hi-lo);
    private static BuildTimberPart Part(Vector3 lo,Vector3 hi,int grain)=>new(){bounds=Box(lo,hi),grainAxis=grain};
    public static bool PlanOverlap(Bounds a,Bounds b)=>Mathf.Min(a.max.x,b.max.x)-Mathf.Max(a.min.x,b.min.x)>.005f && Mathf.Min(a.max.z,b.max.z)-Mathf.Max(a.min.z,b.min.z)>.005f;
    private static void AddCut(List<Bounds> list,Bounds cut,Bounds outer)
    {
        if(!PlanOverlap(cut,outer))return;list.Add(Box(new Vector3(Mathf.Max(cut.min.x,outer.min.x),outer.min.y,Mathf.Max(cut.min.z,outer.min.z)),
            new Vector3(Mathf.Min(cut.max.x,outer.max.x),outer.max.y,Mathf.Min(cut.max.z,outer.max.z))));
    }
    public static List<Bounds> Subtract(IEnumerable<Bounds> source,IEnumerable<Bounds> cuts)
    {
        var result=new List<Bounds>(source);
        foreach(var cut in cuts)
        {
            var next=new List<Bounds>();
            foreach(var b in result)
            {
                if(!PlanOverlap(b,cut)){next.Add(b);continue;}
                Vector3 lo=b.min,hi=b.max;float x0=Mathf.Max(lo.x,cut.min.x),x1=Mathf.Min(hi.x,cut.max.x),z0=Mathf.Max(lo.z,cut.min.z),z1=Mathf.Min(hi.z,cut.max.z);
                void Add(Vector3 a,Vector3 c){if(c.x-a.x>.005f && c.z-a.z>.005f)next.Add(Box(a,c));}
                Add(lo,new Vector3(x0,hi.y,hi.z));Add(new Vector3(x1,lo.y,lo.z),hi);
                Add(new Vector3(x0,lo.y,lo.z),new Vector3(x1,hi.y,z0));Add(new Vector3(x0,lo.y,z1),new Vector3(x1,hi.y,hi.z));
            }
            result=next;
        }
        Sort(result);return result;
    }
    private static void Sort(List<Bounds> boxes)=>boxes.Sort((a,b)=>{for(int i=0;i<3;i++){int c=a.min[i].CompareTo(b.min[i]);if(c!=0)return c;}for(int i=0;i<3;i++){int c=a.size[i].CompareTo(b.size[i]);if(c!=0)return c;}return 0;});
    private static void MergeCuts(List<Bounds> cuts)
    {
        bool changed=true;
        while(changed)
        {
            changed=false;
            for(int i=0;i<cuts.Count && !changed;i++)for(int j=i+1;j<cuts.Count;j++)
            {
                var a=cuts[i];var b=cuts[j];
                bool sameX=Mathf.Abs(a.min.x-b.min.x)<.001f && Mathf.Abs(a.max.x-b.max.x)<.001f && a.max.z>=b.min.z-.001f && b.max.z>=a.min.z-.001f;
                bool sameZ=Mathf.Abs(a.min.z-b.min.z)<.001f && Mathf.Abs(a.max.z-b.max.z)<.001f && a.max.x>=b.min.x-.001f && b.max.x>=a.min.x-.001f;
                if(!sameX && !sameZ)continue;
                a.Encapsulate(b);cuts[i]=a;cuts.RemoveAt(j);changed=true;break;
            }
        }
    }
    private static string Key(BuildDefinition d,string type,List<Bounds> boxes)
    {
        var s=new StringBuilder(d.GetEntityId()+":"+type);foreach(var b in boxes)for(int i=0;i<3;i++){s.Append(':').Append(Mathf.RoundToInt(b.min[i]*10000));s.Append(',').Append(Mathf.RoundToInt(b.size[i]*10000));}return s.ToString();
    }
    public void Dispose(){foreach(var mesh in meshes.Values)BuildLifetime.Destroy(mesh);meshes.Clear();}
}
