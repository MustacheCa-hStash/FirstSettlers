Shader "FirstSettlers/Murky Planar Water"
{
    Properties
    {
        _ShallowWater("Shallow blue", Color) = (0.12, 0.34, 0.47, 1)
        _DeepWater("Deep blue", Color) = (0.025, 0.12, 0.23, 1)
        _VisibilityDepth("Tint depth", Range(0.25, 24)) = 8
        _ShallowOpacity("Shallow tint", Range(0, 1)) = 0.05
        _DeepOpacity("Deep tint", Range(0, 1)) = 0.38
        _ReflectionStrength("Planar reflection strength", Range(0, 1)) = 0.5
        _ReflectionBlur("Distant reflection mip", Range(0, 5)) = 2
        _ReflectionSharpDistance("Sharp reflection distance", Float) = 25
        _ReflectionSoftDistance("Soft reflection distance", Float) = 180
        _ReflectionTint("Reflection blue tint", Range(0, 1)) = 0.12
        _ReflectionDistortion("Reflection ripple distortion", Range(0, 0.06)) = 0.04
        _RefractionStrength("Underwater distortion", Range(0, 0.02)) = 0.014
        _WaveStrength("Surface normal movement", Range(0, 0.5)) = 0.34
        _FlowDirection("Current direction (world XZ)", Vector) = (0.8, 0.6, 0, 0)
        _FlowSpeed("Current speed (world units / second)", Range(0, 2)) = 0.9
        _RippleScale("Ripple tiling (per world unit)", Range(0.02, 0.5)) = 0.08
        _DetailRippleStrength("Small ripple strength", Range(0, 1)) = 0.65
        _FoamAmount("Intersection ripple amount", Range(0, 1)) = 0.12
        _FoamColor("Intersection ripple color", Color) = (0.55, 0.75, 0.8, 1)
        _WaveAmplitude("Maximum combined wave height (metres)", Range(0, 0.3)) = 0.16
        _WaveSteepness("Gerstner crest steepness", Range(0, 1)) = 0.6
        _WaveLengthScale("Geometric wavelength scale", Range(0.5, 2)) = 1
        _WaveSpeed("Geometric wave speed", Range(0, 2)) = 1
        _WaveFadeStart("Full geometric waves distance", Float) = 60
        _WaveFadeEnd("Flat water distance", Float) = 120
        _SunGlintStrength("Sun highlight strength", Range(0, 2)) = 0.65
        _SunGlintSharpness("Sun highlight sharpness", Range(32, 512)) = 160
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Cull Back
        ZWrite Off
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "WaterWaves.hlsl"
        ENDHLSL

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_WaterReflectionTex);
            SAMPLER(sampler_WaterReflectionTex);
            float _WaterReflectionValid;
            TEXTURE2D(_WaterNormalTex);
            SAMPLER(sampler_WaterNormalTex);
            float _WaterNormalValid;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 depthData : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
                float3 restWS : TEXCOORD2;
                float3 waveNormalWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.restWS = TransformObjectToWorld(input.positionOS.xyz);
                EvaluateWaterWaves(output.restWS, input.depthData, output.positionWS, output.waveNormalWS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.eyeDepth = -TransformWorldToView(output.positionWS).z;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(screenUV);
                float3 bedPosition = ComputeWorldSpacePosition(screenUV, WaterDeviceDepth(rawDepth), UNITY_MATRIX_I_VP);
                float waterDepth = max(0, input.positionWS.y - bedPosition.y);

                // World-space advection keeps the current continuous across chunks and
                // LODs. Subtract travel so positive speed moves along FlowDirection.
                float2 p = input.restWS.xz;
                float travel = _Time.y * _FlowSpeed;
                float2 flow = WaterFlowDirection();
                float2 across = float2(-flow.y, flow.x);
                float2 flowPosition = float2(dot(p, flow), dot(p, across));
                float2 slope = 0.0;
                if (_WaterNormalValid > 0.5)
                {
                    float2 advected = flowPosition - float2(travel, 0.0);
                    // Broad undulation, flow-aligned ripples, and a weaker angled
                    // detail layer share a downstream current instead of sliding apart.
                    float2 broad = SAMPLE_TEXTURE2D(_WaterNormalTex, sampler_WaterNormalTex,
                        advected * (_RippleScale * 0.42) + float2(0.31, 0.67)).rg * 2.0 - 1.0;
                    float2 stretch = float2(1.25, 0.75);
                    float2 a = SAMPLE_TEXTURE2D(_WaterNormalTex, sampler_WaterNormalTex,
                        advected * (_RippleScale * stretch)).rg * 2.0 - 1.0;
                    // Rotate both the sampling coordinates and sampled slopes.
                    const float c = 0.94;
                    const float s = 0.342;
                    float2 detailPosition = float2(c * advected.x + s * advected.y,
                        -s * advected.x + c * advected.y);
                    float2 b = SAMPLE_TEXTURE2D(_WaterNormalTex, sampler_WaterNormalTex,
                        detailPosition * (_RippleScale * 2.75) + float2(0.53, 0.19)).rg * 2.0 - 1.0;
                    b = float2(c * b.x - s * b.y, s * b.x + c * b.y);
                    float distanceFade = 1.0 - smoothstep(70.0, 200.0, input.eyeDepth);
                    float detailFade = 1.0 - smoothstep(35.0, 120.0, input.eyeDepth);
                    float2 localSlope = (broad * 0.35 + a * stretch
                        + b * (_DetailRippleStrength * detailFade)) * distanceFade;
                    slope = flow * localSlope.x + across * localSlope.y;
                }
                float3 waveNormal = normalize(input.waveNormalWS);
                float3 normalWS = normalize(waveNormal + float3(-slope.x * _WaveStrength, 0,
                    -slope.y * _WaveStrength));
                // Texture offsets live in screen space: project the horizontal normal
                // into the camera axes so turning the view doesn't turn the current.
                float2 distortion = TransformWorldToViewDir(float3(normalWS.x, 0.0, normalWS.z), false).xy;
                distortion.x *= _ScaledScreenParams.y / _ScaledScreenParams.x;

                float depthBlend = 1.0 - exp2(-waterDepth / max(0.01, _VisibilityDepth));
                half3 waterTint = lerp(_ShallowWater.rgb, _DeepWater.rgb, depthBlend);
                float opacity = lerp(_ShallowOpacity, _DeepOpacity, depthBlend);

                // Keep the bed visible, with small moving refraction away from shore.
                float refractionFade = smoothstep(0.05, 0.8, waterDepth);
                float2 refractedUV = saturate(screenUV + distortion *
                    (_RefractionStrength * refractionFade));
                // A displaced lookup must still be behind the water. Otherwise it
                // pulls an above-water bank or foreground object into the riverbed.
                float refractedDepth = SampleSceneDepth(refractedUV);
                #if !UNITY_REVERSED_Z
                    refractedDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, refractedDepth);
                #endif
                float3 refractedPosition = ComputeWorldSpacePosition(refractedUV, refractedDepth, UNITY_MATRIX_I_VP);
                if (refractedPosition.y >= input.positionWS.y - 0.01)
                    refractedUV = screenUV;
                half3 bedColor = SampleSceneColor(refractedUV);
                half3 color = lerp(bedColor, waterTint, opacity);

                // Sparse normal-driven shoreline highlights avoid repeating depth rings.
                float contact = 1.0 - smoothstep(0.02, 0.45, waterDepth);
                float ripple = contact * smoothstep(0.45, 0.8,
                    abs(slope.x * 0.8 + slope.y * 0.6)) * _FoamAmount;
                color = lerp(color, _FoamColor.rgb, ripple);

                // The reflection camera is optional; refraction still works without it.
                if (_WaterReflectionValid > 0.5)
                {
                    // Mirrored-camera projection equals the viewer's projection of a
                    // point mirrored around mean water Y. Apply its UV delta to the
                    // existing flat-plane mapping so platform texture orientation is
                    // preserved exactly when amplitude is zero.
                    float3 mirrored = input.positionWS;
                    mirrored.y = 2 * input.restWS.y - mirrored.y;
                    float4 reflectedCS = TransformWorldToHClip(mirrored);
                    float4 surfaceCS = TransformWorldToHClip(input.positionWS);
                    float2 reflectionDelta = (reflectedCS.xy / max(reflectedCS.w, 0.001)
                        - surfaceCS.xy / max(surfaceCS.w, 0.001)) * 0.5;
                    #if UNITY_UV_STARTS_AT_TOP
                        reflectionDelta.y = -reflectionDelta.y;
                    #endif
                    float2 reflectedUV = screenUV + reflectionDelta;
                    reflectedUV += distortion * (_ReflectionDistortion * refractionFade);
                    float edge = min(min(reflectedUV.x, reflectedUV.y),
                        min(1.0 - reflectedUV.x, 1.0 - reflectedUV.y));
                    if (edge > 0.0)
                    {
                        float reflectionMip = _ReflectionBlur * smoothstep(
                            _ReflectionSharpDistance, max(_ReflectionSharpDistance + 0.01,
                            _ReflectionSoftDistance), input.eyeDepth);
                        half3 reflected = SAMPLE_TEXTURE2D_LOD(_WaterReflectionTex,
                            sampler_WaterReflectionTex, reflectedUV, reflectionMip).rgb;
                        reflected = lerp(reflected, waterTint, _ReflectionTint);

                        float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                        float facing = saturate(dot(normalWS, viewDirection));
                        float fresnel = 0.045 + 0.955 * pow(1.0 - facing, 5.0);
                        float reflectionBlend = _ReflectionStrength * fresnel * smoothstep(0.0, 0.08, edge);
                        color = lerp(color, reflected, reflectionBlend);
                    }
                }

                Light sun = GetMainLight();
                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 halfVector = SafeNormalize(sun.direction + viewWS);
                float glint = pow(saturate(dot(normalWS, halfVector)), _SunGlintSharpness);
                color += sun.color * (glint * _SunGlintStrength * 0.35
                    * saturate(dot(normalWS, sun.direction)) * sun.distanceAttenuation);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
        // Positive linear eye depth, cleared to zero outside the actual displaced water.
        Pass
        {
            Name "DistantFogWaterMask"
            Tags { "LightMode" = "DistantFogWaterMask" }
            Blend One Zero
            ZWrite Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct MaskAttributes { float4 positionOS : POSITION; float4 depthData : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct MaskVaryings { float4 positionCS : SV_POSITION; float eyeDepth : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            MaskVaryings MaskVert(MaskAttributes input)
            {
                MaskVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS, normalWS;
                EvaluateWaterWaves(TransformObjectToWorld(input.positionOS.xyz), input.depthData, positionWS, normalWS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.eyeDepth = -TransformWorldToView(positionWS).z;
                return output;
            }
            float MaskFrag(MaskVaryings input) : SV_Target { return input.eyeDepth; }
            ENDHLSL
        }
    }
}
