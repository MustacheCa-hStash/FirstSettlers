Shader "Custom/CloverInstancedSimpleLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Black / White Clover Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base Tint", Color) = (0.28, 0.58, 0.20, 1)
        _DarkColor("Dark Clover Color", Color) = (0.13, 0.34, 0.10, 1)
        _MidColor("Mid Clover Color", Color) = (0.23, 0.50, 0.16, 1)
        _LightColor("Light Clover Color", Color) = (0.38, 0.68, 0.24, 1)

        [PerRendererData] _CloverInstanceData("Clover Instance Data", Vector) = (0, 0, 0, 0)

        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        _AlphaFromLuminance("Alpha From Luminance", Range(0, 1)) = 0
        _TextureContrast("Texture Contrast", Range(0.1, 4)) = 1.3
        _TextureShadeStrength("Texture Shade Strength", Range(0, 1.5)) = 0.75
        _VariationScale("Smooth Variation Scale", Float) = 5.5
        _VariationStrength("Smooth Variation Strength", Range(0, 1)) = 0.18
        _InstanceVariationStrength("Instance Variation Strength", Range(0, 1)) = 0.14
        _NormalUpBlend("Upward Normal Blend", Range(0, 1)) = 0.72
        _FadeStartDistance("Fade Start Distance", Float) = 42
        _FadeEndDistance("Fade End Distance", Float) = 58
        _FadeDitherPixelSize("Fade Dither Pixel Size", Float) = 1
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _DarkColor;
                half4 _MidColor;
                half4 _LightColor;
                half _Cutoff;
                half _AlphaFromLuminance;
                half _TextureContrast;
                half _TextureShadeStrength;
                half _VariationScale;
                half _VariationStrength;
                half _InstanceVariationStrength;
                half _NormalUpBlend;
                half _FadeStartDistance;
                half _FadeEndDistance;
                half _FadeDitherPixelSize;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(CloverInstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _CloverInstanceData)
            UNITY_INSTANCING_BUFFER_END(CloverInstanceProperties)

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
                float4 screenPosition : TEXCOORD3;
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
                half x1 = lerp(a, b, f.x);
                half x2 = lerp(c, d, f.x);
                return lerp(x1, x2, f.y);
            }

            half3 CloverPalette(half t)
            {
                t = saturate(t);

                if (t < 0.5h)
                    return lerp(_DarkColor.rgb, _MidColor.rgb, t * 2.0h);

                return lerp(_MidColor.rgb, _LightColor.rgb, (t - 0.5h) * 2.0h);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                half3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = normalize(lerp(normalWS, half3(0.0h, 1.0h, 0.0h), _NormalUpBlend));
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.screenPosition = ComputeScreenPos(OUT.positionCS);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half luminance = dot(baseSample.rgb, half3(0.299h, 0.587h, 0.114h));
                half clipAlpha = lerp(baseSample.a, luminance, _AlphaFromLuminance);
                clip(clipAlpha - _Cutoff);

                half4 instanceData = UNITY_ACCESS_INSTANCED_PROP(CloverInstanceProperties, _CloverInstanceData);
                half fadeRange = max(_FadeEndDistance - _FadeStartDistance, 0.001h);
                half distanceFade = _FadeEndDistance > _FadeStartDistance
                    ? saturate((_FadeEndDistance - distance(GetCameraPositionWS(), IN.positionWS)) / fadeRange)
                    : 1.0h;
                float2 screenUv = IN.screenPosition.xy / max(IN.screenPosition.w, 0.0001);
                float2 pixelCoord = floor(screenUv * _ScreenParams.xy / max(_FadeDitherPixelSize, 1.0h));
                half ditherThreshold = Hash21(pixelCoord + instanceData.xy * float2(97.13, 41.71));
                clip(distanceFade - ditherThreshold);

                half worldNoise = SmoothNoise(IN.positionWS.xz * _VariationScale + instanceData.x * 17.0h);
                half uvNoise = SmoothNoise(IN.uv * (_VariationScale * 1.9h) + instanceData.y * 23.0h);
                half smoothVariation = (worldNoise * 0.65h + uvNoise * 0.35h - 0.5h) * 2.0h;
                half instanceVariation = (instanceData.y - 0.5h) * 2.0h;

                half paletteT = saturate(0.47h + smoothVariation * _VariationStrength + instanceVariation * _InstanceVariationStrength);
                half3 cloverColor = CloverPalette(paletteT) * _BaseColor.rgb;

                half shade = saturate((luminance - 0.5h) * _TextureContrast + 0.5h);
                half textureShade = lerp(1.0h, lerp(0.55h, 1.16h, shade), _TextureShadeStrength);

                half3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient + mainLight.color * (0.38h + ndotl * 0.62h);

                half3 color = cloverColor * textureShade * lighting;
                return half4(color, clipAlpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
