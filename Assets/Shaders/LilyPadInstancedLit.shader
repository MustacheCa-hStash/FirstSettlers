Shader "Custom/LilyPadInstancedLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Lily Pad Mask (R Veins, A Cutout)", 2D) = "white" {}
        _PadColor("Pad Color", Color) = (0.29, 0.46, 0.16, 1)
        _VeinColor("Vein Color", Color) = (0.19, 0.32, 0.11, 1)
        _VeinStrength("Vein Strength", Range(0, 1)) = 0.25
        _ColorVariation("Instance Color Variation", Range(0, 0.4)) = 0.12
        _AmbientFloor("Ambient Floor", Range(0, 1)) = 0.58
        _DirectLightStrength("Direct Light Strength", Range(0, 2)) = 0.65
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.35
        [PerRendererData] _PadTint("Instance Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        LOD 100
        Cull Off
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _PadColor;
                half4 _VeinColor;
                half _VeinStrength;
                half _ColorVariation;
                half _AmbientFloor;
                half _DirectLightStrength;
                half _Cutoff;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(LilyPadInstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PadTint)
            UNITY_INSTANCING_BUFFER_END(LilyPadInstanceProperties)

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
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
                half variation : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half HashPosition(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positionInputs.positionCS;
                OUT.shadowCoord = GetShadowCoord(positionInputs);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);

                // One stable tint per instance; rotation does not change its color.
                float2 originXZ = TransformObjectToWorld(float3(0, 0, 0)).xz;
                OUT.variation = HashPosition(originXZ);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(mask.a - _Cutoff);

                // The source green is a mask, not the final albedo. Red marks the veins.
                // sRGB sampling puts the roughly 110/255 red veins near 0.16 in linear space.
                half veinMask = saturate(mask.r * 6.0h);
                half3 albedo = lerp(_PadColor.rgb, _VeinColor.rgb, veinMask * _VeinStrength);
                albedo *= lerp(1.0h - _ColorVariation, 1.0h + _ColorVariation, IN.variation);
                albedo *= UNITY_ACCESS_INSTANCED_PROP(LilyPadInstanceProperties, _PadTint).rgb;

                half3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight(IN.shadowCoord);
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = max(SampleSH(normalWS), _AmbientFloor.xxx);
                half3 lighting = ambient + mainLight.color *
                    (diffuse * mainLight.shadowAttenuation * _DirectLightStrength);

                return half4(albedo * lighting, 1.0h);
            }
            ENDHLSL
        }
    }
}
