using System;

/// <summary>
/// Stable identity for one world-aligned far-terrain quadtree leaf.
/// Origin is the patch-grid coordinate (multiply by SizeInChunks to obtain the
/// logical chunk origin); SizeInChunks is a power of two.
/// </summary>
public readonly struct FarTerrainPatchKey : IEquatable<FarTerrainPatchKey>
{
    public readonly ChunkCoord Origin;
    public readonly int SizeInChunks;

    public FarTerrainPatchKey(ChunkCoord origin, int sizeInChunks)
    {
        Origin = origin;
        SizeInChunks = sizeInChunks;
    }

    public bool Equals(FarTerrainPatchKey other) => Origin == other.Origin && SizeInChunks == other.SizeInChunks;
    public override bool Equals(object obj) => obj is FarTerrainPatchKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Origin, SizeInChunks);
    public static bool operator ==(FarTerrainPatchKey left, FarTerrainPatchKey right) => left.Equals(right);
    public static bool operator !=(FarTerrainPatchKey left, FarTerrainPatchKey right) => !left.Equals(right);
    public override string ToString() => $"{Origin} / {SizeInChunks}x{SizeInChunks}";
}
