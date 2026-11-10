Shader "Hidden/FirstSettlers/GroundContactShadow"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        TEXTURE2D_X_FLOAT(_ContactDepth);
        TEXTURE2D_X(_ContactReceivers);
        TEXTURE2D_X(_ContactMask);
        float4x4 _ContactVP, _ContactInverseVP;
        float4 _ContactCamera, _ContactForward, _ContactSun, _ContactSettings, _ContactTexel;
        half4 _FSGroundDaylight;

        float RawDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_ContactDepth, sampler_PointClamp, uv, 0).r;
        }

        bool IsSky(float raw)
        {
            #if UNITY_REVERSED_Z
                return raw <= 0;
            #else
                return raw >= 1;
            #endif
        }

        float3 WorldPoint(float2 uv, float raw)
        {
            #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
            #endif
            return ComputeWorldSpacePosition(uv, raw, _ContactInverseVP);
        }

        float2 Trace(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float raw = RawDepth(uv);
            float eye = LinearEyeDepth(raw, _ZBufferParams);
            float3 surface = WorldPoint(uv, raw);
            float3 dx = ddx(surface), dy = ddy(surface);
            // Work on upward-facing receivers and discard depth discontinuities.
            float3 n = cross(dx, dy);
            n *= n.y < 0 ? -1 : 1;
            float3 normal = n * rsqrt(max(dot(n,n), 1e-12));
            float range = _ContactSettings.z;
            if (SAMPLE_TEXTURE2D_X_LOD(_ContactReceivers, sampler_PointClamp, uv, 0).r <= 0 ||
                IsSky(raw) || eye >= range || normal.y < .40 ||
                max(dot(dx,dx),dot(dy,dy)) > .18) return float2(0, min(eye, range + 1));

            half visibility = 1;
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(surface));
                half shadowStrength = GetMainLightShadowParams().x;
                visibility = saturate(1.0h - (1.0h - shadow) / max(shadowStrength, .001h));
            #endif
            half weight = visibility * _FSGroundDaylight.x *
                smoothstep(.12h,.27h,_ContactSun.y) * smoothstep(.02h,.30h,dot(normal,_ContactSun.xyz));
            float3 fromCamera = surface - _ContactCamera.xyz;
            float rangeSq = range * range;
            weight *= 1.0h - smoothstep(rangeSq * .3364, rangeSq, dot(fromCamera, fromCamera));
            if (weight <= .005) return float2(0, eye);

            float3 start = surface + normal * .028;
            // Long rays need more taps to keep small blockers from falling between steps.
            int samples = (int)clamp(ceil(8 + (_ContactSettings.y - .7) * 4), 8, 18);
            [loop] for (int i = 1; i <= samples; ++i)
            {
                float t = i / (float)samples;
                float distance = _ContactSettings.y * lerp(.06, 1, t * t);
                float3 ray = start + _ContactSun.xyz * distance;
                float4 clip = mul(_ContactVP, float4(ray,1));
                if (clip.w <= 0) break;
                float2 ndc = clip.xy / clip.w;
                #if UNITY_UV_STARTS_AT_TOP
                    ndc.y = -ndc.y;
                #endif
                float2 rayUV = ndc * .5 + .5;
                if (any(rayUV < .002) || any(rayUV > .998)) break;
                float scene = LinearEyeDepth(RawDepth(rayUV), _ZBufferParams);
                float rayEye = dot(ray - _ContactCamera.xyz, _ContactForward.xyz);
                float gap = rayEye - scene;
                // A finite thickness rejects unrelated silhouettes behind foreground geometry.
                float thickness = .105 + distance * .10;
                if (gap > .017 && gap < thickness)
                    return float2(weight * (1 - t * .72) * (1 - smoothstep(thickness*.65, thickness, gap)), eye);
            }
            return float2(0, eye);
        }

        half4 Composite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half receiver = SAMPLE_TEXTURE2D_X_LOD(_ContactReceivers, sampler_PointClamp, uv, 0).r;
            if (receiver <= 0) return 0;
            float raw = RawDepth(uv);
            float eye = LinearEyeDepth(raw, _ZBufferParams);
            if (IsSky(raw) || eye >= _ContactSettings.z) return 0;
            float2 index = uv * _ContactTexel.zw - .5;
            float2 fraction = frac(index);
            float2 corner = (floor(index) + .5) * _ContactTexel.xy;
            half total = 0, sum = 0;
            [unroll] for (int y = 0; y < 2; y++)
            [unroll] for (int x = 0; x < 2; x++)
            {
                float2 lookup = corner + float2(x,y) * _ContactTexel.xy;
                float2 field = SAMPLE_TEXTURE2D_X_LOD(_ContactMask, sampler_PointClamp, lookup, 0).rg;
                float depthWeight = 1 - smoothstep(.025, .095 + eye * .0015, abs(field.y - eye));
                half spatial = (x == 0 ? 1 - fraction.x : fraction.x) * (y == 0 ? 1 - fraction.y : fraction.y);
                half weight = spatial * depthWeight;
                sum += field.x * weight;
                total += weight;
            }
            half contact = receiver * sum / max(.25h,total);
            if (_ContactSettings.w > .5)
                return half4(.035h,.85h,1.0h,saturate(contact * 3.0h));
            return half4(0,0,0,saturate(_ContactSettings.x * contact));
        }
        ENDHLSL

        Pass
        {
            Name "Trace"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Trace
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            ENDHLSL
        }
        Pass
        {
            Name "Composite"
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            #pragma target 4.5
            ENDHLSL
        }
    }
}
