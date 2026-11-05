Shader "Hidden/FirstSettlers/LandformFieldValidation"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            #include "Assets/Shaders/WorldLandformFields.hlsl"
            float4 _DebugRect;
            struct V { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(appdata_img input) { V o; o.position=UnityObjectToClipPos(input.vertex);o.uv=input.texcoord;return o; }
            float4 frag(V input) : SV_Target
            {
                float2 p=_DebugRect.xy+input.uv*_DebugRect.zw;
                return float4(FSRegionalGeology(p),FSLandformNoise(p,_WorldLandformParams.y*2.2,_WorldLandformSeed+82004),1);
            }
            ENDHLSL
        }
    }
}
