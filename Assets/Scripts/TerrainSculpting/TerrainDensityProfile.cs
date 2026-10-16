using System;

/// <summary>Immutable geometry/encoding profile. Changing it requires a different saved world profile.</summary>
public readonly struct TerrainDensityProfile : IEquatable<TerrainDensityProfile>
{
    public const int BrickEdge = 16;
    public const int SamplesPerBrick = BrickEdge * BrickEdge * BrickEdge;
    public const int DetailedBrickBytes = SamplesPerBrick * sizeof(short);
    public const short MaximumCode = short.MaxValue;
    public readonly double Spacing;
    public readonly double Band;
    public double BrickWidth => Spacing * BrickEdge;
    public double EncodingStep => Band / MaximumCode;
    // Match the actual serialized float world scale, including its binary representation.
    public static TerrainDensityProfile Default => new TerrainDensityProfile(0.3f, 1.2f);

    public TerrainDensityProfile(double spacing, double band)
    {
        if (!Finite(spacing) || spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));
        if (!Finite(band) || band < spacing) throw new ArgumentOutOfRangeException(nameof(band));
        Spacing = spacing;
        Band = band;
    }

    public void Validate()
    {
        if (!Finite(Spacing) || Spacing <= 0 || !Finite(Band) || Band < Spacing)
            throw new ArgumentException("A constructed positive density profile is required.");
    }

    public short Encode(double density)
    {
        if (!Finite(density)) throw new ArgumentOutOfRangeException(nameof(density), "Density must be finite.");
        double normalized = Math.Max(-1, Math.Min(1, density / Band));
        return (short)Math.Round(normalized * MaximumCode, MidpointRounding.AwayFromZero);
    }

    public double Decode(short value)
    {
        if (value == short.MinValue) throw new ArgumentException("Reserved invalid density code.", nameof(value));
        return value * (Band / MaximumCode);
    }

    public bool Equals(TerrainDensityProfile other) => Spacing.Equals(other.Spacing) && Band.Equals(other.Band);
    public override bool Equals(object obj) => obj is TerrainDensityProfile other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Spacing, Band);
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
