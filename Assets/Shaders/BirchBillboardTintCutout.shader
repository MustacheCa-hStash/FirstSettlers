Shader "Custom/BirchBillboardSimpleLitCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Birch Billboard Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base Tint", Color) = (1, 1, 1, 1)
        _SummerLeafColor("Summer Leaf Color", Color) = (0.18, 0.46, 0.14, 1.0)
        _YoungLeafColor("Young Leaf Color", Color) = (0.46, 0.66, 0.20, 1.0)
        _AutumnGoldColor("Autumn Gold Color", Color) = (1.0, 0.68, 0.16, 1.0)
        _AutumnOchreColor("Autumn Ochre Color", Color) = (0.78, 0.46, 0.10, 1.0)
        _LeafShadowColor("Leaf Shadow Color", Color) = (0.055, 0.18, 0.045, 1.0)
        _SeasonAutumnAmount("Season Autumn Amount", Range(0, 1)) = 0.0
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        _LeafWhiteThreshold("Leaf White Threshold", Range(0, 1)) = 0.42
        _LeafWhiteSoftness("Leaf White Softness", Range(0.001, 0.35)) = 0.14
        _CanopyTintStart("Canopy Tint Start", Range(0, 1)) = 0.13
        _CanopyTintFade("Canopy Tint Fade", Range(0.001, 0.5)) = 0.10
        _TrunkProtectCenter("Trunk Protect Center X", Range(0, 1)) = 0.50
        _TrunkProtectWidth("Trunk Protect Width", Range(0.01, 0.5)) = 0.040
        _TrunkProtectEnd("Trunk Protect End Y", Range(0, 1)) = 0.45
        _TrunkProtectFade("Trunk Protect Fade", Range(0.001, 0.5)) = 0.08
        _LeafTintStrength("Leaf Tint Strength", Range(0, 1)) = 1.0
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.42
        _BillboardTintCompression("Billboard Tint Compression", Range(0, 1)) = 0.20
        _InteriorShadeStrength("Interior Canopy Shade", Range(0, 1)) = 0.04
        _LowerShadeStrength("Lower Canopy Shade", Range(0, 1)) = 0.06
        _Brightness("Brightness", Range(0.25, 2)) = 1.0
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.56
        _LightWrap("Billboard Light Softness", Range(0, 1)) = 0.72
        _Smoothness("Smoothness", Range(0, 1)) = 0.06
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.02
        [Toggle] _ForceBillboardFacing("Force Billboard Facing", Float) = 0
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Sway Strength", Range(0, 1)) = 0.07
        _WindSpeed("Wind Sway Speed", Range(0, 8)) = 1.15
        _WindFlutterStrength("Wind Edge Flutter Strength", Range(0, 1)) = 0.018
        _WindFlutterSpeed("Wind Edge Flutter Speed", Range(0, 16)) = 3.8
        _WindGustScale("Wind Gust Scale", Range(0.01, 2)) = 0.16
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
            Name "ForwardBirchBillboard"
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
                half4 _BaseColor;
                half4 _SummerLeafColor;
                half4 _YoungLeafColor;
                half4 _AutumnGoldColor;
                half4 _AutumnOchreColor;
                half4 _LeafShadowColor;
                half _SeasonAutumnAmount;
                half _Cutoff;
                half _LeafWhiteThreshold;
                half _LeafWhiteSoftness;
                half _CanopyTintStart;
                half _CanopyTintFade;
                half _TrunkProtectCenter;
                half _TrunkProtectWidth;
                half _TrunkProtectEnd;
                half _TrunkProtectFade;
                half _LeafTintStrength;
                half _ColorVariationStrength;
                half _BillboardTintCompression;
                half _InteriorShadeStrength;
                half _LowerShadeStrength;
                half _Brightness;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
                half _ForceBillboardFacing;
                float4 _WindDirection;
                half _WindStrength;
                half _WindSpeed;
                half _WindFlutterStrength;
                half _WindFlutterSpeed;
                half _WindGustScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 instanceOriginWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash12(float2 p)
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

            half3 EvaluateBirchLeafColor(float2 uv, float3 instanceOriginWS)
            {
                half treeNoise = Hash12(floor(instanceOriginWS.xz * 0.29h));
                half branchNoise = ValueNoise(uv * 7.5h + instanceOriginWS.xz * 0.18h);
                half fineNoise = ValueNoise(uv * 22.0h + instanceOriginWS.xz * 0.47h);

                half youngMix = smoothstep(0.58h, 0.96h, branchNoise + treeNoise * 0.10h) * _ColorVariationStrength * (1.0h - _SeasonAutumnAmount);
                half goldMix = smoothstep(0.18h, 0.90h, branchNoise + fineNoise * 0.20h) * _SeasonAutumnAmount;
                half ochreMix = smoothstep(0.74h, 0.98h, fineNoise + branchNoise * 0.16h) * _SeasonAutumnAmount * _ColorVariationStrength;

                half3 leafColor = lerp(_SummerLeafColor.rgb, _YoungLeafColor.rgb, youngMix);
                leafColor = lerp(leafColor, _AutumnGoldColor.rgb, goldMix);
                leafColor = lerp(leafColor, _AutumnOchreColor.rgb, ochreMix);
                return leafColor;
            }

            float3 ApplyBillboardWind(float3 positionWS, float3 instanceOriginWS, float2 uv)
            {
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                float heightMask = smoothstep(0.0, 1.0, saturate(uv.y));
                heightMask *= heightMask;

                float instancePhase = Hash12(floor(instanceOriginWS.xz * 0.37)) * 6.2831853;
                float gustPhase = dot(instanceOriginWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed + instancePhase;
                float gust = sin(gustPhase);

                float edgeMask = abs(uv.x - 0.5) * 2.0;
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

                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float3 instanceOriginWS = mul(objectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;

                float3 scaleYVector = float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21);
                float3 scaleZVector = float3(objectToWorld._m02, objectToWorld._m12, objectToWorld._m22);
                float scaleY = length(scaleYVector);
                float scaleZ = length(scaleZVector);

                float3 upWS = float3(0.0, 1.0, 0.0);
                float3 forwardWS = _WorldSpaceCameraPos.xyz - instanceOriginWS;
                forwardWS.y = 0.0;

                float forwardLengthSqr = dot(forwardWS, forwardWS);
                forwardWS = forwardLengthSqr > 0.0001
                    ? forwardWS * rsqrt(forwardLengthSqr)
                    : float3(0.0, 0.0, 1.0);

                float3 rightWS = normalize(cross(forwardWS, upWS));

                float3 billboardPositionWS =
                    instanceOriginWS +
                    rightWS * (IN.positionOS.z * scaleZ) +
                    upWS * (IN.positionOS.y * scaleY);

                float3 normalPositionWS = TransformObjectToWorld(IN.positionOS.xyz);

#if defined(UNITY_INSTANCING_ENABLED)
                float useBillboardFacing = 1.0;
#else
                float useBillboardFacing = step(0.5, _ForceBillboardFacing);
#endif
                float3 finalPositionWS = lerp(normalPositionWS, billboardPositionWS, useBillboardFacing);
                finalPositionWS = ApplyBillboardWind(finalPositionWS, instanceOriginWS, IN.uv);
                half3 billboardNormalWS = normalize(_WorldSpaceCameraPos.xyz - instanceOriginWS);
                billboardNormalWS.y *= 0.25h;
                billboardNormalWS = normalize(lerp(billboardNormalWS, upWS, _LightWrap * 0.35h));

                OUT.positionCS = TransformWorldToHClip(finalPositionWS);
                OUT.instanceOriginWS = instanceOriginWS;
                OUT.positionWS = finalPositionWS;
                OUT.normalWS = billboardNormalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = TransformWorldToShadowCoord(finalPositionWS);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(baseSample.a - _Cutoff);

                half lowChannel = min(baseSample.r, min(baseSample.g, baseSample.b));
                half whiteMask = smoothstep(
                    _LeafWhiteThreshold,
                    saturate(_LeafWhiteThreshold + _LeafWhiteSoftness),
                    lowChannel);
                half canopyMask = smoothstep(
                    _CanopyTintStart,
                    saturate(_CanopyTintStart + _CanopyTintFade),
                    IN.uv.y);
                half trunkDistance = abs(IN.uv.x - _TrunkProtectCenter);
                half trunkCenterMask = 1.0h - smoothstep(_TrunkProtectWidth, _TrunkProtectWidth * 1.65h, trunkDistance);
                half trunkHeightMask = 1.0h - smoothstep(
                    _TrunkProtectEnd,
                    saturate(_TrunkProtectEnd + _TrunkProtectFade),
                    IN.uv.y);
                half trunkProtect = trunkCenterMask * trunkHeightMask;

                half leafMask = saturate(whiteMask * canopyMask * (1.0h - trunkProtect));
                half3 leafColor = EvaluateBirchLeafColor(IN.uv, IN.instanceOriginWS);
                half3 averageLeafColor = lerp(_SummerLeafColor.rgb, _AutumnGoldColor.rgb, saturate(_SeasonAutumnAmount));
                leafColor = lerp(leafColor, averageLeafColor, saturate(_BillboardTintCompression));

                half2 centeredUv = IN.uv * 2.0h - 1.0h;
                half interiorMask = 1.0h - saturate(length(centeredUv * half2(0.92h, 1.18h)));
                half lowerMask = saturate(1.0h - IN.uv.y);
                half shadeMask = saturate(interiorMask * _InteriorShadeStrength + lowerMask * _LowerShadeStrength);
                leafColor = lerp(leafColor, _LeafShadowColor.rgb, shadeMask);

                half3 color = lerp(baseSample.rgb, leafColor, leafMask * _LeafTintStrength);
                half alpha = baseSample.a * _BaseColor.a;
                InputData inputData = InitializeTreeSimpleLitInputData(IN.positionWS, IN.normalWS, IN.positionCS, IN.shadowCoord, _AmbientStrength);
                SurfaceData surfaceData = InitializeTreeSimpleLitSurfaceData(saturate(color * _Brightness), alpha, _Smoothness, _SpecularStrength);
                half4 litColor = UniversalFragmentBlinnPhong(inputData, surfaceData);
                return half4(saturate(litColor.rgb), alpha);
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
                half4 _BaseColor;
                half4 _SummerLeafColor;
                half4 _YoungLeafColor;
                half4 _AutumnGoldColor;
                half4 _AutumnOchreColor;
                half4 _LeafShadowColor;
                half _SeasonAutumnAmount;
                half _Cutoff;
                half _LeafWhiteThreshold;
                half _LeafWhiteSoftness;
                half _CanopyTintStart;
                half _CanopyTintFade;
                half _TrunkProtectCenter;
                half _TrunkProtectWidth;
                half _TrunkProtectEnd;
                half _TrunkProtectFade;
                half _LeafTintStrength;
                half _ColorVariationStrength;
                half _BillboardTintCompression;
                half _InteriorShadeStrength;
                half _LowerShadeStrength;
                half _Brightness;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
                half _ForceBillboardFacing;
                float4 _WindDirection;
                half _WindStrength;
                half _WindSpeed;
                half _WindFlutterStrength;
                half _WindFlutterSpeed;
                half _WindGustScale;
            CBUFFER_END

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

            float3 ApplyBillboardWind(float3 positionWS, float3 instanceOriginWS, float2 uv)
            {
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                float heightMask = smoothstep(0.0, 1.0, saturate(uv.y));
                heightMask *= heightMask;

                float instancePhase = Hash12(floor(instanceOriginWS.xz * 0.37)) * 6.2831853;
                float gustPhase = dot(instanceOriginWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed + instancePhase;
                float gust = sin(gustPhase);
                float edgeMask = abs(uv.x - 0.5) * 2.0;
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

                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float3 instanceOriginWS = mul(objectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;
                float3 scaleYVector = float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21);
                float3 scaleZVector = float3(objectToWorld._m02, objectToWorld._m12, objectToWorld._m22);
                float scaleY = length(scaleYVector);
                float scaleZ = length(scaleZVector);

                float3 upWS = float3(0.0, 1.0, 0.0);
                float3 forwardWS = _WorldSpaceCameraPos.xyz - instanceOriginWS;
                forwardWS.y = 0.0;
                float forwardLengthSqr = dot(forwardWS, forwardWS);
                forwardWS = forwardLengthSqr > 0.0001
                    ? forwardWS * rsqrt(forwardLengthSqr)
                    : float3(0.0, 0.0, 1.0);

                float3 rightWS = normalize(cross(forwardWS, upWS));
                float3 billboardPositionWS =
                    instanceOriginWS +
                    rightWS * (IN.positionOS.z * scaleZ) +
                    upWS * (IN.positionOS.y * scaleY);
                float3 normalPositionWS = TransformObjectToWorld(IN.positionOS.xyz);

                #if defined(UNITY_INSTANCING_ENABLED)
                    float useBillboardFacing = 1.0;
                #else
                    float useBillboardFacing = step(0.5, _ForceBillboardFacing);
                #endif

                float3 finalPositionWS = lerp(normalPositionWS, billboardPositionWS, useBillboardFacing);
                finalPositionWS = ApplyBillboardWind(finalPositionWS, instanceOriginWS, IN.uv);
                half3 billboardNormalWS = normalize(_WorldSpaceCameraPos.xyz - instanceOriginWS);
                billboardNormalWS.y *= 0.25h;
                billboardNormalWS = normalize(lerp(billboardNormalWS, upWS, _LightWrap * 0.35h));

                OUT.positionCS = TransformWorldToTreeShadowClip(finalPositionWS, billboardNormalWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
