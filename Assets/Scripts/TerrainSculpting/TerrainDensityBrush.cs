using System;
using Unity.Mathematics;

public enum TerrainDensityBrushShape { Sphere, Box }

/// <summary>A continuous removal shape. Box extents are half dimensions, not sample counts.</summary>
public readonly struct TerrainDensityBrush
{
    public readonly TerrainDensityBrushShape Shape;
    public readonly double3 Center;
    public readonly double3 HalfExtents;
    public double3 Minimum => Center - HalfExtents;
    public double3 Maximum => Center + HalfExtents;

    private TerrainDensityBrush(TerrainDensityBrushShape shape, double3 center, double3 extents)
    {
        if (!math.all(math.isfinite(center)) || !math.all(math.isfinite(extents)) || math.any(extents <= 0))
            throw new ArgumentOutOfRangeException(nameof(extents), "Finite centre and positive finite dimensions required.");
        if (!math.all(math.isfinite(center - extents)) || !math.all(math.isfinite(center + extents)))
            throw new ArgumentOutOfRangeException(nameof(center));
        Shape = shape; Center = center; HalfExtents = extents;
    }

    public static TerrainDensityBrush Sphere(double3 center, double radius) =>
        new TerrainDensityBrush(TerrainDensityBrushShape.Sphere, center, new double3(radius));
    public static TerrainDensityBrush Box(double3 center, double3 halfExtents) =>
        new TerrainDensityBrush(TerrainDensityBrushShape.Box, center, halfExtents);

    public double SignedDistance(double3 point)
    {
        if (Shape == TerrainDensityBrushShape.Sphere) return math.length(point - Center) - HalfExtents.x;
        double3 q = math.abs(point - Center) - HalfExtents;
        return math.length(math.max(q, 0)) + Math.Min(math.cmax(q), 0);
    }

    internal void Validate()
    {
        if ((Shape != TerrainDensityBrushShape.Sphere && Shape != TerrainDensityBrushShape.Box) ||
            !math.all(math.isfinite(Center)) || !math.all(math.isfinite(HalfExtents)) || math.any(HalfExtents <= 0))
            throw new ArgumentException("Construct a brush with Sphere or Box before editing.");
    }
}
