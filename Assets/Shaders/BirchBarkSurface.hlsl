#ifndef BIRCH_BARK_SURFACE_INCLUDED
#define BIRCH_BARK_SURFACE_INCLUDED
// Shared by the actual bark and unlit semantic bake: cream paper with dark scars.
half3 BirchBarkAlbedo(half3 textureColor, float3 positionOS, half3 paper, half3 warm, half3 scar,
    half threshold, half softness, half scarStrength, half variationStrength,
    half gradientStrength, half heightMin, half heightMax, half brightness)
{
    half luma = dot(textureColor, half3(.299h,.587h,.114h));
    half scarMask = 1-smoothstep(threshold,threshold+max(softness,.001h),luma);
    half variation = sin(dot(positionOS,float3(.61,.27,.48)))*.5h+.5h;
    half3 bark = lerp(paper,warm,variation*variationStrength);
    half height = saturate((positionOS.y-heightMin)/max(heightMax-heightMin,.0001h));
    bark *= lerp(1-gradientStrength,1+gradientStrength,height)*brightness;
    return lerp(bark,scar,saturate(scarMask*scarStrength));
}
#endif
