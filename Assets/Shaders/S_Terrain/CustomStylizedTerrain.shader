Shader "Custom/StylizedTerrainURP"
{
    Properties
    {
        _ControlMap0("Control Map 0", 2D) = "black" {}
        _ControlMap1("Control Map 1", 2D) = "black" {}

        _SandColor("Sand Color", Color) = (0.80, 0.75, 0.55, 1)
        _MudColor("Mud Color", Color) = (0.42, 0.32, 0.22, 1)
        _RockColor("Rock Color", Color) = (0.45, 0.45, 0.45, 1)
        _SnowColor("Snow Color", Color) = (0.92, 0.94, 0.98, 1)
        _CliffColor("Cliff Color", Color) = (0.30, 0.30, 0.30, 1)
        _RiverbedColor("Riverbed Color", Color) = (0.35, 0.30, 0.24, 1)

        _DarkGrassColor("Dark Grass Color", Color) = (0.20, 0.48, 0.18, 1)
        _MidGrassColor("Mid Grass Color", Color) = (0.29, 0.62, 0.24, 1)
        _LightGrassColor("Light Grass Color", Color) = (0.44, 0.78, 0.30, 1)

        _NoiseScale("Noise Scale", Range(0.001, 0.2)) = 0.03
        _NoiseStrength("Noise Strength", Range(0.0, 2.0)) = 1.0
        _BlendSharpness("Blend Sharpness", Range(0.25, 3.0)) = 1.0
        _AmbientStrength("Ambient Strength", Range(0.0, 1.0)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_ControlMap0);
            SAMPLER(sampler_ControlMap0);

            TEXTURE2D(_ControlMap1);
            SAMPLER(sampler_ControlMap1);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _SandColor;
                half4 _MudColor;
                half4 _RockColor;
                half4 _SnowColor;
                half4 _CliffColor;
                half4 _RiverbedColor;

                half4 _DarkGrassColor;
                half4 _MidGrassColor;
                half4 _LightGrassColor;

                float _NoiseScale;
                float _NoiseStrength;
                float _BlendSharpness;
                float _AmbientStrength;
            CBUFFER_END

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                float x1 = lerp(a, b, f.x);
                float x2 = lerp(c, d, f.x);
                return lerp(x1, x2, f.y);
            }

            half3 EvaluateGrassColor(float2 worldXZ)
            {
                float n = ValueNoise(worldXZ * _NoiseScale);
                n = saturate((n - 0.5) * _NoiseStrength + 0.5);
                n = saturate(pow(n, _BlendSharpness));

                if (n < 0.5)
                {
                    return lerp(_DarkGrassColor.rgb, _MidGrassColor.rgb, n * 2.0);
                }

                return lerp(_MidGrassColor.rgb, _LightGrassColor.rgb, (n - 0.5) * 2.0);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;
                OUT.normalWS = normalInputs.normalWS;
                OUT.uv = IN.uv;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 normalWS = normalize(IN.normalWS);

                float4 control0 = SAMPLE_TEXTURE2D(_ControlMap0, sampler_ControlMap0, IN.uv);
                float4 control1 = SAMPLE_TEXTURE2D(_ControlMap1, sampler_ControlMap1, IN.uv);

                half sandWeight = control0.r;
                half mudWeight = control0.g;
                half grassWeight = control0.b;
                half rockWeight = control0.a;

                half snowWeight = control1.r;
                half cliffWeight = control1.g;
                half riverbedWeight = control1.b;

                half3 grassColor = EvaluateGrassColor(IN.positionWS.xz);

                half3 baseColor = 0;
                baseColor += _SandColor.rgb * sandWeight;
                baseColor += _MudColor.rgb * mudWeight;
                baseColor += grassColor * grassWeight;
                baseColor += _RockColor.rgb * rockWeight;
                baseColor += _SnowColor.rgb * snowWeight;
                baseColor += _CliffColor.rgb * cliffWeight;
                baseColor += _RiverbedColor.rgb * riverbedWeight;

                Light mainLight = GetMainLight();
                half3 diffuse = LightingLambert(mainLight.color, mainLight.direction, normalWS) * mainLight.distanceAttenuation;
                half3 lighting = diffuse + half3(_AmbientStrength, _AmbientStrength, _AmbientStrength);

                return half4(baseColor * lighting, 1.0);
            }
            ENDHLSL
        }
    }
}