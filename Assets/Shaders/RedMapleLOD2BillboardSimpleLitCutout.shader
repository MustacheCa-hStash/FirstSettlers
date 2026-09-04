Shader "Custom/RedMapleLOD2BillboardSimpleLitCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo Alpha Atlas", 2D) = "white" {}
        _LeafMask("Leaf Mask Atlas", 2D) = "black" {}
        [MainColor] _BaseColor("Overall Tint", Color) = (1, 1, 1, 1)
        _BarkTint("Bark Tint", Color) = (1, 1, 1, 1)
        _TreeLeafTint("Per Tree Leaf Tint", Color) = (0.88, 0.06, 0.035, 1)
        _BillboardTintAverageColor("Billboard Average Leaf Tint", Color) = (0.84, 0.08, 0.045, 1)
        _BillboardTintCompression("Billboard Tint Compression", Range(0, 1)) = 0.0
        _SummerLeafColor("Summer Leaf Color", Color) = (0.18, 0.42, 0.12, 1.0)
        _AutumnRedColor("Autumn Scarlet Color", Color) = (0.88, 0.06, 0.035, 1.0)
        _AutumnCrimsonColor("Autumn Crimson Color", Color) = (0.48, 0.025, 0.04, 1.0)
        _AutumnOrangeColor("Autumn Orange Color", Color) = (1.0, 0.25, 0.055, 1.0)
        _LeafShadowColor("Leaf Shadow Color", Color) = (0.16, 0.018, 0.018, 1.0)
        _SeasonAutumnAmount("Season Autumn Amount", Range(0, 1)) = 1.0
        _TreeTintStrength("Per Tree Tint Strength", Range(0, 1)) = 0.92
        _LeafTintStrength("Leaf Tint Strength", Range(0, 1)) = 1.0
        _LeafSaturation("Billboard Leaf Saturation", Range(0, 1.25)) = 0.82
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.16
        _LeafContrast("Leaf Detail Contrast", Range(0, 1)) = 0.18
        _LowerShadeStrength("Lower Canopy Shade", Range(0, 1)) = 0.20
        _InteriorShadeStrength("Interior Canopy Shade", Range(0, 1)) = 0.16
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.38
        [Toggle] _AlphaCutoutShadows("Alpha Cutout Shadows", Float) = 1
        _LeafMaskThreshold("Leaf Mask Threshold", Range(0, 1)) = 0.18
        _LeafMaskSoftness("Leaf Mask Softness", Range(0.001, 0.25)) = 0.05
        _Brightness("Brightness", Range(0.25, 2)) = 1.0
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.58
        _LightWrap("Billboard Light Softness", Range(0, 1)) = 0.64
        _Smoothness("Smoothness", Range(0, 1)) = 0.05
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.015
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Sway Strength", Range(0, 1)) = 0.045
        _WindSpeed("Wind Sway Speed", Range(0, 8)) = 1.15
        _WindFlutterStrength("Wind Edge Flutter Strength", Range(0, 1)) = 0.014
        _WindFlutterSpeed("Wind Edge Flutter Speed", Range(0, 16)) = 3.4
        _WindGustScale("Wind Gust Scale", Range(0.01, 2)) = 0.14
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
        Cull Back
        ZWrite On
        ZTest LEqual
        AlphaToMask On

        Pass
        {
            Name "ForwardRedMapleLOD2Billboard"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #include "Assets/Shaders/TreeSimpleLitCommon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_LeafMask);
            SAMPLER(sampler_LeafMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _LeafMask_ST;
                half4 _BaseColor;
                half4 _BarkTint;
                half4 _BillboardTintAverageColor;
                half _BillboardTintCompression;
                half4 _SummerLeafColor;
                half4 _AutumnRedColor;
                half4 _AutumnCrimsonColor;
                half4 _AutumnOrangeColor;
                half4 _LeafShadowColor;
                half _SeasonAutumnAmount;
                half _TreeTintStrength;
                half _LeafTintStrength;
                half _LeafSaturation;
                half _ColorVariationStrength;
                half _LeafContrast;
                half _LowerShadeStrength;
                half _InteriorShadeStrength;
                half _Cutoff;
                half _AlphaCutoutShadows;
                half _LeafMaskThreshold;
                half _LeafMaskSoftness;
                half _Brightness;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
                float4 _WindDirection;
                half _WindStrength;
                half _WindSpeed;
                half _WindFlutterStrength;
                half _WindFlutterSpeed;
                half _WindGustScale;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(TreeInstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TreeLeafTint)
            UNITY_INSTANCING_BUFFER_END(TreeInstanceProperties)

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
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float2 viewUv : TEXCOORD3;
                float3 instanceOriginWS : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float SmoothNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash12(i);
                float b = Hash12(i + float2(1.0, 0.0));
                float c = Hash12(i + float2(0.0, 1.0));
                float d = Hash12(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float LayeredSmoothNoise(float2 p)
            {
                float broad = SmoothNoise(p);
                float fine = SmoothNoise(p * 2.73 + 17.31);
                return broad * 0.72 + fine * 0.28;
            }

            float2 GetAtlasViewUv(float2 atlasUv)
            {
                return frac(min(atlasUv, 0.9999) * 2.0);
            }

            half3 EvaluateAutumnColor(float2 viewUv, float3 instanceOriginWS)
            {
                half treeNoise = Hash12(floor(instanceOriginWS.xz * 0.45h));
                float2 variationUv = viewUv * 3.35 + instanceOriginWS.xz * 0.037;
                half leafNoise = LayeredSmoothNoise(variationUv);
                half fineNoise = SmoothNoise(viewUv * 9.5 + instanceOriginWS.xz * 0.071 + 41.7);

                half crimsonMix = smoothstep(0.24h, 0.86h, leafNoise + treeNoise * 0.06h);
                half orangeMix = smoothstep(0.72h, 0.98h, fineNoise + treeNoise * 0.08h) * _ColorVariationStrength;
                half shadowMix = smoothstep(0.82h, 1.0h, leafNoise + fineNoise * 0.12h);

                half3 autumnColor = lerp(_AutumnRedColor.rgb, _AutumnCrimsonColor.rgb, crimsonMix * _ColorVariationStrength);
                autumnColor = lerp(autumnColor, _AutumnOrangeColor.rgb, orangeMix * 0.48h);
                autumnColor = lerp(autumnColor, _LeafShadowColor.rgb * 1.25h, shadowMix * _ColorVariationStrength * 0.14h);
                return autumnColor;
            }

            float3 ApplyBillboardWind(float3 positionWS, float3 instanceOriginWS, float2 viewUv)
            {
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                float heightMask = smoothstep(0.0, 1.0, saturate(viewUv.y));
                heightMask *= heightMask;

                float instancePhase = Hash12(floor(instanceOriginWS.xz * 0.37)) * 6.2831853;
                float gustPhase = dot(instanceOriginWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed + instancePhase;
                float gust = sin(gustPhase);

                float edgeMask = abs(viewUv.x - 0.5) * 2.0;
                float flutter = sin(_Time.y * _WindFlutterSpeed + instancePhase + edgeMask * 2.4);

                float sway = gust * _WindStrength * heightMask;
                float edgeFlutter = flutter * _WindFlutterStrength * heightMask * edgeMask;
                return positionWS + float3(windDir.x, 0.0, windDir.y) * (sway + edgeFlutter);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);
                float3 instanceOriginWS = mul(GetObjectToWorldMatrix(), float4(0.0, 0.0, 0.0, 1.0)).xyz;
                float2 uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                float2 viewUv = GetAtlasViewUv(uv);
                positionWS = ApplyBillboardWind(positionWS, instanceOriginWS, viewUv);

                half3 normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                normalWS = normalize(lerp(normalWS, half3(0.0h, 1.0h, 0.0h), _LightWrap * 0.30h));

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = normalWS;
                OUT.uv = uv;
                OUT.viewUv = viewUv;
                OUT.instanceOriginWS = instanceOriginWS;
                OUT.shadowCoord = TransformWorldToShadowCoord(positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(baseSample.a - _Cutoff);

                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif

                half3 maskRgb = SAMPLE_TEXTURE2D(_LeafMask, sampler_LeafMask, IN.uv).rgb;
                half maskSample = max(maskRgb.r, max(maskRgb.g, maskRgb.b));
                half leafMask = smoothstep(
                    _LeafMaskThreshold,
                    saturate(_LeafMaskThreshold + _LeafMaskSoftness),
                    maskSample);

                half atlasLuma = saturate(dot(baseSample.rgb, half3(0.299h, 0.587h, 0.114h)));
                half leafDetail = lerp(1.0h - _LeafContrast, 1.0h + _LeafContrast, smoothstep(0.18h, 0.88h, atlasLuma));

                half lowerMask = saturate(1.0h - IN.viewUv.y);
                half2 centeredUv = IN.viewUv * 2.0h - 1.0h;
                half interiorMask = 1.0h - saturate(length(centeredUv * half2(0.82h, 1.05h)));
                half shadeMask = saturate(lowerMask * _LowerShadeStrength + interiorMask * _InteriorShadeStrength);

                half3 autumnColor = EvaluateAutumnColor(IN.viewUv, IN.instanceOriginWS);
                half3 leafColor = lerp(_SummerLeafColor.rgb, autumnColor, saturate(_SeasonAutumnAmount));

                half4 instanceTintProp = UNITY_ACCESS_INSTANCED_PROP(TreeInstanceProperties, _TreeLeafTint);
                half directTintAmount = 1.0h - saturate(instanceTintProp.a);
                half3 instanceLeafTint = lerp(leafColor * instanceTintProp.rgb, instanceTintProp.rgb, directTintAmount);
                instanceLeafTint = lerp(instanceLeafTint, _BillboardTintAverageColor.rgb, saturate(_BillboardTintCompression));
                leafColor = lerp(leafColor, instanceLeafTint, saturate(_TreeTintStrength));
                leafColor = lerp(leafColor, _LeafShadowColor.rgb, shadeMask * leafMask);
                leafColor *= leafDetail;

                half3 barkColor = baseSample.rgb * _BarkTint.rgb;
                half3 albedoDetail = lerp(half3(1.0h, 1.0h, 1.0h), baseSample.rgb / max(atlasLuma, 0.08h), 0.14h);
                half3 tintedLeafColor = lerp(baseSample.rgb, leafColor * albedoDetail, saturate(_LeafTintStrength));
                half tintedLeafLuma = dot(tintedLeafColor, half3(0.299h, 0.587h, 0.114h));
                tintedLeafColor = lerp(half3(tintedLeafLuma, tintedLeafLuma, tintedLeafLuma), tintedLeafColor, _LeafSaturation);
                half3 color = lerp(barkColor, tintedLeafColor, leafMask);

                InputData inputData = InitializeTreeSimpleLitInputData(IN.positionWS, IN.normalWS, IN.positionCS, IN.shadowCoord, _AmbientStrength);
                SurfaceData surfaceData = InitializeTreeSimpleLitSurfaceData(saturate(color * _Brightness), baseSample.a, _Smoothness, _SpecularStrength);
                half4 litColor = UniversalFragmentBlinnPhong(inputData, surfaceData);
                return half4(saturate(litColor.rgb), baseSample.a);
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
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _LeafMask_ST;
                half4 _BaseColor;
                half4 _BarkTint;
                half4 _BillboardTintAverageColor;
                half _BillboardTintCompression;
                half4 _SummerLeafColor;
                half4 _AutumnRedColor;
                half4 _AutumnCrimsonColor;
                half4 _AutumnOrangeColor;
                half4 _LeafShadowColor;
                half _SeasonAutumnAmount;
                half _TreeTintStrength;
                half _LeafTintStrength;
                half _LeafSaturation;
                half _ColorVariationStrength;
                half _LeafContrast;
                half _LowerShadeStrength;
                half _InteriorShadeStrength;
                half _Cutoff;
                half _AlphaCutoutShadows;
                half _LeafMaskThreshold;
                half _LeafMaskSoftness;
                half _Brightness;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
                float4 _WindDirection;
                half _WindStrength;
                half _WindSpeed;
                half _WindFlutterStrength;
                half _WindFlutterSpeed;
                half _WindGustScale;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(TreeInstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TreeLeafTint)
            UNITY_INSTANCING_BUFFER_END(TreeInstanceProperties)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 GetAtlasViewUv(float2 atlasUv)
            {
                return frac(min(atlasUv, 0.9999) * 2.0);
            }

            float3 ApplyBillboardWind(float3 positionWS, float3 instanceOriginWS, float2 viewUv)
            {
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                float heightMask = smoothstep(0.0, 1.0, saturate(viewUv.y));
                heightMask *= heightMask;

                float instancePhase = Hash12(floor(instanceOriginWS.xz * 0.37)) * 6.2831853;
                float gustPhase = dot(instanceOriginWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed + instancePhase;
                float gust = sin(gustPhase);

                float edgeMask = abs(viewUv.x - 0.5) * 2.0;
                float flutter = sin(_Time.y * _WindFlutterSpeed + instancePhase + edgeMask * 2.4);

                float sway = gust * _WindStrength * heightMask;
                float edgeFlutter = flutter * _WindFlutterStrength * heightMask * edgeMask;
                return positionWS + float3(windDir.x, 0.0, windDir.y) * (sway + edgeFlutter);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                float3 instanceOriginWS = mul(GetObjectToWorldMatrix(), float4(0.0, 0.0, 0.0, 1.0)).xyz;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                positionWS = ApplyBillboardWind(positionWS, instanceOriginWS, GetAtlasViewUv(OUT.uv));
                OUT.positionCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                if (_AlphaCutoutShadows > 0.5h)
                {
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                }

                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif

                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
