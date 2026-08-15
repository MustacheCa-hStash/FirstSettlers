Shader "Custom/SpruceLeafTintCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("White Leaf Atlas / Alpha", 2D) = "white" {}
        [MainColor] _LeafColor("Base Needle Color", Color) = (0.18431373, 0.35294118, 0.21176471, 1.0)
        _CoolNeedleColor("Cool Blue-Green Color", Color) = (0.13, 0.25, 0.22, 1.0)
        _DeepNeedleColor("Deep Shadow Green", Color) = (0.055, 0.16, 0.09, 1.0)
        _TipColor("Fresh Tip Color", Color) = (0.30, 0.45, 0.22, 1.0)
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.42
        _NeedleContrast("Needle Contrast", Range(0, 1)) = 0.22
        _TipStrength("Tip Color Strength", Range(0, 1)) = 0.10
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.35
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.35
        _LightWrap("Leaf Light Wrap", Range(0, 1)) = 0.45
        [Toggle] _UseVertexColor("Use Vertex Color", Float) = 0
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Canopy Sway Strength", Range(0, 1)) = 0.08
        _WindSpeed("Wind Canopy Sway Speed", Range(0, 8)) = 1.35
        _WindFlutterStrength("Wind Needle Flutter Strength", Range(0, 1)) = 0.035
        _WindFlutterSpeed("Wind Needle Flutter Speed", Range(0, 16)) = 5.5
        _WindGustScale("Wind Gust Scale", Range(0.01, 2)) = 0.22
        _WindHeightMin("Wind Height Min", Float) = 0
        _WindHeightMax("Wind Height Max", Float) = 8
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
                half4 _CoolNeedleColor;
                half4 _DeepNeedleColor;
                half4 _TipColor;
                half _ColorVariationStrength;
                half _NeedleContrast;
                half _TipStrength;
                half _Cutoff;
                half _AmbientStrength;
                half _LightWrap;
                half _UseVertexColor;
                float4 _WindDirection;
                half _WindStrength;
                half _WindSpeed;
                half _WindFlutterStrength;
                half _WindFlutterSpeed;
                half _WindGustScale;
                half _WindHeightMin;
                half _WindHeightMax;
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
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 ApplyLeafWind(float3 positionWS, float3 positionOS, float2 uv)
            {
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                float heightMask = saturate((positionOS.y - _WindHeightMin) / max(_WindHeightMax - _WindHeightMin, 0.0001));
                heightMask = smoothstep(0.0, 1.0, heightMask);

                float spatialPhase = dot(positionWS.xz, windDir.yx * float2(0.73, -0.61));
                float gustPhase = dot(positionWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed;
                float gust = sin(gustPhase + spatialPhase * 0.25);

                float cardPhase = Hash12(floor(positionWS.xz * 0.8) + floor(uv * 8.0)) * 6.2831853;
                float flutter = sin(_Time.y * _WindFlutterSpeed + cardPhase + spatialPhase * 2.1);

                float swayAmount = gust * _WindStrength * heightMask;
                float flutterAmount = flutter * _WindFlutterStrength * saturate(uv.y + 0.2);

                float3 offset = float3(windDir.x, 0.0, windDir.y) * (swayAmount + flutterAmount);
                offset.y = flutter * _WindFlutterStrength * 0.18 * heightMask;
                return positionWS + offset;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                positionWS = ApplyLeafWind(positionWS, IN.positionOS.xyz, IN.uv);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = normalInputs.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = IN.color;

                return OUT;
            }

            half3 EvaluateSpruceNeedleColor(float2 uv, float3 positionWS)
            {
                half branchNoise = Hash12(floor(positionWS.xz * 0.34h) + floor(uv * 4.0h));
                half fineNoise = Hash12(floor(positionWS.xz * 1.15h) + floor(uv * 17.0h));

                half coolMix = smoothstep(0.18h, 0.78h, branchNoise) * _ColorVariationStrength;
                half deepMix = smoothstep(0.74h, 0.98h, 1.0h - fineNoise + branchNoise * 0.18h) * _ColorVariationStrength * 0.65h;
                half tipMix = smoothstep(0.62h, 0.96h, uv.y + fineNoise * 0.18h) * _TipStrength;

                half3 needleColor = lerp(_LeafColor.rgb, _CoolNeedleColor.rgb, coolMix);
                needleColor = lerp(needleColor, _DeepNeedleColor.rgb, deepMix);
                needleColor = lerp(needleColor, _TipColor.rgb, tipMix);

                half contrastNoise = branchNoise * 0.62h + fineNoise * 0.38h;
                needleColor *= lerp(1.0h - _NeedleContrast, 1.0h + _NeedleContrast, contrastNoise);
                return needleColor;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(atlas.a - _Cutoff);

                half3 leafColor = EvaluateSpruceNeedleColor(IN.uv, IN.positionWS);
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
