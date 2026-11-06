Shader "Hidden/TreeOutlinePressure"
{
    Properties { _VertexIterations("Vertex iterations", Int) = 0 _FragmentIterations("Fragment iterations", Int) = 0 }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            int _VertexIterations, _FragmentIterations;
            struct A { float4 position : POSITION; };
            struct V { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a)
            {
                V o; float3 v = a.position.xyz + float3(.12,.34,.56);
                [loop] for (int i = 0; i < _VertexIterations; i++) v = sin(v.yzx * 1.123 + v.zxy * .873 + i * .015);
                o.position = float4(a.position.xy + v.xy * .00001, .9, 1); o.uv = a.position.xy; return o;
            }
            float4 frag(V v) : SV_Target
            {
                float3 color = float3(v.uv * .1, .3);
                [loop] for (int i = 0; i < _FragmentIterations; i++) color = sin(color.yzx * 1.123 + color.zxy * .873 + i * .015);
                return float4(.02 + abs(color) * .01, 1);
            }
            ENDHLSL
        }
    }
}
