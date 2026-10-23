using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

/// <summary>Session-owned numeric building solids and reversible, bounded CPU foliage work.
/// Workers never access Unity objects. No frame waits for a worker.</summary>
public sealed class BuildFoliageExclusion : IDisposable
{
    public const int InstanceSlice = 128, ShapeSlice = 16, MaxWorkers = 2, MaxStepsPerFrame = 64, MaxPublicationsPerFrame = 4;
    public const double FrameBudgetMs = .75;
    private const float CellSize = 32, Padding = .035f;
    private static readonly ProfilerMarker TickMarker = new("FS.Building.Foliage.BudgetedUpdate");
    private static readonly ProfilerMarker WorkerMarker = new("FS.Building.Foliage.Intersections");
    public static BuildFoliageExclusion Active { get; private set; }
    internal sealed class Cell
    {
        public Vector2Int Key;
        public int Revision;
        public readonly Dictionary<ulong, Shape[]> Owners = new();
        public readonly LinkedList<Filter> Filters = new();
        public LinkedListNode<Filter> DirtyCursor;
        public bool Dirty;
    }
    private sealed class Owner { public Bounds Bounds; public Shape[] Shapes; }
    public sealed class Shape
    {
        private readonly Matrix4x4 inverse;
        private readonly Vector4[] planes;
        internal Shape(Matrix4x4 inverse, Vector4[] planes) { this.inverse = inverse; this.planes = planes; }
        public bool Intersects(Probe probe)
        {
            Vector3 p = inverse.MultiplyPoint3x4(probe.Root);
            Vector3 segment = inverse.MultiplyVector(Vector3.up * probe.Height);
            float enter = 0, leave = 1;
            foreach (var plane in planes)
            {
                float distance = plane.x*p.x + plane.y*p.y + plane.z*p.z + plane.w - Padding - probe.Radius*(float)Math.Sqrt(plane.x*plane.x+plane.z*plane.z);
                float direction = plane.x*segment.x + plane.y*segment.y + plane.z*segment.z;
                if (Math.Abs(direction) < .000001f) { if (distance > 0) return false; continue; }
                float t = -distance / direction;
                if (direction > 0) leave = Math.Min(leave, t); else enter = Math.Max(enter, t);
                if (enter > leave) return false;
            }
            return true;
        }
    }
    public struct Probe
    {
        public Vector3 Root;
        public float Height, Radius;
        public Probe(Vector3 root, float height, float radius = 0) { Root = root; Height = Math.Max(0, height); Radius = Math.Max(0, radius); }
        public Bounds Bounds => new(Root + Vector3.up * (Height*.5f), new Vector3(Radius*2, Height, Radius*2));
    }
    private readonly Dictionary<Vector2Int, Cell> cells = new();
    private readonly Dictionary<ulong, Owner> owners = new();
    private readonly List<Filter> filters = new();
    // Retired tasks still count against admission until finished. Disposal never waits.
    private readonly List<Task> workers = new();
    private readonly Queue<Filter> pending = new();
    private readonly Queue<Cell> dirtyCells = new();
    private int revision, publications;
    public int LastPublications => publications;
    public double LastFrameMs { get; private set; }
    public double PeakFrameMs { get; private set; }
    public int LastSteps { get; private set; }
    public int RunningWorkers => workers.Count;
    public int FilterCount => filters.Count;
    public int SpatialCellCount => cells.Count;
    public bool IsIdle => pending.Count==0 && dirtyCells.Count==0 && workers.Count==0;
    public void Activate() => Active = this;
    private static Vector2Int CellOf(Vector3 p) => new(Mathf.FloorToInt(p.x/CellSize), Mathf.FloorToInt(p.z/CellSize));
    private IEnumerable<Vector2Int> Keys(Bounds bounds)
    {
        var a = CellOf(bounds.min); var b = CellOf(bounds.max);
        for (int z=a.y; z<=b.y; z++) for (int x=a.x; x<=b.x; x++) yield return new Vector2Int(x,z);
    }
    public void Set(BuildPieceRecord piece)
    {
        Remove(piece.Id);
        var state = piece.Resolved;
        var inverse = Matrix4x4.TRS(piece.Origin, BuildGeometry.Rotation(piece.WorldYawStep), Vector3.one).inverse;
        var shapes = new List<Shape>();
        foreach (var box in state.boxes)
        {
            var a=box.min; var b=box.max;
            shapes.Add(new Shape(inverse, new[] { new Vector4(1,0,0,-b.x), new Vector4(-1,0,0,a.x),
                new Vector4(0,1,0,-b.y), new Vector4(0,-1,0,a.y), new Vector4(0,0,1,-b.z), new Vector4(0,0,-1,a.z) }));
        }
        foreach (var volume in state.volumes)
        {
            var planes = new List<Vector4>();
            foreach (var raw in volume.faceAxes)
            {
                var axis=raw.normalized; float min=float.PositiveInfinity, max=float.NegativeInfinity;
                foreach(var vertex in volume.vertices) { float p=Vector3.Dot(vertex,axis); min=Mathf.Min(min,p); max=Mathf.Max(max,p); }
                planes.Add(new Vector4(axis.x,axis.y,axis.z,-max)); planes.Add(new Vector4(-axis.x,-axis.y,-axis.z,min));
            }
            shapes.Add(new Shape(inverse, planes.ToArray()));
        }
        var bounds=BuildGeometry.WorldBounds(state.Bounds,piece.Origin,piece.WorldYawStep); bounds.Expand(Padding*2);
        var owner=new Owner { Bounds=bounds, Shapes=shapes.ToArray() }; owners.Add(piece.Id,owner); revision++;
        foreach(var key in Keys(bounds))
        {
            if(!cells.TryGetValue(key,out var cell))cells.Add(key,cell=new Cell { Key=key });
            cell.Owners[piece.Id]=owner.Shapes; cell.Revision=revision; Dirty(cell);
        }
    }
    public void Remove(ulong id)
    {
        if(!owners.TryGetValue(id,out var owner))return;
        owners.Remove(id); revision++;
        foreach(var key in Keys(owner.Bounds))
        {
            var cell=cells[key];cell.Owners.Remove(id);cell.Revision=revision;Dirty(cell);Prune(cell);
        }
    }
    private void Prune(Cell cell)
    {
        if(cell.Filters.Count==0 && cell.Owners.Count==0 && cells.TryGetValue(cell.Key,out var current) && ReferenceEquals(current,cell))cells.Remove(cell.Key);
    }
    private void Dirty(Cell cell)
    {
        // A second edit may arrive after this pass already visited some consumers.
        // Restart its bounded notification cursor so those consumers cannot miss it.
        if(cell.Dirty) { cell.DirtyCursor=cell.Filters.First;return; }
        cell.Dirty=true;cell.DirtyCursor=cell.Filters.First;dirtyCells.Enqueue(cell);
    }
    private void Enqueue(Filter filter)
    {
        if(filter.Queued || filter.Index<0)return;
        filter.Queued=true;pending.Enqueue(filter);
    }
    private void Subscribe(Filter filter,Bounds bounds)
    {
        foreach(var key in Keys(bounds))
        {
            if(!cells.TryGetValue(key,out var cell))cells.Add(key,cell=new Cell { Key=key });
            filter.Subscriptions.Add((cell,cell.Filters.AddLast(filter)));
        }
    }
    private int Stamp(Bounds bounds)
    {
        int stamp=0; foreach(var key in Keys(bounds))if(cells.TryGetValue(key,out var cell))stamp=Math.Max(stamp,cell.Revision);
        return stamp;
    }
    private IEnumerable<Shape> Shapes(Bounds bounds)
    {
        // Duplicate owners across cells are harmless. Avoid an unbounded deduplication pass.
        foreach(var key in Keys(bounds))if(cells.TryGetValue(key,out var cell))
            foreach(var list in cell.Owners.Values)foreach(var shape in list)yield return shape;
    }
    public Filter Register(int count, Func<int,Probe> read, Action<int,bool[],int> apply)
    {
        var filter=new Filter(this,count,read,apply); filter.Index=filters.Count; filters.Add(filter); Enqueue(filter); return filter;
    }
    private void Unregister(Filter filter)
    {
        int index=filter.Index, last=filters.Count-1;
        if(index<0)return;
        filters[index]=filters[last]; filters[index].Index=index; filters.RemoveAt(last); filter.Index=-1;
        foreach(var subscription in filter.Subscriptions)
        {
            if(subscription.cell.DirtyCursor==subscription.node)subscription.cell.DirtyCursor=subscription.node.Next;
            subscription.cell.Filters.Remove(subscription.node);
            Prune(subscription.cell);
        }
        filter.Subscriptions.Clear();
    }
    public void Tick()
    {
        using var scope=TickMarker.Auto();
        long started=Stopwatch.GetTimestamp(); LastSteps=0;publications=0;
        for(int i=workers.Count-1;i>=0;i--)if(workers[i].IsCompleted)workers.RemoveAt(i);
        while(LastSteps<MaxStepsPerFrame && (pending.Count>0 || dirtyCells.Count>0))
        {
            if(Elapsed(started)>=FrameBudgetMs)break;
            if(dirtyCells.Count>0 && (pending.Count==0 || (LastSteps&1)==0))
            {
                var cell=dirtyCells.Peek();var node=cell.DirtyCursor;
                if(node==null) { cell.Dirty=false;dirtyCells.Dequeue(); }
                else { cell.DirtyCursor=node.Next;Enqueue(node.Value); }
            }
            else
            {
                var filter=pending.Dequeue();filter.Queued=false;
                if(filter.Index>=0) { filter.Step();if(!filter.Settled)Enqueue(filter); }
            }
            LastSteps++;
        }
        LastFrameMs=Elapsed(started); PeakFrameMs=Math.Max(PeakFrameMs,LastFrameMs);
    }
    private static double Elapsed(long start)=>(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
    public void Dispose()
    {
        if(ReferenceEquals(Active,this))Active=null;
        // Tasks own only numeric snapshots; abandoning a finished/unfinished task is safe.
        while(filters.Count>0)filters[filters.Count-1].Dispose();
        cells.Clear(); owners.Clear(); workers.Clear();pending.Clear();dirtyCells.Clear();
    }
    public sealed class Filter : IDisposable
    {
        private readonly BuildFoliageExclusion world;
        private readonly Func<int,Probe> read;
        private readonly Action<int,bool[],int> apply;
        private readonly Probe[] probes;
        private Bounds bounds;
        private int captured, offset, stamp=0, checkedRevision=-1;
        private bool complete=true;
        private IEnumerator<Shape> shapes;
        private bool[] hidden;
        private Task<bool[]> job;
        internal int Index=-1;
        internal bool Queued;
        private readonly List<(Cell cell,LinkedListNode<Filter> node)> subscriptions=new();
        internal List<(Cell cell,LinkedListNode<Filter> node)> Subscriptions=>subscriptions;
        internal bool Settled=>captured==probes.Length && complete;
        public bool IsCurrent => captured==probes.Length && complete && stamp==world.Stamp(bounds);
        internal Filter(BuildFoliageExclusion world,int count,Func<int,Probe> read,Action<int,bool[],int> apply)
        { this.world=world; this.read=read; this.apply=apply; probes=new Probe[count]; }
        internal void Step()
        {
            if(captured<probes.Length)
            {
                int end=Math.Min(probes.Length,captured+InstanceSlice);
                for(;captured<end;captured++) { var p=read(captured); probes[captured]=p; if(captured==0)bounds=p.Bounds; else bounds.Encapsulate(p.Bounds); }
                if(captured==probes.Length)world.Subscribe(this,bounds);
                // The first stamp check must run before this filter can become idle.
                complete=false;
                return;
            }
            if(checkedRevision!=world.revision)
            {
                int current=world.Stamp(bounds); checkedRevision=world.revision;
                if(stamp!=current) { stamp=current; offset=0; complete=false; shapes?.Dispose(); shapes=null; hidden=null; job=null; }
                else if(offset==0 && hidden==null && shapes==null && job==null)complete=true;
            }
            if(complete)return;
            if(probes.Length==0) { complete=true; return; }
            if(job!=null)
            {
                if(!job.IsCompleted)return;
                // Completed only: no synchronous wait for unfinished work, including stale work.
                hidden=job.GetAwaiter().GetResult(); job=null; return;
            }
            int count=Math.Min(InstanceSlice,probes.Length-offset);
            if(shapes==null) { shapes=world.Shapes(bounds).GetEnumerator(); hidden=new bool[count]; }
            // Shape enumeration and every scheduled work packet are bounded independently.
            if(world.workers.Count>=MaxWorkers)return;
            var packet=new List<Shape>(ShapeSlice);
            for(int i=0;i<ShapeSlice;i++)
            {
                if(!shapes.MoveNext())
                {
                    if(packet.Count>0)break;
                    if(world.publications>=MaxPublicationsPerFrame)return;
                    world.publications++;
                    apply(offset,hidden,count); offset+=count; shapes.Dispose(); shapes=null; hidden=null;
                    if(offset==probes.Length)complete=true;
                    return;
                }
                packet.Add(shapes.Current);
            }
            var inputs=new Probe[count]; Array.Copy(probes,offset,inputs,0,count);
            var prior=hidden; var solids=packet.ToArray();
            job=Task.Run(()=>
            {
                using var marker=WorkerMarker.Auto();
                for(int i=0;i<inputs.Length;i++)if(!prior[i])foreach(var shape in solids)if(shape.Intersects(inputs[i])) { prior[i]=true; break; }
                return prior;
            });
            world.workers.Add(job);
        }
        public void Dispose() { world.Unregister(this); shapes?.Dispose(); shapes=null; job=null; }
    }
}
