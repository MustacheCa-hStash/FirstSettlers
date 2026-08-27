Shader "Custom/RedMapleBillboardSimpleLitCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Red Maple Billboard Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base Tint", Color) = (1, 1, 1, 1)
        _BillboardLeafTint("Billboard Leaf Tint", Color) = (0.88, 0.06, 0.035, 1)
        [PerRendererData] _TreeLeafTint("Tree Leaf Tint Multiplier", Color) = (1, 1, 1, 1)
        _BillboardTintAverageColor("Billboard Average Leaf Tint", Color) = (0.88, 0.06, 0.035, 1)
        _BillboardTintCompression("Billboard Tint Compression", Range(0, 1)) = 0.35
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        _LeafMaskThreshold("Leaf White Threshold", Range(0, 1)) = 0.42
        _LeafMaskSoftness("Leaf White Softness", Range(0.001, 0.35)) = 0.14
        _PaleArtifactStrength("Pale Canopy Artifact Tint", Range(0, 1)) = 0.85
        _CanopyTintStart("Canopy Tint Start", Range(0, 1)) = 0.18
        _CanopyTintFade("Canopy Tint Fade", Range(0.001, 0.5)) = 0.18
        _LeafTintStrength("Leaf Tint Strength", Range(0, 1)) = 1.0
        _Brightness("Brightness", Range(0.25, 2)) = 1.0
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.56
        _LightWrap("Billboard Light Softness", Range(0, 1)) = 0.72
        _Smoothness("Smoothness", Range(0, 1)) = 0.06
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.02
        [Toggle] _ForceBillboardFacing("Force Billboard Facing", Float) = 0
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Wind Sway Strength", Range(0, 1)) = 0.08
        _WindSpeed("Wind Sway Speed", Range(0, 8)) = 1.1
        _WindFlutterStrength("Wind Edge Flutter Strength", Range(0, 1)) = 0.018
        _WindFlutterSpeed("Wind Edge Flutter Speed", Range(0, 16)) = 3.6
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
            Name "ForwardRedMapleBillboard"
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
                half4 _BillboardLeafTint;
                half4 _BillboardTintAverageColor;
                half _BillboardTintCompression;
                half _Cutoff;
                half _LeafMaskThreshold;
                half _LeafMaskSoftness;
                half _PaleArtifactStrength;
                half _CanopyTintStart;
                half _CanopyTintFade;
                half _LeafTintStrength;
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

            UNITY_INSTANCING_BUFFER_START(TreeBillboardInstanceProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TreeLeafTint)
            UNITY_INSTANCING_BUFFER_END(TreeBillboardInstanceProperties)

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

                half lowChannel = min(baseSample.r, min(baseSample.g, baseSample.b));
                half highChannel = max(baseSample.r, max(baseSample.g, baseSample.b));
                half luma = dot(baseSample.rgb, half3(0.299h, 0.587h, 0.114h));
                half neutralness = 1.0h - smoothstep(0.08h, 0.28h, highChannel - lowChannel);
                half canopyMask = smoothstep(
                    _CanopyTintStart,
                    saturate(_CanopyTintStart + _CanopyTintFade),
                    IN.uv.y);

                half leafMask = smoothstep(
                    _LeafMaskThreshold,
                    saturate(_LeafMaskThreshold + _LeafMaskSoftness),
                    lowChannel);
                half paleArtifactMask = smoothstep(0.28h, 0.58h, luma) * neutralness * canopyMask * _PaleArtifactStrength;
                leafMask = max(leafMask, paleArtifactMask);

                half4 instanceLeafTint = UNITY_ACCESS_INSTANCED_PROP(TreeBillboardInstanceProperties, _TreeLeafTint);
                half directTintAmount = 1.0h - saturate(instanceLeafTint.a);
                half3 treeLeafTint = lerp(_BillboardLeafTint.rgb * instanceLeafTint.rgb, instanceLeafTint.rgb, directTintAmount);
                half3 compressionTarget = lerp(_BillboardTintAverageColor.rgb, instanceLeafTint.rgb, directTintAmount);
                treeLeafTint = lerp(treeLeafTint, compressionTarget, saturate(_BillboardTintCompression));
                half3 color = lerp(baseSample.rgb, treeLeafTint, leafMask * _LeafTintStrength);
                half alpha = baseSample.a * _BaseColor.a;
                InputData inputData = InitializeTreeSimpleLitInputData(IN.positionWS, IN.normalWS, IN.positionCS, IN.shadowCoord, _AmbientStrength);
                SurfaceData surfaceData = InitializeTreeSimpleLitSurfaceData(saturate(color * _Brightness), alpha, _Smoothness, _SpecularStrength);
                half4 litColor = UniversalFragmentBlinnPhong(inputData, surfaceData);
                return half4(saturate(litColor.rgb), alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
