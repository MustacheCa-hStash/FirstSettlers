using System;

public readonly struct SubChunkCoord : IEquatable<SubChunkCoord>
{
    public readonly int x;
    public readonly int z;

    public SubChunkCoord(int x, int z)
    {
        this.x = x;
        this.z = z;
    }

    public bool Equals(SubChunkCoord other)
    {
        return this.x == other.x && this.z == other.z;
    }

    public override bool Equals(object obj)
    {
        return obj is SubChunkCoord other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(x, z);
    }

    public static bool operator ==(SubChunkCoord a, SubChunkCoord b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(SubChunkCoord a, SubChunkCoord b)
    {
        return !a.Equals(b);
    }

    public override string ToString()
    {
        return $"({x}, {z})";
    }
}