Shader "Custom/RedMapleLeafBillboardCaptureCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Red Maple Leaf Atlas / Alpha", 2D) = "white" {}
        [MainColor] _CaptureLeafColor("Capture Leaf Color", Color) = (0.72, 0.08, 0.045, 1.0)
        _LeafHighlightColor("Capture Leaf Highlight Color", Color) = (0.95, 0.28, 0.055, 1.0)
        _LeafShadowColor("Capture Leaf Shadow Color", Color) = (0.24, 0.025, 0.025, 1.0)
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.42
        _LeafDetailContrast("Leaf Detail Contrast", Range(0, 1)) = 0.18
        _VerticalGradientStrength("Vertical Gradient Strength", Range(0, 1)) = 0.12
        _CardVariationStrength("Card Variation Strength", Range(0, 1)) = 0.035
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.70
        _LightWrap("Leaf Light Wrap", Range(0, 1)) = 0.68
        _Smoothness("Smoothness", Range(0, 1)) = 0.04
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.01
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
        Cull Off
        ZWrite On
        ZTest LEqual
        AlphaToMask On

        Pass
        {
            Name "ForwardRedMapleLeafCapture"
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _CaptureLeafColor;
                half4 _LeafHighlightColor;
                half4 _LeafShadowColor;
                half _Cutoff;
                half _LeafDetailContrast;
                half _VerticalGradientStrength;
                half _CardVariationStrength;
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
                float4 shadowCoord : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half Hash12(float2 p)
            {
                half3 p3 = frac(half3(p.xyx) * half3(0.1031h, 0.1030h, 0.0973h));
                p3 += dot(p3, p3.yzx + 33.33h);
                return frac((p3.x + p3.y) * p3.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;
                OUT.normalWS = normalInputs.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = TransformWorldToShadowCoord(positionInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(atlas.a - _Cutoff);

                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif

                half alphaCoverage = saturate((atlas.a - _Cutoff) / max(1.0h - _Cutoff, 0.001h));
                half atlasLuma = saturate(dot(atlas.rgb, half3(0.299h, 0.587h, 0.114h)));
                half detail = lerp(1.0h - _LeafDetailContrast, 1.0h + _LeafDetailContrast, alphaCoverage * atlasLuma);

                half highlightMask = smoothstep(0.55h, 1.0h, atlasLuma);
                half bottomShade = saturate((1.0h - IN.uv.y) * _VerticalGradientStrength);
                half cardNoise = Hash12(floor(IN.positionWS.xz * 0.71h) + floor(IN.uv * 5.0h));
                half cardVariation = lerp(1.0h - _CardVariationStrength, 1.0h + _CardVariationStrength, cardNoise);

                half3 leafColor = lerp(_CaptureLeafColor.rgb, _LeafHighlightColor.rgb, highlightMask * 0.22h);
                leafColor = lerp(leafColor, _LeafShadowColor.rgb, bottomShade);
                leafColor *= detail * cardVariation;

                InputData inputData = InitializeTreeSimpleLitInputData(IN.positionWS, IN.normalWS, IN.positionCS, IN.shadowCoord, _AmbientStrength);
                inputData.normalWS = normalize(lerp(inputData.normalWS, half3(0.0h, 1.0h, 0.0h), _LightWrap * 0.22h));

                SurfaceData surfaceData = InitializeTreeSimpleLitSurfaceData(leafColor, atlas.a, _Smoothness, _SpecularStrength);
                half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
                return half4(saturate(color.rgb), atlas.a);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
