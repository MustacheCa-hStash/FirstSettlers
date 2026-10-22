Shader "Hidden/StandingTreeSpatialValidation"
{
    Properties { _StandingTreeEnabled("Standing tree instances", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/StandingTreeInstance.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 color : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A input)
            {
                V output; UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float4 leaves = UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeLeaves);
                float4 bark = UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeBark);
                float4 appearance = UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeAppearance);
                output.color = float3(leaves.r, bark.g, appearance.x * .5 + appearance.y * .5);
                return output;
            }
            half4 frag(V input) : SV_Target
            { UNITY_SETUP_INSTANCE_ID(input); ApplyStandingTreeFade(input.positionCS.xy); return half4(input.color, 1); }
            ENDHLSL
        }
    }
}
