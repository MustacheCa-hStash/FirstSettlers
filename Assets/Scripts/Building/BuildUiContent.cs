using UnityEngine;

/// <summary>Presentation metadata; the catalog continues to own gameplay definitions.</summary>
public static class BuildUiContent
{
    public static readonly string[] Categories = { "All pieces", "Foundations", "Walls", "Floors", "Posts", "Stairs", "Roofs" };
    public static string ThumbnailKey(BuildDefinition d) => d.contentId.Replace('.', '_');
    public static int Category(BuildDefinition d) => d.IsHalfGable ? 6 : d.kind switch
    { BuildPartKind.Foundation=>1, BuildPartKind.Wall=>2, BuildPartKind.Floor=>3, BuildPartKind.Corner=>4, BuildPartKind.Stair=>5, BuildPartKind.Roof=>6, _=>0 };
    public static string Name(BuildDefinition d) => d.contentId switch
    {
        "build.prototype.wall"=>"Split-plank wall", "build.wood.w21-half-storey-stair"=>"W21 stair",
        "build.wood.w09-doorway"=>"Doorway wall", "build.wood.w11-shutter-wall"=>"Window wall",
        "build.wood.w15-gable-rising" or "build.wood.w15-gable-falling"=>"Half-gable", _=>d.displayName
    };
    public static int Order(BuildDefinition d) => d.contentId switch
    {
        "build.prototype.foundation"=>0, "build.prototype.floor"=>1, "build.prototype.wall"=>2,
        "build.prototype.corner"=>3, "build.wood.wattle-wall"=>4, "build.wood.w21-half-storey-stair"=>5,
        "build.wood.thatch-roof-panel"=>6, "build.wood.w09-doorway"=>7, "build.wood.w11-shutter-wall"=>8,
        "build.wood.w15-gable-rising"=>9, _=>10
    };
    public static string Dimensions(BuildDefinition d)
    { Vector3 s=d.LocalBounds.size; return $"{s.x:0.##} × {s.y:0.##} × {s.z:0.##} m"; }
    public static string Description(BuildDefinition d) => d.contentId switch
    {
        "build.prototype.foundation"=>"Start your structure with a foundation.",
        "build.prototype.floor"=>"Framed timber floor. Fits around connected stairs and walls.",
        "build.prototype.wall"=>"Full-span timber wall. Junction framing fits automatically.",
        "build.prototype.corner"=>"A timber post for corners, interior supports and stacking.",
        "build.wood.wattle-wall"=>"Woven timber panel for enclosing your structure.",
        "build.wood.w21-half-storey-stair"=>"A half-storey flight. Continue from the upper end.",
        "build.wood.thatch-roof-panel"=>"One pitched panel. Aim at an edge to continue the slope or span.",
        "build.wood.w09-doorway"=>"Wall with a 1.25 × 2.25 m clear doorway.",
        "build.wood.w11-shutter-wall"=>"Wall with a 1 × 0.75 m window opening.",
        "build.wood.w15-gable-rising" or "build.wood.w15-gable-falling"=>"A triangular half-gable. Flip its slope to fit either side of the roof.",
        _=>"Choose this component, then aim at a surface to place it."
    };
}
