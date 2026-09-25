Shader "FirstSettlers/Murky Planar Water"
{
    Properties
    {
        _ShallowWater("Shallow blue", Color) = (0.12, 0.34, 0.47, 1)
        _DeepWater("Deep blue", Color) = (0.025, 0.12, 0.23, 1)
        _VisibilityDepth("Murky depth", Range(0.25, 8)) = 1.4
        _ShallowOpacity("Shallow opacity", Range(0, 1)) = 0.74
        _DeepOpacity("Deep opacity", Range(0, 1)) = 0.97
        _ReflectionStrength("Planar reflection strength", Range(0, 1)) = 0.38
        _ReflectionBlur("Reflection blur", Range(0, 5)) = 3
        _ReflectionTint("Reflection blue tint", Range(0, 1)) = 0.5
        _ReflectionDistortion("Reflection ripple distortion", Range(0, 0.06)) = 0.012
        _RefractionStrength("Underwater distortion", Range(0, 0.02)) = 0.002
        _WaveStrength("Surface normal movement", Range(0, 0.5)) = 0.14
        _FoamAmount("Intersection ripple amount", Range(0, 1)) = 0.22
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
                float waterDepth = max(0.0, sceneEyeDepth - input.eyeDepth);

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

                // The opaque scene texture supplies only a short, tinted glimpse of the bed.
                float2 refractedUV = saturate(screenUV + normalWS.xz * _RefractionStrength);
                half3 bedColor = SampleSceneColor(refractedUV);
                half3 color = lerp(bedColor, waterTint, opacity);

                // The old graph's depth-fade/noise contact effect is kept as a quiet moving ring.
                float contact = 1.0 - smoothstep(0.02, 0.9, waterDepth);
                float ring = sin(waterDepth * 20.0 - t * 2.4
                    + sin(dot(p, float2(1.7, 1.2))) * 0.45);
                float ripple = contact * smoothstep(0.38, 0.86, ring) * _FoamAmount;
                color = lerp(color, _FoamColor.rgb, ripple);

                // The reflection camera is optional; blue murky water remains valid without it.
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
                        half3 reflected = SAMPLE_TEXTURE2D_LOD(_WaterReflectionTex,
                            sampler_WaterReflectionTex, reflectedUV, _ReflectionBlur).rgb;
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
