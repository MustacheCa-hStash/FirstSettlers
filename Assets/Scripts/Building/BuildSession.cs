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
    // Grounding is logical state, never inferred from a streaming collider disappearing.
    public bool Grounded;
    [NonSerialized] public bool Supported;
    [NonSerialized] public Vector3 Origin;
    [NonSerialized] public byte WorldYawStep;
    [NonSerialized] public BuildDefinition Definition;
    [NonSerialized] public Bounds WorldBounds;
    [NonSerialized] public readonly HashSet<ulong> Connections = new();
}

/// <summary>Session authority. Contains no GameObjects, colliders, or renderers. Frames outlive removed anchors.</summary>
public sealed class BuildSession
{
    private static readonly ProfilerMarker SupportMarker = new("FS.Building.Support");
    private ulong nextPiece = 1, nextFrame = 1;
    private readonly Dictionary<ulong, BuildPieceRecord> pieces = new();
    private readonly Dictionary<ulong, BuildGridFrame> frames = new();
    private readonly Dictionary<Vector3Int, HashSet<ulong>> cells = new();
    private readonly List<BuildPieceRecord> neighbours = new();
    private readonly HashSet<ulong> seen = new();
    private readonly Queue<BuildPieceRecord> supportQueue = new();
    public IReadOnlyDictionary<ulong, BuildPieceRecord> Pieces => pieces;
    public IReadOnlyDictionary<ulong, BuildGridFrame> Frames => frames;
    public int Revision { get; private set; }
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
        var indexed = definition.LocalBounds; indexed.Encapsulate(BuildOccupancy.Bounds(definition));
        piece.WorldBounds = BuildGeometry.WorldBounds(indexed, piece.Origin, piece.WorldYawStep);
        piece.OwnFrameId = CreateFrame(piece.Origin, piece.WorldYawStep).Id;
        Bounds search = piece.WorldBounds; search.Expand(.04f);
        Query(search, neighbours);
        foreach (var other in neighbours)
            if (BuildGeometry.Connects(definition, piece.Origin, piece.WorldYawStep, other.Definition, other.Origin, other.WorldYawStep))
            { piece.Connections.Add(other.Id); other.Connections.Add(piece.Id); }
        pieces.Add(piece.Id, piece);
        ForCells(piece.WorldBounds, key => {
            if (!cells.TryGetValue(key, out var ids)) cells.Add(key, ids = new HashSet<ulong>());
            ids.Add(piece.Id);
        });
        RecomputeSupport(); Revision++;
        return piece;
    }
    public bool Remove(ulong id)
    {
        if (!pieces.TryGetValue(id, out var piece)) return false;
        foreach (ulong neighbour in piece.Connections) if (pieces.TryGetValue(neighbour, out var other)) other.Connections.Remove(id);
        ForCells(piece.WorldBounds, key => { if (cells.TryGetValue(key, out var ids)) { ids.Remove(id); if (ids.Count == 0) cells.Remove(key); } });
        pieces.Remove(id); RecomputeSupport(); Revision++; return true;
    }
    public bool Damage(ulong id, float amount)
    {
        if (!pieces.TryGetValue(id, out var piece) || amount <= 0 || float.IsNaN(amount)) return false;
        piece.Health = Mathf.Max(0, piece.Health - amount);
        if (piece.Health == 0) Remove(id); else Revision++;
        return true;
    }
    public void RecomputeSupport()
    {
        using var scope = SupportMarker.Auto();
        supportQueue.Clear();
        foreach (var piece in pieces.Values)
        {
            piece.Supported = piece.Grounded;
            if (piece.Supported) supportQueue.Enqueue(piece);
        }
        while (supportQueue.Count > 0)
            foreach (ulong id in supportQueue.Dequeue().Connections)
                if (pieces.TryGetValue(id, out var other) && !other.Supported) { other.Supported = true; supportQueue.Enqueue(other); }
    }
    public void Query(Bounds bounds, List<BuildPieceRecord> output)
    {
        output.Clear(); seen.Clear();
        // Deliberately no captured delegate in the per-frame query path.
        Vector3Int min = BuildGeometry.Cell(bounds.min), max = BuildGeometry.Cell(bounds.max);
        for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++)
            if (cells.TryGetValue(new Vector3Int(x, y, z), out var ids))
                foreach (ulong id in ids) if (seen.Add(id) && pieces.TryGetValue(id, out var piece) && piece.WorldBounds.Intersects(bounds)) output.Add(piece);
    }
    private static void ForCells(Bounds bounds, Action<Vector3Int> visit)
    {
        Vector3Int min = BuildGeometry.Cell(bounds.min), max = BuildGeometry.Cell(bounds.max);
        for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++) visit(new Vector3Int(x, y, z));
    }
}
