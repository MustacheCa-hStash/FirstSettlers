using System;
using System.Collections.Generic;
using UnityEngine;

public enum TreePlacementDetail
{
    Distant = 0,
    Detailed = 1
}

/// <summary>
/// World-scoped, main-thread tree data. Registration follows placement availability, not render visibility.
/// Records are retained until Clear; changed states also survive placement snapshot replacement.
/// </summary>
public sealed class TreeRegistry
{
    private sealed class ChunkSnapshot
    {
        public TreePlacementDetail Detail;
        public IReadOnlyList<TreeInstanceData> Source;
        public int SourceRevision;
        public readonly List<TreeRecord> Records = new List<TreeRecord>();
        public readonly IReadOnlyList<TreeRecord> View;
        public ChunkSnapshot() { View = Records.AsReadOnly(); }
    }

    private readonly int worldSeed;
    private readonly float chunkWorldSize;
    private readonly Dictionary<TreeId, TreeRecord> records = new Dictionary<TreeId, TreeRecord>();
    private readonly Dictionary<ChunkCoord, ChunkSnapshot> chunks = new Dictionary<ChunkCoord, ChunkSnapshot>();
    private readonly Dictionary<TreeId, TreeState> changedStates = new Dictionary<TreeId, TreeState>();

    public int Count => records.Count;
    public int ChunkCount => chunks.Count;
    /// <summary>Live main-thread view of registered trees, including distant placements and all states.</summary>
    public IEnumerable<TreeRecord> Records => records.Values;
    public event Action<ChunkCoord> ChunkChanged;

    public TreeRegistry(int worldSeed, float chunkWorldSize)
    {
        if (float.IsNaN(chunkWorldSize) || float.IsInfinity(chunkWorldSize) || chunkWorldSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(chunkWorldSize));
        this.worldSeed = worldSeed;
        this.chunkWorldSize = chunkWorldSize;
    }

    public bool TryGet(TreeId id, out TreeRecord record) => records.TryGetValue(id, out record);
    public bool ContainsChunk(ChunkCoord coord) => chunks.ContainsKey(coord);
    public IReadOnlyList<TreeRecord> GetChunk(ChunkCoord coord) => chunks.TryGetValue(coord, out var chunk)
        ? chunk.View : Array.Empty<TreeRecord>();

    /// <summary>
    /// Installs a complete placement snapshot. Detailed terrain wins over late distant results.
    /// Increment sourceRevision when editing an already registered list in place.
    /// Returns false when the snapshot is already current or has lower authority.
    /// </summary>
    public bool RegisterChunk(ChunkCoord coord, IReadOnlyList<TreeInstanceData> placements,
        TreePlacementDetail detail, int sourceRevision = 0)
    {
        if (placements == null) throw new ArgumentNullException(nameof(placements));
        if (detail != TreePlacementDetail.Distant && detail != TreePlacementDetail.Detailed)
            throw new ArgumentOutOfRangeException(nameof(detail));
        chunks.TryGetValue(coord, out var chunk);
        if (chunk != null && (chunk.Detail > detail ||
            (chunk.Detail == detail && ReferenceEquals(chunk.Source, placements) && chunk.SourceRevision == sourceRevision)))
            return false;

        var ids = new HashSet<TreeId>();
        for (int i = 0; i < placements.Count; i++)
        {
            TreeId id = placements[i].id;
            if (!id.IsValid || id.WorldSeed != worldSeed || id.Chunk != coord)
                throw new ArgumentException("Tree ID does not belong to this world and chunk: " + id, nameof(placements));
            if (!ids.Add(id)) throw new ArgumentException("Duplicate tree ID: " + id, nameof(placements));
        }

        if (chunk == null)
        {
            chunk = new ChunkSnapshot();
            chunks.Add(coord, chunk);
        }
        foreach (var previous in chunk.Records)
            if (!ids.Contains(previous.Id)) records.Remove(previous.Id);
        chunk.Records.Clear();
        for (int i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            if (records.TryGetValue(placement.id, out var record))
                record.RefreshPlacement(placement, chunkWorldSize);
            else
            {
                changedStates.TryGetValue(placement.id, out var state);
                record = new TreeRecord(placement, chunkWorldSize, state);
                records.Add(placement.id, record);
            }
            chunk.Records.Add(record);
        }
        chunk.Detail = detail;
        chunk.Source = placements;
        chunk.SourceRevision = sourceRevision;
        ChunkChanged?.Invoke(coord);
        return true;
    }

    public bool TrySetState(TreeId id, TreeState state)
    {
        if (state < TreeState.Standing || state > TreeState.Removed)
            throw new ArgumentOutOfRangeException(nameof(state));
        if (!records.TryGetValue(id, out var record) || record.State == state) return false;
        record.SetState(state);
        if (state == TreeState.Standing) changedStates.Remove(id);
        else changedStates[id] = state;
        ChunkChanged?.Invoke(record.Chunk);
        return true;
    }

    /// <summary>
    /// Appends records whose original trunk origins lie within an XZ radius. Includes all states.
    /// This is a data query, not a canopy extent or physical collision test.
    /// </summary>
    public void CollectOriginsInRadiusXZ(Vector3 center, float radius, List<TreeRecord> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        if (radius < 0f || float.IsNaN(radius) || float.IsInfinity(radius))
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (float.IsNaN(center.x) || float.IsInfinity(center.x) || float.IsNaN(center.z) || float.IsInfinity(center.z))
            throw new ArgumentOutOfRangeException(nameof(center));
        int minX = Mathf.FloorToInt((center.x - radius) / chunkWorldSize);
        int maxX = Mathf.FloorToInt((center.x + radius) / chunkWorldSize);
        int minZ = Mathf.FloorToInt((center.z - radius) / chunkWorldSize);
        int maxZ = Mathf.FloorToInt((center.z + radius) / chunkWorldSize);
        float radiusSquared = radius * radius;
        for (long x = minX; x <= maxX; x++)
            for (long z = minZ; z <= maxZ; z++)
            {
                if (!chunks.TryGetValue(new ChunkCoord((int)x, (int)z), out var chunk)) continue;
                foreach (var record in chunk.Records)
                {
                    Vector3 position = record.WorldPosition;
                    float dx = position.x - center.x, dz = position.z - center.z;
                    if (dx * dx + dz * dz <= radiusSquared) results.Add(record);
                }
            }
    }

    /// <summary>Call only when replacing or disposing the entire world, not when hiding its visuals.</summary>
    public void Clear()
    {
        records.Clear();
        foreach (var chunk in chunks.Values) chunk.Records.Clear();
        chunks.Clear();
        changedStates.Clear();
    }
}
