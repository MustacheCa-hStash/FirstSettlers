#ifndef MAPLE_LEAF_SEASON_PALETTE_INCLUDED
#define MAPLE_LEAF_SEASON_PALETTE_INCLUDED

// Shared by the stylized 3D leaves and octa impostors. Coordinates may come
// from mesh noise or the baked atlas, but palettes and tree tint semantics agree.
half3 MapleSeasonLeafColor(half coordinate, half variation, half season,
    half3 summer, half3 yellow, half3 orange, half3 red,
    half4 treeTint, half tintStrength, half sugarMaple)
{
    half hue = saturate(.5h + (coordinate - .5h) * variation * 2);
    half3 sugarAutumn = lerp(yellow, orange, saturate(hue * 2));
    sugarAutumn = lerp(sugarAutumn, red, saturate(hue * 2 - 1));
    half3 redAutumn = lerp(red, orange, saturate((hue - .38h) * 2) * .75h);
    redAutumn = lerp(redAutumn, yellow, smoothstep(.8h, .98h, hue) * .45h);
    half3 leaf = lerp(summer, lerp(redAutumn, sugarAutumn, sugarMaple), season);
    // Red maple generation uses white as a neutral sentinel and alpha-zero
    // colors for summer-only variants. Sugar maple supplies autumn tree colors.
    half3 deviation = abs(treeTint.rgb - half3(1,1,1));
    half redTintWeight = smoothstep(.015h, .06h, max(deviation.r, max(deviation.g, deviation.b))) * saturate(treeTint.a);
    half tintWeight = lerp(redTintWeight, 1.0h, sugarMaple);
    return lerp(leaf, treeTint.rgb, season * tintStrength * tintWeight);
}
#endif
