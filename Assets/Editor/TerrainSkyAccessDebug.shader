Shader "Hidden/FirstSettlers/SkyAccessValidation"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/TerrainSkyAccess.hlsl"
            float4 _ProbePosition;
            float4 Vert(uint vertexID:SV_VertexID):SV_POSITION{return GetFullScreenTriangleVertexPosition(vertexID);}
            float4 Frag():SV_Target
            {
                float3 tile=floor(_ProbePosition.xyz/32),key=tile-_TerrainSkyOrigin.xyz;
                float index=round(SAMPLE_TEXTURE2D_LOD(_TerrainSkyTable,sampler_TerrainSkyData,(float2(key.x,key.y+8*key.z)+.5)/float2(16,128),0).r*255)-1;
                return float4(_TerrainSkyEnabled,TerrainSkyExposure(_ProbePosition.xyz),index,1);
            }
            ENDHLSL
        }
    }
}
