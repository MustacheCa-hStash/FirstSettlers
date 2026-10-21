Shader "Hidden/TreeImpostor/SugarMapleHQSemanticCapture"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Bark Tint", Color) = (1,1,1,1)
        _Cutoff("Cutoff", Float) = .5
        _Smoothness("Smoothness", Float) = .08
        _BacklightingStrength("Backlighting Strength", Float) = .72
        _BacklightingEnabled("Backlighting Enabled", Float) = 1
        _LeafContrast("Leaf Contrast", Float) = .34
        _CardVariationStrength("Card Variation", Float) = .22
        _UseVertexColor("Use Vertex Color", Float) = 0
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
                half _LeafContrast, _CardVariationStrength, _UseVertexColor, _ColorVariationStrength;
                half _VerticalGradientStrength, _ColorHeightMin, _ColorHeightMax, _Brightness;
                half _CaptureMaterialKind, _CaptureOutput;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float eyeDepth : TEXCOORD3;
                half4 color : COLOR;
            };
            float Hash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(.1031,.1030,.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            half LeafHash(float2 p)
            {
                half3 p3 = frac(half3(p.xyx)*half3(.1031h,.1030h,.0973h));
                p3 += dot(p3,p3.yzx+33.33h);
                return frac((p3.x+p3.y)*p3.z);
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
                output.color = input.color;
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
                    half coverage = saturate((sample.a-_Cutoff)/max(1-_Cutoff,.001h));
                    half detail = lerp(1-_LeafContrast,1+_LeafContrast,coverage*luma);
                    half cardNoise = LeafHash(floor(input.positionOS.xz*.72h)+floor(input.uv*5.0h));
                    detail *= lerp(1-_CardVariationStrength,1+_CardVariationStrength,cardNoise);
                    // Preserve source card detail; palette, per-tree tint and
                    // the colored card-bottom shade remain dynamic at runtime.
                    half3 color = detail.xxx*.5h * lerp(half3(1,1,1),input.color.rgb,saturate(_UseVertexColor));
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
                    return half4(Oct(normalize(input.normalWS)*IS_FRONT_VFACE(facing,1,-1)),leaf?saturate((1-input.uv.y)*_VerticalGradientStrength):1-_Smoothness,leaf?_BacklightingStrength*_BacklightingEnabled:0);
                if (_CaptureOutput < 2.5h)
                {
                    half depth = saturate((input.eyeDepth-_CaptureDepthMinMax.x)/max(_CaptureDepthMinMax.y-_CaptureDepthMinMax.x,.0001));
                    return half4(depth,depth,depth,1);
                }
                return leaf?half4(1,0,LeafHash(floor(input.positionOS.xz*.45h)+floor(input.uv*8.0h)),LeafHash(floor(input.positionOS.xz*1.25h)+floor(input.uv*19.0h))):half4(0,1,0,1);
            }
            ENDHLSL
        }
    }
}
