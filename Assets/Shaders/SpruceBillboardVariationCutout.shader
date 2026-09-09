Shader "Custom/SpruceBillboardVariationSimpleLitCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Spruce Billboard Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base Tint", Color) = (1, 1, 1, 1)
        _BaseNeedleColor("Base Needle Color", Color) = (0.18431373, 0.35294118, 0.21176471, 1.0)
        _CoolNeedleColor("Cool Blue-Green Color", Color) = (0.13, 0.25, 0.22, 1)
        _DeepNeedleColor("Deep Shadow Green", Color) = (0.055, 0.16, 0.09, 1)
        _SunNeedleColor("Fresh Tip Color", Color) = (0.30, 0.45, 0.22, 1)
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.52
        _NeedleContrast("Needle Contrast", Range(0, 1)) = 0.34
        _TipStrength("Tip Color Strength", Range(0, 1)) = 0.1
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        [Toggle] _AlphaCutoutShadows("Alpha Cutout Shadows", Float) = 1
        _Brightness("Brightness", Range(0.25, 2)) = 1.0
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.56
        _LightWrap("Billboard Light Softness", Range(0, 1)) = 0.72
        _Smoothness("Smoothness", Range(0, 1)) = 0.06
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.02
        [Toggle] _ForceBillboardFacing("Force Billboard Facing", Float) = 0
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Sway Strength", Range(0, 1)) = 0.055
        _WindSpeed("Wind Sway Speed", Range(0, 8)) = 1.15
        _WindFlutterStrength("Wind Edge Flutter Strength", Range(0, 1)) = 0.012
        _WindFlutterSpeed("Wind Edge Flutter Speed", Range(0, 16)) = 3.2
        _WindGustScale("Wind Gust Scale", Range(0.01, 2)) = 0.14
    }

    SubShader
    {
        Tags
        {
            "DistantTreeIndirect" = "True"
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
            Name "ForwardSpruceBillboard"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma target 4.5 PROCEDURAL_INSTANCING_ON
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupDistantTree
            #pragma multi_compile_local _ SPRUCE_FAR_SIMPLE
            #pragma multi_compile_fog
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
                half4 _BaseNeedleColor;
                half4 _CoolNeedleColor;
                half4 _DeepNeedleColor;
                half4 _SunNeedleColor;
                half _ColorVariationStrength;
                half _TipStrength;
                half _NeedleContrast;
                half _Cutoff;
                half _LeafMaskThreshold;
                half _LeafMaskSoftness;
                half _LeafTintStrength;
                half _LowerShadeStrength;
                half _InteriorShadeStrength;
                half _FauxShadeStrength;
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
                half3 farNeedleColor : TEXCOORD5;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half3 EvaluateSpruceNeedleColor(float2 uv, float3 instanceOriginWS)
            {
                half branchNoise = Hash12(floor(instanceOriginWS.xz * 0.34h) + floor(uv * 4.0h));
                half fineNoise = Hash12(floor(instanceOriginWS.xz * 1.15h) + floor(uv * 17.0h));
                half coolMix = smoothstep(0.18h, 0.78h, branchNoise) * _ColorVariationStrength;
                half deepMix = smoothstep(0.68h, 0.98h, 1.0h - fineNoise + branchNoise * 0.24h) * _ColorVariationStrength * 0.82h;
                half tipMix = smoothstep(0.62h, 0.96h, uv.y + fineNoise * 0.18h) * _TipStrength;
                half3 needleColor = lerp(_BaseNeedleColor.rgb, _CoolNeedleColor.rgb, coolMix);
                needleColor = lerp(needleColor, _DeepNeedleColor.rgb, deepMix);
                needleColor = lerp(needleColor, _SunNeedleColor.rgb, tipMix);
                half contrastNoise = branchNoise * 0.62h + fineNoise * 0.38h;
                half colorContrast = saturate(_NeedleContrast * 1.35h);
                needleColor *= lerp(1.0h - colorContrast, 1.0h + colorContrast, contrastNoise);
                return needleColor;
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

                float horizontalOS = abs(IN.positionOS.z) > 0.0001
                    ? IN.positionOS.z
                    : IN.positionOS.x;

                float3 billboardPositionWS =
                    instanceOriginWS +
                    rightWS * (horizontalOS * scaleZ) +
                    upWS * (IN.positionOS.y * scaleY);

                float3 normalPositionWS = TransformObjectToWorld(IN.positionOS.xyz);

#if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
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
                OUT.farNeedleColor = 0;
                #if defined(SPRUCE_FAR_SIMPLE)
                    OUT.farNeedleColor = EvaluateSpruceNeedleColor(IN.uv, instanceOriginWS);
                #endif

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                ApplyDistantTreeFade(IN.positionCS.xy);
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(baseSample.a - _Cutoff);

                // Apply the previous leaf treatment everywhere, without a bark/leaf mask.
                half3 variedNeedleColor = IN.farNeedleColor;
                #if !defined(SPRUCE_FAR_SIMPLE)
                    variedNeedleColor = EvaluateSpruceNeedleColor(IN.uv, IN.positionWS);
                #endif
                half atlasLuma = saturate(dot(baseSample.rgb, half3(0.299h, 0.587h, 0.114h)));
                half textureDetail = smoothstep(0.18h, 0.92h, atlasLuma);
                half textureContrast = lerp(1.0h - _NeedleContrast * 0.72h,
                    1.0h + _NeedleContrast * 0.42h, textureDetail);
                half3 color = baseSample.rgb * variedNeedleColor * textureContrast;
                half alpha = baseSample.a;
                #if defined(SPRUCE_FAR_SIMPLE)
                    // No specular, shadow coordinates, view vector or full surface setup.
                    half3 normal = normalize(IN.normalWS);
                    Light light = GetMainLight();
                    half ambient = _AmbientStrength * lerp(1.0h,
                        saturate(_TreeNightAmbientFloorScaleAtMidnight), saturate(_TreeNightAmbientFloorDimAmount));
                    half3 illumination = max(SampleSH(normal), ambient.xxx)
                        + light.color * saturate(dot(normal, light.direction));
                    half3 farColor = MixFog(saturate(color * _Brightness) * illumination,
                        ComputeFogFactor(TransformWorldToHClip(IN.positionWS).z));
                    return half4(saturate(farColor), alpha);
                #else
                InputData inputData = InitializeTreeSimpleLitInputData(IN.positionWS, IN.normalWS, IN.positionCS, IN.shadowCoord, _AmbientStrength);
                SurfaceData surfaceData = InitializeTreeSimpleLitSurfaceData(saturate(color * _Brightness), alpha, _Smoothness, _SpecularStrength);
                half4 litColor = ShadeDistantAwareTree(inputData, surfaceData);
                return half4(saturate(litColor.rgb), alpha);
                #endif
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
                half4 _BaseNeedleColor;
                half4 _CoolNeedleColor;
                half4 _DeepNeedleColor;
                half4 _SunNeedleColor;
                half _ColorVariationStrength;
                half _TipStrength;
                half _NeedleContrast;
                half _Cutoff;
                half _LeafMaskThreshold;
                half _LeafMaskSoftness;
                half _LeafTintStrength;
                half _LowerShadeStrength;
                half _InteriorShadeStrength;
                half _FauxShadeStrength;
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
                float horizontalOS = abs(IN.positionOS.z) > 0.0001
                    ? IN.positionOS.z
                    : IN.positionOS.x;

                float3 billboardPositionWS =
                    instanceOriginWS +
                    rightWS * (horizontalOS * scaleZ) +
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
                ApplyDistantTreeFade(IN.positionCS.xy);

                if (_AlphaCutoutShadows > 0.5h)
                {
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                }
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
