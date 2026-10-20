#ifndef OAK_LEAF_SEASON_PALETTE_INCLUDED
#define OAK_LEAF_SEASON_PALETTE_INCLUDED

half3 OakSeasonLeafColor(half coordinate, half variation, half season,
    half3 summer, half3 ochre, half3 russet, half3 wine, half4 treeTint, half tintStrength)
{
    half hue = saturate(.5h + (coordinate - .5h) * variation * 2);
    half3 autumn = lerp(ochre, russet, saturate(hue * 2));
    autumn = lerp(autumn, wine, saturate(hue * 2 - 1));
    half3 deviation = abs(treeTint.rgb - half3(1,1,1));
    half tintWeight = smoothstep(.015h, .06h, max(deviation.r, max(deviation.g, deviation.b))) * saturate(treeTint.a);
    autumn = lerp(autumn, treeTint.rgb, tintStrength * tintWeight);
    half summerVariation = lerp(.94h, 1.06h, saturate(dot(treeTint.rgb, half3(.299h,.587h,.114h))));
    return lerp(summer * lerp(1.0h, summerVariation, tintWeight), autumn, season);
}
#endif
