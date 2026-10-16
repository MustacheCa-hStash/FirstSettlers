Shader "Hidden/FirstSettlers/FlameShadowDepth"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend One One
            BlendOp Min
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _FSFlameCaptureVP;
            float4 _FSFlameCaptureLight;
            TEXTURE2D(_CasterBaseMap);SAMPLER(sampler_CasterBaseMap);
            float4 _CasterBaseMap_ST;
            float _CasterCutoff;
            struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float2 uv:TEXCOORD1;};
            V vert(A i)
            {V o;o.positionWS=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=mul(_FSFlameCaptureVP,float4(o.positionWS,1));o.uv=i.uv*_CasterBaseMap_ST.xy+_CasterBaseMap_ST.zw;return o;}
            float frag(V i):SV_Target
            {
                if(_CasterCutoff>0)clip(SAMPLE_TEXTURE2D(_CasterBaseMap,sampler_CasterBaseMap,i.uv).a-_CasterCutoff);
                return saturate(length(i.positionWS-_FSFlameCaptureLight.xyz)*_FSFlameCaptureLight.w);
            }
            ENDHLSL
        }
    }
}
