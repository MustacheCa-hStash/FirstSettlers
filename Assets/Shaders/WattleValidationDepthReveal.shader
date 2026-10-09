Shader "Hidden/Building/WattleDepthReveal"
{
    Properties { _Color("Color",Color) = (0,1,0,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            float4 Vertex(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 Fragment() : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
