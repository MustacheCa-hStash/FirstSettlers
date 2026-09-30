Shader "Custom/ButterflyInstancedLit"
{
    Properties
    {
        _Orange("Pastel Orange", Color) = (1.0, 0.64, 0.43, 1)
        _Yellow("Pastel Yellow", Color) = (1.0, 0.89, 0.52, 1)
        _Pink("Pastel Pink", Color) = (1.0, 0.62, 0.75, 1)
        _Purple("Pastel Purple", Color) = (0.76, 0.66, 0.96, 1)
        _Blue("Pastel Blue", Color) = (0.59, 0.82, 0.96, 1)
        _AmbientStrength("Ambient Light", Range(0, 1)) = 0.55
        _DirectStrength("Sun Light", Range(0, 1)) = 0.45
        _FlapFrequency("Flaps Per Second", Range(0, 15)) = 5.5
        _FlapOpenAngle("Open Wing Angle", Range(-20, 45)) = 3
        _FlapClosedAngle("Raised Wing Angle", Range(0, 85)) = 67
        _RestFlapAmplitude("Resting Flap Size", Range(0, 1)) = 0.45
        _HindWingLag("Hind Wing Phase Lag", Range(-1, 1)) = 0.14
        [HideInInspector] _WingHingeX("Wing Hinge X (Mesh Local)", Float) = 5
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
            Name "ButterflyForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Orange;
                half4 _Yellow;
                half4 _Pink;
                half4 _Purple;
                half4 _Blue;
                half _AmbientStrength;
                half _DirectStrength;
                float _FlapFrequency;
                float _FlapOpenAngle;
                float _FlapClosedAngle;
                float _RestFlapAmplitude;
                float _HindWingLag;
                float _WingHingeX;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(ButterflyProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ButterflyInstanceData)
            UNITY_INSTANCING_BUFFER_END(ButterflyProperties)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 wingMotion : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
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
                    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(ButterflyProperties, _ButterflyInstanceData);
                    // The second UV set marks wings; local X determines left/right after FBX axis conversion.
                    float side = positionOS.x < 0.0 ? -1.0 : 1.0;
                    // Standalone prefab preview uses time; live instances supply a continuous phase.
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
                return OUT;
            }

            half3 PaletteColor(float variant)
            {
                if (variant < 0.5) return _Orange.rgb;
                if (variant < 1.5) return _Yellow.rgb;
                if (variant < 2.5) return _Pink.rgb;
                if (variant < 3.5) return _Purple.rgb;
                return _Blue.rgb;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(ButterflyProperties, _ButterflyInstanceData);
                half3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                // Flat wings are visible from either side; both faces receive the same gentle light.
                half facing = abs(dot(normalWS, mainLight.direction));
                half3 lighting = _AmbientStrength.xxx + mainLight.color * (_DirectStrength * facing);
                return half4(PaletteColor(instanceData.x) * lighting, 1.0);
            }
            ENDHLSL
        }
    }
}
