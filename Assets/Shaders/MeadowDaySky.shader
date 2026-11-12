Shader "FirstSettlers/Meadow Day Sky"
{
    Properties
    {
        _CloudNoise("Cloud Noise", 3D) = "white" {}
        [NoScaleOffset] _CloudAtlas("Cloud Light Response and Opacity", 2D) = "black" {}
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
            TEXTURE2D(_CloudAtlas);
            SAMPLER(sampler_CloudAtlas);
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

            half4 DistantBank(float2 uv, float2 down)
            {
                float3 seed = CloudCellRandom(floor(uv));
                float2 fromCellCenter = frac(uv) - .5;
                float2 local = fromCellCenter - (seed.yz - .5) * .055;
                float2 right = float2(down.y, -down.x);
                float tile = 2 + floor(seed.y * 2);
                float2 radius = float2(.53,.20) * lerp(.76, 1, seed.z);
                float2 position = float2(dot(local, right), dot(local, down)) /
                    radius;
                float2 tileUV = saturate(float2(position.x, -position.y) * .5 + .5);
                float2 atlasScale = float2(3, 2);
                float2 tileIndex = float2(fmod(tile, 3), floor(tile / 3));
                float2 atlasUV = (tileIndex + tileUV) / atlasScale;
                // The atlas has clear gutters. Supply the continuous local
                // gradients so a neighboring cell can't select a coarse mip.
                float2 gradientX = float2(dot(ddx(uv), right), -dot(ddx(uv), down)) / (radius * 2 * atlasScale);
                float2 gradientY = float2(dot(ddy(uv), right), -dot(ddy(uv), down)) / (radius * 2 * atlasScale);
                half4 cloud = SAMPLE_TEXTURE2D_GRAD(_CloudAtlas, sampler_CloudAtlas, atlasUV, gradientX, gradientY);
                float edge = 1 - max(abs(position.x), abs(position.y));
                cloud.a *= smoothstep(0, .06, edge) *
                    smoothstep(0, .045, .5 - max(abs(fromCellCenter.x), abs(fromCellCenter.y))) *
                    step(1 - _CloudCoverage * .77, seed.x);
                return cloud;
            }

            half3 LightCloud(half4 cloud, half side, half overhead, float sun, half day, half twilight)
            {
                half shoulder = side < 0 ? cloud.r : cloud.b;
                half response = lerp(cloud.g, shoulder, abs(side) * (1 - overhead * .48));
                half rim = pow(sun, 7) * (1 - cloud.a) * .24;
                half lit = saturate(response * (.80 + overhead * .23) + rim);
                half3 color = lerp(_CloudShade.rgb, _CloudWhite.rgb * _SkySunTint.rgb, lit);
                color = lerp(_NightZenith.rgb * .77 + _HorizonTint.rgb * .21, color, day);
                return color + _SkySunTint.rgb * twilight * .11 * (.28 + response * .72);
            }

            half4 ProjectCloud(float3 ray, float3 rayDx, float3 rayDy, float2 anchor,
                float2 heading, float2 radius, float tile, half opacity, float sun, half day, half twilight)
            {
                float anchorLengthSq = dot(anchor, anchor);
                float inverse = rcp(1 + anchorLengthSq);
                float3 center = float3(2 * anchor.x, 1 - anchorLengthSq, 2 * anchor.y) * inverse;
                float2 turn = 2 * anchor * inverse;
                // Transport a fixed orientation from the zenith to this
                // anchor. The basis is defined even at the zenith itself.
                float3 axisX = float3(1 - anchor.x * turn.x, -turn.x, -anchor.y * turn.x);
                float3 axisZ = float3(-anchor.x * turn.y, -turn.y, 1 - anchor.y * turn.y);
                float3 right = axisX * heading.x + axisZ * heading.y;
                float3 up = cross(center, right);
                float distance = dot(ray, center);
                if (distance < .45) return 0;

                float2 plane = float2(dot(ray, right), dot(ray, up)) / distance;
                float2 local = plane / radius;
                if (any(abs(local) >= 1)) return 0;

                float2 atlasScale = float2(3, 2);
                float2 tileIndex = float2(fmod(tile, 3), floor(tile / 3));
                float2 atlasUV = (tileIndex + local * .5 + .5) / atlasScale;
                // Different screen pixels may select different neighbor cells.
                // Derive mips from the continuous ray and this cloud's plane.
                float2 gradientX = (float2(dot(rayDx, right), dot(rayDx, up)) - plane * dot(rayDx, center)) /
                    (distance * radius * 2 * atlasScale);
                float2 gradientY = (float2(dot(rayDy, right), dot(rayDy, up)) - plane * dot(rayDy, center)) /
                    (distance * radius * 2 * atlasScale);
                half4 sample = SAMPLE_TEXTURE2D_GRAD(_CloudAtlas, sampler_CloudAtlas, atlasUV, gradientX, gradientY);
                half side = dot(_SkySunDirection.xyz, right);
                half overhead = saturate(_SkySunDirection.y);
                half alpha = sample.a * opacity;
                return half4(LightCloud(sample, side, overhead, sun, day, twilight) * alpha, alpha);
            }

            half4 CloudGroups(float3 ray, float3 rayDx, float3 rayDy, float2 dome, float2 drift,
                float sun, half day, half twilight)
            {
                float spacing = max(1.5, _CloudScale * 5);
                float2 origin = float2(.45,.54);
                float2 offset = origin + drift * 1.55;
                float2 baseCell = floor(dome * spacing + offset);
                half4 layers = 0;
                [loop] for (int row = -1; row <= 1; row++)
                {
                    [loop] for (int column = -1; column <= 1; column++)
                    {
                        float2 cell = baseCell + float2(column, row);
                        float3 seed = CloudCellRandom(cell + float2(5.2, 12.7));
                        if (seed.x < 1 - _CloudCoverage) continue;
                        float2 home = cell + .5 + (seed.yz - .5) * .30;
                        float2 anchor = (home - offset) / spacing;
                        float2 nominal = home - origin;
                        float angle = (seed.z - .5) * .48;
                        float2 heading = dot(nominal, nominal) < .30
                            ? float2(cos(angle), sin(angle))
                            : normalize(float2(nominal.y, -nominal.x));
                        float2 perpendicular = float2(-heading.y, heading.x);
                        float angularScale = 2 / (spacing * (1 + dot(anchor, anchor)));
                        float mainTile = floor(seed.y * 3);
                        float2 mainRadius = float2(.51,.43) * angularScale * lerp(.74, 1, seed.z);

                        // The companion shares the group's drift and sits
                        // close enough to overlap a shoulder or trail beside it.
                        float direction = seed.y < .5 ? -1 : 1;
                        float2 companion = anchor + (heading * (direction * lerp(.30,.43,seed.y)) +
                            perpendicular * (seed.z - .45) * .39) / spacing;
                        float2 companionHeading = normalize(heading + perpendicular * (seed.x - .5) * .43);
                        half4 satellite = ProjectCloud(ray, rayDx, rayDy, companion, companionHeading,
                            float2(.29,.24) * angularScale, 2 + floor(seed.z * 3), .80, sun, day, twilight);
                        half4 mainCloud = ProjectCloud(ray, rayDx, rayDy, anchor, heading, mainRadius,
                            mainTile, 1, sun, day, twilight);
                        half4 group = mainCloud + satellite * (1 - mainCloud.a);
                        // Cells index candidates only. Nothing is clipped at
                        // their boundaries, and overlap order is stable.
                        layers = group + layers * (1 - group.a);
                    }
                }
                return layers;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 ray = normalize(input.direction);
                float3 rayDx = ddx(ray), rayDy = ddy(ray);
                float y = saturate(ray.y);
                half day = _SkyWeather.x, twilight = _SkyWeather.y;
                float sun = saturate(dot(ray, _SkySunDirection.xyz));
                half3 zenith = lerp(_NightZenith.rgb, _DayZenith.rgb, day);
                half3 sky = lerp(_HorizonTint.rgb, zenith, pow(y, .37));
                sky += _SkySunTint.rgb * pow(sun, 11) * (day * .045 + twilight * .19);

                if (ray.y > .002)
                {
                    float2 drift = _CloudWind.xy * _CloudHours;
                    float2 high = ray.xz / (y + .27) * float2(.42, .87) + drift * .49;
                    float cirrus = smoothstep(.52, .74, Noise(high, .78).x) *
                        smoothstep(.02, .23, y) * _HighClouds;
                    sky = lerp(sky, lerp(_HorizonTint.rgb * .7, _CloudWhite.rgb, day * .73), cirrus);

                    // Use the dome to find nearby groups and distant banks.
                    float inverseDomeScale = rcp(1 + y);
                    float2 dome = ray.xz * inverseDomeScale;
                    float3 sunDirection = _SkySunDirection.xyz;
                    float2 domeSun = (sunDirection.xz - ray.xz * sunDirection.y * inverseDomeScale) * inverseDomeScale;
                    half overhead = saturate(sunDirection.y);
                    half bankCoverage = 0;
                    if (y < .37)
                    {
                        float2 bankOffset = drift * 1.55 + float2(8.72, 6.21);
                        float2 bankUV = dome * (_CloudScale * 16) + bankOffset;
                        float2 bankDown = normalize(floor(bankUV) + .5 - bankOffset);
                        half4 bank = DistantBank(bankUV, bankDown);
                        bankCoverage = bank.a * smoothstep(.018, .086, y) * (1 - smoothstep(.18, .37, y));
                        half bankSide = clamp(dot(domeSun, float2(bankDown.y, -bankDown.x)), -1, 1);
                        half3 bankColor = LightCloud(bank, bankSide, overhead, sun, day, twilight);
                        sky = lerp(sky, lerp(sky, bankColor, .56), bankCoverage);
                    }
                    half4 cumulus = CloudGroups(ray, rayDx, rayDy, dome, drift, sun, day, twilight);
                    half visibility = smoothstep(.014, .12, y) * lerp(.70, 1, smoothstep(.02, .28, y));
                    half coverage = cumulus.a * visibility;
                    sky = sky * (1 - coverage) + cumulus.rgb * visibility;
                    half skyVisibility = (1 - coverage) * (1 - bankCoverage * .56);
                    sky += _SkySunTint.rgb * smoothstep(.99970,.99992,sun) *
                        _SkyWeather.z * skyVisibility * 2.5;
                    float moon = saturate(dot(ray, _SkyMoonDirection.xyz));
                    sky += half3(.52,.61,.77) * smoothstep(.99980,.99994,moon) *
                        _SkyWeather.w * skyVisibility;
                }
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
