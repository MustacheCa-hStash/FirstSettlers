using System;
using Unity.Mathematics;

/// <summary>One integer lattice, including correct ownership left/below world origin.</summary>
public static class TerrainDensityCoordinates
{
    public static int FloorDiv(int value, int divisor)
    {
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        int q = value / divisor;
        return value % divisor < 0 ? q - 1 : q;
    }

    public static int3 BrickOf(int3 sample) => new int3(
        FloorDiv(sample.x, TerrainDensityProfile.BrickEdge),
        FloorDiv(sample.y, TerrainDensityProfile.BrickEdge),
        FloorDiv(sample.z, TerrainDensityProfile.BrickEdge));

    public static int3 LocalOf(int3 sample)
    {
        int3 owner = BrickOf(sample);
        // The product can equal int.MinValue, but never overflows for a valid sample.
        return sample - owner * TerrainDensityProfile.BrickEdge;
    }

    public static int Index(int3 local)
    {
        if (math.any(local < 0) || math.any(local >= TerrainDensityProfile.BrickEdge))
            throw new ArgumentOutOfRangeException(nameof(local));
        return local.x + TerrainDensityProfile.BrickEdge * (local.y + TerrainDensityProfile.BrickEdge * local.z);
    }

    public static int3 LocalFromIndex(int index)
    {
        if (index < 0 || index >= TerrainDensityProfile.SamplesPerBrick) throw new ArgumentOutOfRangeException(nameof(index));
        return new int3(index % 16, index / 16 % 16, index / 256);
    }

    public static int3 Origin(int3 brick)
    {
        return new int3(checked(brick.x * 16), checked(brick.y * 16), checked(brick.z * 16));
    }

    public static double3 ToWorld(int3 sample, TerrainDensityProfile profile) => (double3)sample * profile.Spacing;
    public static int3 FloorSample(double3 world, TerrainDensityProfile profile) => new int3(
        FloorIndex(world.x / profile.Spacing), FloorIndex(world.y / profile.Spacing), FloorIndex(world.z / profile.Spacing));

    // Snap only arithmetic roundoff at an integer lattice position, not a gameplay-sized tolerance.
    internal static int FloorIndex(double q)
    {
        if (!TerrainDensityProfile.Finite(q)) throw new ArgumentOutOfRangeException(nameof(q));
        double rounded = Math.Round(q);
        if (Math.Abs(q - rounded) <= 8 * 2.2204460492503131e-16 * Math.Max(1, Math.Abs(q))) q = rounded;
        double floor = Math.Floor(q);
        if (floor < int.MinValue || floor > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(q));
        return (int)floor;
    }
}
