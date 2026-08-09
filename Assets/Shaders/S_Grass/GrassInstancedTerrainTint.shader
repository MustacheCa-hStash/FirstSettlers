Shader "Custom/GrassInstancedTerrainTint"
{
    Properties
    {
        [MainTexture] _BaseMap("Base / Alpha Map", 2D) = "white" {}
        _GrassTex("Grass Detail Texture", 2D) = "white" {}
        _GrassNormal("Grass Normal", 2D) = "bump" {}

        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Color("Color", Color) = (1, 1, 1, 1)

        _DarkGrassColor("Dark Grass Color", Color) = (0.20, 0.48, 0.18, 1)
        _MidGrassColor("Mid Grass Color", Color) = (0.29, 0.62, 0.24, 1)
        _LightGrassColor("Light Grass Color", Color) = (0.44, 0.78, 0.30, 1)

        _ForestDarkGrassColor("Forest Dark Grass Color", Color) = (0.045, 0.16, 0.05, 1)
        _ForestMidGrassColor("Forest Mid Grass Color", Color) = (0.085, 0.25, 0.085, 1)
        _ForestLightGrassColor("Forest Light Grass Color", Color) = (0.17, 0.38, 0.14, 1)

        [PerRendererData] _GrassInstanceData("Grass Instance Data", Vector) = (0, 0, 0, 0)

        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        _AlphaClipThreshold("Alpha Clip Threshold", Range(0, 1)) = 0.4

        _NoiseScale("Noise Scale", Float) = 0.023
        _NoiseStrength("Noise Strength", Range(0, 4)) = 1
        _BlendSharpness("Blend Sharpness", Range(0.05, 4)) = 0.61

        _GrassDetailStrength("Grass Detail Strength", Range(0, 1)) = 0.35
        _GrassDetailContrast("Grass Detail Contrast", Range(0.1, 6)) = 2.14

        _BladeMinY("Blade Min Y", Float) = 0
        _BladeMaxY("Blade Max Y", Float) = 0.16
        _BaseUpwardBlend("Base Upward Normal Blend", Range(0, 1)) = 0.39
        _TipUpwardBlend("Tip Upward Normal Blend", Range(0, 1)) = 0.86
        _UpwardNormalBlend("Upward Normal Blend", Range(0, 1)) = 0.65
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

            TEXTURE2D(_GrassTex);
            SAMPLER(sampler_GrassTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _GrassTex_ST;
                half4 _BaseColor;
                half4 _Color;
                half4 _DarkGrassColor;
                half4 _MidGrassColor;
                half4 _LightGrassColor;
                half4 _ForestDarkGrassColor;
                half4 _ForestMidGrassColor;
                half4 _ForestLightGrassColor;
                half _Cutoff;
                half _AlphaClipThreshold;
                half _NoiseScale;
                half _NoiseStrength;
                half _BlendSharpness;
                half _GrassDetailStrength;
                half _GrassDetailContrast;
                half _BladeMinY;
                half _BladeMaxY;
                half _BaseUpwardBlend;
                half _TipUpwardBlend;
                half _UpwardNormalBlend;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(GrassInstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GrassInstanceData)
            UNITY_INSTANCING_BUFFER_END(GrassInstanceProperties)

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
                float2 baseUV : TEXCOORD2;
                float2 grassUV : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half ValueNoise(float2 worldXZ)
            {
                float2 p = worldXZ * _NoiseScale;
                float2 i = floor(p);
                float2 f = frac(p);

                f = f * f * (3.0 - 2.0 * f);

                half a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453123);
                half b = frac(sin(dot(i + float2(1.0, 0.0), float2(127.1, 311.7))) * 43758.5453123);
                half c = frac(sin(dot(i + float2(0.0, 1.0), float2(127.1, 311.7))) * 43758.5453123);
                half d = frac(sin(dot(i + float2(1.0, 1.0), float2(127.1, 311.7))) * 43758.5453123);

                half x1 = lerp(a, b, f.x);
                half x2 = lerp(c, d, f.x);
                half n = lerp(x1, x2, f.y);

                n = saturate((n - 0.5h) * _NoiseStrength + 0.5h);
                return saturate(pow(n, max(_BlendSharpness, 0.001h)));
            }

            half3 PaletteTint(float2 worldXZ, half forestBlend)
            {
                half3 darkGrass = lerp(_DarkGrassColor.rgb, _ForestDarkGrassColor.rgb, forestBlend);
                half3 midGrass = lerp(_MidGrassColor.rgb, _ForestMidGrassColor.rgb, forestBlend);
                half3 lightGrass = lerp(_LightGrassColor.rgb, _ForestLightGrassColor.rgb, forestBlend);

                half n = ValueNoise(worldXZ);

                if (n < 0.5h)
                {
                    return lerp(darkGrass, midGrass, n * 2.0h);
                }

                return lerp(midGrass, lightGrass, (n - 0.5h) * 2.0h);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;
                OUT.baseUV = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.grassUV = TRANSFORM_TEX(IN.uv, _GrassTex);

                half3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                half bladeHeight = saturate((IN.positionOS.y - _BladeMinY) / max(_BladeMaxY - _BladeMinY, 0.0001h));
                half upwardBlend = saturate(lerp(_BaseUpwardBlend, _TipUpwardBlend, bladeHeight) * _UpwardNormalBlend);
                OUT.normalWS = normalize(lerp(normalWS, half3(0.0h, 1.0h, 0.0h), upwardBlend));

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.baseUV);
                half cutoff = max(_Cutoff, _AlphaClipThreshold);
                clip(baseSample.a - cutoff);

                half4 instanceData = UNITY_ACCESS_INSTANCED_PROP(GrassInstanceProperties, _GrassInstanceData);
                half forestBlend = saturate(instanceData.x);

                half3 grassTint = PaletteTint(IN.positionWS.xz, forestBlend);

                half3 detailSample = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, IN.grassUV).rgb;
                half detailLum = dot(detailSample, half3(0.299h, 0.587h, 0.114h));
                half detailCentered = (detailLum - 0.5h) * 2.0h;
                half detail = detailCentered * _GrassDetailContrast;
                half grassVariation = saturate(1.0h + detail * _GrassDetailStrength);

                half3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient + mainLight.color * (0.35h + ndotl * 0.65h);

                half3 color = grassTint * grassVariation * _BaseColor.rgb * _Color.rgb * lighting;
                return half4(color, baseSample.a * _BaseColor.a * _Color.a);
            }
            ENDHLSL
        }
    }
}
