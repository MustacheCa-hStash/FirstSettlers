Shader "Custom/FlowerInstancedUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base / Alpha Map", 2D) = "white" {}
        _MaskMap("Color Mask Map (R Petals, G Stem, B Center)", 2D) = "white" {}

        [PerRendererData] _FlowerPetalColor("Petal Color (Instanced)", Color) = (1, 0.1, 0.05, 1)
        _FlowerPetalTint("Petal Tint", Color) = (1, 1, 1, 1)
        _FlowerStemColor("Stem Color", Color) = (0.05, 0.8, 0.12, 1)
        _FlowerCenterColor("Center Color", Color) = (0.18, 0.09, 0.03, 1)

        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        _MaskSharpness("Mask Sharpness", Range(0.25, 8)) = 1
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

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_MaskMap);
            SAMPLER(sampler_MaskMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _MaskMap_ST;
                half4 _FlowerPetalTint;
                half4 _FlowerStemColor;
                half4 _FlowerCenterColor;
                half _Cutoff;
                half _MaskSharpness;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(FlowerInstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FlowerPetalColor)
            UNITY_INSTANCING_BUFFER_END(FlowerInstanceProperties)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 baseUV : TEXCOORD0;
                float2 maskUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positionInputs.positionCS;
                OUT.baseUV = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.maskUV = TRANSFORM_TEX(IN.uv, _MaskMap);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.baseUV);
                clip(baseSample.a - _Cutoff);

                half3 rawMask = saturate(SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, IN.maskUV).rgb);
                rawMask = pow(rawMask, max(_MaskSharpness, 0.001h));

                half maskTotal = max(rawMask.r + rawMask.g + rawMask.b, 0.0001h);
                half3 mask = rawMask / maskTotal;

                half4 petalColor = UNITY_ACCESS_INSTANCED_PROP(FlowerInstanceProperties, _FlowerPetalColor);
                half3 petal = petalColor.rgb * _FlowerPetalTint.rgb;
                half3 stem = _FlowerStemColor.rgb;
                half3 center = _FlowerCenterColor.rgb;

                half3 color =
                    petal * mask.r +
                    stem * mask.g +
                    center * mask.b;

                return half4(color, baseSample.a);
            }
            ENDHLSL
        }
    }
}
