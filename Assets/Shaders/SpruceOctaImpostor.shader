Shader "Custom/SpruceOctaImpostor"
{
    Properties
    {
        [MainTexture] _AlbedoCoverage("Albedo / Coverage", 2D) = "white" {}
        _SurfaceAtlas("Surface: RG Normal, B Roughness, A Transmission", 2D) = "gray" {}
        _DepthAtlas("Linear First-Surface Depth", 2D) = "black" {}
        _MaterialIdAtlas("Material ID: R Leaf, G Bark", 2D) = "black" {}
        _AmbientAtlas("Ambient: RG Bent Normal, B Sky, A AO", 2D) = "white" {}
        _LeafSeasonTint("Leaf Season Tint", Color) = (1,1,1,1)
        [PerRendererData] _TreeLeafTint("Per Instance Leaf Tint", Color) = (1,1,1,1)
        _SeasonStrength("Season Strength", Range(0,1)) = 1
        _Cutoff("Coverage Cutoff", Range(0,1)) = 0.008
        _CaptureCenterLS("Capture Center Local", Vector) = (0,3,0,0)
        _CaptureRadius("Capture Radius", Float) = 4.3
        _FramesPerAxis("Frames Per Axis", Float) = 12
        _AtlasTileResolution("Primary Atlas Tile Resolution", Float) = 256
        _AmbientTileResolution("Ambient Atlas Tile Resolution", Float) = 128
        _AtlasPadding("Atlas Tile Padding", Float) = 2
        _DepthParallax("Depth Parallax", Range(0,1)) = 0.45
        _AOStrength("Ambient Occlusion Strength", Range(0,1)) = 0.65
        _SkyStrength("Sky Visibility Strength", Range(0,2)) = 1
        _AmbientFloor("Minimum Ambient Light", Range(0,1)) = 0.429
        _LeafLightWrap("Leaf Sun Wrap", Range(0,1)) = 0.55
        _FoliageBrightness("Foliage Brightness", Range(0.25,2)) = 1.25
        _TransmissionStrength("Leaf Transmission Strength", Range(0,2)) = 0.6
        _WindStrength("Canopy Wind Strength", Range(0,0.5)) = 0.035
        _WindSpeed("Canopy Wind Speed", Range(0,8)) = 1.2
        [Enum(Lit,0,Raw Albedo,1,Coverage,2,Material ID,3,Fixed Frame Raw LOD0,4)] _DebugView("Debug View", Float) = 0
        _DebugFrameX("Debug Fixed Frame X", Range(0,23)) = 0
        _DebugFrameY("Debug Fixed Frame Y", Range(0,23)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite On
        ZTest LEqual
        // Preserve fractional atlas coverage at surviving alpha-tested pixels.
        // With MSAA enabled, AlphaToMask converts this into partial sample
        // coverage for a softer foliage silhouette.
        AlphaToMask On

        Pass
        {
            Name "ForwardSpruceOctaImpostor"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_AlbedoCoverage); SAMPLER(sampler_AlbedoCoverage);
            TEXTURE2D(_SurfaceAtlas); SAMPLER(sampler_SurfaceAtlas);
            TEXTURE2D(_DepthAtlas); SAMPLER(sampler_DepthAtlas);
            TEXTURE2D(_MaterialIdAtlas); SAMPLER(sampler_MaterialIdAtlas);
            TEXTURE2D(_AmbientAtlas); SAMPLER(sampler_AmbientAtlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _AlbedoCoverage_TexelSize, _SurfaceAtlas_TexelSize, _DepthAtlas_TexelSize, _MaterialIdAtlas_TexelSize, _AmbientAtlas_TexelSize;
                half4 _LeafSeasonTint;
                float4 _CaptureCenterLS;
                half _SeasonStrength, _Cutoff, _CaptureRadius, _FramesPerAxis, _AtlasTileResolution, _AmbientTileResolution, _AtlasPadding;
                half _DepthParallax, _AOStrength, _SkyStrength, _AmbientFloor, _LeafLightWrap, _FoliageBrightness, _TransmissionStrength, _WindStrength, _WindSpeed, _DebugView, _DebugFrameX, _DebugFrameY;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(SpruceOctaInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TreeLeafTint)
            UNITY_INSTANCING_BUFFER_END(SpruceOctaInstance)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 viewDirectionLS : TEXCOORD1;
                float2 proxyUV : TEXCOORD2;
                float3 rightWS : TEXCOORD3;
                float3 upWS : TEXCOORD4;
                float scale : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float2 OctEncode(float3 n)
            {
                n /= max(abs(n.x) + abs(n.y) + abs(n.z), 0.0001);
                float2 e = n.xy;
                if (n.z < 0.0)
                    e = (1.0 - abs(e.yx)) * float2(e.x >= 0.0 ? 1.0 : -1.0, e.y >= 0.0 ? 1.0 : -1.0);
                return e * 0.5 + 0.5;
            }

            float3 OctDecode(float2 encoded)
            {
                float2 e = encoded * 2.0 - 1.0;
                float3 n = float3(e.x, e.y, 1.0 - abs(e.x) - abs(e.y));
                if (n.z < 0.0)
                {
                    float oldX = n.x;
                    n.x = (1.0 - abs(n.y)) * (oldX >= 0.0 ? 1.0 : -1.0);
                    n.y = (1.0 - abs(oldX)) * (n.y >= 0.0 ? 1.0 : -1.0);
                }
                return normalize(n);
            }

            half4 SampleAtlas(TEXTURE2D_PARAM(textureToSample, samplerToSample), float2 octaUV, float2 proxyUV, float2 textureSize)
            {
                float2 grid = octaUV * _FramesPerAxis - 0.5;
                float2 stride = textureSize / _FramesPerAxis;
                // Derive the tile size from the imported texture, rather than assuming
                // its original PNG size survived Unity's Max Size import setting.
                float2 tileResolution = stride - _AtlasPadding * 2.0;
                // A single full-tree frame is deliberate for this validation pass. Naively
                // blending four image captures produces four visible ghost trees; seam-aware
                // directional interpolation will be added after the proxy is validated.
                float2 cell = clamp(floor(grid + 0.5), 0.0, _FramesPerAxis - 1.0);
                float2 pixel = cell * stride + _AtlasPadding + saturate(proxyUV) * tileResolution;
                return SAMPLE_TEXTURE2D(textureToSample, samplerToSample, (pixel + 0.5) / textureSize);
            }

            half4 SampleAtlasFrameLod0(TEXTURE2D_PARAM(textureToSample, samplerToSample), float2 frame, float2 proxyUV, float2 textureSize)
            {
                float2 stride = textureSize / _FramesPerAxis;
                float2 tileResolution = stride - _AtlasPadding * 2.0;
                float2 cell = clamp(floor(frame + 0.5), 0.0, _FramesPerAxis - 1.0);
                float2 pixel = cell * stride + _AtlasPadding + saturate(proxyUV) * tileResolution;
                return SAMPLE_TEXTURE2D_LOD(textureToSample, samplerToSample, (pixel + 0.5) / textureSize, 0.0);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float4x4 worldToObject = GetWorldToObjectMatrix();
                float scale = length(float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20));
                float3 centerWS = mul(objectToWorld, float4(_CaptureCenterLS.xyz, 1.0)).xyz;
                float3 cameraDirectionWS = normalize(_WorldSpaceCameraPos.xyz - centerWS);
                float3 upReference = abs(dot(cameraDirectionWS, float3(0,1,0))) > 0.98 ? normalize(float3(objectToWorld._m02, objectToWorld._m12, objectToWorld._m22)) : float3(0,1,0);
                float3 rightWS = normalize(cross(upReference, cameraDirectionWS));
                float3 upWS = normalize(cross(cameraDirectionWS, rightWS));
                float2 billboard = input.uv * 2.0 - 1.0;
                float wind = sin(_Time.y * _WindSpeed + dot(centerWS.xz, float2(0.19, 0.37))) * _WindStrength * saturate(input.uv.y);
                float3 positionWS = centerWS + rightWS * billboard.x * _CaptureRadius * scale + upWS * billboard.y * _CaptureRadius * scale + float3(wind, 0, wind * 0.35);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.viewDirectionLS = normalize(mul((float3x3)worldToObject, cameraDirectionWS));
                output.proxyUV = input.uv;
                output.rightWS = rightWS;
                output.upWS = upWS;
                output.scale = scale;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 octaUV = OctEncode(input.viewDirectionLS);
                half4 albedo = SampleAtlas(TEXTURE2D_ARGS(_AlbedoCoverage, sampler_AlbedoCoverage), octaUV, input.proxyUV, _AlbedoCoverage_TexelSize.zw);
                if (_DebugView > 3.5h)
                {
                    half4 fixedAlbedo = SampleAtlasFrameLod0(TEXTURE2D_ARGS(_AlbedoCoverage, sampler_AlbedoCoverage), float2(_DebugFrameX, _DebugFrameY), input.proxyUV, _AlbedoCoverage_TexelSize.zw);
                    clip(fixedAlbedo.a - _Cutoff);
                    return half4(fixedAlbedo.rgb, fixedAlbedo.a);
                }
                clip(albedo.a - _Cutoff);
                half4 surface = SampleAtlas(TEXTURE2D_ARGS(_SurfaceAtlas, sampler_SurfaceAtlas), octaUV, input.proxyUV, _SurfaceAtlas_TexelSize.zw);
                half4 depth = SampleAtlas(TEXTURE2D_ARGS(_DepthAtlas, sampler_DepthAtlas), octaUV, input.proxyUV, _DepthAtlas_TexelSize.zw);
                half4 materialId = SampleAtlas(TEXTURE2D_ARGS(_MaterialIdAtlas, sampler_MaterialIdAtlas), octaUV, input.proxyUV, _MaterialIdAtlas_TexelSize.zw);
                half4 ambient = SampleAtlas(TEXTURE2D_ARGS(_AmbientAtlas, sampler_AmbientAtlas), octaUV, input.proxyUV, _AmbientAtlas_TexelSize.zw);

                // Diagnostic views expose the exact captured frame and coverage
                // before normals, AO, lighting, tint, and parallax can affect it.
                if (_DebugView > 0.5h && _DebugView < 1.5h)
                    return half4(albedo.rgb, albedo.a);
                if (_DebugView > 1.5h && _DebugView < 2.5h)
                    return half4(albedo.aaa, albedo.a);
                if (_DebugView > 2.5h)
                    return half4(materialId.rg, 0.0h, albedo.a);

                float3 normalLS = OctDecode(surface.rg);
                float3 bentNormalLS = OctDecode(ambient.rg);
                float3x3 objectToWorld = (float3x3)GetObjectToWorldMatrix();
                half3 normalWS = normalize(mul(objectToWorld, normalLS));
                half3 bentNormalWS = normalize(mul(objectToWorld, bentNormalLS));
                half leafWeight = materialId.r / max(materialId.r + materialId.g, 0.0001h);
                half3 perInstanceTint = UNITY_ACCESS_INSTANCED_PROP(SpruceOctaInstance, _TreeLeafTint).rgb;
                half3 leafTint = lerp(half3(1,1,1), _LeafSeasonTint.rgb * perInstanceTint, _SeasonStrength);
                half3 baseColor = albedo.rgb * lerp(half3(1,1,1), leafTint, leafWeight);

                float capturedDepth = lerp(_CaptureRadius * 1.25, _CaptureRadius * 3.75, depth.r);
                float parallaxOffset = (_CaptureRadius * 2.5 - capturedDepth) * _DepthParallax * input.scale;
                float3 shadedPositionWS = input.positionWS + normalize(_WorldSpaceCameraPos.xyz - input.positionWS) * parallaxOffset;
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(shadedPositionWS));
                half normalDirect = saturate(dot(normalWS, mainLight.direction));
                half wrappedLeafDirect = saturate(dot(normalWS, mainLight.direction) * 0.5h + 0.5h);
                half direct = lerp(normalDirect, wrappedLeafDirect, leafWeight * _LeafLightWrap) * mainLight.shadowAttenuation;
                // Match the authored spruce leaf shader, which never lets its
                // diffuse ambient contribution collapse below _AmbientStrength
                // (0.429 on the source material).  Sky visibility still dims
                // open-sky lighting above that conservative foliage floor.
                half3 sky = max(SampleSH(bentNormalWS) * ambient.b, _AmbientFloor.xxx) * _SkyStrength;
                half occlusion = 1.0h - ambient.a * _AOStrength;
                half roughness = surface.b;
                half3 diffuse = baseColor * (sky * occlusion + mainLight.color * direct) * lerp(1.0h, _FoliageBrightness, leafWeight);
                half backlight = pow(saturate(dot(-normalWS, mainLight.direction)), 3.0h) * surface.a * leafWeight * _TransmissionStrength;
                diffuse += baseColor * mainLight.color * backlight * mainLight.shadowAttenuation;
                return half4(saturate(diffuse), albedo.a);
            }
            ENDHLSL
        }
    }
}
