#ifndef TREE_SHADOW_CASTER_COMMON_INCLUDED
#define TREE_SHADOW_CASTER_COMMON_INCLUDED

float3 _LightDirection;
float3 _LightPosition;
half _AlphaCutoutShadows;

float4 TransformWorldToTreeShadowClip(float3 positionWS, float3 normalWS)
{
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif

    positionWS = ApplyShadowBias(positionWS, normalWS, lightDirectionWS);
    float4 positionCS = TransformWorldToHClip(positionWS);

    #if UNITY_REVERSED_Z
        positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #else
        positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #endif

    return positionCS;
}

#endif
