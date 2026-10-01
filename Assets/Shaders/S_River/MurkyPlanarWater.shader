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
        _ReflectionDistortion("Reflection ripple distortion", Range(0, 0.06)) = 0.012
        _RefractionStrength("Underwater distortion", Range(0, 0.02)) = 0.008
        _WaveStrength("Surface normal movement", Range(0, 0.5)) = 0.18
        _FoamAmount("Intersection ripple amount", Range(0, 1)) = 0.12
        _FoamColor("Intersection ripple color", Color) = (0.55, 0.75, 0.8, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Cull Back
        ZWrite Off
        ZTest LEqual

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

            TEXTURE2D(_WaterReflectionTex);
            SAMPLER(sampler_WaterReflectionTex);
            float _WaterReflectionValid;
            TEXTURE2D(_WaterNormalTex);
            SAMPLER(sampler_WaterNormalTex);
            float _WaterNormalValid;

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
                half _FoamAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
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
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                // Convert the depth-buffer gap into vertical depth. Eye-space depth
                // alone stretches tint contours into bands at grazing view angles.
                float depthGap = max(0.0, sceneEyeDepth - input.eyeDepth);
                float waterDepth = depthGap * abs(_WorldSpaceCameraPos.y - input.positionWS.y)
                    / max(input.eyeDepth, 0.01);

                // Two drifting, tiled noise normals break up straight wave fronts.
                // Their mipmaps flatten detail naturally as the water recedes.
                float2 p = input.positionWS.xz;
                float t = _Time.y;
                float2 slope = 0.0;
                if (_WaterNormalValid > 0.5)
                {
                    float2 a = SAMPLE_TEXTURE2D(_WaterNormalTex, sampler_WaterNormalTex,
                        p * 0.08 + t * float2(0.027, 0.018)).rg * 2.0 - 1.0;
                    float2 b = SAMPLE_TEXTURE2D(_WaterNormalTex, sampler_WaterNormalTex,
                        p * 0.22 + t * float2(-0.036, 0.024)).rg * 2.0 - 1.0;
                    float distanceFade = 1.0 - smoothstep(70.0, 200.0, input.eyeDepth);
                    slope = (a + b * 0.5) * distanceFade;
                }
                float3 normalWS = normalize(float3(-slope.x * _WaveStrength, 1.0,
                    -slope.y * _WaveStrength));

                float depthBlend = 1.0 - exp2(-waterDepth / max(0.01, _VisibilityDepth));
                half3 waterTint = lerp(_ShallowWater.rgb, _DeepWater.rgb, depthBlend);
                float opacity = lerp(_ShallowOpacity, _DeepOpacity, depthBlend);

                // Keep the bed visible, with small moving refraction away from shore.
                float refractionFade = smoothstep(0.05, 0.8, waterDepth);
                float2 refractedUV = saturate(screenUV + normalWS.xz *
                    (_RefractionStrength * refractionFade));
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
                    // The mirrored camera and the viewer project points on the flat water
                    // plane to the same screen coordinates. A second GPU projection here
                    // inverted the render texture and detached nearby reflections.
                    float2 reflectedUV = screenUV;
                    reflectedUV += normalWS.xz * _ReflectionDistortion;
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

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
