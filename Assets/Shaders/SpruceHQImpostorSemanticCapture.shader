Shader "Hidden/TreeImpostor/SpruceHQSemanticCapture"
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
        _MacroVariationScale("Macro Scale", Float) = .738
        _VerticalGradientStrength("Bark Height Gradient", Float) = .316
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
                half _CrackThreshold, _CrackSoftness, _CrackDarkness, _RidgeStrength, _Brightness, _MacroVariationScale, _VerticalGradientStrength;
                half _CaptureMaterialKind, _CaptureOutput;
                float4 _CaptureDepthMinMax;
                float4x4 _CaptureWorldToLocal;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionVS : TEXCOORD2; float2 uv : TEXCOORD3; float3 positionWS : TEXCOORD4; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionOS = input.positionOS.xyz; output.positionWS=position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionVS = TransformWorldToView(position.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash12(i);
                float b = Hash12(i + float2(1.0, 0.0));
                float c = Hash12(i + float2(0.0, 1.0));
                float d = Hash12(i + float2(1.0, 1.0));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half3 EvaluateSpruceNeedleColor(float2 uv, float3 positionWS, float3 positionOS)
            {
                half macroNoise = ValueNoise(positionWS.xz * _MacroVariationScale + uv * 1.7h);
                half fineNoise = ValueNoise(positionWS.xz * (_MacroVariationScale * 5.3h) + uv.yx * 8.0h);
                half branchNoise = lerp(macroNoise, fineNoise, _FineVariationStrength);
                half height01 = saturate((positionOS.y - _ColorHeightMin) / max(_ColorHeightMax - _ColorHeightMin, 0.0001h));

                half coolMix = smoothstep(0.18h, 0.78h, branchNoise) * _ColorVariationStrength * _MacroVariationStrength;
                half deepMix = smoothstep(0.60h, 0.96h, 1.0h - branchNoise) * _ColorVariationStrength * 0.58h;
                half tipMix = smoothstep(0.58h, 0.94h, height01 + (fineNoise - 0.5h) * 0.16h) * _TipStrength * (0.35h + _HeightColorVariation * 0.65h);

                half3 needleColor = lerp(_LeafColor.rgb, _CoolNeedleColor.rgb, coolMix);
                needleColor = lerp(needleColor, _DeepNeedleColor.rgb, deepMix);
                needleColor = lerp(needleColor, _TipColor.rgb, tipMix);

                half contrastNoise = macroNoise * 0.70h + fineNoise * 0.30h;
                half colorContrast = saturate(_NeedleContrast * 1.35h);
                needleColor *= lerp(1.0h - colorContrast, 1.0h + colorContrast, contrastNoise);
                return needleColor;
            }

            half3 ApplySpruceNeedleDefinition(half4 atlas, half3 leafColor)
            {
                half atlasLuma = saturate(dot(atlas.rgb, half3(0.299h, 0.587h, 0.114h)));
                half textureDetail = smoothstep(0.18h, 0.92h, atlasLuma);
                half textureContrast = lerp(1.0h - _NeedleContrast * 0.72h, 1.0h + _NeedleContrast * 0.42h, textureDetail);

                half edgeMask = 1.0h - smoothstep(_Cutoff, saturate(_Cutoff + 0.24h), atlas.a);
                half edgeDarken = lerp(1.0h, 0.86h, edgeMask * saturate(_NeedleContrast));

                return leafColor * textureContrast * edgeDarken;
            }

            half3 LeafAlbedo(half4 sample, float2 uv, float3 positionWS, float3 positionOS)
            {
                return sample.rgb * ApplySpruceNeedleDefinition(sample,EvaluateSpruceNeedleColor(uv,positionWS,positionOS));
            }
            half3 BarkAlbedo(half4 sample, float2 uv, float3 positionWS)
            {
                half barkLuma = dot(sample.rgb, half3(0.299h, 0.587h, 0.114h));
                half crackMask = 1.0h - smoothstep(_CrackThreshold, _CrackThreshold + _CrackSoftness, barkLuma);
                half ridgeMask = smoothstep(0.58h, 0.98h, barkLuma);

                half plateNoise = ValueNoise(uv * float2(5.5, 2.0));
                half fineNoise = ValueNoise(uv * float2(20.0, 8.0));
                half worldNoise = ValueNoise(positionWS.xz * 0.48 + uv.yx * 1.7);
                half variation = plateNoise * 0.56h + fineNoise * 0.27h + worldNoise * 0.17h;

                half3 barkColor = lerp(_BaseColor.rgb, _RidgeColor.rgb, ridgeMask * _RidgeStrength * (0.55h + variation * 0.55h));
                barkColor *= lerp(1.0h - _ColorVariationStrength, 1.0h + _ColorVariationStrength, variation);

                half heightShade = lerp(1.0h - _VerticalGradientStrength, 1.0h + _VerticalGradientStrength, saturate(uv.y));
                barkColor *= heightShade * _Brightness;
                half barkDetail = lerp(0.72h, 1.22h, smoothstep(0.16h, 0.92h, barkLuma));
                barkColor *= barkDetail;
                barkColor = lerp(barkColor, _CreviceColor.rgb, saturate(crackMask * _CrackDarkness));
                return barkColor;
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
                    return half4(leaf ? LeafAlbedo(sample,input.uv,input.positionWS,input.positionOS) : BarkAlbedo(sample,input.uv,input.positionWS), leaf ? sample.a : 1.0h);

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
