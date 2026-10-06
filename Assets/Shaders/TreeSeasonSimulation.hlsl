#ifndef TREE_SEASON_SIMULATION_INCLUDED
#define TREE_SEASON_SIMULATION_INCLUDED

// Preview globals deliberately live outside UnityPerMaterial so cached/instanced
// materials react immediately without rewriting assets or rebuilding the world.
float _TreeSeasonSimulationEnabled;
float _TreeSeasonSimulationAutumnAmount;

half TreeSeasonAutumnAmount(half authoredAmount)
{
    return saturate(_TreeSeasonSimulationEnabled > .5 ? _TreeSeasonSimulationAutumnAmount : authoredAmount);
}

half TreeSeasonDirectTintAmount(half authoredAmount)
{
    // Some grassland variants carry a direct summer tint in leafTint.a. Let the
    // seasonal palette show through while previewing; preserve normal behavior off.
    return _TreeSeasonSimulationEnabled > .5 ? 0.0h : authoredAmount;
}

half TreeSeasonAutumnTintStrength(half authoredStrength)
{
    return authoredStrength * (_TreeSeasonSimulationEnabled > .5 ? saturate(_TreeSeasonSimulationAutumnAmount) : 1.0h);
}
#endif
