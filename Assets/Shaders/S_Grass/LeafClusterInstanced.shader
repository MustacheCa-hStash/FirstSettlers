Shader "FirstSettlers/Leaf Cluster Instanced"
{
    Properties
    {
        [MainTexture] _BaseMap("Painted Leaf Atlas", 2D) = "white" {}
        [MainColor] _BaseColor("Leaf Tint", Color) = (1,1,1,1)
        [ToggleUI] _UseAtlasColor("Use Painted Atlas Color", Float) = 0
        [PerRendererData] _LeafInstanceTint("Instance Tint", Vector) = (1,1,1,1)
        [PerRendererData] _LeafScatterParams("Scatter Seed, Amount, Spread, Enabled", Vector) = (1,1,1,0)
        [NoScaleOffset] _LeafVariationTex("Baked Scatter Data", 2D) = "black" {}
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.45
        _AmbientStrength("Minimum Ambient", Range(0,1)) = 0.12
        _FadeStart("Detail Fade Start", Float) = 20
        _FadeEnd("Detail Fade End", Float) = 28
    }
    SubShader
    {
        Tags { "ForestScatterIndirect"="True" "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off ZWrite On AlphaToMask On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_LeafVariationTex); SAMPLER(sampler_LeafVariationTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _LeafVariationTex_TexelSize;
            half4 _BaseColor;
            half _Cutoff, _AmbientStrength, _UseAtlasColor;
            float _FadeStart, _FadeEnd;
            float4 _LeafViewer;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(LeafProperties)
            UNITY_DEFINE_INSTANCED_PROP(float4, _LeafInstanceTint)
            UNITY_DEFINE_INSTANCED_PROP(float4, _LeafScatterParams)
        UNITY_INSTANCING_BUFFER_END(LeafProperties)
        #include "Assets/Shaders/ForestScatterInstance.hlsl"
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            float4 scatter : TEXCOORD1;
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
            half leafVisible : TEXCOORD5;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float4 scatter=ForestScatterParams(UNITY_ACCESS_INSTANCED_PROP(LeafProperties,_LeafScatterParams));
            output.leafVisible=1;
            if(scatter.w>.5 && input.scatter.w>.5)
            {
                float index=(clamp(scatter.x,1,8192)-1)*18+clamp(input.scatter.z,0,8)*2;
                float row=floor(index/256);
                float2 lookupUV=float2(index-row*256+.5,row+.5)*_LeafVariationTex_TexelSize.xy;
                float4 shape=SAMPLE_TEXTURE2D_LOD(_LeafVariationTex,sampler_LeafVariationTex,lookupUV,0);
                float4 placement=SAMPLE_TEXTURE2D_LOD(_LeafVariationTex,sampler_LeafVariationTex,
                    lookupUV+float2(_LeafVariationTex_TexelSize.x,0),0);
                float2x2 rotation=float2x2(shape.x,-shape.y,shape.y,shape.x);
                input.positionOS.xz=input.scatter.xy*scatter.z+placement.xy+
                    mul(rotation,input.positionOS.xz-input.scatter.xy)*shape.z;
                input.normalOS.xz=mul(rotation,input.normalOS.xz);
                // Keep the first leaf in both LODs; all other identities thin
                // consistently so each patch has a different leaf quantity.
                output.leafVisible=placement.z<scatter.y ? 1 : 0;
                if(output.leafVisible<.5)
                {
                    // All vertices of a hidden leaf form a degenerate primitive:
                    // no rasterization, atlas fetch, lighting or depth fragments.
                    output.positionCS=float4(0,0,0,1);
                    output.positionWS=0;output.normalWS=float3(0,1,0);
                    output.uv=0;output.color=0;output.fog=0;
                    return output;
                }
                input.color.rgb*=shape.w;
            }
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.color = input.color * _BaseColor * ForestScatterTint(UNITY_ACCESS_INSTANCED_PROP(LeafProperties, _LeafInstanceTint));
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        half4 LeafSample(Varyings input)
        {
            clip(input.leafVisible-.5);
            half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            clip(texel.a - _Cutoff);
            float2 viewer = _LeafViewer.w > 0.5 ? _LeafViewer.xz : _WorldSpaceCameraPos.xz;
            float fade = 1 - smoothstep(_FadeStart, max(_FadeEnd, _FadeStart + 0.01), distance(input.positionWS.xz, viewer));
            // Screen-space dither removes coverage without an alpha-blended overdraw layer.
            float dither = frac(dot(floor(input.positionCS.xy), float2(0.754877666, 0.569840296)));
            clip(fade - dither - 0.0001);
            texel.rgb = lerp(half3(1,1,1), texel.rgb, saturate(_UseAtlasColor));
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
            AlphaToMask Off
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
            AlphaToMask Off
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
