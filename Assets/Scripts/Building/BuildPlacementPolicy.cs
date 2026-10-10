using UnityEngine;

/// <summary>Eligibility is independent of render fitting. Build-piece intersections are advisory.</summary>
public static class BuildPlacementPolicy
{
    public static bool IsDuplicate(BuildPieceRecord a,BuildPieceRecord b)
    {
        if(a.Definition.kind!=b.Definition.kind)return false;
        int turn=BuildGeometry.Turn(a.WorldYawStep-b.WorldYawStep);
        bool sameShapeHeading=a.Definition.kind is BuildPartKind.Stair or BuildPartKind.Roof ? turn==0 :
            a.Definition.kind==BuildPartKind.Wall ? turn%4==0 : turn%2==0;
        if(!sameShapeHeading)return false;
        Bounds ab=BuildGeometry.WorldBounds(a.Definition.LocalBounds,a.Origin,a.WorldYawStep);
        Bounds bb=BuildGeometry.WorldBounds(b.Definition.LocalBounds,b.Origin,b.WorldYawStep);
        return (ab.center-bb.center).sqrMagnitude<.000144f && (ab.size-bb.size).sqrMagnitude<.000144f;
    }
    public static BuildPlacementWarning Assess(BuildPieceRecord a,BuildResolvedState sa,BuildPieceRecord b,BuildResolvedState sb)
    {
        bool roofStair=BuildResolvedGeometry.IsRoofStairPair(a.Definition.kind,b.Definition.kind);
        if(BuildResolvedGeometry.Overlaps(a,sa,b,sb))return roofStair?BuildPlacementWarning.RoofStairOverlap:BuildPlacementWarning.BuildOverlap;
        if(a.Definition.kind==BuildPartKind.Stair && BuildResolvedGeometry.BlocksStairPassage(a,b,sb) ||
            b.Definition.kind==BuildPartKind.Stair && BuildResolvedGeometry.BlocksStairPassage(b,a,sa))
            return roofStair?BuildPlacementWarning.RoofStairHeadroom:BuildPlacementWarning.StairHeadroom;
        return BuildPlacementWarning.None;
    }
    public static int Priority(BuildPlacementWarning warning)=>warning switch
    {
        BuildPlacementWarning.BuildOverlap or BuildPlacementWarning.RoofStairOverlap=>3,
        BuildPlacementWarning.StairHeadroom or BuildPlacementWarning.RoofStairHeadroom=>2,
        BuildPlacementWarning.UnresolvedJoint=>1,
        _=>0
    };
    public static string Describe(BuildPlacementWarning warning)=>warning switch
    {
        BuildPlacementWarning.BuildOverlap=>"Build-piece overlap allowed; colliders remain solid",
        BuildPlacementWarning.StairHeadroom=>"Low stair clearance; placement allowed",
        BuildPlacementWarning.UnresolvedJoint=>"Joint fitting unavailable; full solid shape retained",
        BuildPlacementWarning.RoofStairOverlap=>"Roof/stair overlap allowed; colliders may block movement",
        BuildPlacementWarning.RoofStairHeadroom=>"Low stair headroom under roof; placement allowed",
        _=>null
    };
}
