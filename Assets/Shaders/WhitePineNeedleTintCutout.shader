Shader "Custom/WhitePineNeedleTintCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("White Pine Needle Atlas / Alpha", 2D) = "white" {}
        [MainColor] _NeedleColor("Needle Color", Color) = (0.12, 0.34, 0.18, 1.0)
        _NeedleShadowColor("Needle Shadow Color", Color) = (0.035, 0.16, 0.08, 1.0)
        _NeedleTipColor("Needle Tip Color", Color) = (0.46, 0.68, 0.34, 1.0)
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.38
        _TipStrength("Tip Brightness Strength", Range(0, 1)) = 0.28
        _VerticalGradientStrength("Vertical Gradient Strength", Range(0, 1)) = 0.22
        _StrandContrast("Strand Contrast", Range(0, 1)) = 0.35
        _CardVariationStrength("Card Variation Strength", Range(0, 1)) = 0.18
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.40
        _LightWrap("Needle Light Wrap", Range(0, 1)) = 0.65
        [Toggle] _UseVertexColor("Use Vertex Color", Float) = 0
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
            Name "ForwardNeedles"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _NeedleColor;
                half4 _NeedleShadowColor;
                half4 _NeedleTipColor;
                half _Cutoff;
                half _TipStrength;
                half _VerticalGradientStrength;
                half _StrandContrast;
                half _CardVariationStrength;
                half _AmbientStrength;
                half _LightWrap;
                half _UseVertexColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
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
                OUT.color = IN.color;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(atlas.a - _Cutoff);

                half alphaCoverage = saturate((atlas.a - _Cutoff) / max(1.0h - _Cutoff, 0.001h));
                half atlasLuma = dot(atlas.rgb, half3(0.299h, 0.587h, 0.114h));

                half tipMask = pow(saturate(IN.uv.y), 1.35h) * _TipStrength;
                half baseShadeMask = saturate((1.0h - IN.uv.y) * _VerticalGradientStrength);
                half cardNoise = Hash12(floor(IN.positionWS.xz * 0.8h) + floor(IN.uv * 7.0h));
                half cardVariation = lerp(1.0h - _CardVariationStrength, 1.0h + _CardVariationStrength, cardNoise);

                half strandHighlight = lerp(1.0h - _StrandContrast, 1.0h + _StrandContrast, alphaCoverage * atlasLuma);

                half3 needleColor = lerp(_NeedleColor.rgb, _NeedleShadowColor.rgb, baseShadeMask);
                needleColor = lerp(needleColor, _NeedleTipColor.rgb, tipMask);
                needleColor *= strandHighlight * cardVariation;
                needleColor *= lerp(half3(1.0h, 1.0h, 1.0h), IN.color.rgb, saturate(_UseVertexColor));

                Light mainLight = GetMainLight();
                half3 normalWS = normalize(IN.normalWS);
                half wrappedNdotL = saturate((dot(normalWS, mainLight.direction) + _LightWrap) / (1.0h + _LightWrap));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = max(ambient, _AmbientStrength.xxx) + mainLight.color * wrappedNdotL;

                return half4(needleColor * lighting, atlas.a);
            }
            ENDHLSL
        }
    }
}
