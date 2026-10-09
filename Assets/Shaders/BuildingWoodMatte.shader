Shader "Custom/BuildingWoodMatte"
{
    Properties
    {
        [MainTexture] _BaseMap("Ordinary Wood Trim (UV0)", 2D) = "white" {}
        [MainColor] _BaseColor("Texture Tint", Color) = (1, 1, 1, 1)
        _Saturation("Texture Saturation", Range(0, 1.5)) = 0.9
        _Contrast("Texture Contrast", Range(0.5, 1.5)) = 0.9
        _Brightness("Texture Brightness", Range(0.5, 1.5)) = 1
        _AmbientFloor("Daytime Ambient Floor", Range(0, 1)) = 0.18
        _DirectLightStrength("Diffuse Light Strength", Range(0, 2)) = 0.85
        _LightWrap("Soft Diffuse Wrap", Range(0, 0.5)) = 0.08
        _ShadowStrength("Received Shadow Strength", Range(0, 1)) = 1
        _ShadowTint("Subtle Cool Shadow Tint", Color) = (0.86, 0.91, 1, 1)
        _ShadowTintStrength("Shadow Tint Strength", Range(0, 0.5)) = 0.12
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Cutout", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Render Face Culling", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull [_Cull]
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        // Reuse the existing sun-cycle ambient-floor dimming, without any tree deformation/fade.
        #include "Assets/Shaders/TreeNightLighting.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadowTint;
            half _Saturation;
            half _Contrast;
            half _Brightness;
            half _AmbientFloor;
            half _DirectLightStrength;
            half _LightWrap;
            half _ShadowStrength;
            half _ShadowTintStrength;
            half _AlphaClip;
            half _Cutoff;
            half _Cull;
        CBUFFER_END

        struct WoodAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct WoodVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            half4 fogAndVertexLight : TEXCOORD3;
            float4 shadowCoord : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        WoodVaryings WoodVertex(WoodAttributes input)
        {
            WoodVaryings output = (WoodVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = positions.positionCS;
            output.positionWS = positions.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.shadowCoord = GetShadowCoord(positions);
            output.fogAndVertexLight.x = ComputeFogFactor(positions.positionCS.z);
            output.fogAndVertexLight.yzw = VertexLighting(positions.positionWS, output.normalWS);
            return output;
        }

        half3 WoodDiffuse(Light light, half3 normalWS)
        {
            half diffuse = saturate((dot(normalWS, light.direction) + _LightWrap) / (1.0h + _LightWrap));
            half shadow = lerp(1.0h, light.shadowAttenuation, _ShadowStrength);
            return light.color * (diffuse * light.distanceAttenuation * shadow * _DirectLightStrength);
        }

        void WoodClipAlpha(half alpha)
        {
            #if defined(_ALPHATEST_ON)
                clip(alpha * _BaseColor.a - _Cutoff);
            #endif
        }

        void WoodClipTexture(float2 uv)
        {
            #if defined(_ALPHATEST_ON)
                WoodClipAlpha(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WoodVertex
            #pragma fragment WoodFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES

            half4 WoodFragment(WoodVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                WoodClipAlpha(texel.a);
                half3 albedo = texel.rgb;
                half luma = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                albedo = lerp(luma.xxx, albedo, _Saturation);
                albedo = saturate((albedo - 0.18h) * _Contrast + 0.18h);
                albedo *= _BaseColor.rgb * _Brightness;

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                // A thin weave sheet must receive light on the side facing the camera.
                // Opaque wood keeps its original normal and back-face-culling behavior.
                if (_Cull < 0.5h) normalWS *= IS_FRONT_VFACE(facing, 1.0h, -1.0h);
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = input.shadowCoord;
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                half4 shadowMask = half4(1, 1, 1, 1);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                half3 lighting = max(SampleSH(normalWS), TreeNightAmbientFloor(_AmbientFloor).xxx) * ao.indirectAmbientOcclusion;
                lighting += WoodDiffuse(mainLight, normalWS) * ao.directAmbientOcclusion;

                #if defined(_ADDITIONAL_LIGHTS)
                    uint lightCount = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
                        {
                            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                            Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                            lighting += WoodDiffuse(light, normalWS) * ao.directAmbientOcclusion;
                        }
                    #endif
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                        lighting += WoodDiffuse(light, normalWS) * ao.directAmbientOcclusion;
                    LIGHT_LOOP_END
                #endif
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    lighting += (_Cull < 0.5h ? VertexLighting(input.positionWS, normalWS) : input.fogAndVertexLight.yzw) * _DirectLightStrength;
                #endif

                half shadowSide = 1.0h - saturate(dot(normalWS, mainLight.direction));
                half shadowTint = max(shadowSide, (1.0h - mainLight.shadowAttenuation) * _ShadowStrength) * _ShadowTintStrength;
                half3 color = albedo * lighting * lerp(half3(1, 1, 1), _ShadowTint.rgb, shadowTint);
                // Diffuse only: no view-dependent specular, metallic or reflection-probe contribution.
                return half4(MixFog(color, input.fogAndVertexLight.x), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WoodShadowVertex
            #pragma fragment WoodDepthFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;

            WoodVaryings WoodShadowVertex(WoodAttributes input)
            {
                WoodVaryings output = (WoodVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                // Bias a two-sided sheet toward the light-facing side, including
                // when its authored front normal points away from that light.
                if (_Cull < 0.5h && dot(normalWS, lightDirectionWS) < 0) normalWS = -normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            half4 WoodDepthFragment(WoodVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                WoodClipTexture(input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WoodVertex
            #pragma fragment WoodDepthFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            half4 WoodDepthFragment(WoodVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                WoodClipTexture(input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WoodVertex
            #pragma fragment WoodDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 WoodDepthNormalsFragment(WoodVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                WoodClipTexture(input.uv);
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                if (_Cull < 0.5h) normalWS *= IS_FRONT_VFACE(facing, 1.0h, -1.0h);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(normalWS);
                    return half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), 0);
                #else
                    return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
