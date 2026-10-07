Shader "Hidden/TreeImpostor/RedMapleSemanticCapture"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _Cutoff("Cutoff", Float) = 0.5
        _Smoothness("Smoothness", Float) = 0.1
        _BacklightingStrength("Backlighting Strength", Float) = 0.5
        _LeafContrast("Leaf Detail Contrast", Float) = 0.34
        _CardVariationStrength("Leaf Card Variation", Float) = 0.22
        _ColorVariationStrength("Leaf Palette Variation", Float) = 0.62
        _CrackThreshold("Crack Threshold", Float) = 0.42
        _CrackSoftness("Crack Softness", Float) = 0.075
        _CrackDarkness("Crack Darkness", Float) = 0.92
        _RidgeStrength("Ridge Strength", Float) = 0.34
        _Brightness("Brightness", Float) = 1
        _BarkSaturation("Bark Saturation", Float) = 0.35
        _BarkContrast("Bark Contrast", Float) = 0.75
        _BarkMidpoint("Bark Contrast Midpoint", Float) = 0.18
        _CaptureMaterialKind("Capture Material Kind", Float) = 0
        _CaptureOutput("Capture Output", Float) = 0
        _CaptureDepthMinMax("Capture Depth Min Max", Vector) = (0,1,0,0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        ZWrite On
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff, _Smoothness, _BacklightingStrength;
                half _LeafContrast, _CardVariationStrength, _ColorVariationStrength;
                half _CrackThreshold, _CrackSoftness, _CrackDarkness, _RidgeStrength, _Brightness;
                half _BarkSaturation, _BarkContrast, _BarkMidpoint;
                half _CaptureMaterialKind, _CaptureOutput;
                float4 _CaptureDepthMinMax;
                float4x4 _CaptureWorldToLocal;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
            };

            float2 EncodeOct(float3 normal)
            {
                normal /= max(abs(normal.x) + abs(normal.y) + abs(normal.z), 1e-5);
                float2 encoded = normal.xy;
                if (normal.z < 0.0)
                {
                    float2 signs = float2(
                        encoded.x >= 0.0 ? 1.0 : -1.0,
                        encoded.y >= 0.0 ? 1.0 : -1.0);
                    encoded = (1.0 - abs(encoded.yx)) * signs;
                }
                return encoded * 0.5 + 0.5;
            }

            half Hash12(float2 p)
            {
                half3 p3 = frac(half3(p.xyx) * half3(0.1031h, 0.1030h, 0.0973h));
                p3 += dot(p3, p3.yzx + 33.33h);
                return frac((p3.x + p3.y) * p3.z);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.positionOS = input.positionOS.xyz;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.eyeDepth = -TransformWorldToView(positionInputs.positionWS).z;
                return output;
            }

            half3 MapleLeafValue(half4 sample, float3 positionOS)
            {
                // This deliberately contains no summer or autumn hue.  RGB is a
                // neutral reflectance/value field; the runtime material supplies
                // season palettes and per-instance tint without double-tinting.
                half textureLuma = dot(sample.rgb, half3(0.299h, 0.587h, 0.114h));
                half alphaDetail = saturate((sample.a - _Cutoff) / max(1.0h - _Cutoff, 0.001h));
                half detail = lerp(1.0h - _LeafContrast, 1.0h + _LeafContrast, saturate(textureLuma * alphaDetail));
                half cardNoise = Hash12(floor(positionOS.xz * 0.72h) + floor(positionOS.y * 0.45h));
                detail *= lerp(1.0h - _CardVariationStrength, 1.0h + _CardVariationStrength, cardNoise);
                return detail.xxx;
            }

            half3 BarkAlbedo(half4 sample)
            {
                // Matches RedMapleStylizedBarkCommon's unlit material-colour
                // construction while intentionally omitting lighting, snow, and
                // world/height variation that belong to runtime shading.
                half barkLuma = dot(sample.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 color = lerp(barkLuma.xxx, sample.rgb, _BarkSaturation);
                color = max(0.0h, _BarkMidpoint + (color - _BarkMidpoint) * _BarkContrast);
                return color * _BaseColor.rgb * _Brightness;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 sample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                bool leaf = _CaptureMaterialKind < 0.5h;
                if (leaf) clip(sample.a - _Cutoff);

                if (_CaptureOutput < 0.5h)
                    return half4(leaf ? MapleLeafValue(sample, input.positionOS) : BarkAlbedo(sample), leaf ? sample.a : 1.0h);

                if (_CaptureOutput < 1.5h)
                {
                    float faceSign = IS_FRONT_VFACE(facing, 1.0, -1.0);
                    float3 normalOS = normalize(mul((float3x3)_CaptureWorldToLocal, input.normalWS) * faceSign);
                    return half4(EncodeOct(normalOS), 1.0h - _Smoothness, leaf ? _BacklightingStrength : 0.0h);
                }

                if (_CaptureOutput < 2.5h)
                {
                    half depth = saturate((input.eyeDepth - _CaptureDepthMinMax.x) / max(_CaptureDepthMinMax.y - _CaptureDepthMinMax.x, 0.0001));
                    return half4(depth, depth, depth, 1.0h);
                }

                // R/G are categorical material IDs. B retains a stable leaf
                // palette coordinate for later summer-to-autumn variation.
                half paletteCoordinate = Hash12(floor(input.positionOS.xz * 0.45h) + floor(input.positionOS.y * 0.38h));
                return leaf ? half4(1.0h, 0.0h, paletteCoordinate, 1.0h) : half4(0.0h, 1.0h, 0.0h, 1.0h);
            }
            ENDHLSL
        }
    }
}
