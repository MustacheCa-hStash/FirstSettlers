Shader "Custom/TallFlowerVertexMaskInstanced"
{
    Properties
    {
        [MainTexture] _BaseMap("Petal / Stem Color Mask", 2D) = "white" {}
        [PerRendererData] _FlowerPetalColor("Instance Petal Variation", Color) = (1, 1, 1, 1)
        _DarkPetalColor("Dark Petal / Magenta", Color) = (0.44, 0.12, 0.58, 1)
        _LightPetalColor("Light Petal / Soft Pink", Color) = (0.84, 0.48, 0.68, 1)
        _StemColor("Stem", Color) = (0.14, 0.54, 0.18, 1)
        _Cutoff("Alpha Clip", Range(0, 1)) = 0.5
        _MaskSharpness("Petal Pink Bias", Range(0.25, 4)) = 0.65
        _PetalHighlightThreshold("Pink Mask Plateau (sRGB)", Range(0.4, 1)) = 0.706
        _PetalHighlightSoftness("Pink Mask Transition", Range(0.01, 0.4)) = 0.16
        _PetalVariationStrength("Petal Color Variation", Range(0, 0.25)) = 0.09
        _AmbientStrength("Minimum Ambient Strength", Range(0, 1)) = 0.40
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindHeight("Mesh Height For Sway", Float) = 1
        _WindStrength("Stem Sway Strength", Range(0, 1)) = 0.12
        _WindSpeed("Sway Speed", Range(0, 8)) = 1.8
        _WindFlutterStrength("Flower Head Flutter", Range(0, 1)) = 0.025
        _WindFlutterSpeed("Flutter Speed", Range(0, 16)) = 6
        _WindGustScale("Gust Scale", Range(0.01, 2)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _DarkPetalColor;
                half4 _LightPetalColor;
                half4 _StemColor;
                half _Cutoff;
                half _MaskSharpness;
                half _PetalHighlightThreshold;
                half _PetalHighlightSoftness;
                half _PetalVariationStrength;
                half _AmbientStrength;
                float4 _WindDirection;
                half _WindHeight;
                half _WindStrength;
                half _WindSpeed;
                half _WindFlutterStrength;
                half _WindFlutterSpeed;
                half _WindGustScale;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(TallFlowerProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FlowerPetalColor)
            UNITY_INSTANCING_BUFFER_END(TallFlowerProperties)

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
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD2;
                float3 instanceOriginWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float heightMask = saturate(IN.positionOS.y / max(_WindHeight, 0.0001));
                heightMask *= heightMask;
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                float phase = Hash12(floor(positionWS.xz * 0.45)) * 6.2831853;
                float spatialPhase = dot(positionWS.xz, windDir.yx * float2(0.86, -0.62));
                float gust = sin(dot(positionWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed + phase);
                float flutter = sin(_Time.y * _WindFlutterSpeed + spatialPhase * 2.7 + phase * 1.41);
                float bend = gust * _WindStrength * heightMask;
                float headFlutter = flutter * _WindFlutterStrength * saturate(IN.uv.y) * heightMask;
                positionWS += float3(windDir.x, 0.0, windDir.y) * (bend + headFlutter);
                positionWS.y += -abs(bend) * 0.12 + headFlutter * 0.16;

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = positionWS;
                OUT.instanceOriginWS = mul(GetObjectToWorldMatrix(), float4(0, 0, 0, 1)).xyz;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = IN.color;
                return OUT;
            }

            half LinearToSrgb(half value)
            {
                value = saturate(value);
                return value <= 0.0031308h
                    ? value * 12.92h
                    : 1.055h * pow(value, 1.0h / 2.4h) - 0.055h;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(tex.a - _Cutoff);

                // Blender's RGB vertex masks are preferred when present. The imported
                // Lupine FBX currently has no color layer, so Unity supplies white;
                // in that case use the assigned red/green mask texture instead.
                half vertexMaskPresent = step(0.015h, max(
                    abs(IN.color.r - 1.0h),
                    max(abs(IN.color.g - 1.0h), abs(IN.color.b - 1.0h))));
                half petalMask = saturate(lerp(tex.r, IN.color.r, vertexMaskPresent));
                half stemMask = saturate(lerp(tex.g, IN.color.g, vertexMaskPresent));
                // Red petal values win if both mask channels contain a value.
                half stem = saturate(stemMask * (1.0h - petalMask));
                // The mask is authored as display-space grayscale. Convert sampled
                // linear values back before comparing with the requested R=180 cutoff.
                half displayMask = LinearToSrgb(petalMask);
                half pinkRamp = smoothstep(
                    max(0.0h, _PetalHighlightThreshold - _PetalHighlightSoftness),
                    _PetalHighlightThreshold,
                    displayMask);
                // Above the threshold, all bright mask values share the same pink.
                half3 petal = lerp(_DarkPetalColor.rgb, _LightPetalColor.rgb, pinkRamp);
                half treeNoise = Hash12(floor(IN.instanceOriginWS.xz * 0.31h));
                half flowerNoise = Hash12(floor(IN.instanceOriginWS.xz * 0.57h) + floor(IN.uv * 5.0h));
                half fineNoise = Hash12(floor(IN.instanceOriginWS.xz * 1.17h) + floor(IN.uv * 18.0h));
                half colorNoise = treeNoise * 0.30h + flowerNoise * 0.45h + fineNoise * 0.25h;
                petal *= lerp(1.0h - _PetalVariationStrength, 1.0h + _PetalVariationStrength, colorNoise);
                petal *= UNITY_ACCESS_INSTANCED_PROP(TallFlowerProperties, _FlowerPetalColor).rgb;
                half3 color = lerp(petal, _StemColor.rgb, stem);

                half3 normalWS = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = max(SampleSH(normalWS), _AmbientStrength.xxx);
                half3 lighting = ambient + mainLight.color * (0.18h + ndotl * 0.30h) * mainLight.shadowAttenuation;
                return half4(color * lighting, tex.a);
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
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Cutoff;
                float4 _WindDirection;
                half _WindHeight;
                half _WindStrength;
                half _WindSpeed;
                half _WindFlutterStrength;
                half _WindFlutterSpeed;
                half _WindGustScale;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
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

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float heightMask = saturate(IN.positionOS.y / max(_WindHeight, 0.0001));
                heightMask *= heightMask;
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                float phase = Hash12(floor(positionWS.xz * 0.45)) * 6.2831853;
                float spatialPhase = dot(positionWS.xz, windDir.yx * float2(0.86, -0.62));
                float gust = sin(dot(positionWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed + phase);
                float flutter = sin(_Time.y * _WindFlutterSpeed + spatialPhase * 2.7 + phase * 1.41);
                float bend = gust * _WindStrength * heightMask;
                float headFlutter = flutter * _WindFlutterStrength * saturate(IN.uv.y) * heightMask;
                positionWS += float3(windDir.x, 0.0, windDir.y) * (bend + headFlutter);
                positionWS.y += -abs(bend) * 0.12 + headFlutter * 0.16;
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

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
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
