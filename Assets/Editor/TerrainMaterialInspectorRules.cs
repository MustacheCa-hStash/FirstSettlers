using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class TerrainMaterialInspectorRules
{
    private static readonly string[] UvTransforms = { "_LeafLitterAlbedo", "_LeafLitterNormal", "_LeafLitterAO", "_LeafLitterHeight", "_MixedForestFloorAlbedo", "_MixedForestFloorNormal", "_MixedForestFloorHeight", "_DenseMossAlbedo", "_DenseMossAO", "_DenseMossHeight" };
    private static readonly string[] TraditionalGrass = { "_GrassAlbedo", "_GrassNormal", "_GrassTilingNear", "_GrassTilingFar", "_GrassTilingNearDistance", "_GrassTilingFarDistance", "_GrassDetailStrength", "_GrassDetailContrast" };
    private static readonly (string control, string map)[] OptionalMaps = {
        ("_LeafLitterAOStrength", "_LeafLitterAO"), ("_LeafLitterHeightStrength", "_LeafLitterHeight"),
        ("_BareDirtAOStrength", "_BareDirtAO"), ("_BareDirtHeightStrength", "_BareDirtHeight"),
        ("_MossAOStrength", "_MossAO"), ("_MossHeightStrength", "_MossHeight"),
        ("_DenseMossAOStrength", "_DenseMossAO"), ("_DenseMossHeightStrength", "_DenseMossHeight"),
        ("_MixedForestFloorHeightStrength", "_MixedForestFloorHeight") };

    internal static bool HasUvTransform(string name) => Array.IndexOf(UvTransforms, name) >= 0;
    internal static bool IsRuntimeInput(string name) => name.StartsWith("_ControlMap", StringComparison.Ordinal) || name.StartsWith("_TerrainHorizon", StringComparison.Ordinal);
    internal static string Keyword(string name) => name == "_RockDetail" ? "_ROCK_DETAIL" : name == "_GrassBladeGround" ? "_GRASS_BLADE_GROUND" : null;
    private static MaterialProperty Property(MaterialProperty[] properties, string name) => Array.Find(properties, p => p.name == name);
    private static bool Zero(MaterialProperty[] properties, string name)
    {
        var p = Property(properties, name);
        return p != null && !p.hasMixedValue && p.floatValue <= 0f;
    }
    private static bool Toggle(MaterialProperty[] properties, string name, bool enabled)
    {
        var p = Property(properties, name);
        return p != null && !p.hasMixedValue && (p.floatValue > 0.5f) == enabled;
    }

    internal static string InactiveReason(string name, MaterialProperty[] properties)
    {
        if (IsRuntimeInput(name)) return "Generated and assigned by terrain streaming / World Manager.";
        if (Array.IndexOf(TraditionalGrass, name) >= 0 && Toggle(properties, "_GrassBladeGround", true)) return "Matching Blade Ground is selected.";
        if ((name == "_GrassSurfaceMap" || name.StartsWith("_GrassGround", StringComparison.Ordinal)) && Toggle(properties, "_GrassBladeGround", false)) return "Select Use Matching Blade Ground to use these controls.";
        if ((name.StartsWith("_Rock", StringComparison.Ordinal) && name != "_RockDetail" && name != "_RockColor" || name == "_CliffNormalStrength") && Toggle(properties, "_RockDetail", false)) return "Enable Rock / Cliff Detail is off; the fallback colors still apply.";
        foreach (var pair in OptionalMaps)
            if (name == pair.control)
            {
                var map = Property(properties, pair.map);
                if (map != null && !map.hasMixedValue && map.textureValue == null) return "Assign the optional map to use this strength.";
            }
        if (name == "_GrassGroundHeightFadeStart" || name == "_GrassGroundHeightFadeEnd")
            if (Zero(properties, "_GrassGroundHeightDepth")) return "Blade-ground parallax depth is zero.";
        if (name == "_GrassGroundNormalFadeStart" || name == "_GrassGroundNormalFadeEnd")
            if (Zero(properties, "_GrassNormalStrength")) return "Grass Normal Strength is zero.";
        if (name == "_RockNormalFadeStart" || name == "_RockNormalFadeEnd")
            if (Zero(properties, "_RockNormalStrength") && Zero(properties, "_CliffNormalStrength")) return "Both rock and cliff normal strengths are zero.";
        if (name == "_GrassGroundDetailContrast" && Zero(properties, "_GrassGroundDetailStrength")) return "Blade-ground tone strength is zero.";
        if (name == "_GrassDetailContrast" && Zero(properties, "_GrassDetailStrength")) return "Traditional grass detail strength is zero.";
        if (name == "_ForestFloorMacroScale" && Zero(properties, "_ForestFloorMacroStrength")) return "Forest macro tone strength is zero.";
        if (name == "_DistanceBlendNoiseScale" && Zero(properties, "_DistanceBlendNoiseStrength")) return "Distance blend noise strength is zero.";
        return null;
    }

    internal static GUIContent Label(MaterialProperty p)
    {
        string text = p.displayName, tip = "";
        switch (p.name)
        {
            case "_NoiseScale": text = "Grass Color Noise Frequency"; tip = "Noise frequency in world XZ. Higher values make smaller color patches."; break;
            case "_NoiseStrength": text = "Grass Color Noise Contrast"; break;
            case "_BlendSharpness": text = "Grass Color Noise Bias"; tip = "Exponent applied to grass color noise; higher values favor darker palette colors. Independent of Surface Blend Sharpness."; break;
            case "_SurfaceBlendSharpness": tip = "Exponent applied to generated surface weights. Higher values sharpen sand/mud/grass/rock/snow/cliff/riverbed transitions."; break;
            case "_LeafLitterTiling": text = "Litter / Mixed Floor World Tiling"; break;
            case "_LeafLitterNormalStrength": text = "Litter / Mixed Floor Normal Strength"; break;
            case "_MossTiling": text = "Moss / Dense Moss World Tiling"; break;
            case "_MossDetailContrast": text = "Moss Texture / Broad Fill Blend"; tip = "0 uses Broad Fill Color, 1 uses the sampled ordinary/dense moss textures. This is a color blend, not a texture contrast exponent."; break;
            case "_RockDetailStrength": text = "Distance Fallback Average Color Strength"; tip = "Blends average albedo into Rock/Cliff fallback colors. Does not tint fully applied near rock textures."; break;
            case "_RockTextureStrength": tip = "At 1, nearby rock/cliff uses the texture's own color. Lower values blend toward distance fallback color. Normal strength is independent."; break;
            case "_ReceiveShadows": text = "Preview Receive Shadows"; tip = "Used on standalone previews. World Manager > Terrain Receive Shadows overrides this on generated near/far chunks."; break;
            case "_GrassSurfaceMap": tip = "Packed linear texture: R=tone, GB=normal XZ, A=height. It is not an ordinary albedo or Unity normal map."; break;
        }
        if ((p.name.Contains("Tiling") && !p.name.Contains("Distance")) || p.name == "_ForestFloorMacroScale")
            tip += " Repeats per world unit; higher values make smaller tiles.";
        if (p.name == "_LeafLitterTiling" || p.name == "_MossTiling")
            tip += " Leaf/mixed/dense maps can multiply this with their per-map UV Tiling.";
        if (p.name.Contains("Fade") || p.name.EndsWith("Distance", StringComparison.Ordinal)) tip += " Camera distance in world units.";
        if (p.name.Contains("HeightStrength")) tip += " Shifts texture sampling UVs; does not change terrain geometry.";
        if (p.propertyType == ShaderPropertyType.Texture)
            tip += HasUvTransform(p.name) ? " Uses layer world tiling followed by this map's UV Tiling / Offset." : " Uses shared world-space coordinates; per-slot Tiling / Offset is not supported.";
        return new GUIContent(text, tip.Trim());
    }
}
