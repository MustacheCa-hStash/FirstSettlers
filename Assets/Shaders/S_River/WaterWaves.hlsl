#ifndef FIRST_SETTLERS_WATER_WAVES_INCLUDED
#define FIRST_SETTLERS_WATER_WAVES_INCLUDED

// One material layout and one displacement function for the color and fog passes.
CBUFFER_START(UnityPerMaterial)
    half4 _ShallowWater;
    half4 _DeepWater;
    half4 _FoamColor;
    half _VisibilityDepth;
    half _ShallowOpacity;
    half _DeepOpacity;
    half _ReflectionStrength;
    half _ReflectionBlur;
    half _ReflectionSharpDistance;
    half _ReflectionSoftDistance;
    half _ReflectionTint;
    half _ReflectionDistortion;
    half _RefractionStrength;
    half _WaveStrength;
    float4 _FlowDirection;
    float _FlowSpeed;
    float _RippleScale;
    half _DetailRippleStrength;
    half _FoamAmount;
    float _WaveAmplitude;
    float _WaveSteepness;
    float _WaveLengthScale;
    float _WaveSpeed;
    float _WaveFadeStart;
    float _WaveFadeEnd;
    half _SunGlintStrength;
    half _SunGlintSharpness;
CBUFFER_END

// The streaming owner conservatively limits waves to the normal-mesh domain.
// Unconfigured standalone surfaces use the material distance instead.
float _WaterWaveDistanceLimit;

float2 WaterFlowDirection()
{
    float lengthSq = dot(_FlowDirection.xy, _FlowDirection.xy);
    return lengthSq > 0.0001 ? _FlowDirection.xy * rsqrt(max(lengthSq, 0.0001)) : float2(1, 0);
}

void AddGerstner(float2 p, float2 direction, float wavelength, float weight, float time,
    inout float3 offset, inout float3 dx, inout float3 dz)
{
    float k = TWO_PI / (wavelength * max(0.5, _WaveLengthScale));
    float amplitude = clamp(_WaveAmplitude, 0, 0.3) * weight;
    float horizontal = amplitude * saturate(_WaveSteepness);
    float phase = k * dot(direction, p) - (sqrt(9.81 * k) + k * _FlowSpeed) * time;
    float s, c; sincos(phase, s, c);
    offset += float3(direction.x * horizontal * c, amplitude * s, direction.y * horizontal * c);
    float3 derivative = float3(-direction.x * horizontal * s, amplitude * c, -direction.y * horizontal * s) * k;
    dx += derivative * direction.x;
    dz += derivative * direction.y;
}

void EvaluateWaterWaves(float3 restWS, float4 depthData, out float3 positionWS, out float3 normalWS)
{
    positionWS = restWS; normalWS = float3(0, 1, 0);
    if (depthData.w < 0.5 || _WaveAmplitude <= 0) return;

    float end = max(0.01, min(_WaveFadeEnd,
        _WaterWaveDistanceLimit > 0 ? _WaterWaveDistanceLimit : _WaveFadeEnd));
    float start = min(_WaveFadeStart, end * 0.65);
    float2 cameraDelta = restWS.xz - _WorldSpaceCameraPos.xz;
    float distance = length(cameraDelta);
    float u = saturate((distance - start) / max(0.01, end - start));
    float fade = 1 - u * u * (3 - 2 * u);
    float2 fadeGradient = -6 * u * (1 - u) / max(0.01, end - start)
        * cameraDelta / max(distance, 0.001);

    // Depth and its world-space gradient are baked from padded terrain heights.
    // Dry coverage under the bank stays still; full waves begin at 0.9 m depth.
    float shoreU = saturate((depthData.x - 0.08) / 0.82);
    float shore = shoreU * shoreU * (3 - 2 * shoreU);
    float2 shoreGradient = 6 * shoreU * (1 - shoreU) / 0.82 * depthData.yz;
    float envelope = shore * fade;
    float2 envelopeGradient = shoreGradient * fade + shore * fadeGradient;
    if (envelope <= 0) return;

    float2 flow = WaterFlowDirection(), across = float2(-flow.y, flow.x);
    float3 offset = 0, dx = 0, dz = 0;
    float time = _Time.y * _WaveSpeed;
    AddGerstner(restWS.xz, flow, 7.5, 0.40, time, offset, dx, dz);
    AddGerstner(restWS.xz, flow * 0.8 + across * 0.6, 5.2, 0.30, time, offset, dx, dz);
    AddGerstner(restWS.xz, flow * 0.6 - across * 0.8, 4.1, 0.18, time, offset, dx, dz);
    AddGerstner(restWS.xz, -flow * 0.6 + across * 0.8, 10.8, 0.12, time, offset, dx, dz);
    positionWS += offset * envelope;
    // Include the derivatives of both envelopes: normals still match the shape
    // at shore and distance fades, rather than lighting a nonexistent full wave.
    float3 tangentX = float3(1, 0, 0) + dx * envelope + offset * envelopeGradient.x;
    float3 tangentZ = float3(0, 0, 1) + dz * envelope + offset * envelopeGradient.y;
    normalWS = normalize(cross(tangentZ, tangentX));
}

float WaterDeviceDepth(float raw)
{
    #if UNITY_REVERSED_Z
        return raw;
    #else
        return lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
    #endif
}
#endif
