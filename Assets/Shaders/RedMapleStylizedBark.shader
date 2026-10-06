Shader "Custom/RedMapleStylizedBark"
{
    Properties
    {
        [MainTexture] _BaseMap("Bark Base Color", 2D) = "white" {}
        [MainColor] _BaseColor("Cool Gray-Brown Bark Tint", Color) = (0.93, 0.97, 1, 1)
        _TreeBarkTint("Per Tree Bark Tint", Color) = (1, 1, 1, 1)
        _Brightness("Brightness", Range(0.25, 2)) = 1.05
        _BarkSaturation("Texture Color Saturation", Range(0, 1)) = 0.35
        _BarkContrast("Ridge / Furrow Contrast", Range(0, 2)) = 0.75
        _BarkMidpoint("Contrast Midpoint (Linear)", Range(0.02, 1)) = 0.18
        _ColorVariationStrength("Soft Brightness Variation", Range(0, 0.5)) = 0.02
        _VerticalGradientStrength("Height Gradient Strength", Range(0, 0.5)) = 0.025
        _ColorHeightMin("Gradient Height Min", Float) = 0
        _ColorHeightMax("Gradient Height Max", Float) = 7
        _AmbientStrength("Ambient Floor", Range(0, 1)) = 0.24
        _Smoothness("Smoothness", Range(0, 1)) = 0.1
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.025
        [Toggle] _UseVertexColor("Multiply Vertex Color", Float) = 0
        _SnowCoverage("Snow Coverage", Range(0, 1)) = 0
        _SnowColor("Snow Color", Color) = (0.9, 0.94, 0.97, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On
        ZTest LEqual
        HLSLINCLUDE
        #include "Assets/Shaders/RedMapleStylizedBarkCommon.hlsl"
        ENDHLSL
        Pass
        {
            Name "ForwardBark"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RedMapleBarkVertex
            #pragma fragment RedMapleBarkFragment
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
            #pragma vertex RedMapleBarkShadowVertex
            #pragma fragment RedMapleBarkDepthFragment
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
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RedMapleBarkVertex
            #pragma fragment RedMapleBarkDepthFragment
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
            #pragma vertex RedMapleBarkVertex
            #pragma fragment RedMapleBarkNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

