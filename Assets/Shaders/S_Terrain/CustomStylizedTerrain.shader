Shader "Custom/StylizedTerrainURP"
{
    Properties
    {
        [HideInInspector] [NoScaleOffset] _ControlMap0("Control Map 0", 2D) = "black" {}
        [HideInInspector] [NoScaleOffset] _ControlMap1("Control Map 1", 2D) = "black" {}
        [HideInInspector] [NoScaleOffset] _ControlMap2("Ground Cover Map", 2D) = "black" {}
        _SurfaceBlendSharpness("Surface Blend Sharpness", Range(0.25, 4.0)) = 1.0

        _SandColor("Sand Color", Color) = (0.80, 0.75, 0.55, 1)
        [NoScaleOffset] _SandAlbedo("Sand Albedo", 2D) = "white" {}
        [NoScaleOffset] _SandNormal("Sand Normal", 2D) = "bump" {}
        _SandTiling("Sand Tiling (Repeats Per World Unit)", Float) = 0.35
        _SandDetailStrength("Sand Albedo Strength", Range(0, 1)) = 0.35
        _SandNormalStrength("Sand Normal Strength", Range(0, 2)) = 0.30
        [ToggleUI] _SplitSandSurfaces("Separate Shore and Desert Sand", Float) = 0
        [NoScaleOffset] _DesertSandAlbedo("Desert Sand Albedo", 2D) = "white" {}
        [NoScaleOffset] _DesertSandNormal("Desert Sand Normal", 2D) = "bump" {}
        _DesertSandTiling("Desert Sand Tiling (Repeats Per World Unit)", Float) = 0.25
        _ShoreSandDampness("Waterline Sand Darkening", Range(0, 0.5)) = 0.22
        _MudColor("Mud Color", Color) = (0.42, 0.32, 0.22, 1)
        _DrySurfaceStrength("Desert Sediment Strength", Range(0,1)) = 1
        [NoScaleOffset] _ClayAlbedo("Cracked Clay Albedo", 2D) = "white" {}
        [NoScaleOffset] _ClayNormal("Cracked Clay Normal", 2D) = "bump" {}
        _ClayColor("Cracked Clay Tint", Color) = (1,1,1,1)
        _ClayAverageColor("Cracked Clay Distance Color", Color) = (0.76,0.67,0.55,1)
        _ClayTiling("Cracked Clay Tiling", Float) = 0.75
        _ClayNormalStrength("Cracked Clay Normal Strength", Range(0,1)) = 0.28
        [NoScaleOffset] _WashAlbedo("Dry Wash Gravel Albedo", 2D) = "white" {}
        [NoScaleOffset] _WashNormal("Dry Wash Gravel Normal", 2D) = "bump" {}
        _WashColor("Dry Wash Gravel Tint", Color) = (1,1,1,1)
        _WashAverageColor("Dry Wash Distance Color", Color) = (0.65,0.55,0.43,1)
        _WashTiling("Dry Wash Tiling", Float) = 0.80
        _WashNormalStrength("Dry Wash Normal Strength", Range(0,1)) = 0.22
        _DrySurfaceFadeStart("Dry Surface Detail Fade Start", Float) = 70
        _DrySurfaceFadeEnd("Dry Surface Detail Fade End", Float) = 220
        _DrySurfaceNormalEnd("Dry Surface Normal Fade End", Float) = 65
        [NoScaleOffset] _IceAlbedo("Glacier Ice Albedo", 2D) = "white" {}
        [NoScaleOffset] _IceNormal("Glacier Ice Normal", 2D) = "bump" {}
        _IceColor("Glacier Ice Tint", Color) = (0.84,0.92,1,1)
        _IceAverageColor("Glacier Ice Distance Average", Color) = (0.60,0.75,0.78,1)
        _IceTiling("Glacier Ice Repeats Per World Unit", Float) = 0.08
        _IceNormalStrength("Glacier Ice Normal Strength", Range(0,1)) = 0.16
        _IceFadeStart("Glacier Ice Detail Fade Start", Float) = 90
        _IceFadeEnd("Glacier Ice Detail Fade End", Float) = 260
        _RockColor("Rock Color (Distance Fallback)", Color) = (0.45, 0.45, 0.45, 1)
        [Toggle(_ROCK_DETAIL)] _RockDetail("Enable Rock / Cliff Detail", Float) = 0
        [NoScaleOffset] _RockAlbedo("Rock / Cliff Albedo", 2D) = "white" {}
        [NoScaleOffset] _RockNormal("Rock / Cliff Normal", 2D) = "bump" {}
        _RockTiling("Rock / Cliff Tiling (Repeats Per World Unit)", Float) = 0.12
        _RockSideTiling("Near Cliff Side Repeats (Horizontal / Vertical)", Vector) = (0, 0, 0, 0)
        _RockTextureStrength("Rock / Cliff Texture Strength", Range(0, 1)) = 1
        _RockAlbedoTint("Rock / Cliff Albedo Tint", Color) = (1, 1, 1, 1)
        _RockDetailStrength("Rock / Cliff Distant Average Color Strength", Range(0, 1)) = 0.5
        _RockAverageAlbedo("Rock / Cliff Average Albedo (Distance Fallback)", Color) = (1,1,1,1)
        _RockNormalStrength("Rock Normal Strength", Range(0, 2)) = 0.45
        _CliffNormalStrength("Cliff Normal Strength", Range(0, 2)) = 0.65
        _RockFarTiling("Rock / Cliff Far Tiling (Repeats Per World Unit)", Float) = 0.0025
        _RockScaleFadeStart("Rock / Cliff Far Scale Blend Start", Float) = 180
        _RockScaleFadeEnd("Rock / Cliff Far Scale Blend End", Float) = 650
        _RockFarMipBias("Rock / Cliff Far Mip Bias", Range(0, 4)) = 1.5
        _RockFarMinMip("Rock / Cliff Far Minimum Mip", Range(0, 6)) = 3
        _RockDetailFadeStart("Rock Color Detail Fade Start", Float) = 2500
        _RockDetailFadeEnd("Rock Color Detail Fade End", Float) = 6000
        _RockNormalFadeStart("Rock / Cliff Normal Fade Start", Float) = 35
        _RockNormalFadeEnd("Rock / Cliff Normal Fade End", Float) = 110
        _SnowColor("Snow Base Color", Color) = (0.92, 0.94, 0.98, 1)
        _CliffColor("Cliff Color (Distance Fallback)", Color) = (0.30, 0.30, 0.30, 1)
        _RiverbedColor("Riverbed Color", Color) = (0.35, 0.30, 0.24, 1)

        _DarkGrassColor("Dark Grass Color", Color) = (0.20, 0.48, 0.18, 1)
        _MidGrassColor("Mid Grass Color", Color) = (0.29, 0.62, 0.24, 1)
        _LightGrassColor("Light Grass Color", Color) = (0.44, 0.78, 0.30, 1)
        _GroundDarkGrassColor("Ground Cover Dark Grass Color", Color) = (0.10, 0.28, 0.09, 1)
        _GroundMidGrassColor("Ground Cover Mid Grass Color", Color) = (0.16, 0.40, 0.13, 1)
        _GroundLightGrassColor("Ground Cover Light Grass Color", Color) = (0.25, 0.52, 0.19, 1)
        _LeafLitterColor("Leaf Litter Color", Color) = (0.24, 0.14, 0.07, 1)
        _BareDirtColor("Bare Dirt Color", Color) = (0.20, 0.13, 0.08, 1)
        _MossColor("Moss Color", Color) = (0.08, 0.28, 0.10, 1)
        _LeafLitterAlbedo("Leaf Litter Albedo", 2D) = "white" {}
        _LeafLitterNormal("Leaf Litter Normal", 2D) = "bump" {}
        _LeafLitterAO("Leaf Litter AO (Optional)", 2D) = "white" {}
        _LeafLitterHeight("Leaf Litter Height (Optional)", 2D) = "gray" {}
        _LeafLitterAOStrength("Leaf Litter AO Strength", Range(0, 1)) = 0
        _LeafLitterHeightStrength("Leaf Litter Height Depth", Range(0, 0.05)) = 0
        _LeafLitterTiling("Leaf Litter Tiling", Float) = 0.35
        _LeafLitterNormalStrength("Leaf Litter Normal Strength", Range(0.0, 2.0)) = 0.45
        _LeafLitterNormalFadeStart("Litter Normal Fade Start", Float) = 12
        _LeafLitterNormalFadeEnd("Litter Normal Fade End", Float) = 45
        _ForestFloorMacroScale("Forest Floor Macro Repeats Per Meter", Float) = 0.025
        _ForestFloorMacroStrength("Forest Floor Macro Tone Strength", Range(0, 0.3)) = 0.12
        _ForestFloorScatter("Forest Floor Warp / Patch Blend / Frequency / Rotation", Vector) = (0, 0, 0, 0)
        _ForestFloorMossStrength("Forest Floor Moss Coverage", Range(0, 1)) = 1
        [NoScaleOffset] _BareDirtAlbedo("Bare Dirt Albedo", 2D) = "white" {}
        [NoScaleOffset] _BareDirtNormal("Bare Dirt Normal", 2D) = "bump" {}
        [NoScaleOffset] _BareDirtAO("Bare Dirt AO (Optional)", 2D) = "white" {}
        [NoScaleOffset] _BareDirtHeight("Bare Dirt Height (Optional)", 2D) = "gray" {}
        _BareDirtAOStrength("Bare Dirt AO Strength", Range(0, 1)) = 0
        _BareDirtHeightStrength("Bare Dirt Height Depth", Range(0, 0.05)) = 0
        _BareDirtTiling("Bare Dirt Tiling", Float) = 0.35
        _BareDirtNormalStrength("Bare Dirt Normal Strength", Range(0.0, 2.0)) = 0.35
        [NoScaleOffset] _MossAlbedo("Moss Albedo", 2D) = "white" {}
        [NoScaleOffset] _MossNormal("Moss Normal", 2D) = "bump" {}
        [NoScaleOffset] _MossAO("Moss AO (Optional)", 2D) = "white" {}
        [NoScaleOffset] _MossHeight("Moss Height (Optional)", 2D) = "gray" {}
        _MossAOStrength("Moss AO Strength", Range(0, 1)) = 0
        _MossHeightStrength("Moss Height Depth", Range(0, 0.05)) = 0
        _MossTiling("Moss Tiling", Float) = 0.32
        _MossNormalStrength("Moss Normal Strength", Range(0.0, 2.0)) = 0.25
        _MossDetailContrast("Moss Fine Detail Contrast", Range(0, 1)) = 0.35
        _MossSaturation("Moss Saturation", Range(0, 1)) = 0.7
        _MossFillColor("Moss Broad Fill Color", Color) = (0.12, 0.18, 0.035, 1)
        _MixedForestFloorAlbedo("Mixed Forest Floor Albedo", 2D) = "white" {}
        _MixedForestFloorNormal("Mixed Forest Floor Normal", 2D) = "bump" {}
        _MixedForestFloorHeight("Mixed Forest Floor Height", 2D) = "gray" {}
        _MixedForestFloorColor("Mixed Forest Floor Tint", Color) = (1, 1, 1, 1)
        _MixedForestFloorHeightStrength("Mixed Forest Floor Height Depth", Range(0, 0.05)) = 0
        _DenseMossAlbedo("Dense Moss Albedo", 2D) = "white" {}
        _DenseMossAO("Dense Moss AO", 2D) = "white" {}
        _DenseMossHeight("Dense Moss Height", 2D) = "gray" {}
        _DenseMossColor("Dense Moss Tint", Color) = (1, 1, 1, 1)
        _DenseMossAOStrength("Dense Moss AO Strength", Range(0, 1)) = 0
        _DenseMossHeightStrength("Dense Moss Height Depth", Range(0, 0.05)) = 0

        [NoScaleOffset] _GrassAlbedo("Grass Albedo", 2D) = "white" {}
        [NoScaleOffset] _GrassNormal("Grass Normal", 2D) = "bump" {}
        _GrassTilingNear("Grass Tiling Near", Float) = 0.5
        _GrassTilingFar("Grass Tiling Far", Float) = 0.15
        _GrassTilingNearDistance("Grass Tiling Near Distance", Float) = 100.0
        _GrassTilingFarDistance("Grass Tiling Far Distance", Float) = 500.0
        _GrassNormalStrength("Grass Normal Strength", Range(0.0, 2.0)) = 0.6
        _GrassDetailStrength("Grass Detail Strength", Range(0.0, 1.0)) = 0.35
        _GrassDetailContrast("Grass Detail Contrast", Range(0.5, 3.0)) = 1.35
        [Toggle(_GRASS_BLADE_GROUND)] _GrassBladeGround("Use Matching Blade Ground", Float) = 0
        [NoScaleOffset] _GrassSurfaceMap("Blade Ground (Tone, Normal XZ, Height)", 2D) = "gray" {}
        _GrassGroundTiling("Blade Ground Near Repeats Per Meter", Float) = 0.45
        _GrassGroundFarTiling("Blade Ground Far Repeats Per Meter", Float) = 0.06
        _GrassGroundScaleFadeStart("Blade Ground Far Scale Blend Start", Float) = 30
        _GrassGroundScaleFadeEnd("Blade Ground Far Scale Blend End", Float) = 100
        _GrassGroundGridScale("Blade Ground Randomized Blend Scale", Float) = 0.65
        _GrassGroundSingleSample("Blade Ground Far Single Sample Fade (Start / End)", Vector) = (0, 0, 0, 0)
        _GrassGroundDetailStrength("Blade Ground Tone Strength", Range(0, 1)) = 0.65
        _GrassGroundDetailContrast("Blade Ground Tone Contrast", Range(0.5, 3)) = 1.6
        _GrassGroundTint("Blade Ground Average Tint", Color) = (0.90, 0.90, 0.78, 1)
        _MeadowTurfTint("Meadow Turf Tint", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _NearMeadowAlbedo("Near Meadow Turf Albedo", 2D) = "white" {}
        [NoScaleOffset] _NearMeadowRelief("Near Meadow Turf Normal and AO", 2D) = "white" {}
        [HideInInspector] _UseNearMeadowTurf("Use Near Meadow Turf", Float) = 0
        [HDR] _ForestGrassGroundTint("Forest Grass Substrate Tint", Color) = (1, 1, 1, 1)
        _GrassGroundDetailFadeStart("Blade Ground Detail Fade Start", Float) = 180
        _GrassGroundDetailFadeEnd("Blade Ground Detail Fade End", Float) = 400
        _GrassGroundNormalFadeStart("Blade Ground Normal Fade Start", Float) = 10
        _GrassGroundNormalFadeEnd("Blade Ground Normal Fade End", Float) = 30
        _GrassGroundHeightDepth("Blade Ground Parallax Depth (Meters)", Range(0, 0.02)) = 0.008
        _GrassGroundHeightFadeStart("Blade Ground Parallax Fade Start", Float) = 4
        _GrassGroundHeightFadeEnd("Blade Ground Parallax Fade End", Float) = 12

        [NoScaleOffset] _SnowAlbedo("Snow Albedo", 2D) = "white" {}
        _SnowTint("Snow Tint", Color) = (0.95, 0.97, 1.00, 1)
        [NoScaleOffset] _SnowNormal("Snow Normal", 2D) = "bump" {}
        _SnowNormalStrength("Snow Normal Strength", Range(0.0, 2.0)) = 0.35
        _SnowRockSeparation("Snow / Rock Separation", Range(0, 1)) = 1
        _SnowCoverageThreshold("Snow Coverage Threshold", Range(0.05, 0.95)) = 0.5
        _SnowBoundaryWidth("Snow Boundary Width", Range(0.01, 1)) = 0.2
        _SnowTilingNear("Snow Tiling Near", Float) = 0.06
        _SnowTilingFar("Snow Tiling Far", Float) = 0.02
        _SnowTilingNearDistance("Snow Tiling Near Distance", Float) = 25.0
        _SnowTilingFarDistance("Snow Tiling Far Distance", Float) = 200.0

        _SnowTriplanarStart("Snow Triplanar Start", Range(0.0, 1.0)) = 0.35
        _SnowTriplanarEnd("Snow Triplanar End", Range(0.0, 1.0)) = 0.75
        _SnowTriplanarSharpness("Snow Triplanar Sharpness", Range(1.0, 8.0)) = 4.0

        _DistanceBlendNoiseScale("Distance Blend Noise Scale", Float) = 0.01
        _DistanceBlendNoiseStrength("Distance Blend Noise Strength", Range(0.0, 1.0)) = 0.15

        _NoiseScale("Noise Scale", Range(0.001, 0.2)) = 0.03
        _NoiseStrength("Noise Strength", Range(0.0, 2.0)) = 1.0
        _BlendSharpness("Blend Sharpness", Range(0.25, 3.0)) = 1.0
        _AmbientStrength("Minimum Ambient Strength", Range(0.0, 1.0)) = 0.26
        _ReceiveShadows("Receive Shadows", Float) = 1.0
        [HideInInspector] [NoScaleOffset] _ExcavationAlbedo("Excavated Stone Albedo", 2D) = "white" {}
        [HideInInspector] [NoScaleOffset] _ExcavationNormal("Excavated Stone Normal", 2D) = "bump" {}
        [HideInInspector] _ExcavationTint("Excavated Stone Tint", Color) = (1,1,1,1)
        [HideInInspector] _ExcavationTiling("Excavated Stone Repeats Per Metre", Float) = 0.65
        [HideInInspector] _ExcavationNormalStrength("Excavated Stone Normal Strength", Float) = 0
        [HideInInspector] [NoScaleOffset] _TerrainHorizon0("Terrain Horizon 0", 2D) = "black" {}
        [HideInInspector] [NoScaleOffset] _TerrainHorizon1("Terrain Horizon 1", 2D) = "black" {}
        [HideInInspector] _TerrainHorizonUV("Terrain Horizon UV", Vector) = (0,0,0,0)
        [HideInInspector] _TerrainHorizonParams("Terrain Horizon Strength / Softness", Vector) = (0,0,0,0)
        [HideInInspector] _TerrainHorizonTint("Terrain Horizon Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma target 4.5
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ROCK_DETAIL
            #pragma shader_feature_local_fragment _GRASS_BLADE_GROUND
            // Runtime chunk materials select this variant; retain it in player builds.
            #pragma multi_compile_local _ _TERRAIN_EXCAVATION
            #pragma multi_compile_fog
            // PC_Renderer uses Forward+: GetMainLight needs its clustered attenuation variant.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/TerrainSkyAccess.hlsl"
            #include "Assets/Shaders/LocalFlameLighting.hlsl"
            #include "Assets/Shaders/ForestCanopyLighting.hlsl"
            #include "Assets/Shaders/WorldLandformFields.hlsl"
            #include "Assets/Shaders/HeightfieldCliffNormal.hlsl"
            #include "Assets/Shaders/TerrainSurfaceFrame.hlsl"

            TEXTURE2D(_ControlMap0);
            // Chunk control maps share clamp/filter settings. Repeat detail
            // normals share their layer's albedo sampler, leaving room for URP
            // shadow samplers within the D3D11 limit of 16 sampler registers.
            SAMPLER(sampler_ControlMap0);

            TEXTURE2D(_ControlMap1);


            TEXTURE2D(_ControlMap2);
            // Reuse the linear-clamp control-map sampler: no extra sampler registers.
            TEXTURE2D(_TerrainHorizon0);
            TEXTURE2D(_TerrainHorizon1);


            TEXTURE2D(_GrassAlbedo);
            SAMPLER(sampler_GrassAlbedo);

            TEXTURE2D(_GrassNormal);

            TEXTURE2D(_GrassSurfaceMap);
            SAMPLER(sampler_GrassSurfaceMap);
            TEXTURE2D(_NearMeadowAlbedo);
            TEXTURE2D(_NearMeadowRelief);
            SAMPLER(sampler_NearMeadowAlbedo);

            TEXTURE2D(_LeafLitterAlbedo);
            SAMPLER(sampler_LeafLitterAlbedo);

            TEXTURE2D(_LeafLitterNormal);

            TEXTURE2D(_LeafLitterAO);
            TEXTURE2D(_LeafLitterHeight);

            TEXTURE2D(_BareDirtAlbedo);
            SAMPLER(sampler_BareDirtAlbedo);

            TEXTURE2D(_BareDirtNormal);

            TEXTURE2D(_BareDirtAO);
            TEXTURE2D(_BareDirtHeight);

            TEXTURE2D(_MossAlbedo);
            SAMPLER(sampler_MossAlbedo);

            TEXTURE2D(_MossNormal);

            TEXTURE2D(_MossAO);
            TEXTURE2D(_MossHeight);
            TEXTURE2D(_MixedForestFloorAlbedo);
            TEXTURE2D(_MixedForestFloorNormal);
            TEXTURE2D(_MixedForestFloorHeight);
            TEXTURE2D(_DenseMossAlbedo);
            TEXTURE2D(_DenseMossAO);
            TEXTURE2D(_DenseMossHeight);

            TEXTURE2D(_SandAlbedo);
            TEXTURE2D(_DesertSandAlbedo);
            TEXTURE2D(_DesertSandNormal);
            TEXTURE2D(_IceAlbedo);
            TEXTURE2D(_IceNormal);
            TEXTURE2D(_ClayAlbedo);
            TEXTURE2D(_ClayNormal);
            TEXTURE2D(_WashAlbedo);
            TEXTURE2D(_WashNormal);
            SAMPLER(sampler_SandAlbedo);
            TEXTURE2D(_SandNormal);

            TEXTURE2D(_SnowAlbedo);
            SAMPLER(sampler_SnowAlbedo);

            TEXTURE2D(_SnowNormal);


            TEXTURE2D(_RockAlbedo);
            SAMPLER(sampler_RockAlbedo);
            TEXTURE2D(_RockNormal);
            TEXTURE2D(_ExcavationAlbedo);
            TEXTURE2D(_ExcavationNormal);
            SAMPLER(sampler_ExcavationAlbedo);


            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 surfaceData : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionRWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                float2 horizonLight : TEXCOORD5;
                float exposure : TEXCOORD6;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ControlMap0_TexelSize;
                half4 _ExcavationTint;
                float _ExcavationTiling;
                float _ExcavationNormalStrength;
                float _SurfaceBlendSharpness;
                half4 _SandColor;
                float _SandTiling;
                float _SandDetailStrength;
                float _SandNormalStrength;
                float _SplitSandSurfaces, _DesertSandTiling, _ShoreSandDampness;
                half4 _MudColor;
                half4 _ClayColor, _WashColor, _ClayAverageColor, _WashAverageColor;
                float _DrySurfaceStrength, _ClayTiling, _WashTiling;
                float _ClayNormalStrength, _WashNormalStrength;
                float _DrySurfaceFadeStart, _DrySurfaceFadeEnd, _DrySurfaceNormalEnd;
                half4 _IceColor, _IceAverageColor;
                float _IceTiling, _IceNormalStrength, _IceFadeStart, _IceFadeEnd;
                half4 _RockColor;
                float _RockDetail;
                float _RockTiling;
                float4 _RockSideTiling;
                float _RockDetailStrength;
                float _RockTextureStrength;
                half4 _RockAlbedoTint;
                half4 _RockAverageAlbedo;
                float _RockNormalStrength;
                float _CliffNormalStrength;
                float _RockDetailFadeStart;
                float _RockDetailFadeEnd;
                float _RockFarTiling;
                float _RockScaleFadeStart;
                float _RockScaleFadeEnd;
                float _RockFarMipBias;
                float _RockFarMinMip;
                float4 _RockAlbedo_TexelSize;
                float _RockNormalFadeStart;
                float _RockNormalFadeEnd;
                half4 _SnowColor;
                half4 _CliffColor;
                half4 _RiverbedColor;

                half4 _DarkGrassColor;
                half4 _MidGrassColor;
                half4 _LightGrassColor;
                half4 _GroundDarkGrassColor;
                half4 _GroundMidGrassColor;
                half4 _GroundLightGrassColor;
                half4 _LeafLitterColor;
                half4 _BareDirtColor;
                half4 _MossColor;
                half4 _MossFillColor;
                half4 _MixedForestFloorColor;
                half4 _DenseMossColor;
                float _LeafLitterTiling;
                float4 _LeafLitterAlbedo_ST;
                float4 _LeafLitterNormal_ST;
                float4 _LeafLitterAO_ST;
                float4 _LeafLitterHeight_ST;
                float _LeafLitterNormalStrength;
                float _LeafLitterNormalFadeStart;
                float _LeafLitterNormalFadeEnd;
                float _ForestFloorMacroScale;
                float _ForestFloorMacroStrength;
                float4 _ForestFloorScatter;
                half _ForestFloorMossStrength;
                float _LeafLitterAOStrength;
                float _LeafLitterHeightStrength;
                float _BareDirtTiling;
                float _BareDirtNormalStrength;
                float _BareDirtAOStrength;
                float _BareDirtHeightStrength;
                float _MossTiling;
                float _MossNormalStrength;
                float _MossDetailContrast;
                float _MossSaturation;
                float _MossAOStrength;
                float _MossHeightStrength;
                float4 _MixedForestFloorAlbedo_ST;
                float4 _MixedForestFloorNormal_ST;
                float4 _MixedForestFloorHeight_ST;
                float _MixedForestFloorHeightStrength;
                float4 _DenseMossAlbedo_ST;
                float4 _DenseMossAO_ST;
                float4 _DenseMossHeight_ST;
                float _DenseMossAOStrength;
                float _DenseMossHeightStrength;

                half4 _SnowTint;

                float _GrassTilingNear;
                float _GrassTilingFar;
                float _GrassTilingNearDistance;
                float _GrassTilingFarDistance;
                float _GrassNormalStrength;
                float _GrassDetailStrength;
                float _GrassDetailContrast;
                float _GrassBladeGround;
                float _GrassGroundTiling;
                float _GrassGroundFarTiling;
                float _GrassGroundScaleFadeStart;
                float _GrassGroundScaleFadeEnd;
                float _GrassGroundGridScale;
                float4 _GrassGroundSingleSample;
                float _GrassGroundDetailStrength;
                float _GrassGroundDetailContrast;
                half4 _GrassGroundTint;
                half4 _MeadowTurfTint;
                half _UseNearMeadowTurf;
                half4 _ForestGrassGroundTint;
                float _GrassGroundDetailFadeStart;
                float _GrassGroundDetailFadeEnd;
                float _GrassGroundNormalFadeStart;
                float _GrassGroundNormalFadeEnd;
                float _GrassGroundHeightDepth;
                float _GrassGroundHeightFadeStart;
                float _GrassGroundHeightFadeEnd;

                float _SnowNormalStrength;
                float _SnowRockSeparation;
                float _SnowCoverageThreshold;
                float _SnowBoundaryWidth;
                float _SnowTilingNear;
                float _SnowTilingFar;
                float _SnowTilingNearDistance;
                float _SnowTilingFarDistance;
                float _SnowTriplanarStart;
                float _SnowTriplanarEnd;
                float _SnowTriplanarSharpness;

                float _DistanceBlendNoiseScale;
                float _DistanceBlendNoiseStrength;

                float _NoiseScale;
                float _NoiseStrength;
                float _BlendSharpness;
                float _AmbientStrength;
                float _ReceiveShadows;
                float4 _TerrainHorizonUV;
                float4 _TerrainHorizonParams;
                half4 _TerrainHorizonTint;
            CBUFFER_END

            half HorizonChannel(half4 first, half4 second, uint direction)
            {
                return direction < 4u ? first[direction & 3u] : second[direction & 3u];
            }

            half TerrainHorizonVisibility(float3 positionWS, float2 horizonLight)
            {
                [branch] if (_TerrainHorizonParams.x <= 0.0) return 1.0h;
                float2 uv = positionWS.xz * _TerrainHorizonUV.xy + _TerrainHorizonUV.zw;
                half4 first = SAMPLE_TEXTURE2D(_TerrainHorizon0, sampler_ControlMap0, uv);
                half4 second = SAMPLE_TEXTURE2D(_TerrainHorizon1, sampler_ControlMap0, uv);
                // Channels run +X, +X/+Z, +Z, ... . Light angles are calculated per vertex.
                float azimuth = horizonLight.x;
                uint sector = (uint)floor(azimuth);
                float horizon = lerp(HorizonChannel(first, second, sector),
                    HorizonChannel(first, second, (sector + 1u) & 7u), frac(azimuth)) * (PI * 0.5);
                float halfWidth = _TerrainHorizonParams.y * 0.5;
                half visibility = smoothstep(horizon - halfWidth, horizon + halfWidth, horizonLight.y);
                return lerp(1.0h, visibility, (half)_TerrainHorizonParams.x);
            }

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                float x1 = lerp(a, b, f.x);
                float x2 = lerp(c, d, f.x);
                return lerp(x1, x2, f.y);
            }

            half3 EvaluateGrassTint(
                float2 worldXZ,
                half3 darkGrassColor,
                half3 midGrassColor,
                half3 lightGrassColor)
            {
                float n = ValueNoise(worldXZ * _NoiseScale);
                n = saturate((n - 0.5) * _NoiseStrength + 0.5);
                n = saturate(pow(n, _BlendSharpness));

                if (n < 0.5)
                {
                    return lerp(darkGrassColor, midGrassColor, n * 2.0);
                }

                return lerp(midGrassColor, lightGrassColor, (n - 0.5) * 2.0);
            }

            half3 ForestFloorMacroTone(float2 worldXZ)
            {
                // Low-contrast world-space decomposition/moisture variation, shared across chunks.
                // Arithmetic noise avoids another texture or an additional terrain layer.
                float2 p = worldXZ * _ForestFloorMacroScale;
                float macro = ValueNoise(p + float2(17.3, 41.7)) * 0.72 +
                    ValueNoise(p * 3.1 + float2(-23.8, 8.4)) * 0.28;
                half variation = (macro * 2.0 - 1.0) * _ForestFloorMacroStrength;
                return (1.0h + variation) * half3(1.0h + variation * 0.12h,
                    1.0h, 1.0h - variation * 0.16h);
            }

            #if defined(_GRASS_BLADE_GROUND)
            #include "GrassGroundSurface.hlsl"
            #endif

            float3 ApplyDetailNormal(float3 baseNormalWS, float3 tangentNormal, float strength)
            {
                tangentNormal.xy *= strength;
                tangentNormal.z = sqrt(saturate(1.0 - dot(tangentNormal.xy, tangentNormal.xy)));

                float3 referenceUp = abs(baseNormalWS.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                float3 tangentWS = normalize(cross(referenceUp, baseNormalWS));
                float3 bitangentWS = normalize(cross(baseNormalWS, tangentWS));

                float3 mappedNormalWS =
                    tangentWS * tangentNormal.x +
                    bitangentWS * tangentNormal.y +
                    baseNormalWS * tangentNormal.z;

                return normalize(mappedNormalWS);
            }

            float2 RotateUV90(float2 uv)
            {
                return float2(-uv.y, uv.x);
            }

            float GetDistanceBlend(float distanceToCamera, float nearDistance, float farDistance)
            {
                float denom = max(farDistance - nearDistance, 1e-5);
                return saturate((distanceToCamera - nearDistance) / denom);
            }

            float GetNoisyDistanceBlend(float distanceToCamera, float nearDistance, float farDistance, float2 worldXZ)
            {
                float t = GetDistanceBlend(distanceToCamera, nearDistance, farDistance);
                float noise = ValueNoise(worldXZ * _DistanceBlendNoiseScale);
                float noisyT = t + (noise - 0.5) * _DistanceBlendNoiseStrength;
                return saturate(noisyT);
            }

            float3 SampleSnowTriplanarAlbedo(float3 positionWS, float3 normalWS, float snowTiling)
            {
                float3 blend = pow(abs(normalWS), _SnowTriplanarSharpness);
                blend /= max(dot(blend, 1.0.xxx), 1e-5);

                float2 uvX = RotateUV90(positionWS.yz * snowTiling);
                float2 uvY = RotateUV90(positionWS.xz * snowTiling);
                float2 uvZ = RotateUV90(positionWS.xy * snowTiling);

                float3 sampleX = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, uvX).rgb;
                float3 sampleY = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, uvY).rgb;
                float3 sampleZ = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, uvZ).rgb;

                return sampleX * blend.x + sampleY * blend.y + sampleZ * blend.z;
            }

            float3 SampleSnowTriplanarPerturbation(float3 positionWS, float3 normalWS, float snowTiling)
            {
                float3 blend = pow(abs(normalWS), _SnowTriplanarSharpness);
                blend /= max(dot(blend, 1.0.xxx), 1e-5);

                float2 uvX = RotateUV90(positionWS.yz * snowTiling);
                float2 uvY = RotateUV90(positionWS.xz * snowTiling);
                float2 uvZ = RotateUV90(positionWS.xy * snowTiling);

                float3 sampleX = UnpackNormal(SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowAlbedo, uvX));
                float3 sampleY = UnpackNormal(SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowAlbedo, uvY));
                float3 sampleZ = UnpackNormal(SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowAlbedo, uvZ));

                // Match RotateUV90 on each projection: yz -> (-z,y), xz -> (-z,x), xy -> (-y,x).
                // Convert texture slopes into world space before blending, as for rock detail.
                return float3(0, sampleX.y, -sampleX.x) * blend.x
                     + float3(sampleY.y, 0, -sampleY.x) * blend.y
                     + float3(sampleZ.y, -sampleZ.x, 0) * blend.z;
            }

            half3 SampleFarRockProjection(float2 uv, float2 dx, float2 dy)
            {
                // Use the pixel footprint even under divergent surface/axis branches.
                // A coarser world scale must not pull full-resolution mips back in.
                // Far color uses isotropic trilinear filtering (no anisotropic taps).
                float2 texelDx = dx * _RockAlbedo_TexelSize.zw;
                float2 texelDy = dy * _RockAlbedo_TexelSize.zw;
                float footprintSquared = max(dot(texelDx, texelDx), dot(texelDy, texelDy));
                float mip = max(0.5 * log2(max(footprintSquared, 1e-8)) + max(_RockFarMipBias, 0.0), _RockFarMinMip);
                return SAMPLE_TEXTURE2D_LOD(_RockAlbedo, sampler_RockAlbedo, uv, mip).rgb;
            }

            half3 SampleFarRockColor(float3 positionWS, float3 baseNormalWS, float3 positionDx, float3 positionDy)
            {
                float3 blend = max(pow(abs(baseNormalWS), 4.0) - 0.01, 0.0);
                blend /= max(dot(blend, float3(1, 1, 1)), 1e-5);
                float3 axisSign = step(0.0, baseNormalWS) * 2.0 - 1.0;
                float tiling = max(_RockFarTiling, 0.0001);
                float3 p = positionWS * tiling, dx = positionDx * tiling, dy = positionDy * tiling;
                half3 color = 0;
                // Same world-up projection frames as near rock; no new texture,
                // normal reads, sampler, geometry, control map or shader variant.
                [branch] if (blend.x > 0.0)
                    color += SampleFarRockProjection(p.zy * float2(-axisSign.x, 1), dx.zy, dy.zy) * blend.x;
                [branch] if (blend.y > 0.0)
                    color += SampleFarRockProjection(p.zx * float2(axisSign.y, 1), dx.zx, dy.zx) * blend.y;
                [branch] if (blend.z > 0.0)
                    color += SampleFarRockProjection(p.xy * float2(axisSign.z, 1), dx.xy, dy.xy) * blend.z;
                return color;
            }

            void SampleRockDetail(float3 positionWS, float3 baseNormalWS, float3 positionDx, float3 positionDy, float strength,
                out half3 albedo, out float3 detailNormalWS)
            {
                // Ease negligible axes to zero before branching: continuous weights,
                // with one projection on axis-aligned cliffs instead of three.
                float3 blend = max(pow(abs(baseNormalWS), 4.0) - 0.01, 0.0);
                blend /= max(dot(blend, float3(1, 1, 1)), 1e-5);
                float3 axisSign = step(0.0, baseNormalWS) * 2.0 - 1.0;
                float tiling = max(_RockTiling, 0.0001);
                float3 p = positionWS * tiling;
                float3 dx = positionDx * tiling;
                float3 dy = positionDy * tiling;
                // Both side projections keep texture V aligned with world up.
                // Signed U axes match a right-handed tangent frame on either face.
                float2 side = _RockSideTiling.x > 0 ? max(_RockSideTiling.xy, 0.0001) / tiling : 1.0.xx;
                float2 uvX = p.zy * float2(-axisSign.x, 1) * side;
                float2 uvY = p.zx * float2(axisSign.y, 1);
                float2 uvZ = p.xy * float2(axisSign.z, 1) * side;
                float2 signX = float2(-axisSign.x, 1) * side;
                float2 signY = float2(axisSign.y, 1);
                float2 signZ = float2(axisSign.z, 1) * side;
                albedo = 0;
                float3 perturbation = 0;
                // Explicit gradients are calculated outside divergent branches.
                // Normals share the albedo repeat sampler; no new sampler registers.
                [branch] if (blend.x > 0.0)
                {
                    albedo += SAMPLE_TEXTURE2D_GRAD(_RockAlbedo, sampler_RockAlbedo, uvX, dx.zy * signX, dy.zy * signX).rgb * blend.x;
                    [branch] if (strength > 0.001)
                    {
                        float3 n = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_RockNormal, sampler_RockAlbedo, uvX, dx.zy * signX, dy.zy * signX));
                        perturbation += float3(0, n.y, -n.x * axisSign.x) * blend.x;
                    }
                }
                [branch] if (blend.y > 0.0)
                {
                    albedo += SAMPLE_TEXTURE2D_GRAD(_RockAlbedo, sampler_RockAlbedo, uvY, dx.zx * signY, dy.zx * signY).rgb * blend.y;
                    [branch] if (strength > 0.001)
                    {
                        float3 n = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_RockNormal, sampler_RockAlbedo, uvY, dx.zx * signY, dy.zx * signY));
                        perturbation += float3(n.y, 0, n.x * axisSign.y) * blend.y;
                    }
                }
                [branch] if (blend.z > 0.0)
                {
                    albedo += SAMPLE_TEXTURE2D_GRAD(_RockAlbedo, sampler_RockAlbedo, uvZ, dx.xy * signZ, dy.xy * signZ).rgb * blend.z;
                    [branch] if (strength > 0.001)
                    {
                        float3 n = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_RockNormal, sampler_RockAlbedo, uvZ, dx.xy * signZ, dy.xy * signZ));
                        perturbation += float3(n.x * axisSign.z, n.y, 0) * blend.z;
                    }
                }
                // Project slopes onto the actual mesh tangent plane. Flat maps
                // preserve the mesh normal, including across projection blends.
                perturbation -= baseNormalWS * dot(perturbation, baseNormalWS);
                detailNormalWS = normalize(baseNormalWS + perturbation * strength);
            }

            void ReadIceTile(float2 uv,float2 dx,float2 dy,float strength,out half3 albedo,out float3 normalTS)
            {
                // Four overlapping interior windows are periodic even when AI artwork
                // has slightly different edge pixels. The selected source remains untouched.
                float2 f=frac(uv),blend=smoothstep(0.0,1.0,f);
                albedo=0;normalTS=0;
                [unroll] for(int i=0;i<4;i++)
                {
                    float2 corner=float2(i&1,i>>1);
                    float weight=(corner.x>0?blend.x:1.0-blend.x)*(corner.y>0?blend.y:1.0-blend.y);
                    float2 sampleUV=(f-corner)*.5+.5;
                    albedo+=SAMPLE_TEXTURE2D_GRAD(_IceAlbedo,sampler_SandAlbedo,sampleUV,dx*.5,dy*.5).rgb*weight;
                    [branch] if(strength>.001)
                        normalTS+=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_IceNormal,sampler_SandAlbedo,sampleUV,dx*.5,dy*.5))*weight;
                }
                if(strength<=.001)normalTS=float3(0,0,1);
                else normalTS=normalize(normalTS);
                albedo=lerp(_IceAverageColor.rgb,albedo,.7);
            }
            void SampleGlacierDetail(float3 positionWS, float3 baseNormalWS, float3 positionDx, float3 positionDy, float strength,
                out half3 albedo, out float3 detailNormalWS)
            {
                // Ease negligible axes to zero before branching: continuous weights,
                // with one projection on axis-aligned cliffs instead of three.
                float3 blend = max(pow(abs(baseNormalWS), 4.0) - 0.01, 0.0);
                blend /= max(dot(blend, float3(1, 1, 1)), 1e-5);
                float3 axisSign = step(0.0, baseNormalWS) * 2.0 - 1.0;
                float tiling = max(_IceTiling, 0.0001);
                float3 p = positionWS * tiling;
                float3 dx = positionDx * tiling;
                float3 dy = positionDy * tiling;
                // Both side projections keep texture V aligned with world up.
                // Signed U axes match a right-handed tangent frame on either face.
                float2 uvX = p.zy * float2(-axisSign.x, 1);
                float2 uvY = p.zx * float2(axisSign.y, 1);
                float2 uvZ = p.xy * float2(axisSign.z, 1);
                float2 signX = float2(-axisSign.x, 1);
                float2 signY = float2(axisSign.y, 1);
                float2 signZ = float2(axisSign.z, 1);
                albedo = 0;
                float3 perturbation = 0;
                // Explicit gradients are calculated outside divergent branches.
                // Normals share the albedo repeat sampler; no new sampler registers.
                [branch] if (blend.x > 0.0)
                {
                    half3 color;float3 n;ReadIceTile(uvX,dx.zy*signX,dy.zy*signX,strength,color,n);
                    albedo += color * blend.x;
                    [branch] if (strength > 0.001)
                    {
                        perturbation += float3(0, n.y, -n.x * axisSign.x) * blend.x;
                    }
                }
                [branch] if (blend.y > 0.0)
                {
                    half3 color;float3 n;ReadIceTile(uvY,dx.zx*signY,dy.zx*signY,strength,color,n);
                    albedo += color * blend.y;
                    [branch] if (strength > 0.001)
                    {
                        perturbation += float3(n.y, 0, n.x * axisSign.y) * blend.y;
                    }
                }
                [branch] if (blend.z > 0.0)
                {
                    half3 color;float3 n;ReadIceTile(uvZ,dx.xy*signZ,dy.xy*signZ,strength,color,n);
                    albedo += color * blend.z;
                    [branch] if (strength > 0.001)
                    {
                        perturbation += float3(n.x * axisSign.z, n.y, 0) * blend.z;
                    }
                }
                // Project slopes onto the actual mesh tangent plane. Flat maps
                // preserve the mesh normal, including across projection blends.
                perturbation -= baseNormalWS * dot(perturbation, baseNormalWS);
                detailNormalWS = normalize(baseNormalWS + perturbation * strength);
            }
            void SampleSandDetail(float3 positionWS, float2 dx, float2 dy, float3 baseNormalWS,
                out half3 albedo, out float3 detailNormalWS)
            {
                float heightAboveWater = positionWS.y - _WorldGlacialParams.y;
                float band = _WorldGlacialParams.z;
                bool splitSurfaces = _SplitSandSurfaces > 0.5 && band > 0.0;
                half inland = splitSurfaces ? smoothstep(band, band * 2.5, heightAboveWater) : 0.0h;
                albedo = 0;
                float3 tangentNormal = 0;
                [branch] if (inland < 1.0h)
                {
                    float scale = max(_SandTiling, 0.0001);
                    float2 uv = positionWS.xz * scale;
                    half3 shore = SAMPLE_TEXTURE2D_GRAD(_SandAlbedo, sampler_SandAlbedo, uv, dx * scale, dy * scale).rgb;
                    half damp = splitSurfaces ? 1.0h - smoothstep(0.0, band, heightAboveWater) : 0.0h;
                    albedo = shore * (1.0h - damp * _ShoreSandDampness) * (1.0h - inland);
                    tangentNormal = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_SandNormal, sampler_SandAlbedo, uv,
                        dx * scale, dy * scale)) * (1.0h - inland);
                }
                [branch] if (inland > 0.0h)
                {
                    float scale = max(_DesertSandTiling, 0.0001);
                    float2 uv = positionWS.xz * scale;
                    albedo += SAMPLE_TEXTURE2D_GRAD(_DesertSandAlbedo, sampler_SandAlbedo, uv,
                        dx * scale, dy * scale).rgb * inland;
                    tangentNormal += UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_DesertSandNormal, sampler_SandAlbedo, uv,
                        dx * scale, dy * scale)) * inland;
                }
                detailNormalWS = ApplyDetailNormal(baseNormalWS, tangentNormal, _SandNormalStrength);
            }

            void ReadDesertSediment(float2 uv, float2 dx, float2 dy, bool clay, float normalStrength,
                out half3 albedo, out float3 normalTS)
            {
                normalTS = float3(0,0,1);
                // Share the existing repeat sampler; no extra sampler registers.
                [branch] if (clay)
                {
                    albedo = SAMPLE_TEXTURE2D_GRAD(_ClayAlbedo,sampler_SandAlbedo,uv,dx,dy).rgb;
                    [branch] if(normalStrength>0.001) normalTS=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_ClayNormal,sampler_SandAlbedo,uv,dx,dy));
                }
                else
                {
                    albedo = SAMPLE_TEXTURE2D_GRAD(_WashAlbedo,sampler_SandAlbedo,uv,dx,dy).rgb;
                    [branch] if(normalStrength>0.001) normalTS=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_WashNormal,sampler_SandAlbedo,uv,dx,dy));
                }
            }

            void SampleDesertSediment(float3 positionWS,float3 baseNormalWS,float3 positionDx,float3 positionDy,
                float distanceToCamera,bool clay,out half3 albedo,out float3 detailNormalWS)
            {
                half3 tint=clay?_ClayColor.rgb:_WashColor.rgb;
                half3 average=clay?_ClayAverageColor.rgb:_WashAverageColor.rgb;
                float fade=1.0-GetDistanceBlend(distanceToCamera,_DrySurfaceFadeStart,_DrySurfaceFadeEnd);
                float macro=FSLandformNoise(positionWS.xz,max(_WorldLandformParams.y*0.45,1.0),_WorldLandformSeed+89113);
                tint*=1.0+macro*0.045;
                albedo=average*tint;detailNormalWS=baseNormalWS;
                [branch] if(fade<=0.001) return;
                float tiling=max(clay?_ClayTiling:_WashTiling,0.001);
                float normalStrength=(clay?_ClayNormalStrength:_WashNormalStrength)*
                    (1.0-smoothstep(_DrySurfaceNormalEnd*0.35,max(_DrySurfaceNormalEnd,0.01),distanceToCamera));
                float2 f=frac(positionWS.xz*tiling),blend=f*f*(3.0-2.0*f);
                float2 dx=positionDx.xz*tiling*0.5,dy=positionDy.xz*tiling*0.5;
                half3 color=0;float3 normal=0;
                // Overlapping texture windows fade out before reaching an image edge.
                // This is a continuous periodic material even when an authored source's edge pixels differ.
                [unroll] for(int i=0;i<4;i++)
                {
                    float2 corner=float2(i&1,(i>>1)&1);
                    float2 uv=(f-corner)*0.5+0.5;
                    float2 pair=lerp(1.0-blend,blend,corner);
                    float weight=pair.x*pair.y;
                    half3 sampleColor;float3 sampleNormal;
                    ReadDesertSediment(uv,dx,dy,clay,normalStrength,sampleColor,sampleNormal);
                    color+=sampleColor*weight;normal+=sampleNormal*weight;
                }
                albedo=lerp(average,color,fade)*tint;
                detailNormalWS=ApplyDetailNormal(baseNormalWS,normalize(normal),normalStrength*fade);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = positionInputs.positionCS;
                OUT.positionRWS = FSTerrainRelativePosition(positionInputs.positionWS);
                OUT.normalWS = normalInputs.normalWS;
                OUT.uv = IN.uv;
                OUT.exposure = 0;
                #if defined(_TERRAIN_EXCAVATION)
                OUT.exposure = IN.surfaceData.x;
                #endif
                OUT.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                OUT.shadowCoord = GetShadowCoord(positionInputs);
                half3 lightDirection = GetMainLight().direction;
                OUT.horizonLight = float2(
                    frac(atan2(lightDirection.z, lightDirection.x) * (1.0 / (2.0 * PI))) * 8.0,
                    atan2(lightDirection.y, max(length(lightDirection.xz), 0.0001)));

                return OUT;
            }

            void SampleExcavation(float3 p,float3 n,float3 positionDx,float3 positionDy,out half3 albedo,out float3 normalWS)
            {
                float3 geologyTint = FSRegionalRockTint(p);
                float3 weights=max(pow(abs(n),4.0)-0.01,0.0);
                weights/=max(dot(weights,1.0),1e-5);
                float3 signs=step(0.0,n)*2.0-1.0;
                float3 dx=positionDx*_ExcavationTiling,dy=positionDy*_ExcavationTiling;
                p*=_ExcavationTiling;albedo=0;float3 perturbation=0;
                [branch] if(weights.x>0)
                {
                    float2 s=float2(-signs.x,1),uv=p.zy*s;
                    albedo+=SAMPLE_TEXTURE2D_GRAD(_ExcavationAlbedo,sampler_ExcavationAlbedo,uv,dx.zy*s,dy.zy*s).rgb*weights.x;
                    if(_ExcavationNormalStrength>0){float3 t=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_ExcavationNormal,sampler_ExcavationAlbedo,uv,dx.zy*s,dy.zy*s));perturbation+=float3(0,t.y,-t.x*signs.x)*weights.x;}
                }
                [branch] if(weights.y>0)
                {
                    float2 s=float2(signs.y,1),uv=p.zx*s;
                    albedo+=SAMPLE_TEXTURE2D_GRAD(_ExcavationAlbedo,sampler_ExcavationAlbedo,uv,dx.zx*s,dy.zx*s).rgb*weights.y;
                    if(_ExcavationNormalStrength>0){float3 t=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_ExcavationNormal,sampler_ExcavationAlbedo,uv,dx.zx*s,dy.zx*s));perturbation+=float3(t.y,0,t.x*signs.y)*weights.y;}
                }
                [branch] if(weights.z>0)
                {
                    float2 s=float2(signs.z,1),uv=p.xy*s;
                    albedo+=SAMPLE_TEXTURE2D_GRAD(_ExcavationAlbedo,sampler_ExcavationAlbedo,uv,dx.xy*s,dy.xy*s).rgb*weights.z;
                    if(_ExcavationNormalStrength>0){float3 t=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_ExcavationNormal,sampler_ExcavationAlbedo,uv,dx.xy*s,dy.xy*s));perturbation+=float3(t.x*signs.z,t.y,0)*weights.z;}
                }
                perturbation-=n*dot(perturbation,n);
                normalWS=normalize(n+perturbation*_ExcavationNormalStrength);albedo*=_ExcavationTint.rgb * geologyTint;
            }

            half4 ShadeTerrainSurface(half3 albedo,float3 normalWS,Varyings IN,FSTerrainSurfaceFrame surface,
                half terrainVisibility, half woodland)
            {
                float4 shadowCoord=IN.shadowCoord;
                #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                // Cascade selection is discontinuous. Interpolating coordinates selected
                // at vertices produces dark triangular streaks across cascade boundaries.
                shadowCoord=TransformWorldToShadowCoord(surface.positionWS);
                #endif
                Light mainLight=GetMainLight(shadowCoord);
                #if !defined(_TERRAIN_EXCAVATION)
                    mainLight.shadowAttenuation = FSDistantForestShadow(surface.positionWS,
                        mainLight.direction.y, mainLight.shadowAttenuation);
                #endif
                // The field supplies location/ambient, not the direction of the sun.
                // Use full geometric roof occlusion here even when day/night art
                // intentionally weakens outdoor directional shadows.
                half2 caveLighting=TerrainCaveLighting(surface.positionWS,normalize(IN.normalWS));
                mainLight.shadowAttenuation=TerrainCaveDirectionalShadow(mainLight.shadowAttenuation,GetMainLightShadowParams().x,caveLighting.y);
                half shadowAttenuation=lerp(1.0h,mainLight.shadowAttenuation*terrainVisibility,saturate(_ReceiveShadows));
                half3 diffuse=LightingLambert(mainLight.color,mainLight.direction,normalWS)*mainLight.distanceAttenuation*shadowAttenuation;
                // Visibility offsets follow the geometric surface, independently of
                // triplanar/detail normal perturbations used for diffuse shading.
                half caveAmbient=caveLighting.x;
                half3 ambient=max(SampleSH(normalWS),half3(_AmbientStrength,_AmbientStrength,_AmbientStrength))*caveAmbient;
                #if !defined(_TERRAIN_EXCAVATION)
                    half groundShade = FSForestGroundLight(surface.positionWS);
                    ambient *= FSSceneGroundAmbient(groundShade);
                    diffuse *= FSSceneGroundSun(groundShade, shadowAttenuation, woodland);
                #endif
                half3 color=albedo*(diffuse+ambient);
                half tintWeight=saturate(1.0h-terrainVisibility)*saturate(_TerrainHorizonParams.z)*saturate(_ReceiveShadows);
                color*=lerp(half3(1,1,1),_TerrainHorizonTint.rgb,tintWeight);
                // Stable derivatives and the mesher's 2.5 cm normal-displacement
                // tolerance keep PCF samples on curved stone from shadowing themselves.
                float3 flamePlaneNormal=normalize(cross(surface.dpdxWS,surface.dpdyWS));
                color+=albedo*FSLocalFlameDiffuse(surface.positionWS,normalWS,flamePlaneNormal,.025*saturate(IN.exposure));
                return half4(MixFogColor(color,unity_FogColor.rgb*caveAmbient,IN.fogFactor),1);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                FSTerrainSurfaceFrame surface=FSCreateTerrainSurfaceFrame(IN.positionRWS);
                half3 baseNormalWS = normalize(IN.normalWS);
                float3 surfaceDx=surface.dpdxWS,surfaceDy=surface.dpdyWS;
                #if !defined(_TERRAIN_EXCAVATION)
                baseNormalWS=FSHeightfieldCliffNormal(baseNormalWS,surfaceDx,surfaceDy);
                #endif
                // Terrain edge diagnostic insertion point.
                half3 normalWS = baseNormalWS;
                half3 baseColor = 0;
                half terrainVisibility = 1;
                half woodland = 0;
                #if defined(_TERRAIN_EXCAVATION)
                float exposure=saturate(IN.exposure);
                float3 excavationDx=surfaceDx,excavationDy=surfaceDy;
                // Underground-only tiles have no exterior control maps. Their complete
                // stone surface bypasses outdoor layer evaluation, including normalization
                // of an empty weighted normal that would poison subsequent light response.
                [branch] if(exposure>=.999)
                {
                    SampleExcavation(surface.positionWS,baseNormalWS,excavationDx,excavationDy,baseColor,normalWS);
                }
                else
                #endif
                {
                // Control pixels represent mesh samples, including the endpoints of each chunk.
                float2 controlUV = IN.uv * (1.0 - _ControlMap0_TexelSize.xy) + 0.5 * _ControlMap0_TexelSize.xy;
                float4 control0 = SAMPLE_TEXTURE2D(_ControlMap0, sampler_ControlMap0, controlUV);
                float4 control1 = SAMPLE_TEXTURE2D(_ControlMap1, sampler_ControlMap0, controlUV);
                float4 control2 = SAMPLE_TEXTURE2D(_ControlMap2, sampler_ControlMap0, controlUV);
                // Cover maps are zero outside the grass substrate. Divide by its
                // unsharpened support so the same transition is not applied twice,
                // exposing a green strip between forest litter and rock/cliff.
                float coverSupport = max(control0.b, 0.001);
                float forestCoverVariant = saturate(control1.a / coverSupport);
                control0 = pow(saturate(control0), max(_SurfaceBlendSharpness, 0.25));
                control1 = pow(saturate(control1), max(_SurfaceBlendSharpness, 0.25));
                float totalWeight = dot(control0, float4(1, 1, 1, 1)) + dot(control1.rgb, float3(1, 1, 1));
                control0 /= max(totalWeight, 0.00001);
                control1 /= max(totalWeight, 0.00001);

                // Resolve coverage after control-map filtering, before either albedo or normals.
                // Only redistribute the snow/rock/cliff share: grass, litter support, sand,
                // mud, riverbed and snow-on-grass dusting retain their existing weights.
                float bareRock = control0.a + control1.g;
                float snowRockTotal = bareRock + control1.r;
                float snowShare = saturate(control1.r / max(snowRockTotal, 1e-6));
                float threshold = clamp(_SnowCoverageThreshold, 0.05, 0.95);
                // A derivative-sized minimum margin antialiases distant/subpixel edges.
                // Clamp to the endpoints so pure rock and pure snow stay pure.
                float halfWidth = min(max(max(_SnowBoundaryWidth, 0.01) * 0.5,
                    fwidth(snowShare) * 0.5), min(threshold, 1.0 - threshold));
                float resolvedShare = smoothstep(threshold - halfWidth, threshold + halfWidth, snowShare);
                float resolvedSnow = snowRockTotal * lerp(snowShare, resolvedShare, saturate(_SnowRockSeparation));
                float rockScale = (snowRockTotal - resolvedSnow) / max(bareRock, 1e-6);
                control0.a *= rockScale;
                control1.g *= rockScale;
                control1.r = resolvedSnow;

                // Coarse distant control pixels can straddle a snowy cap and a
                // tall headwall. Reject the interpolated snow on the actual face
                // instead of stretching white teeth down it. Use the geometric
                // normal (before texture detail); retain the same total coverage.
                if (_WorldGlacialParams.x > 0.0 && surface.positionWS.y > _WorldGlacialParams.y + 1.0)
                {
                    float shedSnow = control1.r * (1.0 - smoothstep(0.3420, 0.60, abs(baseNormalWS.y)));
                    control1.r -= shedSnow;
                    control0.a += shedSnow;
                }

                // A coarse control pixel across the ice toe otherwise draws a
                // regular row of rocky lobes up the face. Resolve only its existing
                // ice/rock support on steep geometry; flat ground keeps its weights.
                float glacialRegion=0.0;
                if (control1.b > 0.001 && _WorldGlacialParams.x > 0.0 && surface.positionWS.y > _WorldGlacialParams.y + 0.5)
                {
                    glacialRegion=smoothstep(.15,.28,FSColdProvince(surface.positionWS.xz));
                    float support=smoothstep(.005,.12,control1.b)*glacialRegion;
                    float steep=1.0-smoothstep(.35,.70,abs(baseNormalWS.y));
                    float transfer=support*steep;
                    float rock=control0.a+control1.g;
                    control1.b+=rock*transfer;
                    control0.a*=1.0-transfer;control1.g*=1.0-transfer;
                }

                half sandWeight = control0.r;
                half mudWeight = control0.g;
                half grassWeight = control0.b;
                half rockWeight = control0.a;

                half snowWeight = control1.r;
                half cliffWeight = control1.g;
                half riverbedWeight = control1.b;

                half darkGrassCoverWeight = saturate(control2.r / coverSupport);
                woodland = smoothstep(.45h, .9h, darkGrassCoverWeight * grassWeight);
                half leafLitterWeight = saturate(control2.g / coverSupport);
                half bareDirtWeight = saturate(control2.b / coverSupport);
                half mossWeight = control2.a * _ForestFloorMossStrength;
                half forestVariantWeight = forestCoverVariant;

                float distanceToCamera = surface.distanceToCamera;
                float3 sedimentDx=surfaceDx,sedimentDy=surfaceDy;
                #if defined(_ROCK_DETAIL)
                float3 rockPositionDx = surface.dpdxWS;
                float3 rockPositionDy = surface.dpdyWS;
                #endif
                #if defined(_GRASS_BLADE_GROUND)
                // Explicit derivatives remain valid when distant detail takes
                // the branch that skips texture fetches altogether.
                float2 grassGroundDx = surface.dpdxWS.xz;
                float2 grassGroundDy = surface.dpdyWS.xz;
                #else
                float grassDistanceBlend = GetNoisyDistanceBlend(
                    distanceToCamera,
                    _GrassTilingNearDistance,
                    _GrassTilingFarDistance,
                    surface.positionWS.xz
                );
                #endif

                float snowDistanceBlend = GetNoisyDistanceBlend(
                    distanceToCamera,
                    _SnowTilingNearDistance,
                    _SnowTilingFarDistance,
                    surface.positionWS.xz
                );

                float3 weightedNormal = baseNormalWS * (sandWeight + mudWeight + rockWeight + cliffWeight + riverbedWeight);

                half3 regionalSandTint = 1.0h;
                [branch] if (sandWeight > 0.001h) regionalSandTint = FSRegionalSandTint(surface.positionWS);
                baseColor += _SandColor.rgb * regionalSandTint * sandWeight;
                baseColor += _MudColor.rgb * mudWeight;
                half3 regionalRockTint = 1.0h;
                [branch] if (rockWeight + cliffWeight > 0.001h) regionalRockTint = FSRegionalRockTint(surface.positionWS);
                baseColor += _RockColor.rgb * regionalRockTint * rockWeight;
                baseColor += _CliffColor.rgb * regionalRockTint * cliffWeight;
                baseColor += _RiverbedColor.rgb * riverbedWeight;

                [branch] if(mudWeight+riverbedWeight>0.001 && _DrySurfaceStrength>0.001 && _WorldLandformParams.x>0.5)
                {
                    float dry=smoothstep(0.08,0.18,FSRegionalGeology(surface.positionWS.xz).x)*_DrySurfaceStrength;
                    [branch] if(dry>0.001 && mudWeight>0.001)
                    {
                        half3 deposit;float3 depositNormal;
                        SampleDesertSediment(surface.positionWS,baseNormalWS,sedimentDx,sedimentDy,distanceToCamera,false,deposit,depositNormal);
                        baseColor+=(deposit-_MudColor.rgb)*mudWeight*dry;
                        weightedNormal+=(depositNormal-baseNormalWS)*mudWeight*dry;
                    }
                    [branch] if(dry>0.001 && riverbedWeight>0.001)
                    {
                        half3 clay;float3 clayNormal;
                        SampleDesertSediment(surface.positionWS,baseNormalWS,sedimentDx,sedimentDy,distanceToCamera,true,clay,clayNormal);
                        baseColor+=(clay-_RiverbedColor.rgb)*riverbedWeight*dry;
                        weightedNormal+=(clayNormal-baseNormalWS)*riverbedWeight*dry;
                    }
                }

                // Ice uses the riverbed channel only on above-water cold glacier ground.
                // Desert clay and submerged riverbeds keep their existing meanings.
                [branch] if(riverbedWeight>0.001h && _WorldGlacialParams.x>0.0 && surface.positionWS.y>_WorldGlacialParams.y+0.5)
                {
                    half iceWeight=riverbedWeight*glacialRegion;
                    [branch] if(iceWeight>0.001h)
                    {
                        float iceFade=1.0-GetDistanceBlend(distanceToCamera,_IceFadeStart,_IceFadeEnd);
                        half3 iceColor=_IceAverageColor.rgb;float3 iceNormal=baseNormalWS;
                        [branch] if(iceFade>0.001)
                        {
                            half3 sampledIce;float3 sampledNormal;
                            float normalFade=1.0-GetDistanceBlend(distanceToCamera,35.0,110.0);
                            SampleGlacierDetail(surface.positionWS,baseNormalWS,sedimentDx,sedimentDy,
                                _IceNormalStrength*normalFade,sampledIce,sampledNormal);
                            iceColor=lerp(iceColor,sampledIce,iceFade);
                            iceNormal=normalize(lerp(baseNormalWS,sampledNormal,iceFade));
                        }
                        baseColor+=(iceColor*_IceColor.rgb-_RiverbedColor.rgb)*iceWeight;
                        weightedNormal+=(iceNormal-baseNormalWS)*iceWeight;
                    }
                }
                if (sandWeight > 0.001h)
                {
                    half3 sandAlbedo;
                    float3 sandNormalWS;
                    SampleSandDetail(surface.positionWS, surfaceDx.xz, surfaceDy.xz, baseNormalWS, sandAlbedo, sandNormalWS);
                    baseColor += _SandColor.rgb * regionalSandTint * sandWeight * (sandAlbedo - 1.0h) * _SandDetailStrength;
                    weightedNormal += (sandNormalWS - baseNormalWS) * sandWeight;
                }

                #if defined(_ROCK_DETAIL)
                float rockSurfaceWeight = rockWeight + cliffWeight;
                float rockFade = 1.0 - GetDistanceBlend(distanceToCamera, _RockDetailFadeStart, _RockDetailFadeEnd);
                half3 rockTint = (_RockColor.rgb * rockWeight + _CliffColor.rgb * cliffWeight) * regionalRockTint;
                // Keep the existing distant color, independently of nearby texture
                // strength. The sampled texture supplies its own untinted color.
                half3 distantRockColor = rockTint * lerp(half3(1, 1, 1), _RockAverageAlbedo.rgb, _RockDetailStrength);
                baseColor += rockTint * (_RockAverageAlbedo.rgb - 1.0h) * _RockDetailStrength;
                [branch] if (rockSurfaceWeight > 0.001 && rockFade > 0.001)
                {
                    float normalFade = 1.0 - GetDistanceBlend(distanceToCamera, _RockNormalFadeStart, _RockNormalFadeEnd);
                    float scaleBlend = smoothstep(0.0, 1.0, GetDistanceBlend(distanceToCamera, _RockScaleFadeStart, _RockScaleFadeEnd));
                    float normalStrength = (_RockNormalStrength * rockWeight + _CliffNormalStrength * cliffWeight)
                                         / max(rockSurfaceWeight, 0.001);
                    half3 rockAlbedo = 0;
                    // Only the transition samples both scales. Normal mapping
                    // keeps its independent near fade, even with custom settings.
                    [branch] if (scaleBlend < 1.0 || normalStrength * normalFade > 0.001)
                    {
                        half3 nearAlbedo;
                        float3 rockNormalWS;
                        SampleRockDetail(surface.positionWS, baseNormalWS, rockPositionDx, rockPositionDy,
                            normalStrength * rockFade * normalFade, nearAlbedo, rockNormalWS);
                        rockAlbedo = nearAlbedo * (1.0 - scaleBlend);
                        weightedNormal += (rockNormalWS - baseNormalWS) * rockSurfaceWeight;
                    }
                    [branch] if (scaleBlend > 0.0 && _RockTextureStrength > 0.001)
                        rockAlbedo += SampleFarRockColor(surface.positionWS, baseNormalWS, rockPositionDx, rockPositionDy) * scaleBlend;
                    baseColor += (rockAlbedo * _RockAlbedoTint.rgb * regionalRockTint * rockSurfaceWeight - distantRockColor) * _RockTextureStrength * rockFade;
                }
                #endif

                if (grassWeight > 0.001h)
                {
                    #if defined(_GRASS_BLADE_GROUND)
                    half grassVariation = 1.0h;
                    float remainingGrass = (1.0 - leafLitterWeight) * (1.0 - bareDirtWeight) * (1.0 - mossWeight);
                    half nearTurfWeight = saturate(_UseNearMeadowTurf * (1.0h - darkGrassCoverWeight));
                    [branch] if (nearTurfWeight < .999h)
                        SampleGrassGround(surface.positionWS, baseNormalWS, distanceToCamera, remainingGrass,
                            grassGroundDx, grassGroundDy, grassVariation, normalWS);
                    #else
                    float2 grassUVNear = surface.positionWS.xz * _GrassTilingNear;
                    float2 grassUVFar = surface.positionWS.xz * _GrassTilingFar;

                    half3 grassTexNear = SAMPLE_TEXTURE2D(_GrassAlbedo, sampler_GrassAlbedo, grassUVNear).rgb;
                    half3 grassTexFar = SAMPLE_TEXTURE2D(_GrassAlbedo, sampler_GrassAlbedo, grassUVFar).rgb;
                    half3 grassTex = lerp(grassTexNear, grassTexFar, grassDistanceBlend);
                    half grassLuma = dot(grassTex, half3(0.299h, 0.587h, 0.114h));
                    half grassCentered = (grassLuma - 0.5h) * 2.0h;
                    half grassDetail = grassCentered * _GrassDetailContrast;
                    half grassVariation = saturate(1.0h + grassDetail * _GrassDetailStrength);
                    #endif

                    half3 grassTint = EvaluateGrassTint(
                        surface.positionWS.xz,
                        _DarkGrassColor.rgb,
                        _MidGrassColor.rgb,
                        _LightGrassColor.rgb);

                    half3 darkGroundGrassTint = EvaluateGrassTint(
                        surface.positionWS.xz,
                        _GroundDarkGrassColor.rgb,
                        _GroundMidGrassColor.rgb,
                        _GroundLightGrassColor.rgb);

                    half3 grassColor = grassTint * grassVariation;
                    grassColor = lerp(grassColor, darkGroundGrassTint * grassVariation, darkGrassCoverWeight);
                    #if defined(_GRASS_BLADE_GROUND)
                    // Keep the clutter-matching average tone at every distance,
                    // including after the blade surface map has faded away.
                    grassColor *= _GrassGroundTint.rgb;
                    grassColor *= lerp(_MeadowTurfTint.rgb, half3(1, 1, 1), darkGrassCoverWeight);
                    [branch] if (nearTurfWeight * remainingGrass > .001h)
                    {
                        half3 turfColor;
                        float3 turfNormal;
                        SampleNearMeadowTurf(surface.positionWS, baseNormalWS, distanceToCamera,
                            grassGroundDx, grassGroundDy, turfColor, turfNormal);
                        grassColor = lerp(grassColor, turfColor, nearTurfWeight);
                        normalWS = normalize(lerp(normalWS, turfNormal, nearTurfWeight));
                    }
                    #endif

                    // One height sample offsets detail UVs for shallow forest-floor relief.
                    // A zero strength skips the sample, so unassigned maps cost nothing.
                    float3 viewDirectionWS = surface.viewDirectionWS;
                    float2 parallaxDirection = viewDirectionWS.xz / max(abs(viewDirectionWS.y), 0.35);
                    float litterNormalFade = 1.0 - smoothstep(_LeafLitterNormalFadeStart,
                        max(_LeafLitterNormalFadeEnd, _LeafLitterNormalFadeStart + 0.01), distanceToCamera);
                    float2 litterWorldUV = surface.positionWS.xz * _LeafLitterTiling;
                    [branch] if (leafLitterWeight > 0.001h && _ForestFloorScatter.x > 0.0)
                    {
                        // Bend the tile in continuous world space. Height, albedo,
                        // normals and AO stay on the same surface across chunk seams.
                        float2 p = surface.positionWS.xz * _ForestFloorScatter.z;
                        float2 flow = sin(float2(p.y + .71 * sin(p.x * .46),
                                                 p.x - .63 * sin(p.y * .57)));
                        flow = .72 * flow + .28 * sin(float2(p.x * .67 + p.y * .51,
                                                            p.x * .42 - p.y * .73));
                        litterWorldUV += flow * _ForestFloorScatter.x;
                        float pockets = .50 + .27 * sin(p.x * .40 + sin(p.y * .55)) +
                                             .23 * sin(p.y * .47 - p.x * .25);
                        forestVariantWeight = lerp(forestVariantWeight, smoothstep(.27,.80,pockets),
                                                   saturate(_ForestFloorScatter.y));
                    }
                    float2 leafLitterBaseUV = litterWorldUV;
                    if (leafLitterWeight > 0.001h && _LeafLitterHeightStrength * litterNormalFade > 0.001)
                    {
                        float2 heightUV = leafLitterBaseUV * _LeafLitterHeight_ST.xy + _LeafLitterHeight_ST.zw;
                        half height = SAMPLE_TEXTURE2D(_LeafLitterHeight, sampler_LeafLitterAlbedo, heightUV).r;
                        leafLitterBaseUV -= parallaxDirection * ((height - 0.5h) * _LeafLitterHeightStrength * litterNormalFade);
                    }
                    float2 leafLitterUV = leafLitterBaseUV * _LeafLitterAlbedo_ST.xy + _LeafLitterAlbedo_ST.zw;
                    half3 leafLitterTex = SAMPLE_TEXTURE2D(_LeafLitterAlbedo, sampler_LeafLitterAlbedo, leafLitterUV).rgb;
                    half3 leafLitterColor = leafLitterTex * _LeafLitterColor.rgb;
                    if (leafLitterWeight > 0.001h && _LeafLitterAOStrength > 0.0)
                    {
                        float2 aoUV = leafLitterBaseUV * _LeafLitterAO_ST.xy + _LeafLitterAO_ST.zw;
                        half ao = SAMPLE_TEXTURE2D(_LeafLitterAO, sampler_LeafLitterAlbedo, aoUV).r;
                        leafLitterColor *= lerp(1.0h, ao, _LeafLitterAOStrength);
                    }
                    float2 mixedForestUV = litterWorldUV;
                    bool rotateDampLitter = _ForestFloorScatter.w > .5;
                    if (leafLitterWeight > 0.001h && forestVariantWeight > 0.001h)
                    {
                        float2 mixedParallax = parallaxDirection;
                        if (rotateDampLitter)
                        {
                            mixedForestUV = float2(-mixedForestUV.y, mixedForestUV.x) + float2(.31,.73);
                            mixedParallax = float2(-parallaxDirection.y, parallaxDirection.x);
                        }
                        if (_MixedForestFloorHeightStrength * litterNormalFade > 0.001)
                        {
                            float2 heightUV = mixedForestUV * _MixedForestFloorHeight_ST.xy + _MixedForestFloorHeight_ST.zw;
                            half height = SAMPLE_TEXTURE2D(_MixedForestFloorHeight, sampler_LeafLitterAlbedo, heightUV).r;
                            mixedForestUV -= mixedParallax * ((height - 0.5h) * _MixedForestFloorHeightStrength * litterNormalFade);
                        }
                        float2 albedoUV = mixedForestUV * _MixedForestFloorAlbedo_ST.xy + _MixedForestFloorAlbedo_ST.zw;
                        half3 mixedForestColor = SAMPLE_TEXTURE2D(_MixedForestFloorAlbedo, sampler_LeafLitterAlbedo, albedoUV).rgb * _MixedForestFloorColor.rgb;
                        leafLitterColor = lerp(leafLitterColor, mixedForestColor, forestVariantWeight);
                    }

                    float2 bareDirtUV = surface.positionWS.xz * _BareDirtTiling;
                    if (bareDirtWeight > 0.001h && _BareDirtHeightStrength > 0.0)
                    {
                        half height = SAMPLE_TEXTURE2D(_BareDirtHeight, sampler_BareDirtAlbedo, bareDirtUV).r;
                        bareDirtUV -= parallaxDirection * ((height - 0.5h) * _BareDirtHeightStrength);
                    }
                    half3 bareDirtTex = SAMPLE_TEXTURE2D(_BareDirtAlbedo, sampler_BareDirtAlbedo, bareDirtUV).rgb;
                    half3 bareDirtColor = bareDirtTex * _BareDirtColor.rgb;
                    if (bareDirtWeight > 0.001h && _BareDirtAOStrength > 0.0)
                    {
                        half ao = SAMPLE_TEXTURE2D(_BareDirtAO, sampler_BareDirtAlbedo, bareDirtUV).r;
                        bareDirtColor *= lerp(1.0h, ao, _BareDirtAOStrength);
                    }

                    if (leafLitterWeight > 0.001h && _ForestFloorMacroStrength > 0.0)
                        leafLitterColor *= ForestFloorMacroTone(surface.positionWS.xz);
                    grassColor = lerp(grassColor, leafLitterColor, leafLitterWeight);
                    grassColor = lerp(grassColor, bareDirtColor, bareDirtWeight);
                    baseColor += grassColor * grassWeight;

                    #if !defined(_GRASS_BLADE_GROUND)
                    float3 grassTangentNormalNear = UnpackNormal(
                        SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassAlbedo, grassUVNear)
                    );
                    float3 grassTangentNormalFar = UnpackNormal(
                        SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassAlbedo, grassUVFar)
                    );
                    float3 grassTangentNormal = normalize(lerp(grassTangentNormalNear, grassTangentNormalFar, grassDistanceBlend));

                    normalWS = ApplyDetailNormal(baseNormalWS, grassTangentNormal, _GrassNormalStrength);
                    #endif

                    if (leafLitterWeight > 0.001h && litterNormalFade > 0.001)
                    {
                        float2 leafLitterNormalUV = leafLitterBaseUV * _LeafLitterNormal_ST.xy + _LeafLitterNormal_ST.zw;
                        float3 leafLitterTangentNormal = UnpackNormal(
                            SAMPLE_TEXTURE2D(_LeafLitterNormal, sampler_LeafLitterAlbedo, leafLitterNormalUV)
                        );

                        float3 leafLitterNormalWS = ApplyDetailNormal(
                            baseNormalWS,
                            leafLitterTangentNormal,
                            _LeafLitterNormalStrength * litterNormalFade);

                        normalWS = normalize(lerp(normalWS, leafLitterNormalWS, leafLitterWeight));
                        if (forestVariantWeight > 0.001h)
                        {
                            float2 mixedNormalUV = mixedForestUV * _MixedForestFloorNormal_ST.xy + _MixedForestFloorNormal_ST.zw;
                            float3 mixedNormal = UnpackNormal(SAMPLE_TEXTURE2D(_MixedForestFloorNormal, sampler_LeafLitterAlbedo, mixedNormalUV));
                            if (rotateDampLitter)
                                mixedNormal.xy = float2(mixedNormal.y, -mixedNormal.x);
                            float3 mixedNormalWS = ApplyDetailNormal(baseNormalWS, mixedNormal, _LeafLitterNormalStrength * litterNormalFade);
                            normalWS = normalize(lerp(normalWS, mixedNormalWS, forestVariantWeight * leafLitterWeight));
                        }
                    }

                    if (bareDirtWeight > 0.001h)
                    {
                        float3 bareDirtTangentNormal = UnpackNormal(
                            SAMPLE_TEXTURE2D(_BareDirtNormal, sampler_BareDirtAlbedo, bareDirtUV)
                        );

                        float3 bareDirtNormalWS = ApplyDetailNormal(
                            baseNormalWS,
                            bareDirtTangentNormal,
                            _BareDirtNormalStrength);

                        normalWS = normalize(lerp(normalWS, bareDirtNormalWS, bareDirtWeight));
                    }

                    weightedNormal += normalWS * grassWeight;
                }
                else
                {
                    weightedNormal += baseNormalWS * grassWeight;
                }

                if (snowWeight > 0.001h)
                {
                    float2 snowUVNear = RotateUV90(surface.positionWS.xz * _SnowTilingNear);
                    float2 snowUVFar = RotateUV90(surface.positionWS.xz * _SnowTilingFar);

                    half3 snowTexUVNear = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, snowUVNear).rgb;
                    half3 snowTexUVFar = SAMPLE_TEXTURE2D(_SnowAlbedo, sampler_SnowAlbedo, snowUVFar).rgb;
                    half3 snowTexUV = lerp(snowTexUVNear, snowTexUVFar, snowDistanceBlend);

                    half3 snowTexTriNear = SampleSnowTriplanarAlbedo(surface.positionWS, baseNormalWS, _SnowTilingNear);
                    half3 snowTexTriFar = SampleSnowTriplanarAlbedo(surface.positionWS, baseNormalWS, _SnowTilingFar);
                    half3 snowTexTriplanar = lerp(snowTexTriNear, snowTexTriFar, snowDistanceBlend);

                    float slope = 1.0 - abs(baseNormalWS.y);
                    float snowTriplanarBlend = saturate(
                        (slope - _SnowTriplanarStart) / max(_SnowTriplanarEnd - _SnowTriplanarStart, 1e-5)
                    );

                    half3 snowTex = lerp(snowTexUV, snowTexTriplanar, snowTriplanarBlend);
                    half3 snowColor = snowTex * _SnowTint.rgb * _SnowColor.rgb;
                    baseColor += snowColor * snowWeight;

                    float3 snowTangentNormalUVNear = UnpackNormal(
                        SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowAlbedo, snowUVNear)
                    );
                    float3 snowTangentNormalUVFar = UnpackNormal(
                        SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowAlbedo, snowUVFar)
                    );
                    float3 snowTangentNormalUV = lerp(snowTangentNormalUVNear, snowTangentNormalUVFar, snowDistanceBlend);
                    float3 snowPerturbationUV = float3(snowTangentNormalUV.y, 0, -snowTangentNormalUV.x);

                    float3 snowPerturbationTriNear = SampleSnowTriplanarPerturbation(surface.positionWS, baseNormalWS, _SnowTilingNear);
                    float3 snowPerturbationTriFar = SampleSnowTriplanarPerturbation(surface.positionWS, baseNormalWS, _SnowTilingFar);
                    float3 snowPerturbationTri = lerp(snowPerturbationTriNear, snowPerturbationTriFar, snowDistanceBlend);

                    float3 snowPerturbation = lerp(snowPerturbationUV, snowPerturbationTri, snowTriplanarBlend);
                    snowPerturbation -= baseNormalWS * dot(snowPerturbation, baseNormalWS);
                    // An unassigned/flat normal map leaves the geometric normal unchanged.
                    float3 snowNormalWS = normalize(baseNormalWS + snowPerturbation * _SnowNormalStrength);
                    weightedNormal += snowNormalWS * snowWeight;
                }
                else
                {
                    weightedNormal += baseNormalWS * snowWeight;
                }
                normalWS = dot(weightedNormal,weightedNormal)>1e-8?normalize(weightedNormal):baseNormalWS;

                // Independent cover on the finished substrate. Leave grass/rock/litter
                // control interpolation unchanged; only actual moss patches take priority.
                // This curve matches ForestFloorPolicy.MossDominance for foliage suppression.
                float mossDominance = smoothstep(0.03, 0.58, mossWeight);
                float mossBlend = mossDominance * saturate(grassWeight + rockWeight + cliffWeight) *
                    saturate((abs(baseNormalWS.y) - 0.25) / 0.35);
                if (mossBlend > 0.001)
                {
                    float reliefFade = 1.0 - smoothstep(12.0, 45.0, distanceToCamera);
                    float3 viewDirectionWS = surface.viewDirectionWS;
                    float2 parallaxDirection = viewDirectionWS.xz / max(abs(viewDirectionWS.y), 0.35);
                    float2 mossUV = surface.positionWS.xz * _MossTiling;
                    if (_MossHeightStrength > 0.0 && reliefFade > 0.001)
                    {
                        half height = SAMPLE_TEXTURE2D(_MossHeight, sampler_MossAlbedo, mossUV).r;
                        mossUV -= parallaxDirection * ((height - 0.5h) * _MossHeightStrength * reliefFade);
                    }
                    half3 mossColor = SAMPLE_TEXTURE2D(_MossAlbedo, sampler_MossAlbedo, mossUV).rgb * _MossColor.rgb;
                    if (_MossAOStrength > 0.0)
                        mossColor *= lerp(1.0h, SAMPLE_TEXTURE2D(_MossAO, sampler_MossAlbedo, mossUV).r, _MossAOStrength);
                    float denseBlend = smoothstep(0.4, 0.85, mossDominance);
                    if (denseBlend > 0.001)
                    {
                        float2 denseUV = surface.positionWS.xz * _MossTiling;
                        if (_DenseMossHeightStrength > 0.0 && reliefFade > 0.001)
                        {
                            half height = SAMPLE_TEXTURE2D(_DenseMossHeight, sampler_MossAlbedo,
                                denseUV * _DenseMossHeight_ST.xy + _DenseMossHeight_ST.zw).r;
                            denseUV -= parallaxDirection * ((height - 0.5h) * _DenseMossHeightStrength * reliefFade);
                        }
                        half3 denseColor = SAMPLE_TEXTURE2D(_DenseMossAlbedo, sampler_MossAlbedo,
                            denseUV * _DenseMossAlbedo_ST.xy + _DenseMossAlbedo_ST.zw).rgb * _DenseMossColor.rgb;
                        if (_DenseMossAOStrength > 0.0)
                            denseColor *= lerp(1.0h, SAMPLE_TEXTURE2D(_DenseMossAO, sampler_MossAlbedo,
                                denseUV * _DenseMossAO_ST.xy + _DenseMossAO_ST.zw).r, _DenseMossAOStrength);
                        mossColor = lerp(mossColor,denseColor,denseBlend);
                    }
                    // Broad olive fill keeps the photographic source's fine twigs/grain quiet.
                    // A Color property is converted by Unity for the active color space.
                    // A literal RGB here was interpreted as linear in-game but gamma in
                    // the old authoring project, making the live floor pale and washed out.
                    mossColor = lerp(_MossFillColor.rgb,mossColor,saturate(_MossDetailContrast));
                    half mossLuma = dot(mossColor,half3(0.299h,0.587h,0.114h));
                    mossColor = lerp(mossLuma.xxx,mossColor,saturate(_MossSaturation));
                    mossColor *= lerp(1.0,ForestFloorMacroTone(surface.positionWS.xz),0.35);
                    baseColor = lerp(baseColor,mossColor,mossBlend);
                    float3 mossNormalWS = baseNormalWS;
                    if (reliefFade > 0.001 && _MossNormalStrength > 0.0 && denseBlend < 0.999)
                        mossNormalWS = ApplyDetailNormal(baseNormalWS,UnpackNormal(
                            SAMPLE_TEXTURE2D(_MossNormal,sampler_MossAlbedo,mossUV)),
                            _MossNormalStrength * reliefFade * (1.0-denseBlend));
                    normalWS = normalize(lerp(normalWS,mossNormalWS,mossBlend));
                }


                // The visible green substrate can come from grass, litter or moss. Match it
                // after compositing so green pockets under sedges share the same correction.
                half greenDominance = (baseColor.g - baseColor.r) / max(baseColor.g, 0.01h);
                half forestGreen = smoothstep(.28h, .72h, greenDominance) * darkGrassCoverWeight * grassWeight;
                baseColor *= lerp(half3(1, 1, 1), _ForestGrassGroundTint.rgb, forestGreen);
                terrainVisibility = TerrainHorizonVisibility(surface.positionWS, IN.horizonLight);
                #if defined(_TERRAIN_EXCAVATION)
                [branch] if(exposure>0)
                {
                    half3 stone;float3 stoneNormal;
                    SampleExcavation(surface.positionWS,baseNormalWS,excavationDx,excavationDy,stone,stoneNormal);
                    baseColor=lerp(baseColor,stone,exposure);normalWS=normalize(lerp(normalWS,stoneNormal,exposure));
                    terrainVisibility=lerp(terrainVisibility,1,exposure);
                }
                #endif
                }
                return ShadeTerrainSurface(baseColor,normalWS,IN,surface,terrainVisibility,woodland);
            }
            ENDHLSL
        }
        Pass
        {
            Name "GroundContactReceiver"
            Tags { "LightMode" = "GroundContactReceiver" }
            ZWrite Off
            ZTest Equal
            ColorMask R
            HLSLPROGRAM
            #pragma vertex ReceiverVert
            #pragma fragment ReceiverFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 ReceiverVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return GetVertexPositionInputs(positionOS.xyz).positionCS;
            }

            half4 ReceiverFrag() : SV_Target { return half4(1, 0, 0, 0); }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct ShadowAttributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            float4 ShadowVert(ShadowAttributes input):SV_POSITION
            {
                float3 positionWS=TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS=TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 direction=normalize(_LightPosition-positionWS);
                #else
                float3 direction=_LightDirection;
                #endif
                // Cavity ceilings face inward; bias their two-sided caster toward the light.
                normalWS=dot(normalWS,direction)<0?-normalWS:normalWS;
                float4 positionCS=TransformWorldToHClip(ApplyShadowBias(positionWS,normalWS,direction));
                #if UNITY_REVERSED_Z
                positionCS.z=min(positionCS.z,UNITY_NEAR_CLIP_VALUE*positionCS.w);
                #else
                positionCS.z=max(positionCS.z,UNITY_NEAR_CLIP_VALUE*positionCS.w);
                #endif
                return positionCS;
            }
            half4 ShadowFrag():SV_Target { return 0; }
            ENDHLSL
        }
    }
    CustomEditor "TerrainMaterialInspector"
}
