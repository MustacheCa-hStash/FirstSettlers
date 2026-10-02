Shader "FirstSettlers/Forest Grass Instanced"
{
    Properties
    {
        [MainColor] _BaseColor("Plain Olive Blade", Color) = (0.29,0.40,0.17,1)
        _DryColor("Plain Dry Blade", Color) = (0.38,0.33,0.18,1)
        _RootColor("Brown Root", Color) = (0.19,0.16,0.10,1)
        _RootBlendHeight("Root Transition Fraction", Range(0.01,0.2)) = 0.08
        _InstanceVariation("Uniform Tuft Variation", Range(0,0.2)) = 0.06
        _AmbientStrength("Minimum Ambient", Range(0,1)) = 0.2
        _UpwardNormalBlend("Soft Blade Lighting", Range(0,1)) = 0.65
        [Toggle] _ReceiveShadows("Receive Shadows", Float) = 1
        [PerRendererData] _GrassInstanceData("Grass Instance Data", Vector) = (1,0,0,0)
        _WindDirection("Wind Direction", Vector) = (1,0,0.35,0)
        _WindStrength("Wind Bend Strength", Range(0,1)) = 0.018
        _WindSpeed("Wind Speed", Range(0,8)) = 2.2
        _WindFlutterStrength("Wind Tip Flutter", Range(0,1)) = 0.005
        _WindFlutterSpeed("Wind Flutter Speed", Range(0,16)) = 7.5
        _WindGustScale("Wind Gust Scale", Range(0.01,2)) = 0.55
    }
    SubShader
    {
        Tags { "GrassIndirect"="True" "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On ZTest LEqual
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Assets/Shaders/GrassIndirectInstance.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _DryColor, _RootColor;
            half _RootBlendHeight, _InstanceVariation, _AmbientStrength, _UpwardNormalBlend, _ReceiveShadows;
            float4 _WindDirection;
            float _WindStrength, _WindSpeed, _WindFlutterStrength, _WindFlutterSpeed, _WindGustScale;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(GrassInstanceProperties)
            UNITY_DEFINE_INSTANCED_PROP(float4, _GrassInstanceData)
        UNITY_INSTANCING_BUFFER_END(GrassInstanceProperties)
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
            half4 color : TEXCOORD2;
            half bladeHeight : TEXCOORD3;
            half fog : TEXCOORD4;
            float4 shadowCoord : TEXCOORD5;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            half4 data = GetGrassInstanceData(UNITY_ACCESS_INSTANCED_PROP(GrassInstanceProperties, _GrassInstanceData));
            float3 position = TransformObjectToWorld(input.positionOS.xyz);
            float height = saturate(input.uv.y);
            // Same slow gust/tip movement as the grassland tuft, with much less travel.
            float2 wind = normalize(_WindDirection.xz + float2(0.0001,0));
            float spatial = dot(position.xz, wind.yx * float2(0.93,-0.71));
            float phase = data.y * 6.2831853;
            float gust = sin(dot(position.xz,wind) * _WindGustScale + _Time.y * _WindSpeed + phase + spatial * 0.4);
            float flutter = sin(_Time.y * _WindFlutterSpeed + spatial * 3.1 + phase * 1.37);
            float bend = gust * _WindStrength * height * height;
            position.xz += wind * (bend + flutter * _WindFlutterStrength * height);
            position.y -= abs(bend) * 0.18;
            output.positionWS = position;
            output.positionCS = TransformWorldToHClip(position);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                output.shadowCoord = ComputeScreenPos(output.positionCS);
            #else
                output.shadowCoord = TransformWorldToShadowCoord(position);
            #endif
            output.normalWS = normalize(lerp(TransformObjectToWorldNormal(input.normalOS), half3(0,1,0), _UpwardNormalBlend));
            // Constant value/tone over each whole blade; alpha is a dry-blade selector.
            half variation = 1 + (data.y * 2 - 1) * _InstanceVariation;
            output.color = half4(lerp(_BaseColor.rgb, _DryColor.rgb, input.color.a) * input.color.rgb * variation,1);
            output.bladeHeight = height;
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        half4 frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half root = 1 - smoothstep(0, max(_RootBlendHeight,0.001h), input.bladeHeight);
            half3 albedo = lerp(input.color.rgb, _RootColor.rgb, root);
            half3 normal = normalize(input.normalWS);
            Light light = GetMainLight(input.shadowCoord);
            half shadow = lerp(1, light.shadowAttenuation, saturate(_ReceiveShadows));
            half3 lighting = max(SampleSH(normal), _AmbientStrength.xxx) + light.color *
                (0.35h + saturate(dot(normal,light.direction)) * 0.65h) * shadow * light.distanceAttenuation;
            return half4(MixFog(albedo * lighting,InitializeInputDataFog(float4(input.positionWS,1),input.fog)),1);
        }
        half4 depth(Varyings input) : SV_Target { return 0; }
        half4 depthNormal(Varyings input) : SV_Target
        {
            float3 normal = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normal);
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)),0);
            #else
                return half4(normal,0);
            #endif
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma target 4.5 PROCEDURAL_INSTANCING_ON
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupGrassIndirect
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma target 4.5 PROCEDURAL_INSTANCING_ON
            #pragma vertex vert
            #pragma fragment depth
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupGrassIndirect
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma target 4.5 PROCEDURAL_INSTANCING_ON
            #pragma vertex vert
            #pragma fragment depthNormal
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupGrassIndirect
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
