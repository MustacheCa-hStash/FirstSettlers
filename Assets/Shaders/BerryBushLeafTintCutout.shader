Shader "Custom/BerryBushLeafTintCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Berry Bush Leaf Atlas / Alpha", 2D) = "white" {}
        [MainColor] _LeafColor("Leaf Color", Color) = (0.12, 0.36, 0.12, 1.0)
        _YoungLeafColor("Young / Sunlit Leaf Color", Color) = (0.38, 0.62, 0.22, 1.0)
        _ShadowLeafColor("Shadow Leaf Color", Color) = (0.035, 0.14, 0.045, 1.0)
        _EdgeHighlightColor("Thin Edge Highlight", Color) = (0.55, 0.74, 0.34, 1.0)
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.42
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.30
        _LeafContrast("Atlas Leaf Contrast", Range(0, 1)) = 0.38
        _LeafSeparation("Leaf Separation", Range(0, 1)) = 0.35
        _VerticalGradientStrength("Vertical Gradient Strength", Range(0, 1)) = 0.24
        _CardVariationStrength("Card Variation Strength", Range(0, 1)) = 0.18
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.45
        _LightWrap("Leaf Light Wrap", Range(0, 1)) = 0.65
        _SurfaceHueVariation("Surface Hue Variation", Range(0, 1)) = 0.10
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
            Name "ForwardLeaf"
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
                half4 _LeafColor;
                half4 _YoungLeafColor;
                half4 _ShadowLeafColor;
                half4 _EdgeHighlightColor;
                half _Cutoff;
                half _ColorVariationStrength;
                half _LeafContrast;
                half _LeafSeparation;
                half _VerticalGradientStrength;
                half _CardVariationStrength;
                half _AmbientStrength;
                half _LightWrap;
                half _SurfaceHueVariation;
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

            half3 ShiftGreenHue(half3 color, half amount)
            {
                half warm = amount * 0.08h;
                half cool = amount * 0.05h;
                color.r += warm;
                color.g *= 1.0h + amount * 0.08h;
                color.b += cool;
                return saturate(color);
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
                half atlasLuma = saturate(dot(atlas.rgb, half3(0.299h, 0.587h, 0.114h)));

                half cardNoise = Hash12(floor(IN.positionWS.xz * 0.85h) + floor(IN.uv * 7.0h));
                half leafNoise = Hash12(floor(IN.positionWS.xz * 1.75h) + floor(IN.uv * 23.0h));
                half smallNoise = Hash12(floor(IN.positionWS.xz * 4.0h) + floor(IN.uv * 41.0h));

                half youngMix = saturate(leafNoise * _ColorVariationStrength + atlasLuma * 0.28h);
                half3 leafColor = lerp(_LeafColor.rgb, _YoungLeafColor.rgb, youngMix);
                leafColor = ShiftGreenHue(leafColor, (smallNoise - 0.5h) * _SurfaceHueVariation);

                half bottomShade = saturate((1.0h - IN.uv.y) * _VerticalGradientStrength);
                leafColor = lerp(leafColor, _ShadowLeafColor.rgb, bottomShade);

                half edgeLift = smoothstep(0.03h, 0.62h, alphaCoverage) * (1.0h - smoothstep(0.62h, 1.0h, alphaCoverage));
                leafColor = lerp(leafColor, _EdgeHighlightColor.rgb, edgeLift * _LeafSeparation);

                half atlasDetail = lerp(1.0h - _LeafContrast, 1.0h + _LeafContrast, atlasLuma);
                half coverageSeparation = lerp(1.0h - _LeafSeparation * 0.35h, 1.0h, alphaCoverage);
                half cardVariation = lerp(1.0h - _CardVariationStrength, 1.0h + _CardVariationStrength, cardNoise);
                leafColor *= atlasDetail * coverageSeparation * cardVariation;
                leafColor *= lerp(half3(1.0h, 1.0h, 1.0h), IN.color.rgb, saturate(_UseVertexColor));

                Light mainLight = GetMainLight();
                half3 normalWS = normalize(IN.normalWS);
                half wrappedNdotL = saturate((dot(normalWS, mainLight.direction) + _LightWrap) / (1.0h + _LightWrap));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = max(ambient, _AmbientStrength.xxx) + mainLight.color * wrappedNdotL;

                return half4(leafColor * lighting, atlas.a);
            }
            ENDHLSL
        }
    }
}
