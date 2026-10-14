Shader "Custom/DaisyWeedsPainted"
{
    Properties
    {
        [MainTexture] _BaseMap("Painted Daisy and Leaf Atlas",2D)="white"{}
        [MainColor] _BaseColor("Tint",Color)=(1,1,1,1)
        _StemColor("Stem and Calyx",Color)=(0.28,0.43,0.09,1)
        _Cutoff("Alpha Cutoff",Range(0,1))=0.45
        _AmbientStrength("Ambient Floor",Range(0,1))=0.18
        _NormalUpBlend("Soft Leaf Lighting",Range(0,1))=0.20
        _InstanceTintStrength("Subtle Petal Variation",Range(0,0.3))=0.12
        _WindStrength("Gentle Sway",Range(0,0.05))=0.018
        [PerRendererData] _FlowerPetalColor("Instance Petal Tint",Color)=(1,1,1,1)
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
        Cull Off ZWrite On AlphaToMask On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Assets/Shaders/TreeNightLighting.hlsl"
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor,_StemColor;
            half _Cutoff,_AmbientStrength,_NormalUpBlend,_InstanceTintStrength,_WindStrength;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(DaisyInstances)
            UNITY_DEFINE_INSTANCED_PROP(float4,_FlowerPetalColor)
        UNITY_INSTANCING_BUFFER_END(DaisyInstances)
        struct Attributes
        {
            float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;
            float2 uv:TEXCOORD2;half4 color:TEXCOORD3;half fog:TEXCOORD4;
            float4 shadowCoord:TEXCOORD5;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        float3 Sway(float3 p)
        {
            float3 origin=TransformObjectToWorld(float3(0,0,0));
            float phase=dot(origin.xz,float2(.41,.67));float bend=saturate(p.y/1.22);bend*=bend;
            p.x+=sin(_Time.y*1.6+phase+p.y*.8)*_WindStrength*bend;
            p.z+=cos(_Time.y*1.1+phase)*_WindStrength*.6*bend;
            return p;
        }
        Varyings Vert(Attributes input)
        {
            Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs positions=GetVertexPositionInputs(Sway(input.positionOS.xyz));
            o.positionWS=positions.positionWS;o.positionCS=positions.positionCS;o.shadowCoord=GetShadowCoord(positions);
            o.normalWS=TransformObjectToWorldNormal(input.normalOS);
            o.uv=TRANSFORM_TEX(input.uv,_BaseMap);o.color=input.color;o.fog=ComputeFogFactor(o.positionCS.z);return o;
        }
        half4 Surface(Varyings input)
        {
            half4 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv);
            half alpha=lerp(1,tex.a,input.color.a)*_BaseColor.a;clip(alpha-_Cutoff);
            half3 albedo=lerp(_StemColor.rgb,tex.rgb,input.color.a)*_BaseColor.rgb*lerp(.88h,1.0h,input.color.g);
            half3 tint=UNITY_ACCESS_INSTANCED_PROP(DaisyInstances,_FlowerPetalColor).rgb;
            albedo*=lerp(half3(1,1,1),clamp(tint,.75h,1.1h),input.color.r*_InstanceTintStrength);
            return half4(albedo,alpha);
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags{"LightMode"="UniversalForwardOnly"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half4 Frag(Varyings input,FRONT_FACE_TYPE facing:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 surface=Surface(input);
                half3 normal=normalize(input.normalWS)*IS_FRONT_VFACE(facing,1.0h,-1.0h);
                normal=normalize(lerp(normal,half3(0,1,0),_NormalUpBlend*input.color.a));
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord=input.shadowCoord;
                #else
                    float4 shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun=GetMainLight(shadowCoord);
                half diffuse=saturate((dot(normal,sun.direction)+.16h)/1.16h);
                half3 light=max(SampleSH(normal),TreeNightAmbientFloor(_AmbientStrength).xxx);
                light+=sun.color*diffuse*sun.shadowAttenuation*sun.distanceAttenuation*.85h;
                return half4(MixFog(surface.rgb*light,input.fog),surface.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ColorMask 0 ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection,_LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings o=Vert(input);UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction=normalize(_LightPosition-o.positionWS);
                #else
                    float3 direction=_LightDirection;
                #endif
                half3 normal=normalize(o.normalWS);if(dot(normal,direction)<0)normal=-normal;
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,normal,direction));
                #if UNITY_REVERSED_Z
                    o.positionCS.z=min(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z=max(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 DepthFrag(Varyings input):SV_Target{UNITY_SETUP_INSTANCE_ID(input);Surface(input);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings input):SV_Target{UNITY_SETUP_INSTANCE_ID(input);Surface(input);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags{"LightMode"="DepthNormalsOnly"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFrag(Varyings input,FRONT_FACE_TYPE facing:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);Surface(input);
                half3 normal=normalize(input.normalWS)*IS_FRONT_VFACE(facing,1.0h,-1.0h);
                normal=normalize(lerp(normal,half3(0,1,0),_NormalUpBlend*input.color.a));
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal)*.5+.5)),0);
                #else
                    return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
