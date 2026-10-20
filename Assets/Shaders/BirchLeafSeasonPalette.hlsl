#ifndef BIRCH_LEAF_SEASON_PALETTE_INCLUDED
#define BIRCH_LEAF_SEASON_PALETTE_INCLUDED
#include "Assets/Shaders/OakLeafSeasonPalette.hlsl"
// The completed deciduous interpolation also suits birch's three gold shades.
half3 BirchSeasonLeafColor(half coordinate, half variation, half season,
    half3 summer, half3 gold, half3 ochre, half3 paleGold, half4 treeTint, half tintStrength)
{
    return OakSeasonLeafColor(coordinate, variation, season, summer, gold, ochre, paleGold, treeTint, tintStrength);
}
#endif
