Shader "Custom/CattailInstancedCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Cattail Atlas (RGBA)", 2D) = "white" {}
        _HeadTint("Head Tint", Color) = (0.88, 0.78, 0.72, 1)
        _LeafTint("Stem and Leaf Tint", Color) = (0.82, 1, 0.76, 1)
        _HeadAtlasSplit("Head / Leaf Atlas Split", Range(0, 1)) = 0.46
        _ColorVariation("Instance Color Variation", Range(0, 0.4)) = 0.12
        _AmbientFloor("Ambient Floor", Range(0, 1)) = 0.56
        _DirectLightStrength("Direct Light Strength", Range(0, 2)) = 0.62
        _UpwardLighting("Even Plane Lighting", Range(0, 1)) = 0.65
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        LOD 100
        Cull Off
        ZWrite On
        AlphaToMask On

        Pass
        {
            Name "ForwardLit"
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
                half4 _HeadTint;
                half4 _LeafTint;
                half _HeadAtlasSplit;
                half _ColorVariation;
                half _AmbientFloor;
                half _DirectLightStrength;
                half _UpwardLighting;
                half _Cutoff;
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
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half variation : TEXCOORD2;
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

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.variation = HashPosition(TransformObjectToWorld(float3(0, 0, 0)).xz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(texel.a - _Cutoff);

                half headMask = 1.0h - smoothstep(_HeadAtlasSplit - 0.02h,
                    _HeadAtlasSplit + 0.02h, IN.uv.x);
                half3 tint = lerp(_LeafTint.rgb, _HeadTint.rgb, headMask);
                half variation = lerp(1.0h - _ColorVariation,
                    1.0h + _ColorVariation, IN.variation);
                half3 albedo = texel.rgb * tint * variation;

                half3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                // Symmetric lighting keeps the back of each thin plane equally bright.
                // Blending toward an upward response softens differences between crossed leaves.
                half planeLight = abs(dot(normalWS, mainLight.direction));
                half upLight = saturate(mainLight.direction.y);
                half diffuse = lerp(planeLight, upLight, _UpwardLighting);
                half3 ambient = max(SampleSH(half3(0.0h, 1.0h, 0.0h)), _AmbientFloor.xxx);
                half3 lighting = ambient + mainLight.color * (diffuse * _DirectLightStrength);

                return half4(albedo * lighting, texel.a);
            }
            ENDHLSL
        }
    }
}
