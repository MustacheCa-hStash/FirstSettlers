Shader "Hidden/FirstSettlers/CaveLightValidation"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/LocalFlameLighting.hlsl"
            float4 _ProbePosition;
            float4 Vert(uint id:SV_VertexID):SV_POSITION{return GetFullScreenTriangleVertexPosition(id);}
            float4 Frag():SV_Target
            {
                float3 p=_ProbePosition.xyz,delta=p-_FSFlamePositions[0].xyz;
                return float4(FSFlameHead(p)>0?1:0,FSFlameVisibility(0,p,delta,float3(0,1,0),false),FSFlameVisibility(0,p,delta,float3(0,1,0),true),FSFlameUnit(0,p,float3(0,1,0),float3(0,1,0)).x);
            }
            ENDHLSL
        }
    }
}
