Shader "Hidden/FirstSettlers/DistantFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        TEXTURE2D_X_FLOAT(_FogDepth);
        TEXTURE2D_X(_FogWater);
        TEXTURE2D_X(_FogTexture);
        TEXTURE3D(_FogNoise);
        SAMPLER(sampler_FogNoise);
        float4x4 _FogInverseVP;
        float4 _FogCamera, _FogDistance, _FogHeight, _FogShape, _FogClear;
        float4 _FogWind, _FogQuality, _FogColor, _FogTexture_TexelSize;

        struct FogRay
        {
            float3 origin, direction;
            float distance, surfaceY, kind;
        };
        float DeviceDepth(float raw)
        {
            #if UNITY_REVERSED_Z
                return raw;
            #else
                return lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
            #endif
        }
        FogRay GetRay(float2 uv)
        {
            FogRay ray;
            float raw = SAMPLE_TEXTURE2D_X_LOD(_FogDepth, sampler_PointClamp, uv, 0).r;
            #if UNITY_REVERSED_Z
                bool sky = raw <= 0;
                float farDepth = 0, nearDepth = 1;
            #else
                bool sky = raw >= 1;
                float farDepth = 1, nearDepth = UNITY_NEAR_CLIP_VALUE;
            #endif
            float3 farPoint = ComputeWorldSpacePosition(uv, farDepth, _FogInverseVP);
            ray.origin = _FogCamera.xyz;
            if (_FogCamera.w > .5) ray.origin = ComputeWorldSpacePosition(uv, nearDepth, _FogInverseVP);
            ray.direction = normalize(farPoint - ray.origin);
            float3 surface = ComputeWorldSpacePosition(uv, DeviceDepth(raw), _FogInverseVP);
            ray.distance = sky ? _FogDistance.z : length(surface - ray.origin);
            ray.surfaceY = surface.y;
            ray.kind = sky ? 1 : 0;
            if (_FogClear.w > .5 && SAMPLE_TEXTURE2D_X_LOD(_FogWater, sampler_PointClamp, uv, 0).r > .5 && abs(ray.direction.y) > 0.00001)
            {
                float waterDistance = (_FogClear.z - ray.origin.y) / ray.direction.y;
                if (waterDistance > 0 && (sky || waterDistance <= ray.distance + 1))
                {
                    ray.distance = waterDistance; ray.surfaceY = _FogClear.z; ray.kind = 2;
                }
            }
            return ray;
        }
        float Protection(FogRay ray)
        {
            return ray.kind == 1 ? 1 : 1 - smoothstep(_FogClear.x, _FogClear.y, ray.surfaceY);
        }
        float4 March(FogRay ray, float2 uv)
        {
            float guide = saturate(ray.distance / _FogDistance.z);
            float4 empty = float4(0, 1, guide, ray.kind);
            float start = _FogDistance.x, end = min(ray.distance, _FogDistance.z);
            if (end <= start || Protection(ray) <= 0) return empty;

            // Intersect the maximum possible height extent first: no samples in clear air.
            float bottom = _FogHeight.x;
            float top = _FogHeight.y + _FogHeight.w;
            if (abs(ray.direction.y) > .00001)
            {
                float a = (bottom - ray.origin.y) / ray.direction.y;
                float b = (top - ray.origin.y) / ray.direction.y;
                start = max(start, min(a, b)); end = min(end, max(a, b));
            }
            else if (ray.origin.y < bottom || ray.origin.y > top) return empty;
            if (end <= start) return empty;

            int steps = (int)_FogQuality.x;
            float stepSize = (end - start) / steps;
            float random = frac(52.9829189 * frac(dot(floor(uv * _FogTexture_TexelSize.zw), float2(.06711056, .00583715))));
            float offset = lerp(.5, random, _FogQuality.y);
            float transmission = 1, scatter = 0;
            [loop] for (int i = 0; i < steps; i++)
            {
                float distance = start + (i + offset) * stepSize;
                float3 p = ray.origin + ray.direction * distance;
                float3 noiseUV = (p - _FogWind.xyz * _FogWind.w) * _FogShape.x;
                float2 noise = SAMPLE_TEXTURE3D_LOD(_FogNoise, sampler_FogNoise, noiseUV, 0).rg;
                float ceiling = _FogHeight.y + (noise.r * 2 - 1) * _FogHeight.w;
                float upper = smoothstep(0, _FogHeight.z, ceiling - p.y);
                float lower = smoothstep(0, _FogHeight.z, p.y - bottom);
                float densityNoise = lerp(1, smoothstep(.15, .8, noise.r), _FogShape.y);
                densityNoise *= lerp(1, .6 + noise.g * .8, _FogShape.z);
                float distanceFade = smoothstep(_FogDistance.x, _FogDistance.x + _FogDistance.y, distance);
                float extinction = _FogDistance.w * upper * lower * densityNoise * distanceFade;
                float alpha = 1 - exp(-extinction * stepSize);
                float illumination = 1 + _FogShape.w * (noise.r - .5 + .25 * upper);
                scatter += transmission * alpha * illumination;
                transmission *= 1 - alpha;
                if (transmission < .005) { transmission = 0; break; }
            }
            return float4(scatter, transmission, guide, ray.kind);
        }
        float4 FragMarch(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return March(GetRay(input.texcoord), input.texcoord);
        }
        float4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            FogRay ray = GetRay(uv);
            float protection = Protection(ray);
            if (ray.distance <= _FogDistance.x || protection <= 0) return float4(0, 0, 0, 1);
            float2 pixel = uv * _FogTexture_TexelSize.zw - .5;
            float2 basePixel = floor(pixel), fraction = frac(pixel);
            float2 fog = 0;
            float weightSum = 0;
            float guide = saturate(ray.distance / _FogDistance.z);
            [unroll] for (int y = 0; y < 2; y++)
                [unroll] for (int x = 0; x < 2; x++)
                {
                    float2 tapUV = (basePixel + float2(x, y) + .5) * _FogTexture_TexelSize.xy;
                    float4 sample = SAMPLE_TEXTURE2D_X_LOD(_FogTexture, sampler_PointClamp, tapUV, 0);
                    float weight = (x == 0 ? 1 - fraction.x : fraction.x) * (y == 0 ? 1 - fraction.y : fraction.y);
                    weight *= exp2(-abs(sample.b - guide) * 180);
                    weight *= abs(sample.a - ray.kind) < .25 ? 1 : 0;
                    fog += sample.rg * weight; weightSum += weight;
                }
            // Rare silhouette mismatch: march this pixel instead of leaking fog across it.
            fog = weightSum > .0001 ? fog / weightSum : March(ray, uv).rg;
            return float4(_FogColor.rgb * fog.r * protection, lerp(1, fog.g, protection));
        }
        ENDHLSL

        Pass
        {
            Name "Raymarch"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragMarch
            ENDHLSL
        }
        Pass
        {
            Name "Composite"
            Blend One SrcAlpha, Zero One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }
    Fallback Off
}
