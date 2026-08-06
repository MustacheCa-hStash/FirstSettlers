Shader "Custom/TreeBillboardAxisDiagnostic"
{
    Properties
    {
        [MainTexture] _BaseMap("Billboard Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        _RotationAxis("Rotation Axis 0=X 1=Y 2=Z", Range(0, 2)) = 1
        _RotationSpeed("Rotation Speed", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }

        LOD 100
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                float _RotationAxis;
                float _RotationSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float3 RotateAroundX(float3 p, float s, float c)
            {
                return float3(
                    p.x,
                    p.y * c - p.z * s,
                    p.y * s + p.z * c);
            }

            float3 RotateAroundY(float3 p, float s, float c)
            {
                return float3(
                    p.x * c + p.z * s,
                    p.y,
                    -p.x * s + p.z * c);
            }

            float3 RotateAroundZ(float3 p, float s, float c)
            {
                return float3(
                    p.x * c - p.y * s,
                    p.x * s + p.y * c,
                    p.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float angle = _Time.y * _RotationSpeed;
                float s = sin(angle);
                float c = cos(angle);
                float3 positionOS = IN.positionOS.xyz;

                if (_RotationAxis < 0.5)
                {
                    positionOS = RotateAroundX(positionOS, s, c);
                }
                else if (_RotationAxis < 1.5)
                {
                    positionOS = RotateAroundY(positionOS, s, c);
                }
                else
                {
                    positionOS = RotateAroundZ(positionOS, s, c);
                }

                VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
                OUT.positionCS = positionInputs.positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(baseSample.a - _Cutoff);

                return baseSample;
            }
            ENDHLSL
        }
    }
}
