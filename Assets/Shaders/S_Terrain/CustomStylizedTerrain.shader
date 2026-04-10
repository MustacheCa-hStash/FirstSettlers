Shader "Custom/StylizedTerrainURP"
{
    Properties
    {
        _ControlMap0("Control Map 0", 2D) = "black" {}
        _ControlMap1("Control Map 1", 2D) = "black" {}

        _SandColor("Sand Color", Color) = (0.80, 0.75, 0.55, 1)
        _MudColor("Mud Color", Color) = (0.42, 0.32, 0.22, 1)
        _RockColor("Rock Color", Color) = (0.45, 0.45, 0.45, 1)
        _SnowColor("Snow Base Color", Color) = (0.92, 0.94, 0.98, 1)
        _CliffColor("Cliff Color", Color) = (0.30, 0.30, 0.30, 1)
        _RiverbedColor("Riverbed Color", Color) = (0.35, 0.30, 0.24, 1)

        _DarkGrassColor("Dark Grass Color", Color) = (0.20, 0.48, 0.18, 1)
        _MidGrassColor("Mid Grass Color", Color) = (0.29, 0.62, 0.24, 1)
        _LightGrassColor("Light Grass Color", Color) = (0.44, 0.78, 0.30, 1)

        _GrassAlbedo("Grass Albedo", 2D) = "white" {}
        _GrassNormal("Grass Normal", 2D) = "bump" {}
        _GrassTilingNear("Grass Tiling Near", Float) = 0.5
        _GrassTilingFar("Grass Tiling Far", Float) = 0.15
        _GrassTilingNearDistance("Grass Tiling Near Distance", Float) = 100.0
        _GrassTilingFarDistance("Grass Tiling Far Distance", Float) = 500.0
        _GrassNormalStrength("Grass Normal Strength", Range(0.0, 2.0)) = 0.6
        _GrassDetailStrength("Grass Detail Strength", Range(0.0, 1.0)) = 0.35
        _GrassDetailContrast("Grass Detail Contrast", Range(0.5, 3.0)) = 1.35

        _SnowAlbedo("Snow Albedo", 2D) = "white" {}
        _SnowTint("Snow Tint", Color) = (0.95, 0.97, 1.00, 1)
        _SnowNormal("Snow Normal", 2D) = "bump" {}
        _SnowNormalStrength("Snow Normal Strength", Range(0.0, 2.0)) = 0.35
        _SnowTilingNear("Snow Tiling Near", Float) = 0.06
        _SnowTilingFar("Snow Tiling Far", Float) = 0.02
        _SnowTilingNearDistance("Snow Tiling Near Distance", Float) = 25.0
        _SnowTilingFarDistance("Snow Tiling Far Distance", Float) = 200.0

        _SnowTriplanarStart("Snow Triplanar Start", Range(0.0, 1.0)) = 0.35
        _SnowTriplanarEnd("Snow Triplanar End", Range(0.0, 1.0)) = 0.75
        _SnowTriplanarSharpness("Snow Triplanar Sharpness", Range(1.0, 8.0)) = 4.0

        _DistanceBlendNoiseScale("Distance Blend Noise Scale", Float) = 0.01
        _DistanceBlendNoiseStrength("Distance Blend Noise Strength", Range(0.0, 1.0)) = 0.15

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

            TEXTURE2D(_GrassAlbedo);
            SAMPLER(sampler_GrassAlbedo);

            TEXTURE2D(_GrassNormal);
            SAMPLER(sampler_GrassNormal);

            TEXTURE2D(_SnowAlbedo);
            SAMPLER(sampler_SnowAlbedo);

            TEXTURE2D(_SnowNormal);
            SAMPLER(sampler_SnowNormal);

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

                half4 _SnowTint;

                float _GrassTilingNear;
                float _GrassTilingFar;
                float _GrassTilingNearDistance;
                float _GrassTilingFarDistance;
                float _GrassNormalStrength;
                float _GrassDetailStrength;
                float _GrassDetailContrast;

                float _SnowNormalStrength;
                float _SnowTilingNear;
                float _SnowTilingFar;
                float _SnowTilingNearDistance;
                float _SnowTilingFarDistance;
                float _SnowTriplanarStart;
                float _SnowTriplanarEnd;
                float _SnowTriplanarSharpness;

                float _DistanceBlendNoiseScale;
                float _DistanceBlendNoiseStrength;

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

            half3 EvaluateGrassTint(float2 worldXZ)
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

            float3 ApplyDetailNormal(float3 baseNormalWS, float3 tangentNormal, float strength)
            {
                tangentNormal.xy *= strength;
                tangentNormal.z = sqrt(saturate(1.0 - dot(tangentNormal.xy, tangentNormal.xy)));

                float3 referenceUp = abs(baseNormalWS.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                float3 tangentWS = normalize(cross(referenceUp, baseNormalWS));
                float3 bitangentWS = normalize(cross(baseNormalWS, tangentWS));

                float3 mappedNormalWS =
                    tangentWS * tangentNormal.x +
                    bitangentWS * tangentNormal.y +
                    baseNormalWS * tangentNormal.z;

                return normalize(mappedNormalWS);
            }

            float2 RotateUV90(float2 uv)
            {
                return float2(-uv.y, uv.x);
            }

            float GetDistanceBlend(float distanceToCamera, float nearDistance, float farDistance)
            {
                float denom = max(farDistance - nearDistance, 1e-5);
                return saturate((distanceToCamera - nearDistance) / denom);
            }

            float GetNoisyDistanceBlend(float distanceToCamera, float nearDistance, float farDistance, float2 worldXZ)
            {
                float t = GetDistanceBlend(distanceToCamera, nearDistance, farDistance);
                float noise = ValueNoise(worldXZ * _DistanceBlendNoiseScale);
                float noisyT = t + (noise - 0.5) * _DistanceBlendNoiseStrength;
                return saturate(noisyT);
            }

            float3 SampleSnowTriplanarAlbedo(float3 positionWS, float3 normalWS, float snowTiling)
            {
                float3 blend = pow(abs(normalWS), _SnowTriplanarSharpness);
                blend /= max(dot(blend, 1.0.xxx), 1e-5);

                float2 uvX = RotateUV90(positionWS.yz * snowTiling);
                float2 uvY = RotateUV90(positionWS.xz * snowTiling);
                float2 uvZ = RotateUV90(positionWS.xy * snowTiling);

                float3 sampleX = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, uvX).rgb;
                float3 sampleY = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, uvY).rgb;
                float3 sampleZ = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, uvZ).rgb;

                return sampleX * blend.x + sampleY * blend.y + sampleZ * blend.z;
            }

            float3 SampleSnowTriplanarNormalApprox(float3 positionWS, float3 normalWS, float snowTiling)
            {
                float3 blend = pow(abs(normalWS), _SnowTriplanarSharpness);
                blend /= max(dot(blend, 1.0.xxx), 1e-5);

                float2 uvX = RotateUV90(positionWS.yz * snowTiling);
                float2 uvY = RotateUV90(positionWS.xz * snowTiling);
                float2 uvZ = RotateUV90(positionWS.xy * snowTiling);

                float3 sampleX = UnpackNormal(SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowNormal, uvX));
                float3 sampleY = UnpackNormal(SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowNormal, uvY));
                float3 sampleZ = UnpackNormal(SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowNormal, uvZ));

                return normalize(sampleX * blend.x + sampleY * blend.y + sampleZ * blend.z);
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
                half3 baseNormalWS = normalize(IN.normalWS);
                half3 normalWS = baseNormalWS;

                float4 control0 = SAMPLE_TEXTURE2D(_ControlMap0, sampler_ControlMap0, IN.uv);
                float4 control1 = SAMPLE_TEXTURE2D(_ControlMap1, sampler_ControlMap1, IN.uv);

                half sandWeight = control0.r;
                half mudWeight = control0.g;
                half grassWeight = control0.b;
                half rockWeight = control0.a;

                half snowWeight = control1.r;
                half cliffWeight = control1.g;
                half riverbedWeight = control1.b;

                float distanceToCamera = distance(_WorldSpaceCameraPos.xyz, IN.positionWS);

                float grassDistanceBlend = GetNoisyDistanceBlend(
                    distanceToCamera,
                    _GrassTilingNearDistance,
                    _GrassTilingFarDistance,
                    IN.positionWS.xz
                );

                float snowDistanceBlend = GetNoisyDistanceBlend(
                    distanceToCamera,
                    _SnowTilingNearDistance,
                    _SnowTilingFarDistance,
                    IN.positionWS.xz
                );

                half3 baseColor = 0;

                baseColor += _SandColor.rgb * sandWeight;
                baseColor += _MudColor.rgb * mudWeight;
                baseColor += _RockColor.rgb * rockWeight;
                baseColor += _CliffColor.rgb * cliffWeight;
                baseColor += _RiverbedColor.rgb * riverbedWeight;

                if (grassWeight > 0.001h)
                {
                    float2 grassUVNear = IN.positionWS.xz * _GrassTilingNear;
                    float2 grassUVFar = IN.positionWS.xz * _GrassTilingFar;

                    half3 grassTexNear = SAMPLE_TEXTURE2D(_GrassAlbedo, sampler_GrassAlbedo, grassUVNear).rgb;
                    half3 grassTexFar = SAMPLE_TEXTURE2D(_GrassAlbedo, sampler_GrassAlbedo, grassUVFar).rgb;
                    half3 grassTex = lerp(grassTexNear, grassTexFar, grassDistanceBlend);

                    half3 grassTint = EvaluateGrassTint(IN.positionWS.xz);

                    half grassLuma = dot(grassTex, half3(0.299h, 0.587h, 0.114h));
                    half grassCentered = (grassLuma - 0.5h) * 2.0h;
                    half grassDetail = grassCentered * _GrassDetailContrast;
                    half grassVariation = saturate(1.0h + grassDetail * _GrassDetailStrength);

                    half3 grassColor = grassTint * grassVariation;
                    baseColor += grassColor * grassWeight;

                    float3 grassTangentNormalNear = UnpackNormal(
                        SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassNormal, grassUVNear)
                    );
                    float3 grassTangentNormalFar = UnpackNormal(
                        SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassNormal, grassUVFar)
                    );
                    float3 grassTangentNormal = normalize(lerp(grassTangentNormalNear, grassTangentNormalFar, grassDistanceBlend));

                    normalWS = ApplyDetailNormal(baseNormalWS, grassTangentNormal, _GrassNormalStrength);
                }

                if (snowWeight > 0.001h)
                {
                    float2 snowUVNear = RotateUV90(IN.positionWS.xz * _SnowTilingNear);
                    float2 snowUVFar = RotateUV90(IN.positionWS.xz * _SnowTilingFar);

                    half3 snowTexUVNear = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, snowUVNear).rgb;
                    half3 snowTexUVFar = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, snowUVFar).rgb;
                    half3 snowTexUV = lerp(snowTexUVNear, snowTexUVFar, snowDistanceBlend);

                    half3 snowTexTriNear = SampleSnowTriplanarAlbedo(IN.positionWS, baseNormalWS, _SnowTilingNear);
                    half3 snowTexTriFar = SampleSnowTriplanarAlbedo(IN.positionWS, baseNormalWS, _SnowTilingFar);
                    half3 snowTexTriplanar = lerp(snowTexTriNear, snowTexTriFar, snowDistanceBlend);

                    float slope = 1.0 - abs(baseNormalWS.y);
                    float snowTriplanarBlend = saturate(
                        (slope - _SnowTriplanarStart) / max(_SnowTriplanarEnd - _SnowTriplanarStart, 1e-5)
                    );

                    half3 snowTex = lerp(snowTexUV, snowTexTriplanar, snowTriplanarBlend);
                    half3 snowColor = snowTex * _SnowTint.rgb * _SnowColor.rgb;
                    baseColor += snowColor * snowWeight;

                    float3 snowTangentNormalUVNear = UnpackNormal(
                        SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowNormal, snowUVNear)
                    );
                    float3 snowTangentNormalUVFar = UnpackNormal(
                        SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowNormal, snowUVFar)
                    );
                    float3 snowTangentNormalUV = normalize(lerp(snowTangentNormalUVNear, snowTangentNormalUVFar, snowDistanceBlend));

                    float3 snowTangentNormalTriNear = SampleSnowTriplanarNormalApprox(IN.positionWS, baseNormalWS, _SnowTilingNear);
                    float3 snowTangentNormalTriFar = SampleSnowTriplanarNormalApprox(IN.positionWS, baseNormalWS, _SnowTilingFar);
                    float3 snowTangentNormalTri = normalize(lerp(snowTangentNormalTriNear, snowTangentNormalTriFar, snowDistanceBlend));

                    float3 snowTangentNormal = normalize(lerp(snowTangentNormalUV, snowTangentNormalTri, snowTriplanarBlend));

                    normalWS = ApplyDetailNormal(normalWS, snowTangentNormal, _SnowNormalStrength);
                }

                Light mainLight = GetMainLight();
                half3 diffuse = LightingLambert(mainLight.color, mainLight.direction, normalWS) * mainLight.distanceAttenuation;
                half3 lighting = diffuse + half3(_AmbientStrength, _AmbientStrength, _AmbientStrength);

                return half4(baseColor * lighting, 1.0);
            }
            ENDHLSL
        }
    }
}