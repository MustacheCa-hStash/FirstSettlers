#ifndef FIRST_SETTLERS_WORLD_LANDFORM_FIELDS
#define FIRST_SETTLERS_WORLD_LANDFORM_FIELDS
// Visual counterpart of WorldLandforms.Noise/Desert. Defaults to no tint outside a live world.
int _WorldLandformSeed;
float4 _WorldLandformParams; // enabled, sample scale in metres, desert region scale, world scale
float4 _WorldGlacialParams; // strength/enabled, water world Y, shore band world height, reserved

float FSLandformSigned(int2 cell, int seed)
{
    uint h = (uint)seed ^ (uint)cell.x * 374761393u ^ (uint)cell.y * 668265263u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h ^= h >> 16;
    return (h & 0x00ffffffu) / 8388607.5 - 1.0;
}
float FSLandformNoise(float2 position, float scale, int seed)
{
    float2 p = position / max(scale, _WorldLandformParams.w);
    int2 cell = (int2)floor(p);
    float2 f = p - cell;
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float a = FSLandformSigned(cell, seed), b = FSLandformSigned(cell + int2(1,0), seed);
    float c = FSLandformSigned(cell + int2(0,1), seed), d = FSLandformSigned(cell + 1, seed);
    return a + (b-a)*u.x + (c-a)*u.y + (a-b-c+d)*u.x*u.y;
}
float2 FSRegionalGeology(float2 positionWS)
{
    float scale = _WorldLandformParams.y;
    float desertScale = scale * _WorldLandformParams.z;
    float dry = smoothstep(0.04, 0.42,
        FSLandformNoise(positionWS, desertScale, _WorldLandformSeed + 81001) +
        FSLandformNoise(positionWS, desertScale * 0.37, _WorldLandformSeed + 81002) * 0.12);
    float sediment = smoothstep(-0.35, 0.30,
        FSLandformNoise(positionWS, scale * 5.0, _WorldLandformSeed + 82001));
    return float2(dry, sediment);
}
float FSColdProvince(float2 positionWS)
{
    if(_WorldGlacialParams.x<=0.0 || _WorldLandformParams.x<0.5)return 0;
    float scale=_WorldLandformParams.y;
    float dry=FSRegionalGeology(positionWS).x;
    float wet=1.0-smoothstep(.015,.15,dry);
    [branch] if(wet<=0.0)return 0;
    float n=FSLandformNoise(positionWS,scale*16.0,_WorldLandformSeed+91001)+
        FSLandformNoise(positionWS,scale*5.5,_WorldLandformSeed+91002)*.12;
    return smoothstep(.28,.72,n)*wet*_WorldGlacialParams.x;
}
float3 FSRegionalRockTint(float3 positionWS)
{
    [branch] if (_WorldLandformParams.x < 0.5) return 1.0;
    float2 geology = FSRegionalGeology(positionWS.xz);
    float dry = geology.x, sediment = geology.y;
    // Reuse the current textures: pale limestone, warm ochre sandstone, cool granite, brown dry bedrock.
    float3 hard = lerp(float3(0.91,0.94,0.98), float3(1.17,0.94,0.72), dry);
    float3 soft = lerp(float3(1.16,1.15,1.05), float3(1.48,1.04,0.64), dry);
    return lerp(hard, soft, sediment);
}
float3 FSRegionalSandTint(float3 positionWS)
{
    [branch] if (_WorldLandformParams.x < 0.5) return 1.0;
    float scale = _WorldLandformParams.y;
    float2 geology = FSRegionalGeology(positionWS.xz);
    float province = smoothstep(-0.12,0.32,FSLandformNoise(positionWS.xz,scale*7.0,_WorldLandformSeed+82002));
    float plateau = geology.y * province * (0.24 + geology.x * 0.76);
    float basin = geology.x * (1.0-plateau) *
        smoothstep(0.42,0.72,FSLandformNoise(positionWS.xz,scale*2.2,_WorldLandformSeed+82004));
    return lerp(lerp(float3(1,1,1),float3(1.10,1.01,0.86),geology.x),float3(1.22,1.23,1.14),basin);
}
#endif
