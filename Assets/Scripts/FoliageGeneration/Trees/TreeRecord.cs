using UnityEngine;

public enum TreeState
{
    Standing = 0,
    Cut = 1,
    Fallen = 2,
    Removed = 3
}

/// <summary>World data only. Contains no renderer, collider or GameObject references.</summary>
public sealed class TreeRecord
{
    public TreeId Id => Placement.id;
    public ChunkCoord Chunk => Id.Chunk;
    public TreeInstanceData Placement { get; private set; }
    public Vector3 WorldPosition { get; private set; }
    public float ExclusionRadiusWorld => Placement.exclusionRadiusWorld;
    public TreeState State { get; private set; }

    internal TreeRecord(TreeInstanceData placement, float chunkWorldSize, TreeState state)
    {
        RefreshPlacement(placement, chunkWorldSize);
        State = state;
    }

    internal void RefreshPlacement(TreeInstanceData placement, float chunkWorldSize)
    {
        Placement = placement;
        ChunkCoord coord = placement.id.Chunk;
        WorldPosition = new Vector3((coord.x + 0.5f) * chunkWorldSize, 0f,
            (coord.z + 0.5f) * chunkWorldSize) + placement.localPosition;
    }

    internal void SetState(TreeState state) => State = state;
}
