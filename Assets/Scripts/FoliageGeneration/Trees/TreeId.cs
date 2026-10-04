using System;
using UnityEngine;

// Values are part of generated placement identity. Do not renumber them.
public enum TreePlacementSource
{
    Forest = 1, // Forest and Taiga share the same candidate grid.
    Grassland = 2,
    Snow = 3
}

/// <summary>A generated placement's identity, independent of list order, height, appearance and GameObjects.</summary>
[Serializable]
public struct TreeId : IEquatable<TreeId>
{
    [SerializeField] private int worldSeed;
    [SerializeField] private int chunkX;
    [SerializeField] private int chunkZ;
    [SerializeField] private TreePlacementSource source;
    [SerializeField] private int candidateCell;

    public int WorldSeed => worldSeed;
    public ChunkCoord Chunk => new ChunkCoord(chunkX, chunkZ);
    public TreePlacementSource Source => source;
    public int CandidateCell => candidateCell;
    public bool IsValid => source >= TreePlacementSource.Forest && source <= TreePlacementSource.Snow && candidateCell >= 0;

    public static TreeId Generated(int seed, ChunkCoord chunk, TreePlacementSource source, int candidateCell)
    {
        if (source < TreePlacementSource.Forest || source > TreePlacementSource.Snow)
            throw new ArgumentOutOfRangeException(nameof(source));
        if (candidateCell < 0) throw new ArgumentOutOfRangeException(nameof(candidateCell));
        return new TreeId { worldSeed = seed, chunkX = chunk.x, chunkZ = chunk.z,
            source = source, candidateCell = candidateCell };
    }

    public bool Equals(TreeId other) => worldSeed == other.worldSeed && chunkX == other.chunkX &&
        chunkZ == other.chunkZ && source == other.source && candidateCell == other.candidateCell;
    public override bool Equals(object obj) => obj is TreeId other && Equals(other);
    public override int GetHashCode()
    {
        // The complete tuple is the ID; this hash is only for collection lookup.
        unchecked
        {
            int hash = worldSeed;
            hash = hash * 397 ^ chunkX;
            hash = hash * 397 ^ chunkZ;
            hash = hash * 397 ^ (int)source;
            return hash * 397 ^ candidateCell;
        }
    }

    public static bool operator ==(TreeId a, TreeId b) => a.Equals(b);
    public static bool operator !=(TreeId a, TreeId b) => !a.Equals(b);
    public override string ToString() => IsValid
        ? $"tree:v1:{worldSeed}:{chunkX}:{chunkZ}:{(int)source}:{candidateCell}"
        : "tree:invalid";
}
