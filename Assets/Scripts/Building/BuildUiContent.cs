using UnityEngine;

/// <summary>Presentation metadata; the catalog continues to own gameplay definitions.</summary>
public static class BuildUiContent
{
    public static readonly string[] Categories = { "All pieces", "Foundations", "Walls", "Floors", "Posts", "Stairs", "Roofs", "Furniture", "Lighting" };
    public static string ThumbnailKey(BuildDefinition d) => d.contentId.Replace('.', '_');
    public static int Category(BuildDefinition d) => d.flamePrefab!=null ? 8 : d.IsHalfGable ? 6 : d.kind switch
    { BuildPartKind.Foundation=>1, BuildPartKind.Wall=>2, BuildPartKind.Floor=>3, BuildPartKind.Corner=>4, BuildPartKind.Stair or BuildPartKind.Handrail=>5, BuildPartKind.Roof=>6, BuildPartKind.Furniture=>7, _=>0 };
    public static string Name(BuildDefinition d) => d.contentId switch
    {
        "build.prototype.wall"=>"Split-plank wall", "build.wood.w21-half-storey-stair"=>"W21 stair",
        "build.wood.w22-full-storey-stair"=>"W22 stair", "build.wood.w24-stair-handrail"=>"W24 handrail",
        "build.wood.w09-doorway"=>"Doorway wall", "build.wood.w11-shutter-wall"=>"Window wall",
        "build.wood.w15-gable-rising" or "build.wood.w15-gable-falling"=>"Half-gable", _=>d.displayName
    };
    public static int Order(BuildDefinition d) => d.contentId switch
    {
        "build.prototype.foundation"=>0, "build.prototype.floor"=>1, "build.prototype.wall"=>2,
        "build.prototype.corner"=>3, "build.wood.wattle-wall"=>4, "build.wood.w21-half-storey-stair"=>5,
        "build.wood.thatch-roof-panel"=>6, "build.wood.w09-doorway"=>7, "build.wood.w11-shutter-wall"=>8,
        "build.wood.w15-gable-rising"=>9, "build.wood.w22-full-storey-stair"=>10, "build.wood.w24-stair-handrail"=>11,
        "build.wood.square-infill"=>12,
        "build.stone.s02-coursed-wall"=>13, "build.stone.s03-pier"=>14, "build.stone.s04-flagstone-floor"=>15, "build.stone.s06-wall-cap"=>16,
        "build.stone.s07-low-wall"=>17, "build.stone.s12-gate-bay"=>18,
        "build.furniture.f01-three-leg-stool"=>19, "build.furniture.f02-plank-bench"=>20,
        "build.furniture.f03-pegged-table"=>21, "build.furniture.f04-low-work-table"=>22,
        "build.furniture.f05-sleeping-platform"=>23, "build.furniture.f06-wall-shelf"=>24, _=>25
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
        "build.wood.w22-full-storey-stair"=>"A full-storey flight: 3 m rise over a 4 m run. Aim beneath a piece to descend.",
        "build.wood.w24-stair-handrail"=>"A 2 m rail segment. Aim at either stair edge; use two segments per side of W22.",
        "build.wood.thatch-roof-panel"=>"One pitched panel. Aim at an edge to continue the slope or span.",
        "build.wood.w09-doorway"=>"Wall with a 1.25 × 2.25 m clear doorway.",
        "build.wood.w11-shutter-wall"=>"Wall with a 1 × 0.75 m window opening.",
        "build.wood.w15-gable-rising" or "build.wood.w15-gable-falling"=>"A triangular half-gable. Flip its slope to fit either side of the roof.",
        "build.wood.square-infill"=>"A 2 × 2 m wall panel. Fills rectangular gaps between gables, stacks, or continues sideways.",
        "build.stone.s02-coursed-wall"=>"Coursed stone wall. Full 4 m span and 3 m storey.",
        "build.stone.s03-pier"=>"A freestanding 3 m stone pier for timber bearing and exposed supports.",
        "build.stone.s04-flagstone-floor"=>"Stone paving on solid bedding. Requires foundation coverage beneath the whole tile.",
        "build.stone.s06-wall-cap"=>"A row of rough capstones. Adds 0.25 m above a wall top.",
        "build.stone.s07-low-wall"=>"Low rubble boundary wall. Seats directly on terrain or a supported building.",
        "build.stone.s12-gate-bay"=>"Low stone wings around an empty 1.25 m gate opening. Both wings can seat directly on terrain.",
        "build.furniture.f01-three-leg-stool"=>"A hewn three-leg stool. Place all feet on ground or supported flooring.",
        "build.furniture.f02-plank-bench"=>"Backless plank seating on pegged slab supports.",
        "build.furniture.f03-pegged-table"=>"A split-board table with pegged end frames and a low stretcher.",
        "build.furniture.f04-low-work-table"=>"A low timber work surface on short legs.",
        "build.furniture.f05-sleeping-platform"=>"A low timber bed frame with an open woven cord deck.",
        "build.furniture.f06-wall-shelf"=>"A pegged shelf with two braces. Aim at a supported wall face.",
        "build.lighting.wall-torch"=>"A fibre-bound torch in a pegged wooden holder. Mount on a supported wall to cast warm local light.",
        _=>"Choose this component, then aim at a surface to place it."
    };
}
