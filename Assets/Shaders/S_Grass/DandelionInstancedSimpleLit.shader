Shader "Custom/DandelionInstancedSimpleLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base / Alpha Map", 2D) = "white" {}
        _MaskMap("RGB Mask (R Head/Leaf, G Stem)", 2D) = "black" {}
        [MainColor] _BaseColor("Base Tint", Color) = (1, 1, 1, 1)
        _HeadColor("Yellow Head Color", Color) = (1.0, 0.82, 0.12, 1)
        _HeadWarmColor("Warm Head Variation", Color) = (1.0, 0.62, 0.06, 1)
        _HeadStripeLightColor("Head Stripe Light Color", Color) = (1.0, 0.92, 0.26, 1)
        _HeadStripeDarkColor("Head Stripe Dark Color", Color) = (0.92, 0.52, 0.03, 1)
        _LeafColor("Red Mask Low Leaf Color", Color) = (0.18, 0.44, 0.12, 1)
        _StemColor("Stem Color", Color) = (0.14, 0.42, 0.11, 1)
        _StemLightColor("Stem Light Color", Color) = (0.30, 0.60, 0.18, 1)

        [PerRendererData] _DandelionInstanceData("Dandelion Instance Data", Vector) = (0, 0, 0, 0)

        _Cutoff("RGB Alpha Clip Threshold", Range(0, 1)) = 0.25
        _MaskSharpness("Mask Sharpness", Range(0.25, 8)) = 1.6
        _RedHeadHeightSplit("Red Mask Head Height Split", Range(0, 1)) = 0.45
        _RedHeadSplitFeather("Red Mask Split Feather", Range(0.001, 0.5)) = 0.16
        _TextureShadeStrength("Mask Edge Shade Strength", Range(0, 1.5)) = 0.2
        _VariationScale("Smooth Variation Scale", Float) = 4.5
        _VariationStrength("Smooth Variation Strength", Range(0, 1)) = 0.11
        _InstanceVariationStrength("Instance Variation Strength", Range(0, 1)) = 0.08
        _HeadStripeScale("Head Stripe Scale", Range(1, 40)) = 18
        _HeadStripeStrength("Head Stripe Strength", Range(0, 1)) = 0.38
        _HeadStripeContrast("Head Stripe Contrast", Range(0.25, 2.5)) = 0.9
        _NormalUpBlend("Upward Normal Blend", Range(0, 1)) = 0.55

        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Stem Bend Strength", Range(0, 1)) = 0.035
        _WindSpeed("Wind Stem Bend Speed", Range(0, 8)) = 1.7
        _WindFlutterStrength("Wind Head Flutter Strength", Range(0, 1)) = 0.012
        _WindFlutterSpeed("Wind Head Flutter Speed", Range(0, 16)) = 5.8
        _WindGustScale("Wind Gust Scale", Range(0.01, 2)) = 0.42
        _WindHeightMin("Wind Height Min", Float) = 0
        _WindHeightMax("Wind Height Max", Float) = 0.65
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
                half4 _BaseColor;
                half4 _HeadColor;
                half4 _HeadWarmColor;
                half4 _HeadStripeLightColor;
                half4 _HeadStripeDarkColor;
                half4 _LeafColor;
                half4 _StemColor;
                half4 _StemLightColor;
                half _Cutoff;
                half _MaskSharpness;
                half _RedHeadHeightSplit;
                half _RedHeadSplitFeather;
                half _TextureShadeStrength;
                half _VariationScale;
                half _VariationStrength;
                half _InstanceVariationStrength;
                half _HeadStripeScale;
                half _HeadStripeStrength;
                half _HeadStripeContrast;
                half _NormalUpBlend;
                float4 _WindDirection;
                half _WindStrength;
                half _WindSpeed;
                half _WindFlutterStrength;
                half _WindFlutterSpeed;
                half _WindGustScale;
                half _WindHeightMin;
                half _WindHeightMax;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(DandelionInstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _DandelionInstanceData)
            UNITY_INSTANCING_BUFFER_END(DandelionInstanceProperties)

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
                float2 maskUV : TEXCOORD2;
                float2 baseUV : TEXCOORD3;
                half height01 : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half SmoothNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

                half a = Hash21(i);
                half b = Hash21(i + float2(1.0, 0.0));
                half c = Hash21(i + float2(0.0, 1.0));
                half d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half DandelionHeadStripe(float2 uv, half4 instanceData, half uvNoise, half worldNoise)
            {
                float2 stripeUv = uv * max(_HeadStripeScale, 0.001h);
                half stripeA = sin((stripeUv.y + stripeUv.x * 0.23h) * 6.2831853h + uvNoise * 2.4h + instanceData.x * 6.2831853h);
                half stripeB = sin((stripeUv.y * 0.63h - stripeUv.x * 0.41h) * 6.2831853h + worldNoise * 2.0h + instanceData.y * 6.2831853h);
                half rawStripe = stripeA * 0.68h + stripeB * 0.32h;
                return saturate(rawStripe * _HeadStripeContrast + 0.5h);
            }

            float3 ApplyDandelionWind(float3 positionWS, float3 positionOS, half height01, float2 uv, half4 instanceData)
            {
                float2 windDir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
                half heightMask = height01 * height01;
                half headMask = saturate((height01 - 0.45h) / 0.55h);
                headMask *= headMask;

                float instancePhase = instanceData.x * 6.2831853;
                float spatialPhase = dot(positionWS.xz, windDir.yx * float2(0.86, -0.62));
                float gustPhase = dot(positionWS.xz, windDir) * _WindGustScale + _Time.y * _WindSpeed + instancePhase;
                float gust = sin(gustPhase + spatialPhase * 0.35);
                float flutter = sin(_Time.y * _WindFlutterSpeed + spatialPhase * 2.4 + instancePhase * 1.73);

                float bend = gust * _WindStrength * heightMask;
                float headFlutter = flutter * _WindFlutterStrength * headMask;

                float3 offset = float3(windDir.x, 0.0, windDir.y) * (bend + headFlutter);
                offset.y = -abs(bend) * 0.10 + headFlutter * 0.18;
                return positionWS + offset;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                half4 instanceData = UNITY_ACCESS_INSTANCED_PROP(DandelionInstanceProperties, _DandelionInstanceData);
                half height01 = saturate((IN.positionOS.y - _WindHeightMin) / max(_WindHeightMax - _WindHeightMin, 0.0001h));

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                positionWS = ApplyDandelionWind(positionWS, IN.positionOS.xyz, height01, IN.uv, instanceData);

                half3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = normalize(lerp(normalWS, half3(0.0h, 1.0h, 0.0h), _NormalUpBlend));
                OUT.baseUV = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.maskUV = TRANSFORM_TEX(IN.uv, _MaskMap);
                OUT.height01 = height01;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half baseAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.baseUV).a;
                clip(baseAlpha - _Cutoff);

                half3 rawMask = saturate(SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, IN.maskUV).rgb);
                rawMask = pow(rawMask, max(_MaskSharpness, 0.001h));

                half maskTotal = max(rawMask.r + rawMask.g + rawMask.b, 0.0001h);
                half3 mask = rawMask / maskTotal;
                half4 instanceData = UNITY_ACCESS_INSTANCED_PROP(DandelionInstanceProperties, _DandelionInstanceData);

                half heightHeadT = smoothstep(
                    _RedHeadHeightSplit - _RedHeadSplitFeather,
                    _RedHeadHeightSplit + _RedHeadSplitFeather,
                    IN.height01);

                half worldNoise = SmoothNoise(IN.positionWS.xz * _VariationScale + instanceData.x * 17.0h);
                half uvNoise = SmoothNoise(IN.maskUV * (_VariationScale * 2.1h) + instanceData.y * 23.0h);
                half smoothVariation = (worldNoise * 0.7h + uvNoise * 0.3h - 0.5h) * 2.0h;
                half instanceVariation = (instanceData.y - 0.5h) * 2.0h;
                half variation = smoothVariation * _VariationStrength + instanceVariation * _InstanceVariationStrength;

                half headWarmth = saturate(0.45h + variation);
                half3 headColor = lerp(_HeadColor.rgb, _HeadWarmColor.rgb, headWarmth);
                half headStripe = DandelionHeadStripe(IN.maskUV, instanceData, uvNoise, worldNoise);
                half3 stripeColor = lerp(_HeadStripeDarkColor.rgb, _HeadStripeLightColor.rgb, headStripe);
                headColor = lerp(headColor, stripeColor, _HeadStripeStrength * heightHeadT);
                half3 redMaskColor = lerp(_LeafColor.rgb, headColor, heightHeadT);
                half3 stemColor = lerp(_StemColor.rgb, _StemLightColor.rgb, saturate(0.48h + variation));
                half3 color = redMaskColor * mask.r + stemColor * mask.g;

                half edgeShade = lerp(1.0h, lerp(0.82h, 1.08h, baseAlpha), _TextureShadeStrength);

                half3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient + mainLight.color * (0.42h + ndotl * 0.58h);

                return half4(color * _BaseColor.rgb * edgeShade * lighting, baseAlpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
