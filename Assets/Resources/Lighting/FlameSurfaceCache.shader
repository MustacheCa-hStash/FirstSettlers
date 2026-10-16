Shader "Hidden/FirstSettlers/FlameSurfaceCache"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Shaders/LocalFlameLighting.hlsl"
        float4x4 _FSSurfaceVP;
        float3 _FSSurfaceCenter,_FSSurfaceRight,_FSSurfaceUp,_FSSurfaceDirection;
        int _FSSurfaceSlice;
        ENDHLSL
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            struct A {float4 p:POSITION;float3 n:NORMAL;};
            struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;};
            V vert(A i){V o;o.world=TransformObjectToWorld(i.p.xyz);o.normal=TransformObjectToWorldNormal(i.n);o.p=mul(_FSSurfaceVP,float4(o.world,1));return o;}
            float4 frag(V i):SV_Target
            {
                float3 n=normalize(i.normal);uint face=FSFlameFace(n);
                clip(dot(FSFlameAxis(face),_FSSurfaceDirection)-.5);
                clip(4.001-abs(i.world-_FSSurfaceCenter));
                return float4(n,dot(i.world-_FSSurfaceCenter,_FSSurfaceDirection)+5);
            }
            ENDHLSL
        }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
            V vert(uint id:SV_VertexID){V o;o.p=GetFullScreenTriangleVertexPosition(id);o.uv=GetFullScreenTriangleTexCoord(id);return o;}
            float4 frag(V i):SV_Target
            {
                int2 pixel=(int2)i.p.xy;
                float4 sample=LOAD_TEXTURE2D_ARRAY_LOD(_FSSurfaceNormals,pixel,_FSSurfaceSlice,0);
                if(sample.w<.5)return 0;
                float2 uv=(pixel+.5)/32.0;
                float3 world=_FSSurfaceCenter+_FSSurfaceRight*((uv.x-.5)*8)+_FSSurfaceUp*((uv.y-.5)*8)+_FSSurfaceDirection*(sample.w-5);
                return float4(FSFlameDirect(world,sample.xyz,true,sample.xyz),1);
            }
            ENDHLSL
        }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            TEXTURE2D(_FSSurfaceMipNormals);TEXTURE2D(_FSSurfaceMipIrradiance);
            int _FSSurfaceReadMip;
            int _FSSurfaceMipOutput;
            struct V {float4 p:SV_POSITION;};
            struct O {float4 normal;float4 irradiance;};
            V vert(uint id:SV_VertexID){V o;o.p=GetFullScreenTriangleVertexPosition(id);return o;}
            O Evaluate(V i)
            {
                O o;o.normal=0;o.irradiance=0;int2 origin=(int2)i.p.xy*2;
                float4 ns[4],cs[4];float4 meanN=0,meanC=0;
                [unroll]for(int tap=0;tap<4;tap++)
                {int2 pixel=origin+int2(tap&1,tap>>1);ns[tap]=LOAD_TEXTURE2D_LOD(_FSSurfaceMipNormals,pixel,_FSSurfaceReadMip);cs[tap]=LOAD_TEXTURE2D_LOD(_FSSurfaceMipIrradiance,pixel,_FSSurfaceReadMip);if(ns[tap].w<.5)return o;meanN+=ns[tap]*.25;meanC+=cs[tap]*.25;}
                meanN.xyz=normalize(meanN.xyz);float step=8.0/(32>>_FSSurfaceReadMip);
                [unroll]for(int tap=0;tap<4;tap++)
                {
                    float2 offset=float2(tap&1,tap>>1)-.5;
                    float3 delta=_FSSurfaceRight*(offset.x*step)+_FSSurfaceUp*(offset.y*step)+_FSSurfaceDirection*(ns[tap].w-meanN.w);
                    if(dot(ns[tap].xyz,meanN.xyz)<.98||abs(dot(meanN.xyz,delta))>.012)return o;
                    if(any(abs(cs[tap].rgb-meanC.rgb)>max(meanC.rgb*.25,.001)))return o;
                }
                o.normal=meanN;o.irradiance=meanC;return o;
            }
            float4 frag(V i):SV_Target {O o=Evaluate(i);return _FSSurfaceMipOutput==0?o.normal:o.irradiance;}
            ENDHLSL
        }
    }
}
