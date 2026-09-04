Shader "Custom/OakLeafSimpleLitCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Oak Leaf Atlas / Alpha", 2D) = "white" {}
        [MainColor] _SummerLeafColor("Summer Leaf Color", Color) = (0.18, 0.38, 0.12, 1.0)
        _YoungLeafColor("Young Leaf Color", Color) = (0.36, 0.52, 0.16, 1.0)
        _AutumnRussetColor("Autumn Russet Color", Color) = (0.48, 0.18, 0.06, 1.0)
        _AutumnOchreColor("Autumn Ochre Color", Color) = (0.74, 0.42, 0.12, 1.0)
        _AutumnGoldColor("Autumn Gold Color", Color) = (0.92, 0.58, 0.18, 1.0)
        _LeafShadowColor("Leaf Shadow Color", Color) = (0.055, 0.14, 0.035, 1.0)
        _TreeLeafTint("Per Tree Leaf Tint", Color) = (1, 1, 1, 1)
        _TreeTintStrength("Per Tree Tint Strength", Range(0, 1)) = 0.35
        _SeasonAutumnAmount("Season Autumn Amount", Range(0, 1)) = 0.0
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.42
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.42
        _LeafContrast("Leaf Card Contrast", Range(0, 1)) = 0.30
        _VerticalGradientStrength("Canopy Shade Variation", Range(0, 1)) = 0.08
        _CardVariationStrength("Card Variation Strength", Range(0, 1)) = 0.18
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.44
        _LightWrap("Leaf Light Wrap", Range(0, 1)) = 0.62
        _Smoothness("Smoothness", Range(0, 1)) = 0.08
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.03
        [Toggle] _UseVertexColor("Use Vertex Color", Float) = 0
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Canopy Sway Strength", Range(0, 1)) = 0.10
        _WindSpeed("Wind Canopy Sway Speed", Range(0, 8)) = 1.15
        _WindFlutterStrength("Wind Leaf Flutter Strength", Range(0, 1)) = 0.055
        _WindFlutterSpeed("Wind Leaf Flutter Speed", Range(0, 16)) = 4.8
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
            Name "ForwardOakLeaf"
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
                half4 _AutumnRussetColor;
                half4 _AutumnOchreColor;
                half4 _AutumnGoldColor;
                half4 _LeafShadowColor;
                half4 _TreeLeafTint;
                half _TreeTintStrength;
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

            float3 ApplyLeafWind(float3 positionWS, float3 positionOS, float2 uv)
            {
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                float heightMask = saturate((positionOS.y - _WindHeightMin) / max(_WindHeightMax - _WindHeightMin, 0.0001));
                heightMask = smoothstep(0.0, 1.0, heightMask);

                float spatialPhase = dot(positionWS.xz, windDir.yx * float2(0.73, -0.61));
                float gustPhase = dot(positionWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed;
                float gust = sin(gustPhase + spatialPhase * 0.25);

                float cardPhase = Hash12Float(floor(positionWS.xz * 0.62) + floor(uv * 6.0)) * 6.2831853;
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

            half3 EvaluateOakLeafColor(float2 uv, float3 positionWS)
            {
                half leafNoise = ValueNoise(positionWS.xz * 0.36 + uv * 1.7);
                half fineNoise = ValueNoise(positionWS.xz * 0.95 + uv * 3.1 + 19.37);
                leafNoise = saturate(leafNoise * 0.62h + fineNoise * 0.38h);

                half variationStrength = saturate(_ColorVariationStrength * 1.25h);
                half youngMix = smoothstep(0.54h, 0.96h, leafNoise) * variationStrength * (1.0h - _SeasonAutumnAmount);
                half ochreMix = smoothstep(0.16h, 0.84h, leafNoise + fineNoise * 0.28h);
                half goldMix = smoothstep(0.58h, 0.98h, fineNoise) * variationStrength;
                half russetMix = smoothstep(0.46h, 0.98h, leafNoise) * (1.0h - goldMix * 0.40h);

                half3 summerColor = lerp(_SummerLeafColor.rgb, _YoungLeafColor.rgb, youngMix);
                half3 autumnColor = lerp(_AutumnRussetColor.rgb, _AutumnOchreColor.rgb, ochreMix);
                autumnColor = lerp(autumnColor, _AutumnGoldColor.rgb, goldMix);
                autumnColor = lerp(autumnColor, _LeafShadowColor.rgb * 1.65h, russetMix * variationStrength * 0.20h);

                half3 leafColor = lerp(summerColor, autumnColor, saturate(_SeasonAutumnAmount));
                leafColor = lerp(leafColor, leafColor * _TreeLeafTint.rgb, saturate(_TreeTintStrength));
                return leafColor;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(atlas.a - _Cutoff);

                half alphaCoverage = saturate((atlas.a - _Cutoff) / max(1.0h - _Cutoff, 0.001h));
                half atlasLuma = dot(atlas.rgb, half3(0.299h, 0.587h, 0.114h));

                half3 leafColor = EvaluateOakLeafColor(IN.uv, IN.positionWS);
                half canopyShadeNoise = ValueNoise(IN.positionWS.xz * 0.24 + IN.positionWS.y * 0.08);
                half bottomShade = saturate((1.0h - canopyShadeNoise) * _VerticalGradientStrength);
                leafColor = lerp(leafColor, _LeafShadowColor.rgb, bottomShade);

                half leafDetail = lerp(1.0h - _LeafContrast, 1.0h + _LeafContrast, alphaCoverage * atlasLuma);
                half cardNoise = ValueNoise(IN.positionWS.xz * 0.52 + IN.uv * 2.0 + 7.13);
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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _SummerLeafColor;
                half4 _YoungLeafColor;
                half4 _AutumnRussetColor;
                half4 _AutumnOchreColor;
                half4 _AutumnGoldColor;
                half4 _LeafShadowColor;
                half4 _TreeLeafTint;
                half _TreeTintStrength;
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

            float3 _LightDirection;
            float3 _LightPosition;

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

                float cardPhase = Hash12Float(floor(positionWS.xz * 0.62) + floor(uv * 6.0)) * 6.2831853;
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

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                positionWS = ApplyShadowBias(positionWS, normalWS, lightDirectionWS);
                OUT.positionCS = TransformWorldToHClip(positionWS);

                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
