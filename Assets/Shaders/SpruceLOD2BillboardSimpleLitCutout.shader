Shader "Custom/SpruceLOD2BillboardSimpleLitCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo Alpha Atlas", 2D) = "white" {}
        _LeafMask("Leaf Mask Atlas", 2D) = "black" {}
        [MainColor] _BaseColor("Overall Tint", Color) = (1, 1, 1, 1)
        _BarkTint("Bark Tint", Color) = (1, 1, 1, 1)
        _BaseNeedleColor("Base Needle Color", Color) = (0.18431373, 0.35294118, 0.21176471, 1.0)
        _CoolNeedleColor("Cool Blue-Green Color", Color) = (0.13, 0.25, 0.22, 1.0)
        _DeepNeedleColor("Deep Shadow Green", Color) = (0.055, 0.16, 0.09, 1.0)
        _TipNeedleColor("Fresh Tip Color", Color) = (0.30, 0.45, 0.22, 1.0)
        _LeafTintStrength("Leaf Tint Strength", Range(0, 1)) = 0.95
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.36
        _NeedleContrast("Needle Contrast", Range(0, 1)) = 0.18
        _TipStrength("Tip Color Strength", Range(0, 1)) = 0.18
        _LowerShadeStrength("Lower Canopy Shade", Range(0, 1)) = 0.22
        _InteriorShadeStrength("Interior Canopy Shade", Range(0, 1)) = 0.16
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.38
        _LeafMaskThreshold("Leaf Mask Threshold", Range(0, 1)) = 0.25
        _LeafMaskSoftness("Leaf Mask Softness", Range(0.001, 0.25)) = 0.04
        _Brightness("Brightness", Range(0.25, 2)) = 1.0
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.58
        _LightWrap("Billboard Light Softness", Range(0, 1)) = 0.62
        _Smoothness("Smoothness", Range(0, 1)) = 0.05
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.015
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }

        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual
        AlphaToMask On

        Pass
        {
            Name "ForwardSpruceLOD2Billboard"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #include "Assets/Shaders/TreeSimpleLitCommon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_LeafMask);
            SAMPLER(sampler_LeafMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _LeafMask_ST;
                half4 _BaseColor;
                half4 _BarkTint;
                half4 _BaseNeedleColor;
                half4 _CoolNeedleColor;
                half4 _DeepNeedleColor;
                half4 _TipNeedleColor;
                half _LeafTintStrength;
                half _ColorVariationStrength;
                half _NeedleContrast;
                half _TipStrength;
                half _LowerShadeStrength;
                half _InteriorShadeStrength;
                half _Cutoff;
                half _LeafMaskThreshold;
                half _LeafMaskSoftness;
                half _Brightness;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float2 viewUv : TEXCOORD3;
                float3 instanceOriginWS : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half3 EvaluateSpruceNeedleColor(float2 viewUv, float3 instanceOriginWS)
            {
                half treeNoise = Hash12(floor(instanceOriginWS.xz * 0.31h));
                half branchNoise = Hash12(floor(instanceOriginWS.xz * 0.57h) + floor(viewUv * 5.0h));
                half fineNoise = Hash12(floor(instanceOriginWS.xz * 1.17h) + floor(viewUv * 18.0h));

                half coolMix = smoothstep(0.18h, 0.86h, branchNoise + treeNoise * 0.12h) * _ColorVariationStrength;
                half deepMix = smoothstep(0.66h, 0.98h, 1.0h - fineNoise + branchNoise * 0.16h) * _ColorVariationStrength * 0.72h;
                half tipMix = smoothstep(0.60h, 0.98h, viewUv.y + fineNoise * 0.18h) * _TipStrength;

                half3 needleColor = lerp(_BaseNeedleColor.rgb, _CoolNeedleColor.rgb, coolMix);
                needleColor = lerp(needleColor, _DeepNeedleColor.rgb, deepMix);
                needleColor = lerp(needleColor, _TipNeedleColor.rgb, tipMix);

                half contrastNoise = treeNoise * 0.30h + branchNoise * 0.45h + fineNoise * 0.25h;
                needleColor *= lerp(1.0h - _NeedleContrast, 1.0h + _NeedleContrast, contrastNoise);
                return needleColor;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                half3 normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                normalWS = normalize(lerp(normalWS, half3(0.0h, 1.0h, 0.0h), _LightWrap * 0.30h));

                OUT.positionCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;
                OUT.normalWS = normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.viewUv = frac(OUT.uv * 2.0);
                OUT.instanceOriginWS = mul(GetObjectToWorldMatrix(), float4(0.0, 0.0, 0.0, 1.0)).xyz;
                OUT.shadowCoord = TransformWorldToShadowCoord(positionInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(baseSample.a - _Cutoff);

                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif

                half maskSample = SAMPLE_TEXTURE2D(_LeafMask, sampler_LeafMask, IN.uv).r;
                half leafMask = smoothstep(
                    _LeafMaskThreshold,
                    saturate(_LeafMaskThreshold + _LeafMaskSoftness),
                    maskSample) * _LeafTintStrength;

                half atlasLuma = saturate(dot(baseSample.rgb, half3(0.299h, 0.587h, 0.114h)));
                half leafDetail = lerp(0.72h, 1.20h, smoothstep(0.18h, 0.90h, atlasLuma));

                half lowerMask = saturate(1.0h - IN.viewUv.y);
                half2 centeredUv = IN.viewUv * 2.0h - 1.0h;
                half interiorMask = 1.0h - saturate(length(centeredUv * half2(0.82h, 1.05h)));
                half shadeMask = saturate(lowerMask * _LowerShadeStrength + interiorMask * _InteriorShadeStrength);

                half3 spruceNeedles = EvaluateSpruceNeedleColor(IN.viewUv, IN.instanceOriginWS) * leafDetail;
                spruceNeedles = lerp(spruceNeedles, _DeepNeedleColor.rgb, shadeMask * leafMask);

                half3 barkColor = baseSample.rgb * _BarkTint.rgb;
                half3 color = lerp(barkColor, spruceNeedles, leafMask);

                InputData inputData = InitializeTreeSimpleLitInputData(IN.positionWS, IN.normalWS, IN.positionCS, IN.shadowCoord, _AmbientStrength);
                SurfaceData surfaceData = InitializeTreeSimpleLitSurfaceData(saturate(color * _Brightness), baseSample.a, _Smoothness, _SpecularStrength);
                half4 litColor = UniversalFragmentBlinnPhong(inputData, surfaceData);
                return half4(saturate(litColor.rgb), baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _LeafMask_ST;
                half4 _BaseColor;
                half4 _BarkTint;
                half4 _BaseNeedleColor;
                half4 _CoolNeedleColor;
                half4 _DeepNeedleColor;
                half4 _TipNeedleColor;
                half _LeafTintStrength;
                half _ColorVariationStrength;
                half _NeedleContrast;
                half _TipStrength;
                half _LowerShadeStrength;
                half _InteriorShadeStrength;
                half _Cutoff;
                half _LeafMaskThreshold;
                half _LeafMaskSoftness;
                half _Brightness;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);

                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif

                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
