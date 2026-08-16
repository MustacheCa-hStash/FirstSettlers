Shader "Custom/BirchBarkUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap("Birch Bark Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Paper Bark Color", Color) = (0.92, 0.90, 0.84, 1.0)
        _WarmBarkColor("Warm Bark Color", Color) = (0.74, 0.69, 0.58, 1.0)
        _ScarColor("Black Scar Color", Color) = (0.025, 0.022, 0.018, 1.0)
        _CoolShadowColor("Cool Side Color", Color) = (0.58, 0.62, 0.60, 1.0)
        _ScarThreshold("Scar Threshold", Range(0, 1)) = 0.34
        _ScarSoftness("Scar Softness", Range(0.001, 0.35)) = 0.08
        _ScarStrength("Scar Strength", Range(0, 1)) = 0.95
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.18
        _VerticalGradientStrength("Vertical Gradient Strength", Range(0, 1)) = 0.10
        _FauxSideShadeStrength("Fake Side Shade Strength", Range(0, 1)) = 0.20
        _Brightness("Brightness", Range(0.25, 2)) = 1.0
        [Toggle] _UseVertexColor("Use Vertex Color", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "UnlitBirchBark"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _WarmBarkColor;
                half4 _ScarColor;
                half4 _CoolShadowColor;
                half _ScarThreshold;
                half _ScarSoftness;
                half _ScarStrength;
                half _ColorVariationStrength;
                half _VerticalGradientStrength;
                half _FauxSideShadeStrength;
                half _Brightness;
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

            half ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                half a = Hash12(i);
                half b = Hash12(i + float2(1.0, 0.0));
                half c = Hash12(i + float2(0.0, 1.0));
                half d = Hash12(i + float2(1.0, 1.0));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
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

                half3 normalWS = normalize(IN.normalWS);
                half3 sampleColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                half barkLuma = dot(sampleColor, half3(0.299h, 0.587h, 0.114h));
                half scarMask = 1.0h - smoothstep(_ScarThreshold, _ScarThreshold + _ScarSoftness, barkLuma);

                half verticalNoise = ValueNoise(IN.uv * float2(2.4, 10.0));
                half fineNoise = ValueNoise(IN.uv * float2(16.0, 34.0));
                half worldNoise = ValueNoise(IN.positionWS.xz * 0.38 + IN.uv.yx * 1.5);
                half variation = verticalNoise * 0.45h + fineNoise * 0.30h + worldNoise * 0.25h;

                half3 barkColor = lerp(_BaseColor.rgb, _WarmBarkColor.rgb, variation * _ColorVariationStrength);
                half heightShade = lerp(1.0h - _VerticalGradientStrength, 1.0h + _VerticalGradientStrength, saturate(IN.uv.y));
                half sideMask = saturate(dot(normalWS.xz, normalize(half2(-0.42h, 0.91h))) * 0.5h + 0.5h);
                half sideShade = lerp(1.0h - _FauxSideShadeStrength, 1.0h + _FauxSideShadeStrength * 0.35h, sideMask);

                barkColor *= heightShade * sideShade * _Brightness;
                barkColor = lerp(barkColor, _CoolShadowColor.rgb * barkColor, (1.0h - sideMask) * _FauxSideShadeStrength);
                barkColor = lerp(barkColor, _ScarColor.rgb, saturate(scarMask * _ScarStrength));
                barkColor *= lerp(half3(1.0h, 1.0h, 1.0h), IN.color.rgb, saturate(_UseVertexColor));

                return half4(saturate(barkColor), 1.0h);
            }
            ENDHLSL
        }
    }
}
