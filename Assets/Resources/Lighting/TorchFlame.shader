Shader "FirstSettlers/TorchFlame"
{
    Properties
    {
        _BaseMap("Looping flame atlas",2D)="white"{}
        [HDR]_Emission("Flame brightness",Color)=(3,3,3,1)
        _Phase("Instance phase",Float)=0
        _Flicker("Flicker",Range(0,.2))=.08
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            Cull Off ZWrite Off ZTest LEqual Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _Emission;float _Phase,_Flicker;
            CBUFFER_END
            struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            float Flicker(float t)
            {return 1+_Flicker*(sin(t*7.3+_Phase)*.5+sin(t*11.1+_Phase*1.7)*.3+sin(t*17.9+_Phase*2.3)*.2);}
            V vert(A i)
            {
                V o;float3 origin=TransformObjectToWorld(float3(0,0,0));
                float3 view=normalize(GetCameraPositionWS()-origin);
                float3 up=float3(0,1,0);float3 right=cross(up,view);
                right=length(right)>.05?normalize(right):normalize(UNITY_MATRIX_I_V._m00_m10_m20);
                // Tilt the card gently towards overhead viewers so the flame does not disappear.
                up=normalize(lerp(up,normalize(cross(view,right)),smoothstep(.7,.98,abs(view.y))));
                float width=length(unity_ObjectToWorld._m00_m10_m20);
                float height=length(unity_ObjectToWorld._m01_m11_m21);
                float bend=sin(_Time.y*3.4+_Phase+i.uv.y*6)*.035*i.uv.y*i.uv.y;
                float3 position=origin+right*(i.positionOS.x+bend)*width+up*i.positionOS.y*height*Flicker(_Time.y);
                o.positionCS=TransformWorldToHClip(position);o.uv=i.uv;return o;
            }
            float2 AtlasUV(float2 uv,float frame)
            {
                float2 cell=float2(fmod(frame,6),5-floor(frame/6));
                // Four-pixel gutters within every 128px cell prevent cross-frame bleeding.
                uv=(uv*120+4)/128;
                return (cell+uv)/6;
            }
            half4 frag(V i):SV_Target
            {
                float frame=fmod(_Time.y*24+_Phase*7,36);
                half4 a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,AtlasUV(i.uv,floor(frame)));
                half4 b=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,AtlasUV(i.uv,fmod(floor(frame)+1,36)));
                half4 flame=lerp(a,b,frac(frame));
                half alpha=flame.a;
                return half4(flame.rgb*_Emission.rgb*Flicker(_Time.y)*alpha,alpha);
            }
            ENDHLSL
        }
    }
}
