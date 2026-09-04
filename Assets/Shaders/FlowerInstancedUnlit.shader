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
        _AmbientStrength("Minimum Ambient Strength", Range(0, 1)) = 0.32
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Stem Bend Strength", Range(0, 1)) = 0.035
        _WindSpeed("Wind Stem Bend Speed", Range(0, 8)) = 1.9
        _WindFlutterStrength("Wind Petal Flutter Strength", Range(0, 1)) = 0.012
        _WindFlutterSpeed("Wind Petal Flutter Speed", Range(0, 16)) = 6.8
        _WindGustScale("Wind Gust Scale", Range(0.01, 2)) = 0.48
        _WindHeightMin("Wind Height Min", Float) = 0
        _WindHeightMax("Wind Height Max", Float) = 0.45
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
                half _AmbientStrength;
                float4 _WindDirection;
                half _WindStrength;
                half _WindSpeed;
                half _WindFlutterStrength;
                half _WindFlutterSpeed;
                half _WindGustScale;
                half _WindHeightMin;
                half _WindHeightMax;
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
                half3 normalWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 ApplyFlowerWind(float3 positionWS, float3 positionOS, float2 uv)
            {
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                float heightMask = saturate((positionOS.y - _WindHeightMin) / max(_WindHeightMax - _WindHeightMin, 0.0001));
                heightMask = heightMask * heightMask;

                float instancePhase = Hash12(floor(positionWS.xz * 0.45)) * 6.2831853;
                float spatialPhase = dot(positionWS.xz, windDir.yx * float2(0.86, -0.62));
                float gustPhase = dot(positionWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed + instancePhase;
                float gust = sin(gustPhase + spatialPhase * 0.35);
                float flutter = sin(_Time.y * _WindFlutterSpeed + spatialPhase * 2.7 + instancePhase * 1.41);

                float bend = gust * _WindStrength * heightMask;
                float petalMask = saturate(uv.y) * heightMask;
                float petalFlutter = flutter * _WindFlutterStrength * petalMask;

                float3 offset = float3(windDir.x, 0.0, windDir.y) * (bend + petalFlutter);
                offset.y = -abs(bend) * 0.12 + petalFlutter * 0.16;
                return positionWS + offset;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                positionWS = ApplyFlowerWind(positionWS, IN.positionOS.xyz, IN.uv);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.baseUV = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.maskUV = TRANSFORM_TEX(IN.uv, _MaskMap);
                OUT.normalWS = half3(0.0h, 1.0h, 0.0h);

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

                half3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = max(SampleSH(normalWS), _AmbientStrength.xxx);
                half3 lighting = ambient + mainLight.color * (0.40h + ndotl * 0.60h);

                return half4(color * lighting, baseSample.a);
            }
            ENDHLSL
        }
    }
}
