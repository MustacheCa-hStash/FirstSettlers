#ifndef TREE_NIGHT_LIGHTING_INCLUDED
#define TREE_NIGHT_LIGHTING_INCLUDED

// SunCycleController drives these globals for both 3D trees and impostors.
half _TreeNightAmbientFloorDimAmount;
half _TreeNightAmbientFloorScaleAtMidnight;

half TreeNightAmbientFloor(half authoredFloor)
{
    return authoredFloor * lerp(1.0h, saturate(_TreeNightAmbientFloorScaleAtMidnight),
        saturate(_TreeNightAmbientFloorDimAmount));
}
#endif
