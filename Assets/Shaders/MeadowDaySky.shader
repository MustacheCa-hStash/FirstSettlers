Shader "FirstSettlers/Meadow Day Sky"
{
    Properties
    {
        _CloudNoise("Cloud Noise", 3D) = "white" {}
        _DayZenith("Day Zenith", Color) = (.17,.40,.67,1)
        _NightZenith("Night Zenith", Color) = (.028,.044,.085,1)
        _CloudWhite("Sunlit Cloud", Color) = (.95,.96,.985,1)
        _CloudShade("Cloud Underside", Color) = (.57,.64,.73,1)
        _CloudCoverage("Cumulus Coverage", Range(0,1)) = .48
        _CloudScale("Cumulus Scale", Range(.1,3)) = .61
        _CloudWind("Cloud Drift Per Game Hour", Vector) = (.014,.006,0,0)
        _HighClouds("High Cloud Amount", Range(0,1)) = .23
        [HideInInspector] _HorizonTint("Horizon", Color) = (.63,.71,.78,1)
        [HideInInspector] _SkySunTint("Sun Tint", Color) = (1,.95,.88,1)
        [HideInInspector] _SkySunDirection("Sun Direction", Vector) = (0,.7,-.7,0)
        [HideInInspector] _SkyMoonDirection("Moon Direction", Vector) = (0,1,0,0)
        [HideInInspector] _SkyWeather("Day, Twilight, Sun, Moon", Vector) = (1,0,1,0)
        [HideInInspector] _CloudHours("Cloud Hours", Float) = 14
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            Name "AnimatedSky"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_CloudNoise);
            SAMPLER(sampler_CloudNoise);
            CBUFFER_START(UnityPerMaterial)
                half4 _DayZenith, _NightZenith, _CloudWhite, _CloudShade, _HorizonTint, _SkySunTint;
                float4 _SkySunDirection, _SkyMoonDirection, _SkyWeather, _CloudWind;
                float _CloudCoverage, _CloudScale, _HighClouds, _CloudHours;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float2 Noise(float2 uv, float slice)
            {
                return SAMPLE_TEXTURE3D(_CloudNoise, sampler_CloudNoise, float3(uv, slice)).rg;
            }

            float CloudShape(float2 uv)
            {
                float2 noisePair = Noise(uv, .33);
                return noisePair.x * .65 + noisePair.y * .22 + Noise(uv * 2.07 + .37, .58).y * .13;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 ray = normalize(input.direction);
                float y = saturate(ray.y);
                half day = _SkyWeather.x, twilight = _SkyWeather.y;
                float sun = saturate(dot(ray, _SkySunDirection.xyz));
                half3 zenith = lerp(_NightZenith.rgb, _DayZenith.rgb, day);
                half3 sky = lerp(_HorizonTint.rgb, zenith, pow(y, .37));
                sky += _SkySunTint.rgb * pow(sun, 11) * (day * .09 + twilight * .19);

                if (ray.y > .002)
                {
                    float2 drift = _CloudWind.xy * _CloudHours;
                    float2 high = ray.xz / (y + .27) * float2(.42, .87) + drift * .49;
                    float cirrus = smoothstep(.52, .74, Noise(high, .78).x) *
                        smoothstep(.02, .23, y) * _HighClouds;
                    sky = lerp(sky, lerp(_HorizonTint.rgb * .7, _CloudWhite.rgb, day * .73), cirrus);

                    float2 cloudUV = ray.xz / (y + .25) * _CloudScale + drift;
                    float density = CloudShape(cloudUV);
                    float threshold = lerp(.80, .36, _CloudCoverage);
                    float coverage = smoothstep(threshold - .009, threshold + .041, density);
                    float horizon = smoothstep(.012, .12, y);
                    float brightEdge = saturate(.5 + (density -
                        CloudShape(cloudUV + _SkySunDirection.xz * .041 + float2(.009,.007))) * 6);
                    half3 cloud = lerp(_CloudShade.rgb, _CloudWhite.rgb, brightEdge);
                    cloud = lerp(_HorizonTint.rgb * .79 + _NightZenith.rgb * .29, cloud, day);
                    cloud += _SkySunTint.rgb * twilight * .12 * (.4 + brightEdge * .6);
                    sky = lerp(sky, cloud, coverage * horizon);
                    sky += _SkySunTint.rgb * smoothstep(.99970,.99992,sun) *
                        _SkyWeather.z * (1 - coverage * horizon) * 2.5;
                    float moon = saturate(dot(ray, _SkyMoonDirection.xyz));
                    sky += half3(.52,.61,.77) * smoothstep(.99980,.99994,moon) *
                        _SkyWeather.w * (1 - coverage * horizon);
                }
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
