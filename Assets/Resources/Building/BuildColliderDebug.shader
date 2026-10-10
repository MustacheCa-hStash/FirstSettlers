Shader "FirstSettlers/BuildingColliderDebug"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" "RenderType"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; };
            Varyings Vertex(Attributes input)
            {
                Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);output.color=input.color;return output;
            }
            half4 Fragment(Varyings input):SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
