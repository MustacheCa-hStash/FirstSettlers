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

            float3 CloudCellRandom(float2 cell)
            {
                float3 p = frac(cell.xyx * float3(.1031, .1030, .0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yzz) * p.zyx);
            }

            float Lobe(float2 p, float2 center, float2 radius)
            {
                float2 q = (p - center) / radius;
                return 1 - dot(q, q);
            }

            float3 Cumulus(float2 uv, float2 down)
            {
                float3 seed = CloudCellRandom(floor(uv));
                float2 fromCellCenter = frac(uv) - .5;
                float2 local = fromCellCenter - (seed.xy - .5) * .13;
                float2 right = float2(down.y, -down.x);
                float2 position = float2(dot(local, right), dot(local, down)) /
                    lerp(float2(.22,.088), float2(.39,.18), seed.yz);
                float2 p = position;
                p.x *= seed.x < .5 ? -1 : 1;
                // Low, broad bases and uneven upper lobes give each group a cumulus profile.
                float baseLobe = Lobe(p, float2(.02,.26), float2(.98,.52));
                float center = Lobe(p, float2(-.18,-.19), float2(.68,.76));
                float shoulder = Lobe(p, float2(lerp(.34,.54,seed.x),-.01), float2(.55,.62));
                float tail = Lobe(p, float2(-.72,lerp(.13,.31,seed.z)), float2(.32,.43));
                float shape = max(max(baseLobe, tail), max(center, shoulder));
                float2 noise = Noise(uv * 2.16 + .37, .58);
                float density = shape + (noise.x - .5) * .32 + (noise.y - .5) * .16;
                // Keep the eroded edges continuous where cloud cells meet.
                density = min(density, (.5 - max(abs(fromCellCenter.x), abs(fromCellCenter.y))) * 22 - .08);
                float inhabited = step(lerp(.92,.09,_CloudCoverage), seed.x);
                return float3(density * inhabited - (1 - inhabited), position);
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

                    // Work on the dome so the clouds retain height at grazing view angles.
                    float inverseDomeScale = rsqrt(1 + y);
                    float2 dome = ray.xz * inverseDomeScale;
                    float2 down = normalize(ray.xz + float2(.0001,0));
                    float2 cloudUV = dome * (_CloudScale * 9.5) + drift * 3.2 + float2(.34,.68);
                    float3 cumulus = Cumulus(cloudUV, down);
                    float density = cumulus.x;
                    float coverage = smoothstep(.0, .16, density);
                    float horizon = smoothstep(.045, .17, y);
                    float3 sunDirection = _SkySunDirection.xyz;
                    float2 domeSun = (sunDirection.xz - ray.xz * sunDirection.y / (2 * (1 + y))) * inverseDomeScale;
                    float2 sunInCloud = float2(dot(domeSun,float2(down.y,-down.x)), dot(domeSun,down));
                    float sunFacing = saturate(.52 + dot(cumulus.yz, sunInCloud) * .60);
                    float thickness = smoothstep(.03, .85, density);
                    float underside = saturate(.24 + cumulus.z * .47 + (1 - sunFacing) * .25);
                    underside *= .66 + thickness * .34;
                    half3 cloud = lerp(_CloudWhite.rgb, _CloudShade.rgb, underside);
                    cloud = lerp(_HorizonTint.rgb * .79 + _NightZenith.rgb * .29, cloud, day);
                    cloud += _SkySunTint.rgb * twilight * .12 * (.4 + sunFacing * .6);
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
