#ifndef FS_FOREST_CANOPY_LIGHTING_INCLUDED
#define FS_FOREST_CANOPY_LIGHTING_INCLUDED

half4 _FSForestLeaf, _FSForestBark;
half4 _FSForestDarkness;
half4 _FSOpenGroundLighting, _FSGroundDaylight;
half4 _FSForestDetailLighting;
float4 _FSForestCanopyBounds;
TEXTURE2D(_FSForestCanopyMask);
SAMPLER(sampler_FSForestCanopyMask);
TEXTURE2D(_FSForestSkyField);
SAMPLER(sampler_FSForestSkyField);
TEXTURE2D(_FSDistantForestShadowField);
SAMPLER(sampler_FSDistantForestShadowField);
float4 _FSDistantForestShadowBounds, _FSDistantForestShadowProjection, _FSDistantForestShadowSettings;

half FSDistantForestShadow(float3 positionWS, half sunElevation, half realtimeShadow)
{
    half strength = _FSDistantForestShadowSettings.x * _FSGroundDaylight.x *
        smoothstep(.12h, .27h, sunElevation);
    UNITY_BRANCH if (strength <= 0.0h) return realtimeShadow;
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        half handoff = GetMainLightShadowFade(positionWS);
    #else
        float shadowEnd = _FSDistantForestShadowSettings.y;
        half handoff = smoothstep(shadowEnd * .7, shadowEnd + .01, distance(positionWS, _WorldSpaceCameraPos));
    #endif
    UNITY_BRANCH if (handoff <= .001h) return realtimeShadow;
    float3 fromCamera = positionWS - _WorldSpaceCameraPos;
    float rangeSq = _FSDistantForestShadowSettings.z * _FSDistantForestShadowSettings.z;
    half rangeFade = 1.0h - smoothstep(rangeSq * .64, rangeSq, dot(fromCamera, fromCamera));
    UNITY_BRANCH if (rangeFade <= .001h) return lerp(realtimeShadow, 1.0h, handoff * handoff);
    float2 projected = positionWS.xz - _FSDistantForestShadowProjection.xy * positionWS.y;
    float2 uv = (projected - _FSDistantForestShadowBounds.xy) * _FSDistantForestShadowBounds.z;
    float2 edge = min(uv, 1.0 - uv);
    half border = smoothstep(.015, .10, min(edge.x, edge.y));
    float2 field = SAMPLE_TEXTURE2D(_FSDistantForestShadowField, sampler_FSDistantForestShadowField, saturate(uv)).rg;
    float casterTop = field.y / max(.001, field.x);
    half belowCrown = smoothstep(0.0, 5.0, casterTop - positionWS.y);
    half projectedShadow = 1.0h - saturate(strength * field.x) * border * belowCrown * rangeFade;
    return lerp(realtimeShadow, projectedShadow, handoff * handoff);
}

half3 FSForestDirectionalAmbient(float3 positionWS, half3 normalWS, half3 ambient)
{
    half strength = _FSForestDetailLighting.x * _FSGroundDaylight.y;
    UNITY_BRANCH if (strength <= 0.0h) return ambient;
    float2 uv = (positionWS.xz - _FSForestCanopyBounds.xy) * _FSForestCanopyBounds.z;
    float2 edge = min(uv, 1.0 - uv);
    strength *= smoothstep(0.0, .08, min(edge.x, edge.y));
    half4 field = SAMPLE_TEXTURE2D(_FSForestSkyField, sampler_FSForestSkyField, saturate(uv));
    half2 opening = (field.gb * 255.0h - 128.0h) / 127.0h;
    half cover = max(field.r * .55h, field.a);
    half sideFacing = clamp(dot(normalWS.xz, opening) * 3.1h, -1.0h, 1.0h);
    half sheltered = saturate(cover * 1.6h);
    half up = saturate(normalWS.y);
    half shade = (.34h * sheltered + .23h * saturate(-sideFacing)) * (1.0h - .65h * up);
    half3 sky = SampleSH(normalize(half3(opening.x * 2.0h, .55h, opening.y * 2.0h)));
    half3 fill = sky * saturate(sideFacing);
    // A small warm, diffuse floor contribution on vertical faces.
    half3 bounce = ambient * half3(.10h, .076h, .038h) * sheltered * (1.0h - up);
    return ambient + strength * (fill + bounce - ambient * shade);
}

half FSVisibleDirectSun(half shadow)
{
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    half strength = GetMainLightShadowParams().x;
    return saturate(1.0h - (1.0h - shadow) / max(strength, .001h));
    #else
    return 1.0h;
    #endif
}

half FSForestLeafThinness(half3 leafColor)
{
    // Existing atlases have no thickness channel. Dark veins and stems
    // generally transmit less than the lighter blade in these authored maps.
    return lerp(.48h, 1.0h, smoothstep(.04h, .27h, dot(leafColor, half3(.21h, .72h, .07h))));
}

half FSForestLeafTransmission(half3 viewWS, half3 crownNormalWS, Light sun, half thinness)
{
    // The crown normal is an envelope, so a sun-visible leaf can still transmit
    // through a hole on the nominally sheltered side of that envelope.
    half facingSun = lerp(.22h, 1.0h, smoothstep(-.55h, .65h, dot(crownNormalWS, sun.direction)));
    // Both directions point away from the leaf. Transmission peaks when the
    // viewer and sun are on opposite sides.
    half behindLeaf = smoothstep(.10h, .76h, dot(-viewWS, sun.direction));
    return facingSun * behindLeaf * thinness * FSVisibleDirectSun(sun.shadowAttenuation) *
        _FSForestDetailLighting.y * _FSGroundDaylight.x;
}

half3 FSForestPalette(half3 authored, half4 palette, half reference)
{
    half tone = dot(authored, half3(.2126h, .7152h, .0722h));
    half detail = clamp(sqrt(max(tone, .001h) / reference), .42h, 1.8h);
    return lerp(authored, palette.rgb * detail, saturate(palette.a));
}

half3 FSForestLeafColor(half3 authored)
{
    return FSForestPalette(authored, _FSForestLeaf, .16h);
}

half3 FSForestBarkColor(half3 authored)
{
    return FSForestPalette(authored, _FSForestBark, .13h);
}

half FSForestShadeResponse(half coverage, half strength)
{
    half remaining = 1.0h - saturate(coverage);
    half remainingSquared = remaining * remaining;
    return lerp(1.0h, remaining, saturate(strength)) *
        lerp(1.0h, remaining, saturate(strength - 1.0h)) *
        lerp(1.0h, remainingSquared, saturate((strength - 2.0h) * 0.5h)) *
        lerp(1.0h, remainingSquared * remainingSquared, saturate((strength - 4.0h) * 0.25h));
}

half FSForestCrownFill(half3 normalWS, half interior)
{
    half shade = max(saturate(.55h - .45h * normalWS.y), saturate(interior));
    return FSForestShadeResponse(shade, _FSForestDarkness.x);
}

half FSForestGroundLight(float3 positionWS)
{
    UNITY_BRANCH
    if (_FSForestDarkness.y <= 0.0h) return 1.0h;
    float2 uv = (positionWS.xz - _FSForestCanopyBounds.xy) * _FSForestCanopyBounds.z;
    float2 edge = min(uv, 1.0 - uv);
    half fade = smoothstep(0.0, .08, min(edge.x, edge.y));
    half canopy = SAMPLE_TEXTURE2D(_FSForestCanopyMask, sampler_FSForestCanopyMask, saturate(uv)).r;
    return FSForestShadeResponse(canopy * fade, _FSForestDarkness.y);
}

half FSForestGroundSun(half groundShade)
{
    half localSun = 1.0h + _FSForestDarkness.w * saturate((1.0h - groundShade) * 4.0h);
    return lerp(1.0h, groundShade, saturate(_FSForestDarkness.z)) * localSun;
}

half FSSceneGroundAmbient(half groundShade)
{
    half skyAccess = smoothstep(.55h, .98h, groundShade);
    return groundShade * (1.0h + _FSOpenGroundLighting.y * _FSGroundDaylight.y * skyAccess);
}

half FSSceneGroundSun(half groundShade, half shadow, half woodland)
{
    // URP keeps a direct-light floor when shadow strength is below one.
    // Apply the added light only to the visible sun, without changing that floor.
    half sunVisibility = FSVisibleDirectSun(shadow);
    // A lone meadow tree also stamps the canopy mask. Use habitat separately
    // so that its shade can reduce sky fill without adding forest sun patches.
    half forest = _FSForestDarkness.w * woodland * saturate((1.0h - groundShade) * 4.0h);
    half open = _FSOpenGroundLighting.x * _FSGroundDaylight.x * smoothstep(.55h, .98h, groundShade);
    return lerp(1.0h, groundShade, saturate(_FSForestDarkness.z)) * (1.0h + (forest + open) * sunVisibility);
}

#endif
