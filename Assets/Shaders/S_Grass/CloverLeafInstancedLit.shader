Shader "Custom/CloverLeafInstancedLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Leaf Albedo / Alpha", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        _NormalUpBlend("Leaf Upward Normal Blend", Range(0,1)) = 0.25
        _AmbientStrength("Minimum Ambient", Range(0,1)) = 0.16
        _ReceiveShadows("Receive Shadows", Range(0,1)) = 1
        _InstanceVariationStrength("Instance Tone Variation", Range(0,0.5)) = 0.10
        _FadeStartDistance("Fade Start", Float) = 42
        _FadeEndDistance("Fade End", Float) = 58
        [PerRendererData] _CloverInstanceData("Clover Instance Data", Vector) = (0,0.5,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        ZWrite On
        AlphaToMask On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff, _NormalUpBlend, _AmbientStrength, _ReceiveShadows;
            half _InstanceVariationStrength;
            float _FadeStartDistance, _FadeEndDistance;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(CloverInstanceProperties)
            UNITY_DEFINE_INSTANCED_PROP(float4, _CloverInstanceData)
        UNITY_INSTANCING_BUFFER_END(CloverInstanceProperties)
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
        };
        half Hash21(float2 p)
        {
            float3 q=frac(float3(p.xyx)*float3(.1031,.1030,.0973));
            q+=dot(q,q.yzx+33.33);
            return frac((q.x+q.y)*q.z);
        }
        Varyings Vert(Attributes v)
        {
            Varyings o=(Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o);
            o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS=TransformWorldToHClip(o.positionWS);
            // Vertex alpha distinguishes textured leaves from opaque stems/florets.
            o.normalWS=normalize(lerp(TransformObjectToWorldNormal(v.normalOS),
                half3(0,1,0),_NormalUpBlend*v.color.a));
            o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.color=v.color;
            o.fog=ComputeFogFactor(o.positionCS.z);
            return o;
        }
        half4 Surface(Varyings i)
        {
            half4 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
            half alpha=lerp(1,tex.a,i.color.a)*_BaseColor.a;
            clip(alpha-_Cutoff);
            return half4(lerp(i.color.rgb,tex.rgb*i.color.rgb,i.color.a)*_BaseColor.rgb,alpha);
        }
        void Fade(Varyings i)
        {
            UNITY_SETUP_INSTANCE_ID(i);
            float4 data=UNITY_ACCESS_INSTANCED_PROP(CloverInstanceProperties,_CloverInstanceData);
            float fade=_FadeEndDistance>_FadeStartDistance
                ? saturate((_FadeEndDistance-distance(GetCameraPositionWS(),i.positionWS))/max(.001,_FadeEndDistance-_FadeStartDistance)) : 1;
            clip(fade-Hash21(floor(i.positionCS.xy)+data.xy*float2(97.13,41.71)));
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half4 Frag(Varyings i, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 surface=Surface(i); Fade(i);
                half3 n=normalize(i.normalWS);
                n*=IS_FRONT_VFACE(frontFace,1,-1);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse=saturate(dot(n,light.direction));
                half shadow=lerp(1,light.shadowAttenuation,_ReceiveShadows);
                // Modest wrapped light keeps thin leaf undersides readable.
                half3 ambient=max(SampleSH(n),_AmbientStrength.xxx);
                half3 direct=light.color*(.12h+.88h*diffuse)*shadow;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    ambient*=ao.indirectAmbientOcclusion;
                    direct*=ao.directAmbientOcclusion;
                #endif
                half3 illumination=ambient+direct;
                half4 data=UNITY_ACCESS_INSTANCED_PROP(CloverInstanceProperties,_CloverInstanceData);
                half tone=1+(data.y-.5h)*2*_InstanceVariationStrength;
                return half4(MixFog(surface.rgb*illumination*tone,i.fog),surface.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalsFrag(Varyings i, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                Surface(i); Fade(i);
                float3 n=normalize(i.normalWS)*IS_FRONT_VFACE(frontFace,1,-1);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct=PackNormalOctQuadEncode(n)*.5+.5;
                    return half4(PackFloat2To888(saturate(oct)),0);
                #else
                    return half4(n,0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings i) : SV_Target { Surface(i); Fade(i); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings ShadowVert(Attributes v)
            {
                Varyings o=Vert(v);
                float3 n=TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction=normalize(_LightPosition-o.positionWS);
                #else
                    float3 direction=_LightDirection;
                #endif
                n*=dot(n,direction)<0 ? -1 : 1;
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,n,direction));
                #if UNITY_REVERSED_Z
                    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #else
                    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
                #endif
                return o;
            }
            half4 ShadowFrag(Varyings i) : SV_Target { Surface(i); return 0; }
            ENDHLSL
        }
    }
}
