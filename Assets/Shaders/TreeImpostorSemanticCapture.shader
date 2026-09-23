Shader "Hidden/TreeImpostor/SemanticCapture"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _LeafColor("Leaf Color", Color) = (1,1,1,1)
        _CoolNeedleColor("Cool Needle Color", Color) = (1,1,1,1)
        _DeepNeedleColor("Deep Needle Color", Color) = (1,1,1,1)
        _TipColor("Tip Color", Color) = (1,1,1,1)
        _ColorVariationStrength("Color Variation", Float) = 0
        _MacroVariationStrength("Macro Variation", Float) = 0
        _FineVariationStrength("Fine Variation", Float) = 0
        _HeightColorVariation("Height Variation", Float) = 0
        _ColorHeightMin("Color Height Min", Float) = 0
        _ColorHeightMax("Color Height Max", Float) = 1
        _NeedleContrast("Needle Contrast", Float) = 0
        _TipStrength("Tip Strength", Float) = 0
        _Cutoff("Cutoff", Float) = 0.5
        _Smoothness("Smoothness", Float) = 0
        _BacklightStrength("Backlight Strength", Float) = 0
        _RidgeColor("Ridge Color", Color) = (1,1,1,1)
        _CreviceColor("Crevice Color", Color) = (0,0,0,1)
        _CrackThreshold("Crack Threshold", Float) = 0.5
        _CrackSoftness("Crack Softness", Float) = 0.05
        _CrackDarkness("Crack Darkness", Float) = 0
        _RidgeStrength("Ridge Strength", Float) = 0
        _Brightness("Brightness", Float) = 1
        _CaptureMaterialKind("Capture Material Kind", Float) = 0
        _CaptureOutput("Capture Output", Float) = 0
        _CaptureDepthMinMax("Capture Depth Min Max", Vector) = (0,1,0,0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor, _LeafColor, _CoolNeedleColor, _DeepNeedleColor, _TipColor, _RidgeColor, _CreviceColor;
                half _ColorVariationStrength, _MacroVariationStrength, _FineVariationStrength, _HeightColorVariation;
                half _ColorHeightMin, _ColorHeightMax, _NeedleContrast, _TipStrength, _Cutoff, _Smoothness, _BacklightStrength;
                half _CrackThreshold, _CrackSoftness, _CrackDarkness, _RidgeStrength, _Brightness;
                half _CaptureMaterialKind, _CaptureOutput;
                float4 _CaptureDepthMinMax;
                float4x4 _CaptureWorldToLocal;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionVS : TEXCOORD2; float2 uv : TEXCOORD3; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionVS = TransformWorldToView(position.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half Hash12(float2 p)
            {
                half3 p3 = frac(half3(p.xyx) * half3(0.1031h, 0.1030h, 0.0973h));
                p3 += dot(p3, p3.yzx + 33.33h);
                return frac((p3.x + p3.y) * p3.z);
            }

            half3 LeafAlbedo(half4 sample, float3 positionOS)
            {
                half macro = Hash12(floor(positionOS.xz * 0.16h));
                half fine = Hash12(floor(positionOS.xz * 2.4h + positionOS.y * 0.45h));
                half height01 = saturate((positionOS.y - _ColorHeightMin) / max(_ColorHeightMax - _ColorHeightMin, 0.0001h));
                half cool = smoothstep(0.18h, 0.78h, macro) * _ColorVariationStrength * _MacroVariationStrength;
                half deep = smoothstep(0.60h, 0.96h, 1.0h - macro) * _ColorVariationStrength * 0.58h;
                half tip = smoothstep(0.58h, 0.94h, height01 + (fine - 0.5h) * 0.16h) * _TipStrength * (0.35h + _HeightColorVariation * 0.65h);
                half3 color = lerp(_LeafColor.rgb, _CoolNeedleColor.rgb, cool);
                color = lerp(color, _DeepNeedleColor.rgb, deep);
                color = lerp(color, _TipColor.rgb, tip);
                half luma = saturate(dot(sample.rgb, half3(0.299h, 0.587h, 0.114h)));
                color *= lerp(1.0h - _NeedleContrast * 0.72h, 1.0h + _NeedleContrast * 0.42h, smoothstep(0.18h, 0.92h, luma));
                // The spruce card texture is authored as a dark detail/alpha
                // mask.  Multiplying it by the already-dark needle tint bakes
                // near-black foliage.  Use the authored needle colour as the
                // albedo; sample luminance above still supplies restrained
                // card-level variation through _NeedleContrast.
                return color;
            }

            half3 BarkAlbedo(half4 sample)
            {
                half luma = dot(sample.rgb, half3(0.299h, 0.587h, 0.114h));
                half crack = 1.0h - smoothstep(_CrackThreshold, _CrackThreshold + _CrackSoftness, luma);
                half ridge = smoothstep(0.58h, 0.98h, luma);
                half3 color = lerp(_BaseColor.rgb, _RidgeColor.rgb, ridge * _RidgeStrength);
                color = lerp(color, _CreviceColor.rgb, saturate(crack * _CrackDarkness));
                return color * _Brightness;
            }

            float2 EncodeOct(float3 n)
            {
                n /= max(abs(n.x) + abs(n.y) + abs(n.z), 0.0001);
                float2 e = n.xy;
                if (n.z < 0.0)
                {
                    float2 signs = float2(e.x >= 0.0 ? 1.0 : -1.0, e.y >= 0.0 ? 1.0 : -1.0);
                    e = (1.0 - abs(e.yx)) * signs;
                }
                return e * 0.5 + 0.5;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 sample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                bool leaf = _CaptureMaterialKind < 0.5h;
                if (leaf) clip(sample.a - _Cutoff);

                if (_CaptureOutput < 0.5h)
                    return half4(leaf ? LeafAlbedo(sample, input.positionOS) : BarkAlbedo(sample), leaf ? sample.a : 1.0h);

                if (_CaptureOutput < 1.5h)
                {
                    float faceSign = IS_FRONT_VFACE(facing, 1.0, -1.0);
                    float3 normalOS = normalize(mul((float3x3)_CaptureWorldToLocal, input.normalWS) * faceSign);
                    float2 encodedNormal = EncodeOct(normalOS);
                    return half4(encodedNormal, 1.0h - _Smoothness, leaf ? _BacklightStrength : 0.0h);
                }

                if (_CaptureOutput < 2.5h)
                {
                    float eyeDepth = -input.positionVS.z;
                    float depth = saturate((eyeDepth - _CaptureDepthMinMax.x) / max(_CaptureDepthMinMax.y - _CaptureDepthMinMax.x, 0.0001));
                    return half4(depth, depth, depth, 1.0);
                }

                return leaf ? half4(1, 0, 0, 1) : half4(0, 1, 0, 1);
            }
            ENDHLSL
        }
    }
}
