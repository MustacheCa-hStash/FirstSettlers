using System;

/// <summary>Immutable contract for the canonical AC-diagonal, zero-origin source lattice.
/// Boundary sampling, collar width and guard width are deliberately independent.</summary>
public readonly struct TerrainSourceSurfaceLayout : IEquatable<TerrainSourceSurfaceLayout>
{
    // Derived geometry only. This is deliberately outside the density/save identity.
    public const int ReconstructionVersion=3;
    public readonly double Spacing;
    public readonly int BoundaryStep,CollarCells,GuardCells;
    public TerrainSourceSurfaceLayout(double spacing,int boundaryStep=1,int collarCells=1,int guardCells=2)
    {
        if(!double.IsFinite(spacing)||spacing<=0||boundaryStep<1||boundaryStep>16||collarCells<1||
            collarCells%boundaryStep!=0||guardCells<2)throw new ArgumentException("Unsupported source surface layout.");
        Spacing=spacing;BoundaryStep=boundaryStep;CollarCells=collarCells;GuardCells=guardCells;
    }
    public static TerrainSourceSurfaceLayout Current(TerrainDensityProfile profile)=>
        new TerrainSourceSurfaceLayout(profile.Spacing,ColliderMeshGenerator.RuntimeStep);
    public bool Equals(TerrainSourceSurfaceLayout other)=>Spacing==other.Spacing&&BoundaryStep==other.BoundaryStep&&CollarCells==other.CollarCells&&GuardCells==other.GuardCells;
    public override bool Equals(object obj)=>obj is TerrainSourceSurfaceLayout other&&Equals(other);
    public override int GetHashCode()=>HashCode.Combine(Spacing,BoundaryStep,CollarCells,GuardCells,ReconstructionVersion);
}
