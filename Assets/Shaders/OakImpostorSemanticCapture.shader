Shader "Hidden/TreeImpostor/OakSemanticCapture"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Bark Tint", Color) = (1,1,1,1)
        _Cutoff("Cutoff", Float) = .5
        _Smoothness("Smoothness", Float) = .08
        _BacklightingStrength("Backlighting Strength", Float) = .72
        _BacklightingEnabled("Backlighting Enabled", Float) = 1
        _LeafDetailStrength("Leaf Detail", Float) = 1
        _LeafGradientContrast("Gradient Contrast", Float) = 1.4
        _LeafGradientMidpoint("Gradient Midpoint", Float) = .85
        _ColorVariationStrength("Brightness Variation", Float) = .06
        _VerticalGradientStrength("Bark Height Gradient", Float) = .04
        _ColorHeightMin("Bark Height Min", Float) = 0
        _ColorHeightMax("Bark Height Max", Float) = 7
        _Brightness("Bark Brightness", Float) = 1
        _CaptureMaterialKind("Material Kind", Float) = 0
        _CaptureOutput("Output", Float) = 0
        _CaptureDepthMinMax("Depth Range", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off
        ZWrite On
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _CaptureDepthMinMax;
                half4 _BaseColor;
                half _Cutoff, _Smoothness, _BacklightingStrength, _BacklightingEnabled;
                half _LeafDetailStrength, _LeafGradientContrast, _LeafGradientMidpoint, _ColorVariationStrength;
                half _VerticalGradientStrength, _ColorHeightMin, _ColorHeightMax, _Brightness;
                half _CaptureMaterialKind, _CaptureOutput;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float eyeDepth : TEXCOORD3;
            };
            float Hash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(.1031,.1030,.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            half Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            float2 Oct(float3 n)
            {
                n /= max(abs(n.x)+abs(n.y)+abs(n.z),1e-5);
                float2 p = n.xy;
                if (n.z < 0) p = (1-abs(p.yx)) * float2(p.x>=0?1:-1,p.y>=0?1:-1);
                return p*.5+.5;
            }
            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv,_BaseMap);
                output.eyeDepth = -TransformWorldToView(world).z;
                return output;
            }
            half4 frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 sample = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv);
                bool leaf = _CaptureMaterialKind < .5h;
                if (leaf) clip(sample.a-_Cutoff);
                half noise = Noise(input.positionOS.xz*.7 + input.uv*4.7);
                if (_CaptureOutput < .5h)
                {
                    half luma = dot(sample.rgb,half3(.299h,.587h,.114h));
                    half detail = lerp(1.0h,clamp(1+(luma-_LeafGradientMidpoint)*_LeafGradientContrast,.25h,1.5h),_LeafDetailStrength);
                    detail *= lerp(1-_ColorVariationStrength,1+_ColorVariationStrength,noise);
                    // Half range preserves bright painted detail in an RGBA8
                    // capture. Runtime restores it with foliage brightness 2.
                    half3 color = detail.xxx * .5h;
                    if (!leaf)
                    {
                        half variation = sin(dot(input.positionOS,float3(.61,.27,.48))) * _ColorVariationStrength;
                        half height = saturate((input.positionOS.y-_ColorHeightMin)/max(_ColorHeightMax-_ColorHeightMin,.0001h));
                        color = sample.rgb * _BaseColor.rgb * _Brightness * (1+variation) *
                            lerp(1-_VerticalGradientStrength,1+_VerticalGradientStrength,height);
                    }
                    return half4(color,leaf?sample.a:1);
                }
                if (_CaptureOutput < 1.5h)
                    return half4(Oct(normalize(input.normalWS)*IS_FRONT_VFACE(facing,1,-1)),1-_Smoothness,leaf?_BacklightingStrength*_BacklightingEnabled:0);
                if (_CaptureOutput < 2.5h)
                {
                    half depth = saturate((input.eyeDepth-_CaptureDepthMinMax.x)/max(_CaptureDepthMinMax.y-_CaptureDepthMinMax.x,.0001));
                    return half4(depth,depth,depth,1);
                }
                return leaf?half4(1,0,noise,1):half4(0,1,0,1);
            }
            ENDHLSL
        }
    }
}
