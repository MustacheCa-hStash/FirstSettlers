Shader "Custom/BumblebeeInstancedMatte"
{
    Properties
    {
        [MainTexture] _BaseMap("Bumblebee Color Atlas (UV0)", 2D) = "white" {}
        [MainColor] _BaseColor("Texture Tint", Color) = (1, 1, 1, 1)
        _AmbientStrength("Ambient Light", Range(0, 1)) = 0.7
        _DirectStrength("Sun Light", Range(0, 1)) = 0.3
        _FlapFrequency("Flaps Per Second", Range(0, 20)) = 12
        _FlapOpenAngle("Open Wing Angle", Range(-20, 45)) = 3
        _FlapClosedAngle("Raised Wing Angle", Range(0, 85)) = 35
        _RestFlapAmplitude("Resting Flap Size", Range(0, 1)) = 0.35
        _HindWingLag("Hind Wing Phase Lag", Range(-1, 1)) = 0.14
        [HideInInspector] _WingHingeX("Wing Hinge X (Mesh Local)", Float) = 0.1
        [PerRendererData] _ButterflyInstanceData("Color, Phase, Flap Size, Rest State", Vector) = (0, 0, 1, -1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100
        Cull Off
        ZWrite On

        Pass
        {
            Name "BumblebeeForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _AmbientStrength;
                half _DirectStrength;
                float _FlapFrequency;
                float _FlapOpenAngle;
                float _FlapClosedAngle;
                float _RestFlapAmplitude;
                float _HindWingLag;
                float _WingHingeX;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(BumblebeeProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ButterflyInstanceData)
            UNITY_INSTANCING_BUFFER_END(BumblebeeProperties)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 wingMotion : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionOS = IN.positionOS.xyz;
                float3 normalOS = IN.normalOS;
                if (abs(IN.wingMotion.x) > 0.5)
                {
                    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(BumblebeeProperties, _ButterflyInstanceData);
                    float side = positionOS.x < 0.0 ? -1.0 : 1.0;
                    // Prefab previews use time; the instanced fauna manager supplies its own phase.
                    float phase = (instanceData.w < 0.0
                        ? _Time.y * _FlapFrequency * 6.2831853 : instanceData.y)
                        - IN.wingMotion.y * _HindWingLag;
                    float raised = pow(saturate(0.5 + 0.5 * sin(phase)), 1.2);
                    float restSize = lerp(1.0, _RestFlapAmplitude, saturate(instanceData.w));
                    float angle = radians(lerp(_FlapOpenAngle,
                        lerp(_FlapOpenAngle, _FlapClosedAngle, restSize), raised)) * instanceData.z * side;
                    float sine;
                    float cosine;
                    sincos(angle, sine, cosine);
                    float hingeX = side * _WingHingeX;
                    float x = positionOS.x - hingeX;
                    float y = positionOS.y;
                    positionOS.x = hingeX + cosine * x - sine * y;
                    positionOS.y = sine * x + cosine * y;

                    float nx = normalOS.x;
                    float ny = normalOS.y;
                    normalOS.x = cosine * nx - sine * ny;
                    normalOS.y = sine * nx + cosine * ny;
                }

                OUT.positionCS = TransformObjectToHClip(positionOS);
                OUT.normalWS = TransformObjectToWorldNormal(normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;
                half3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                // Diffuse lighting only. No specular, metallic, or smoothness term.
                half facing = abs(dot(normalWS, mainLight.direction));
                half3 lightColor = saturate(_AmbientStrength.xxx +
                    mainLight.color * (_DirectStrength * facing));
                return half4(albedo * lightColor, 1.0);
            }
            ENDHLSL
        }
    }
}
