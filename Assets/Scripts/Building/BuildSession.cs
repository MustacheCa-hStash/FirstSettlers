using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

[Serializable]
public sealed class BuildGridFrame
{
    public ulong Id;
    public Vector3 Origin;
    public byte YawStep;
}

[Serializable]
public sealed class BuildPieceRecord
{
    public ulong Id;
    public string DefinitionId;
    public ulong FrameId, OwnFrameId;
    public Vector3Int AnchorUnits;
    public byte YawStep;
    public float Health;
    // Legacy serialized metadata. Free placement does not query or require terrain grounding.
    public bool Grounded;
    [NonSerialized] public bool Supported;
    [NonSerialized] public Vector3 Origin;
    [NonSerialized] public byte WorldYawStep;
    [NonSerialized] public BuildDefinition Definition;
    [NonSerialized] public Bounds WorldBounds;
    [NonSerialized] public BuildResolvedState Resolved;
    [NonSerialized] public int ResolvedRevision;
    [NonSerialized] public readonly HashSet<ulong> Connections = new();
}

/// <summary>Session authority. Contains no GameObjects, colliders, or renderers. Frames outlive removed anchors.</summary>
public sealed class BuildSession : IDisposable
{
    private ulong nextPiece = 1, nextFrame = 1;
    private readonly Dictionary<ulong, BuildPieceRecord> pieces = new();
    private readonly Dictionary<ulong, BuildGridFrame> frames = new();
    private readonly Dictionary<Vector3Int, HashSet<ulong>> cells = new();
    private readonly List<BuildPieceRecord> neighbours = new();
    private readonly HashSet<ulong> seen = new();
    private readonly BuildResolution resolution=new();
    private readonly List<LocalFlameGeometry.Part> flameParts=new();
    public IReadOnlyDictionary<ulong, BuildPieceRecord> Pieces => pieces;
    public IReadOnlyDictionary<ulong, BuildGridFrame> Frames => frames;
    public int Revision { get; private set; }
    public BuildFoliageExclusion Foliage { get; } = new();
    public bool TryGet(ulong id, out BuildPieceRecord piece) => pieces.TryGetValue(id, out piece);
    public BuildGridFrame Frame(ulong id) => frames[id];
    public BuildGridFrame CreateFrame(Vector3 origin, int yaw)
    {
        var frame = new BuildGridFrame { Id = nextFrame++, Origin = origin, YawStep = (byte)BuildGeometry.Turn(yaw) };
        frames.Add(frame.Id, frame);
        return frame;
    }
    public BuildPieceRecord Add(BuildDefinition definition, BuildGridFrame frame, Vector3Int anchor, int yaw, bool grounded)
    {
        if (definition == null || frame == null || !frames.ContainsKey(frame.Id)) throw new ArgumentException("A registered frame and component are required.");
        var piece = new BuildPieceRecord { Id = nextPiece++, DefinitionId = definition.contentId, Definition = definition,
            FrameId = frame.Id, AnchorUnits = anchor, YawStep = (byte)BuildGeometry.Turn(yaw), Health = definition.maxHealth,
            Grounded = grounded, Origin = BuildGeometry.WorldPoint(frame, anchor), WorldYawStep = (byte)BuildGeometry.Turn(frame.YawStep + yaw) };
        // Index covers structural sockets AND occupied solids. Rendering has its
        // own mesh bounds and never depends on this broad-phase envelope.
        piece.WorldBounds = IndexBounds(definition,piece.Origin,piece.WorldYawStep);
        piece.OwnFrameId = CreateFrame(piece.Origin, piece.WorldYawStep).Id;
        pieces.Add(piece.Id, piece);
        ForCells(piece.WorldBounds, key => {
            if (!cells.TryGetValue(key, out var ids)) cells.Add(key, ids = new HashSet<ulong>());
            ids.Add(piece.Id);
        });
        RebuildResolution();
        return piece;
    }
    public bool Remove(ulong id)
    {
        if (!pieces.TryGetValue(id, out var piece)) return false;
        foreach (ulong neighbour in piece.Connections) if (pieces.TryGetValue(neighbour, out var other)) other.Connections.Remove(id);
        ForCells(piece.WorldBounds, key => { if (cells.TryGetValue(key, out var ids)) { ids.Remove(id); if (ids.Count == 0) cells.Remove(key); } });
        LocalFlameGeometry.Remove(piece); Foliage.Remove(id);
        pieces.Remove(id); RebuildResolution(); return true;
    }
    public bool Damage(ulong id, float amount)
    {
        if (!pieces.TryGetValue(id, out var piece) || amount <= 0 || float.IsNaN(amount)) return false;
        piece.Health = Mathf.Max(0, piece.Health - amount);
        if (piece.Health == 0) Remove(id); else Revision++;
        return true;
    }
    // Compatibility state for existing tools. Pieces are independent of load-bearing support.
    public void RecomputeSupport() { foreach(var piece in pieces.Values)piece.Supported=true; }
    public void Query(Bounds bounds, List<BuildPieceRecord> output)
    {
        output.Clear(); seen.Clear();
        // Deliberately no captured delegate in the per-frame query path.
        Vector3Int min = BuildGeometry.Cell(bounds.min), max = BuildGeometry.Cell(bounds.max);
        for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++)
            if (cells.TryGetValue(new Vector3Int(x, y, z), out var ids))
                foreach (ulong id in ids) if (seen.Add(id) && pieces.TryGetValue(id, out var piece) && piece.WorldBounds.Intersects(bounds)) output.Add(piece);
    }
    private void RebuildResolution()
    {
        var list=new List<BuildPieceRecord>(pieces.Values);var states=resolution.Resolve(list,p=>NeighboursFor(p));Revision++;
        foreach(var p in list)
        {
            var state=states[p.Id]; bool changed=!SameFoliageSolids(state,p.Resolved);
            p.Resolved=state;p.ResolvedRevision=Revision;p.Connections.Clear();
            if(changed)Foliage.Set(p);
        }
        foreach(var p in list)
        {
            Bounds search=p.WorldBounds;search.Expand(.04f);Query(search,neighbours);
            foreach(var other in neighbours)
                if(other.Id>p.Id && BuildResolvedGeometry.Connects(p,p.Resolved,other,other.Resolved))
                {p.Connections.Add(other.Id);other.Connections.Add(p.Id);}
        }
        RecomputeSupport();
        foreach(var p in list)
        {
            flameParts.Clear();var matrix=Matrix4x4.TRS(p.Origin,BuildGeometry.Rotation(p.WorldYawStep),Vector3.one);
            var state=p.Resolved;
            if(state.mesh!=null)flameParts.Add(new(state.mesh,matrix,p.Definition.material));
            if(state.auxiliaryMesh!=null)flameParts.Add(new(state.auxiliaryMesh,matrix,p.Definition.material));
            foreach(var a in state.attachments)if(a.mesh!=null)flameParts.Add(new(a.mesh,matrix*a.localMatrix,a.material));
            LocalFlameGeometry.Set(p,flameParts);
        }
    }
    private static bool SameFoliageSolids(BuildResolvedState a,BuildResolvedState b)
    {
        if(b==null || a.boxes.Length!=b.boxes.Length || a.volumes.Length!=b.volumes.Length)return false;
        for(int i=0;i<a.boxes.Length;i++)if(a.boxes[i]!=b.boxes[i])return false;
        for(int i=0;i<a.volumes.Length;i++)
        {
            var x=a.volumes[i];var y=b.volumes[i];
            // Fitting may rebuild the numeric wrapper around an unchanged cached convex mesh.
            if(ReferenceEquals(x,y))continue;
            if(x.mesh==null || x.mesh!=y.mesh || x.bounds!=y.bounds)return false;
        }
        return true;
    }
    public Dictionary<ulong,BuildResolvedState> ResolvePreview(BuildPieceRecord candidate)
    {
        // Only direct neighbours can change because of the incoming record. One
        // additional ring supplies complete joint ownership and floor dependencies.
        var direct=NeighboursFor(candidate);var local=new List<BuildPieceRecord>(direct){candidate};
        var ids=new HashSet<ulong>{candidate.Id};foreach(var p in direct)ids.Add(p.Id);
        foreach(var p in direct)foreach(var other in NeighboursFor(p))if(ids.Add(other.Id))local.Add(other);
        PreviewResolutionCount=local.Count;
        var resolved=resolution.Resolve(local,p=>NeighboursFor(p,candidate));
        var result=new Dictionary<ulong,BuildResolvedState>{{candidate.Id,resolved[candidate.Id]}};
        foreach(var p in direct)result[p.Id]=resolved[p.Id];
        return result;
    }
    public int PreviewResolutionCount { get; private set; }
    public HashSet<ulong> PreviewSupport(BuildPieceRecord candidate,Dictionary<ulong,BuildResolvedState> proposed)
    { var result=new HashSet<ulong>(pieces.Keys);result.Add(candidate.Id);return result; }
    public IReadOnlyList<BuildPieceRecord> NeighboursFor(BuildPieceRecord p,BuildPieceRecord extra=null)
    {
        var output=new List<BuildPieceRecord>();Bounds b=p.WorldBounds;b.Expand(.04f);Query(b,output);
        if(extra!=null && p.Id!=extra.Id && b.Intersects(extra.WorldBounds))output.Add(extra);return output;
    }
    public static Bounds IndexBounds(BuildDefinition d,Vector3 origin,int yaw)
    {
        var b=d.LocalBounds;b.Encapsulate(BuildOccupancy.Bounds(d));
        if(d.jointSockets!=null)foreach(var socket in d.jointSockets)b.Encapsulate(socket.localPosition);
        if(d.kind is BuildPartKind.Wall or BuildPartKind.Corner or BuildPartKind.Roof)b.Encapsulate(b.min-Vector3.up*.25f);
        if(d.kind==BuildPartKind.Stair)b.Encapsulate(new Bounds(d.LocalBounds.center+Vector3.up,d.LocalBounds.size+new Vector3(1,2,1)));
        return BuildGeometry.WorldBounds(b,origin,yaw);
    }
    public void Dispose(){Foliage.Dispose();foreach(var p in pieces.Values)LocalFlameGeometry.Remove(p);resolution.Dispose();}
    private static void ForCells(Bounds bounds, Action<Vector3Int> visit)
    {
        Vector3Int min = BuildGeometry.Cell(bounds.min), max = BuildGeometry.Cell(bounds.max);
        for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++) visit(new Vector3Int(x, y, z));
    }
}
