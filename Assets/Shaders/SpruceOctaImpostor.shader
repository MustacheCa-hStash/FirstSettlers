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
        [Toggle] _UseSeasonPalette("Use Maple Season Palette", Float) = 0
        _TreeTintStrength("Per Tree Autumn Tint Strength", Range(0,1)) = 0.65
        _SeasonAutumnAmount("Season Autumn Amount", Range(0,1)) = 1
        _SummerLeafColor("Summer Leaf Color", Color) = (0.18,0.42,0.12,1)
        _AutumnRedColor("Autumn Scarlet Color", Color) = (0.88,0.06,0.035,1)
        _AutumnCrimsonColor("Autumn Crimson Color", Color) = (0.48,0.025,0.04,1)
        _AutumnOrangeColor("Autumn Orange Color", Color) = (1,0.25,0.055,1)
        _AutumnYellowColor("Autumn Yellow Color", Color) = (0.88,0.61,0.12,1)
        _AutumnVariationStrength("Autumn Palette Variation", Range(0,1)) = 0.62
        [Enum(Red Maple,0,Sugar Maple,1)] _SeasonPaletteMode("Season Palette Mode", Float) = 0
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
        _BarkBrightness("Bark Brightness", Range(0.25,3)) = 1
        _BarkShadowColor("Bark Shadow Color", Color) = (0.15,0.15,0.15,1)
        _BarkHighlightColor("Bark Highlight Color", Color) = (0.6,0.6,0.6,1)
        _BarkColorRemap("Bark Color Remap", Range(0,1)) = 0
        _BarkRemapBlackPoint("Bark Remap Black Point", Range(0,1)) = 0.08
        _BarkRemapWhitePoint("Bark Remap White Point", Range(0,1)) = 0.9
        _TransmissionStrength("Leaf Transmission Strength", Range(0,2)) = 0.6
        _WindStrength("Canopy Wind Strength", Range(0,0.5)) = 0.035
        _WindSpeed("Canopy Wind Speed", Range(0,8)) = 1.2
        [Enum(Lit,0,Raw Albedo,1,Coverage,2,Material ID,3,Fixed Frame Raw LOD0,4)] _DebugView("Debug View", Float) = 0
        _DebugFrameX("Debug Fixed Frame X", Range(0,23)) = 0
        _DebugFrameY("Debug Fixed Frame Y", Range(0,23)) = 0
    }

    SubShader
    {
        Tags { "DistantTreeIndirect"="True" "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "IgnoreProjector"="True" }
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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma instancing_options procedural:SetupDistantTree
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/TreeSeasonSimulation.hlsl"
            #include "Assets/Shaders/MapleLeafSeasonPalette.hlsl"
            #include "Assets/Shaders/TreeNightLighting.hlsl"

            TEXTURE2D(_AlbedoCoverage); SAMPLER(sampler_AlbedoCoverage);
            TEXTURE2D(_SurfaceAtlas); SAMPLER(sampler_SurfaceAtlas);
            TEXTURE2D(_DepthAtlas); SAMPLER(sampler_DepthAtlas);
            TEXTURE2D(_MaterialIdAtlas); SAMPLER(sampler_MaterialIdAtlas);
            TEXTURE2D(_AmbientAtlas); SAMPLER(sampler_AmbientAtlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _AlbedoCoverage_TexelSize, _SurfaceAtlas_TexelSize, _DepthAtlas_TexelSize, _MaterialIdAtlas_TexelSize, _AmbientAtlas_TexelSize;
                half4 _LeafSeasonTint, _SummerLeafColor, _AutumnRedColor, _AutumnCrimsonColor, _AutumnOrangeColor, _AutumnYellowColor, _BarkShadowColor, _BarkHighlightColor;
                float4 _CaptureCenterLS;
                half _SeasonStrength, _UseSeasonPalette, _TreeTintStrength, _SeasonAutumnAmount, _AutumnVariationStrength, _SeasonPaletteMode, _Cutoff, _CaptureRadius, _FramesPerAxis, _AtlasTileResolution, _AmbientTileResolution, _AtlasPadding;
                half _DepthParallax, _AOStrength, _SkyStrength, _AmbientFloor, _LeafLightWrap, _FoliageBrightness, _BarkBrightness, _BarkColorRemap, _BarkRemapBlackPoint, _BarkRemapWhitePoint, _TransmissionStrength, _WindStrength, _WindSpeed, _DebugView, _DebugFrameX, _DebugFrameY;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(SpruceOctaInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TreeLeafTint)
            UNITY_INSTANCING_BUFFER_END(SpruceOctaInstance)

            #include "Assets/Shaders/DistantTreeFade.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                nointerpolation float2 atlasFrame : TEXCOORD1;
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

            float2 SelectAtlasFrame(float3 viewDirectionLS)
            {
                float2 frame = clamp(floor(OctEncode(viewDirectionLS) * _FramesPerAxis), 0.0, _FramesPerAxis - 1.0);
                if (_DebugView > 3.5h)
                    frame = clamp(floor(float2(_DebugFrameX, _DebugFrameY) + 0.5), 0.0, _FramesPerAxis - 1.0);
                return frame;
            }

            half4 SampleAtlas(TEXTURE2D_PARAM(textureToSample, samplerToSample), float2 frame, float2 proxyUV, float2 textureSize)
            {
                float2 stride = textureSize / _FramesPerAxis;
                // Derive the tile size from the imported texture, rather than assuming
                // its original PNG size survived Unity's Max Size import setting.
                float2 tileResolution = stride - _AtlasPadding * 2.0;
                // A single full-tree frame is deliberate for this validation pass. Naively
                // blending four image captures produces four visible ghost trees; seam-aware
                // directional interpolation will be added after the proxy is validated.
                float2 pixel = frame * stride + _AtlasPadding + saturate(proxyUV) * tileResolution;
                // UVs describe the capture rectangle's edges. Pixel centers
                // are already at (index + 0.5) / resolution in the baked image.
                return SAMPLE_TEXTURE2D(textureToSample, samplerToSample, pixel / textureSize);
            }

            half4 SampleAtlasFrameLod0(TEXTURE2D_PARAM(textureToSample, samplerToSample), float2 frame, float2 proxyUV, float2 textureSize)
            {
                float2 stride = textureSize / _FramesPerAxis;
                float2 tileResolution = stride - _AtlasPadding * 2.0;
                float2 cell = clamp(floor(frame + 0.5), 0.0, _FramesPerAxis - 1.0);
                float2 pixel = cell * stride + _AtlasPadding + saturate(proxyUV) * tileResolution;
                return SAMPLE_TEXTURE2D_LOD(textureToSample, samplerToSample, pixel / textureSize, 0.0);
            }

            half4 SampleMaterialIdAtlasPoint(float2 frame, float2 proxyUV)
            {
                float2 textureSize = _MaterialIdAtlas_TexelSize.zw;
                float2 stride = textureSize / _FramesPerAxis;
                float2 tileResolution = stride - _AtlasPadding * 2.0;
                float2 pixel = frame * stride + _AtlasPadding + saturate(proxyUV) * tileResolution;
                int2 texel = int2(clamp(floor(pixel), 0.0, textureSize - 1.0));
                return LOAD_TEXTURE2D(_MaterialIdAtlas, texel);
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
                // Camera forward points toward the tree (-cameraDirectionWS).
                // Match LookRotation(-direction, up) used during capture;
                // reversing this cross product mirrors the captured image.
                float3 rightWS = normalize(cross(cameraDirectionWS, upReference));
                float3 upWS = normalize(cross(rightWS, cameraDirectionWS));
                float3 viewDirectionLS = normalize(mul((float3x3)worldToObject, cameraDirectionWS));
                float2 frame = SelectAtlasFrame(viewDirectionLS);
                // Match PositionCameraForFrame in the atlas baker. The root is
                // not at the bottom of the padded image, and its projected
                // position changes with each captured direction.
                float3 frameDirectionLS = OctDecode((frame + 0.5) / _FramesPerAxis);
                float3 frameUpReferenceLS = abs(frameDirectionLS.y) > 0.98 ? float3(0,0,1) : float3(0,1,0);
                float3 frameRightLS = normalize(cross(frameDirectionLS, frameUpReferenceLS));
                float3 frameUpLS = normalize(cross(frameRightLS, frameDirectionLS));
                float2 rootInFrame = float2(dot(-_CaptureCenterLS.xyz, frameRightLS), dot(-_CaptureCenterLS.xyz, frameUpLS));
                float3 rootWS = mul(objectToWorld, float4(0,0,0,1)).xyz;
                float2 billboard = input.uv * 2.0 - 1.0;
                float2 rootedBillboard = billboard * _CaptureRadius - rootInFrame;
                // Keep this affine across the quad so interpolated movement is
                // exactly zero at the root, including roots inside the image.
                float windWeight = rootedBillboard.y / (2.0 * _CaptureRadius);
                float wind = sin(_Time.y * _WindSpeed + dot(centerWS.xz, float2(0.19, 0.37))) * _WindStrength * windWeight;
                float3 positionWS = rootWS + (rightWS * rootedBillboard.x + upWS * rootedBillboard.y) * scale + float3(wind, 0, wind * 0.35);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.atlasFrame = frame;
                output.proxyUV = input.uv;
                output.rightWS = rightWS;
                output.upWS = upWS;
                output.scale = scale;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ApplyDistantTreeFade(input.positionCS.xy);
                float2 frame = input.atlasFrame;
                half4 albedo = SampleAtlas(TEXTURE2D_ARGS(_AlbedoCoverage, sampler_AlbedoCoverage), frame, input.proxyUV, _AlbedoCoverage_TexelSize.zw);
                if (_DebugView > 3.5h)
                {
                    half4 fixedAlbedo = SampleAtlasFrameLod0(TEXTURE2D_ARGS(_AlbedoCoverage, sampler_AlbedoCoverage), float2(_DebugFrameX, _DebugFrameY), input.proxyUV, _AlbedoCoverage_TexelSize.zw);
                    clip(fixedAlbedo.a - _Cutoff);
                    return half4(fixedAlbedo.rgb, fixedAlbedo.a);
                }
                clip(albedo.a - _Cutoff);
                half4 surface = SampleAtlas(TEXTURE2D_ARGS(_SurfaceAtlas, sampler_SurfaceAtlas), frame, input.proxyUV, _SurfaceAtlas_TexelSize.zw);
                half4 depth = SampleAtlas(TEXTURE2D_ARGS(_DepthAtlas, sampler_DepthAtlas), frame, input.proxyUV, _DepthAtlas_TexelSize.zw);
                // Material identity is categorical. Bilinear filtering creates
                // invalid half-leaf/half-bark pixels that show up as pale branch
                // outlines in a tinted neutral-leaf atlas.
                half4 materialId = SampleMaterialIdAtlasPoint(frame, input.proxyUV);
                half4 ambient = SampleAtlas(TEXTURE2D_ARGS(_AmbientAtlas, sampler_AmbientAtlas), frame, input.proxyUV, _AmbientAtlas_TexelSize.zw);

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
                half leafWeight = materialId.r > materialId.g ? 1.0h : 0.0h;
                half4 treeTint = GetDistantTreeTint(UNITY_ACCESS_INSTANCED_PROP(SpruceOctaInstance, _TreeLeafTint));
                half3 legacyLeafTint = lerp(half3(1,1,1), _LeafSeasonTint.rgb * treeTint.rgb, _SeasonStrength);
                half season = TreeSeasonAutumnAmount(_SeasonAutumnAmount);
                half3 seasonalLeafTint = MapleSeasonLeafColor(materialId.b, _AutumnVariationStrength, season,
                    _SummerLeafColor.rgb, _AutumnYellowColor.rgb, _AutumnOrangeColor.rgb, _AutumnRedColor.rgb,
                    treeTint, _TreeTintStrength, _SeasonPaletteMode);
                half3 leafTint = lerp(legacyLeafTint, seasonalLeafTint, _UseSeasonPalette);
                // Maple's stylized bark atlas spans nearly black trunk texels to
                // near-white twig texels. Remapping it into a bounded grey range
                // preserves the pattern without producing black trunks and white
                // interior branch speckles. Spruce leaves this disabled.
                half barkValue = saturate(dot(albedo.rgb, half3(0.299h, 0.587h, 0.114h)));
                half remappedBarkValue = smoothstep(_BarkRemapBlackPoint, max(_BarkRemapWhitePoint, _BarkRemapBlackPoint + 0.001h), barkValue);
                half3 remappedBark = lerp(_BarkShadowColor.rgb, _BarkHighlightColor.rgb, remappedBarkValue);
                half3 barkColor = lerp(albedo.rgb, remappedBark, _BarkColorRemap);
                half3 baseColor = lerp(barkColor, albedo.rgb * leafTint, leafWeight);

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
                half ambientFloor = TreeNightAmbientFloor(_AmbientFloor);
                half3 sky = max(SampleSH(bentNormalWS) * ambient.b, ambientFloor.xxx) * _SkyStrength;
                half occlusion = 1.0h - ambient.a * _AOStrength;
                half roughness = surface.b;
                half surfaceBrightness = lerp(_BarkBrightness, _FoliageBrightness, leafWeight);
                half3 diffuse = baseColor * (sky * occlusion + mainLight.color * direct) * surfaceBrightness;
                half backlight = pow(saturate(dot(-normalWS, mainLight.direction)), 3.0h) * surface.a * leafWeight * _TransmissionStrength;
                diffuse += baseColor * mainLight.color * backlight * mainLight.shadowAttenuation;
                // Use the same scene fog color and distances as terrain/3D
                // trees. Keep coverage intact so fully fogged trees blend into
                // the fog rather than revealing unfogged objects behind them.
                float fogFactor = InitializeInputDataFog(float4(input.positionWS, 1.0), 0.0);
                return half4(MixFog(saturate(diffuse), fogFactor), albedo.a);
            }
            ENDHLSL
        }
    }
}
