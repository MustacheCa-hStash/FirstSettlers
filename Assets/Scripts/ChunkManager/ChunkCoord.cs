using System;

public struct ChunkCoord
{
    public int x;
    public int z;

    public ChunkCoord(int x, int z)
    {
        this.x = x;
        this.z = z;
    }

    public bool Equals(ChunkCoord other)
    {
        return this.x == other.x && this.z == other.z;
    }

    public override bool Equals(object obj)
    {
        return obj is ChunkCoord && Equals((ChunkCoord)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(x, z);
    }

    public static bool operator ==(ChunkCoord a, ChunkCoord b)
    {
        return a.Equals(b);
    }
    public static bool operator !=(ChunkCoord a, ChunkCoord b)
    {
        return !a.Equals(b);
    }
}