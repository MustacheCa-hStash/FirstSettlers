Shader "Custom/TreeBillboardInstancedSimpleLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Tree Billboard Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.58
        _LightWrap("Billboard Light Softness", Range(0, 1)) = 0.72
        _Smoothness("Smoothness", Range(0, 1)) = 0.06
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.02
        [Toggle] _ForceBillboardFacing("Force Billboard Facing", Float) = 0
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

        Pass
        {
            Name "ForwardBillboard"
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
                half _Cutoff;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
                half _ForceBillboardFacing;
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
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

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

                float3 positionWS =
                    instanceOriginWS +
                    rightWS * (IN.positionOS.z * scaleZ) +
                    upWS * (IN.positionOS.y * scaleY);

                float3 normalPositionWS = TransformObjectToWorld(IN.positionOS.xyz);

#if defined(UNITY_INSTANCING_ENABLED)
                float useBillboardFacing = 1.0;
#else
                float useBillboardFacing = step(0.5, _ForceBillboardFacing);
#endif
                float3 finalPositionWS = lerp(normalPositionWS, positionWS, useBillboardFacing);
                half3 billboardNormalWS = normalize(_WorldSpaceCameraPos.xyz - instanceOriginWS);
                billboardNormalWS.y *= 0.25h;
                billboardNormalWS = normalize(lerp(billboardNormalWS, upWS, _LightWrap * 0.35h));

                OUT.positionCS = TransformWorldToHClip(finalPositionWS);
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

                InputData inputData = InitializeTreeSimpleLitInputData(IN.positionWS, IN.normalWS, IN.positionCS, IN.shadowCoord, _AmbientStrength);
                SurfaceData surfaceData = InitializeTreeSimpleLitSurfaceData(baseSample.rgb, baseSample.a, _Smoothness, _SpecularStrength);
                half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
                return half4(saturate(color.rgb), baseSample.a);
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
                half _Cutoff;
                half _AmbientStrength;
                half _LightWrap;
                half _Smoothness;
                half _SpecularStrength;
                half _ForceBillboardFacing;
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
