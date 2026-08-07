Shader "Custom/SpruceLeafTintCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("White Leaf Atlas / Alpha", 2D) = "white" {}
        [MainColor] _LeafColor("Leaf Color", Color) = (0.10, 0.34, 0.16, 1.0)
        _TipColor("Tip Color", Color) = (0.36, 0.78, 0.25, 1.0)
        _TipStrength("Tip Color Strength", Range(0, 1)) = 0.15
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.35
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.35
        _LightWrap("Leaf Light Wrap", Range(0, 1)) = 0.45
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
                half4 _TipColor;
                half _TipStrength;
                half _Cutoff;
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
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = positionInputs.positionCS;
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

                half tipMask = saturate(IN.uv.y) * _TipStrength;
                half3 leafColor = lerp(_LeafColor.rgb, _TipColor.rgb, tipMask);
                leafColor *= lerp(half3(1.0h, 1.0h, 1.0h), IN.color.rgb, saturate(_UseVertexColor));

                Light mainLight = GetMainLight();
                half3 normalWS = normalize(IN.normalWS);
                half wrappedNdotL = saturate((dot(normalWS, mainLight.direction) + _LightWrap) / (1.0h + _LightWrap));
                half3 lighting = max(mainLight.color * wrappedNdotL, _AmbientStrength.xxx);

                return half4(atlas.rgb * leafColor * lighting, atlas.a);
            }
            ENDHLSL
        }
    }
}
