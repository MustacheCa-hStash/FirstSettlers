Shader "FirstSettlers/Forest Fern Instanced"
{
    Properties
    {
        [MainColor] _BaseColor("Fern Tint", Color) = (1,1,1,1)
        _WindStrength("Frond Sway", Range(0,0.1)) = 0.016
        [PerRendererData] _LeafInstanceTint("Instance Tint", Vector) = (1,1,1,1)
        _AmbientStrength("Minimum Ambient", Range(0,1)) = 0.12
        _FadeStart("Detail Fade Start", Float) = 20
        _FadeEnd("Detail Fade End", Float) = 28
    }
    SubShader
    {
        Tags { "ForestScatterIndirect"="True" "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _AmbientStrength, _WindStrength;
            float _FadeStart, _FadeEnd;
            float4 _LeafViewer;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(LeafProperties)
            UNITY_DEFINE_INSTANCED_PROP(float4, _LeafInstanceTint)
        UNITY_INSTANCING_BUFFER_END(LeafProperties)
        #include "Assets/Shaders/ForestScatterInstance.hlsl"
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
            half4 color : TEXCOORD3;
            half fog : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float sway = sin(_Time.y * 1.3 + dot(output.positionWS.xz,float2(.7,.4))) * _WindStrength * saturate(input.positionOS.y * 2.5);
            output.positionWS.xz += float2(sway,sway*.4);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = input.uv;
            output.color = input.color * _BaseColor * ForestScatterTint(UNITY_ACCESS_INSTANCED_PROP(LeafProperties, _LeafInstanceTint));
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        half4 LeafSample(Varyings input)
        {
            half4 texel = half4(1,1,1,1);
            float2 viewer = _LeafViewer.w > 0.5 ? _LeafViewer.xz : _WorldSpaceCameraPos.xz;
            float fade = 1 - smoothstep(_FadeStart, max(_FadeEnd, _FadeStart + 0.01), distance(input.positionWS.xz, viewer));
            // Screen-space dither removes coverage without an alpha-blended overdraw layer.
            float dither = frac(dot(floor(input.positionCS.xy), float2(0.754877666, 0.569840296)));
            clip(fade - dither - 0.0001);
            return texel;
        }
        half4 frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 texel = LeafSample(input);
            half3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0h, -1.0h);
            Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
            half3 ambient = max(SampleSH(normal), _AmbientStrength.xxx);
            half3 lighting = ambient + mainLight.color * saturate(dot(normal, mainLight.direction)) *
                mainLight.distanceAttenuation * mainLight.shadowAttenuation;
            return half4(MixFog(texel.rgb * input.color.rgb * lighting,
                InitializeInputDataFog(float4(input.positionWS,1),input.fog)), texel.a);
        }
        half4 depth(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            LeafSample(input);
            return 0;
        }
        half4 depthNormal(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            LeafSample(input);
            float3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normal);
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
            #else
                return half4(normal, 0);
            #endif
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5 PROCEDURAL_INSTANCING_ON
            #pragma instancing_options procedural:SetupForestScatter
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
            #pragma vertex vert
            #pragma fragment depth
            #pragma multi_compile_instancing
            #pragma target 4.5 PROCEDURAL_INSTANCING_ON
            #pragma instancing_options procedural:SetupForestScatter
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment depthNormal
            #pragma multi_compile_instancing
            #pragma target 4.5 PROCEDURAL_INSTANCING_ON
            #pragma instancing_options procedural:SetupForestScatter
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
