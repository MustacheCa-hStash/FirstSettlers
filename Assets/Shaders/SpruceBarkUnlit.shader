Shader "Custom/SpruceBarkSimpleLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Bark Line Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Mid Bark Color", Color) = (0.20, 0.145, 0.085, 1.0)
        _RidgeColor("Dry Ridge Color", Color) = (0.36, 0.255, 0.135, 1.0)
        _CreviceColor("Dark Crevice Color", Color) = (0.032, 0.024, 0.014, 1.0)
        _CoolShadowColor("Cool Side Color", Color) = (0.12, 0.115, 0.085, 1.0)
        _CrackThreshold("Crack Threshold", Range(0, 1)) = 0.43
        _CrackSoftness("Crack Softness", Range(0.001, 0.35)) = 0.075
        _CrackDarkness("Crack Darkness", Range(0, 1)) = 0.92
        _RidgeStrength("Ridge Highlight Strength", Range(0, 1)) = 0.24
        _ColorVariationStrength("Color Variation Strength", Range(0, 1)) = 0.22
        _VerticalGradientStrength("Vertical Gradient Strength", Range(0, 1)) = 0.13
        [HideInInspector] _FauxSideShadeStrength("Legacy Fake Side Shade Strength", Range(0, 1)) = 0.28
        _Brightness("Brightness", Range(0.25, 2)) = 1.0
        _AmbientStrength("Ambient Floor", Range(0, 1)) = 0.34
        _LightWrap("Bark Light Softness", Range(0, 1)) = 0.22
        _Smoothness("Smoothness", Range(0, 1)) = 0.12
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.06
        [Toggle] _UseVertexColor("Use Vertex Color", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardBark"
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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _RidgeColor;
                half4 _CreviceColor;
                half4 _CoolShadowColor;
                half _CrackThreshold;
                half _CrackSoftness;
                half _CrackDarkness;
                half _RidgeStrength;
                half _ColorVariationStrength;
                half _VerticalGradientStrength;
                half _FauxSideShadeStrength;
                half _Brightness;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
                half _UseVertexColor;
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;
                OUT.normalWS = normalInputs.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = TransformWorldToShadowCoord(positionInputs.positionWS);
                OUT.color = IN.color;

                return OUT;
            }

            InputData InitializeSpruceBarkInputData(Varyings IN)
            {
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = IN.shadowCoord;
                inputData.bakedGI = max(SampleSH(inputData.normalWS), _AmbientStrength.xxx);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
                return inputData;
            }

            SurfaceData InitializeSpruceBarkSurfaceData(half3 barkColor)
            {
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = saturate(barkColor);
                surfaceData.alpha = 1.0h;
                surfaceData.specular = _SpecularStrength.xxx;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
                surfaceData.emission = half3(0.0h, 0.0h, 0.0h);
                surfaceData.occlusion = 1.0h;
                return surfaceData;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half barkLuma = dot(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb, half3(0.299h, 0.587h, 0.114h));
                half crackMask = 1.0h - smoothstep(_CrackThreshold, _CrackThreshold + _CrackSoftness, barkLuma);
                half ridgeMask = smoothstep(0.58h, 0.98h, barkLuma);

                half plateNoise = ValueNoise(IN.uv * float2(5.5, 2.0));
                half fineNoise = ValueNoise(IN.uv * float2(20.0, 8.0));
                half worldNoise = ValueNoise(IN.positionWS.xz * 0.48 + IN.uv.yx * 1.7);
                half variation = plateNoise * 0.56h + fineNoise * 0.27h + worldNoise * 0.17h;

                half3 barkColor = lerp(_BaseColor.rgb, _RidgeColor.rgb, ridgeMask * _RidgeStrength * (0.55h + variation * 0.55h));
                barkColor *= lerp(1.0h - _ColorVariationStrength, 1.0h + _ColorVariationStrength, variation);

                half heightShade = lerp(1.0h - _VerticalGradientStrength, 1.0h + _VerticalGradientStrength, saturate(IN.uv.y));
                barkColor *= heightShade * _Brightness;
                barkColor = lerp(barkColor, _CreviceColor.rgb, saturate(crackMask * _CrackDarkness));
                barkColor *= lerp(half3(1.0h, 1.0h, 1.0h), IN.color.rgb, saturate(_UseVertexColor));

                InputData inputData = InitializeSpruceBarkInputData(IN);
                half3 litNormal = normalize(lerp(inputData.normalWS, half3(0.0h, 1.0h, 0.0h), _LightWrap * 0.18h));
                inputData.normalWS = litNormal;

                SurfaceData surfaceData = InitializeSpruceBarkSurfaceData(barkColor);
                half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);

                Light mainLight = GetMainLight(inputData.shadowCoord);
                half shadowSide = saturate(1.0h - dot(litNormal, mainLight.direction) * 0.5h - 0.5h);
                color.rgb = lerp(color.rgb, color.rgb * _CoolShadowColor.rgb, shadowSide * 0.18h);

                return half4(saturate(color.rgb), 1.0h);
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionInputs.positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float3 positionWS = ApplyShadowBias(positionInputs.positionWS, normalInputs.normalWS, lightDirectionWS);
                OUT.positionCS = TransformWorldToHClip(positionWS);

                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
