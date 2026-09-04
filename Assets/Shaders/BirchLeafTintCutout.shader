Shader "Custom/BirchLeafSimpleLitCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Birch Leaf Atlas / Alpha", 2D) = "white" {}
        [MainColor] _SummerLeafColor("Summer Leaf Color", Color) = (0.18, 0.46, 0.14, 1.0)
        _YoungLeafColor("Young Leaf Color", Color) = (0.46, 0.66, 0.20, 1.0)
        _AutumnGoldColor("Autumn Gold Color", Color) = (1.0, 0.68, 0.16, 1.0)
        _AutumnOchreColor("Autumn Ochre Color", Color) = (0.78, 0.46, 0.10, 1.0)
        _LeafShadowColor("Leaf Shadow Color", Color) = (0.055, 0.18, 0.045, 1.0)
        _SeasonAutumnAmount("Season Autumn Amount", Range(0, 1)) = 0.0
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.42
        [Toggle] _AlphaCutoutShadows("Alpha Cutout Shadows", Float) = 1
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.45
        _LeafContrast("Leaf Detail Contrast", Range(0, 1)) = 0.26
        _VerticalGradientStrength("Lower Leaf Shade", Range(0, 1)) = 0.22
        _CardVariationStrength("Card Variation Strength", Range(0, 1)) = 0.16
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.46
        _LightWrap("Leaf Light Wrap", Range(0, 1)) = 0.62
        _Smoothness("Smoothness", Range(0, 1)) = 0.08
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.03
        [Toggle] _UseVertexColor("Use Vertex Color", Float) = 0
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Canopy Sway Strength", Range(0, 1)) = 0.09
        _WindSpeed("Wind Canopy Sway Speed", Range(0, 8)) = 1.25
        _WindFlutterStrength("Wind Leaf Flutter Strength", Range(0, 1)) = 0.05
        _WindFlutterSpeed("Wind Leaf Flutter Speed", Range(0, 16)) = 5.0
        _WindGustScale("Wind Gust Scale", Range(0.01, 2)) = 0.18
        _WindHeightMin("Wind Height Min", Float) = 0
        _WindHeightMax("Wind Height Max", Float) = 7
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
            Name "ForwardBirchLeaf"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/TreeSimpleLitCommon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _SummerLeafColor;
                half4 _YoungLeafColor;
                half4 _AutumnGoldColor;
                half4 _AutumnOchreColor;
                half4 _LeafShadowColor;
                half _SeasonAutumnAmount;
                half _Cutoff;
                half _ColorVariationStrength;
                half _LeafContrast;
                half _VerticalGradientStrength;
                half _CardVariationStrength;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
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
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half Hash12(float2 p)
            {
                half3 p3 = frac(half3(p.xyx) * half3(0.1031h, 0.1030h, 0.0973h));
                p3 += dot(p3, p3.yzx + 33.33h);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Hash12Float(float2 p)
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

                float cardPhase = Hash12Float(floor(positionWS.xz * 0.68) + floor(uv * 6.0)) * 6.2831853;
                float flutter = sin(_Time.y * _WindFlutterSpeed + cardPhase + spatialPhase * 2.25);

                float swayAmount = gust * _WindStrength * heightMask;
                float flutterAmount = flutter * _WindFlutterStrength * saturate(uv.y + 0.25);

                float3 offset = float3(windDir.x, 0.0, windDir.y) * (swayAmount + flutterAmount);
                offset.y = flutter * _WindFlutterStrength * 0.20 * heightMask;
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
                OUT.shadowCoord = TransformWorldToShadowCoord(positionWS);
                OUT.color = IN.color;

                return OUT;
            }

            half3 EvaluateBirchLeafColor(float2 uv, float3 positionWS)
            {
                half leafNoise = Hash12(floor(positionWS.xz * 0.44h) + floor(uv * 8.0h));
                half fineNoise = Hash12(floor(positionWS.xz * 1.30h) + floor(uv * 21.0h));

                half youngMix = smoothstep(0.60h, 0.96h, leafNoise) * _ColorVariationStrength * (1.0h - _SeasonAutumnAmount);
                half goldMix = smoothstep(0.20h, 0.90h, leafNoise + fineNoise * 0.22h) * _SeasonAutumnAmount;
                half ochreMix = smoothstep(0.72h, 0.98h, fineNoise + leafNoise * 0.18h) * _SeasonAutumnAmount * _ColorVariationStrength;

                half3 leafColor = lerp(_SummerLeafColor.rgb, _YoungLeafColor.rgb, youngMix);
                leafColor = lerp(leafColor, _AutumnGoldColor.rgb, goldMix);
                leafColor = lerp(leafColor, _AutumnOchreColor.rgb, ochreMix);
                return leafColor;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(atlas.a - _Cutoff);

                half alphaCoverage = saturate((atlas.a - _Cutoff) / max(1.0h - _Cutoff, 0.001h));
                half atlasLuma = dot(atlas.rgb, half3(0.299h, 0.587h, 0.114h));

                half3 leafColor = EvaluateBirchLeafColor(IN.uv, IN.positionWS);
                half bottomShade = saturate((1.0h - IN.uv.y) * _VerticalGradientStrength);
                leafColor = lerp(leafColor, _LeafShadowColor.rgb, bottomShade);

                half leafDetail = lerp(1.0h - _LeafContrast, 1.0h + _LeafContrast, alphaCoverage * atlasLuma);
                half cardNoise = Hash12(floor(IN.positionWS.xz * 0.70h) + floor(IN.uv * 5.0h));
                half cardVariation = lerp(1.0h - _CardVariationStrength, 1.0h + _CardVariationStrength, cardNoise);
                leafColor *= leafDetail * cardVariation;
                leafColor *= lerp(half3(1.0h, 1.0h, 1.0h), IN.color.rgb, saturate(_UseVertexColor));

                InputData inputData = InitializeTreeSimpleLitInputData(IN.positionWS, IN.normalWS, IN.positionCS, IN.shadowCoord, _AmbientStrength);
                inputData.normalWS = normalize(lerp(inputData.normalWS, half3(0.0h, 1.0h, 0.0h), _LightWrap * 0.22h));

                SurfaceData surfaceData = InitializeTreeSimpleLitSurfaceData(leafColor, atlas.a, _Smoothness, _SpecularStrength);
                half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
                return half4(saturate(color.rgb), atlas.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Assets/Shaders/TreeShadowCasterCommon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _SummerLeafColor;
                half4 _YoungLeafColor;
                half4 _AutumnGoldColor;
                half4 _AutumnOchreColor;
                half4 _LeafShadowColor;
                half _SeasonAutumnAmount;
                half _Cutoff;
                half _ColorVariationStrength;
                half _LeafContrast;
                half _VerticalGradientStrength;
                half _CardVariationStrength;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash12Float(float2 p)
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

                float cardPhase = Hash12Float(floor(positionWS.xz * 0.68) + floor(uv * 6.0)) * 6.2831853;
                float flutter = sin(_Time.y * _WindFlutterSpeed + cardPhase + spatialPhase * 2.25);

                float swayAmount = gust * _WindStrength * heightMask;
                float flutterAmount = flutter * _WindFlutterStrength * saturate(uv.y + 0.25);

                float3 offset = float3(windDir.x, 0.0, windDir.y) * (swayAmount + flutterAmount);
                offset.y = flutter * _WindFlutterStrength * 0.20 * heightMask;
                return positionWS + offset;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                positionWS = ApplyLeafWind(positionWS, IN.positionOS.xyz, IN.uv);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                OUT.positionCS = TransformWorldToTreeShadowClip(positionWS, normalWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                if (_AlphaCutoutShadows > 0.5h)
                {
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a;
                    clip(alpha - _Cutoff);
                }
                return 0;
            }
            ENDHLSL
        }
    }
}
