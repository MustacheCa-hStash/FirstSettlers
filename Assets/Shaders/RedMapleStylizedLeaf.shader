Shader "Custom/RedMapleStylizedLeaf"
{
    Properties
    {
        [MainTexture] _BaseMap("Leaf Clump / Alpha", 2D) = "white" {}
        [MainColor] _SummerLeafColor("Summer Leaf Color", Color) = (0.29, 0.58, 0.22, 1)
        _AutumnYellowColor("Autumn Yellow", Color) = (0.88, 0.61, 0.12, 1)
        _AutumnOrangeColor("Autumn Orange", Color) = (0.9, 0.3, 0.07, 1)
        _AutumnRedColor("Autumn Red", Color) = (0.76, 0.09, 0.06, 1)
        _SeasonAutumnAmount("Season Autumn Amount (0 = Summer)", Range(0, 1)) = 0
        _AutumnVariationStrength("Autumn Palette Variation", Range(0, 1)) = 0.65
        _TreeLeafTint("Per Tree Leaf Tint", Color) = (0.82, 0.14, 0.085, 1)
        _TreeTintStrength("Per Tree Autumn Tint Strength", Range(0, 1)) = 0.65
        _ColorVariationStrength("Soft Brightness Variation", Range(0, 0.5)) = 0.06
        _LeafDetailStrength("Texture Gradient Strength", Range(0, 1)) = 1
        _LeafGradientContrast("Leaf Gradient Contrast", Range(0, 3)) = 1.7
        _LeafGradientMidpoint("Leaf Gradient Midpoint (Linear)", Range(0.1, 1)) = 0.68
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        [Toggle] _AlphaCutoutShadows("Alpha Cutout Shadows", Float) = 1
        _AmbientStrength("Ambient Floor", Range(0, 1)) = 0.28
        _LightWrap("Leaf Light Softness", Range(0, 1)) = 0.5
        _TranslucencyStrength("Backlighting Strength", Range(0, 1)) = 0.12
        _Smoothness("Smoothness", Range(0, 1)) = 0.08
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.025
        [Toggle] _UseVertexColor("Multiply Vertex Color", Float) = 0
        _SnowCoverage("Snow Coverage", Range(0, 1)) = 0
        _SnowColor("Snow Color", Color) = (0.9, 0.94, 0.97, 1)
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        _WindStrength("Canopy Sway", Range(0, 1)) = 0.11
        _WindSpeed("Sway Speed", Range(0, 8)) = 1.15
        _WindFlutterStrength("Leaf Flutter", Range(0, 1)) = 0.035
        _WindFlutterSpeed("Flutter Speed", Range(0, 16)) = 4.8
        _WindGustScale("Gust Scale", Range(0.01, 2)) = 0.18
        _WindHeightMin("Wind Height Min", Float) = 0
        _WindHeightMax("Wind Height Max", Float) = 7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        ZWrite On
        ZTest LEqual
        HLSLINCLUDE
        #include "Assets/Shaders/RedMapleStylizedLeafCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLeaf"
            Tags { "LightMode"="UniversalForwardOnly" }
            AlphaToMask On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RedMapleLeafVertex
            #pragma fragment RedMapleLeafFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RedMapleLeafShadowVertex
            #pragma fragment RedMapleLeafShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            AlphaToMask On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RedMapleLeafVertex
            #pragma fragment RedMapleLeafDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RedMapleLeafVertex
            #pragma fragment RedMapleLeafNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
