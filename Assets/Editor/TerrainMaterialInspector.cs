using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class TerrainMaterialInspector : ShaderGUI
{
    // One entry per shader property; future public properties still fall through.
    internal static readonly string[][] Sections = {
        new[] { "Surface Blending and Grass Colors", "_SurfaceBlendSharpness", "_DarkGrassColor", "_MidGrassColor", "_LightGrassColor", "_GroundDarkGrassColor", "_GroundMidGrassColor", "_GroundLightGrassColor", "_NoiseScale", "_NoiseStrength", "_BlendSharpness", "_GrassNormalStrength", "_GrassBladeGround" },
        new[] { "Matching Blade Ground", "_GrassSurfaceMap", "_GrassGroundTiling", "_GrassGroundFarTiling", "_GrassGroundScaleFadeStart", "_GrassGroundScaleFadeEnd", "_GrassGroundGridScale", "_GrassGroundDetailStrength", "_GrassGroundDetailContrast", "_GrassGroundTint", "_GrassGroundDetailFadeStart", "_GrassGroundDetailFadeEnd", "_GrassGroundNormalFadeStart", "_GrassGroundNormalFadeEnd", "_GrassGroundHeightDepth", "_GrassGroundHeightFadeStart", "_GrassGroundHeightFadeEnd" },
        new[] { "Traditional Grass Textures", "_GrassAlbedo", "_GrassNormal", "_GrassTilingNear", "_GrassTilingFar", "_GrassTilingNearDistance", "_GrassTilingFarDistance", "_GrassDetailStrength", "_GrassDetailContrast" },
        new[] { "Rock and Cliff", "_RockDetail", "_RockAlbedo", "_RockNormal", "_RockTiling", "_RockTextureStrength", "_RockNormalStrength", "_CliffNormalStrength", "_RockDetailFadeStart", "_RockDetailFadeEnd", "_RockNormalFadeStart", "_RockNormalFadeEnd", "_RockColor", "_CliffColor", "_RockAverageAlbedo", "_RockDetailStrength" },
        new[] { "Sand, Mud and Riverbed", "_SandColor", "_SandAlbedo", "_SandNormal", "_SandTiling", "_SandDetailStrength", "_SandNormalStrength", "_MudColor", "_RiverbedColor" },
        new[] { "Leaf Litter", "_LeafLitterColor", "_LeafLitterAlbedo", "_LeafLitterNormal", "_LeafLitterAO", "_LeafLitterAOStrength", "_LeafLitterHeight", "_LeafLitterHeightStrength", "_LeafLitterTiling", "_LeafLitterNormalStrength", "_LeafLitterNormalFadeStart", "_LeafLitterNormalFadeEnd" },
        new[] { "Mixed Forest Floor", "_MixedForestFloorColor", "_MixedForestFloorAlbedo", "_MixedForestFloorNormal", "_MixedForestFloorHeight", "_MixedForestFloorHeightStrength" },
        new[] { "Bare Dirt", "_BareDirtColor", "_BareDirtAlbedo", "_BareDirtNormal", "_BareDirtAO", "_BareDirtAOStrength", "_BareDirtHeight", "_BareDirtHeightStrength", "_BareDirtTiling", "_BareDirtNormalStrength" },
        new[] { "Moss", "_MossColor", "_MossAlbedo", "_MossNormal", "_MossAO", "_MossAOStrength", "_MossHeight", "_MossHeightStrength", "_MossTiling", "_MossNormalStrength", "_MossDetailContrast", "_MossSaturation", "_MossFillColor" },
        new[] { "Dense Moss", "_DenseMossColor", "_DenseMossAlbedo", "_DenseMossAO", "_DenseMossAOStrength", "_DenseMossHeight", "_DenseMossHeightStrength" },
        new[] { "Forest Floor Macro Variation", "_ForestFloorMacroScale", "_ForestFloorMacroStrength" },
        new[] { "Snow", "_SnowColor", "_SnowTint", "_SnowAlbedo", "_SnowNormal", "_SnowNormalStrength", "_SnowTilingNear", "_SnowTilingFar", "_SnowTilingNearDistance", "_SnowTilingFarDistance", "_SnowTriplanarStart", "_SnowTriplanarEnd", "_SnowTriplanarSharpness" },
        new[] { "Lighting and Distance Blending", "_AmbientStrength", "_DistanceBlendNoiseScale", "_DistanceBlendNoiseStrength" },
        new[] { "Preview and Runtime Inputs", "_ReceiveShadows", "_ControlMap0", "_ControlMap1", "_ControlMap2", "_TerrainHorizon0", "_TerrainHorizon1", "_TerrainHorizonUV", "_TerrainHorizonParams", "_TerrainHorizonTint" }
    };

    public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
    {
        EditorGUILayout.HelpBox("World-space tiling uses repeats per world unit: higher values make smaller tiles. Most textures share their layer's tiling. UV multipliers are shown only for maps that actually support them. Generated chunks copy this base material; regenerate terrain or restart Play mode to update loaded chunks.", MessageType.Info);
        var drawn = new HashSet<string>();
        foreach (var section in Sections)
        {
            string title = section[0], key = "FirstSettlers.TerrainMaterial." + title;
            bool open = EditorGUILayout.Foldout(SessionState.GetBool(key, title != "Traditional Grass Textures" && title != "Preview and Runtime Inputs"), title, true, EditorStyles.foldoutHeader);
            SessionState.SetBool(key, open);
            for (int i = 1; i < section.Length; i++) drawn.Add(section[i]);
            if (!open) continue;
            EditorGUI.indentLevel++;
            string note = SectionNote(title);
            if (note != null) EditorGUILayout.HelpBox(note, MessageType.None);
            for (int i = 1; i < section.Length; i++)
            {
                var property = FindProperty(section[i], properties, false);
                if (property != null) DrawProperty(editor, property, properties);
            }
            if (title == "Lighting and Distance Blending") editor.RenderQueueField();
            EditorGUI.indentLevel--;
        }
        foreach (var p in properties)
            if (!drawn.Contains(p.name) && (p.propertyFlags & ShaderPropertyFlags.HideInInspector) == 0)
                DrawProperty(editor, p, properties);
    }

    private static string SectionNote(string title)
    {
        switch (title)
        {
            case "Surface Blending and Grass Colors": return "Grass color noise affects both open grass and forest grass. Grass Normal Strength applies to the selected ground-texture mode.";
            case "Matching Blade Ground": return "Uses the packed tone / normal XZ / height map. The traditional grass albedo and normal maps are bypassed in this mode. Height offsets texture sampling; it does not displace terrain geometry.";
            case "Traditional Grass Textures": return "Used when Use Matching Blade Ground is off. These are working fallback controls.";
            case "Rock and Cliff": return "Triplanar mapping uses one shared world-space tiling value. Near texture color is untinted at Texture Strength 1. Rock/Cliff colors and average albedo control the distance fallback; normal detail fades independently.";
            case "Leaf Litter": return "World tiling is the base frequency. Per-map UV Tiling multiplies it; Offset shifts that map. Keep albedo/normal/height transforms aligned. Normal strength and fade also apply to Mixed Forest Floor.";
            case "Mixed Forest Floor": return "Selected by generated forest-cover variants. Shares Leaf Litter world tiling, normal strength and normal fade; per-map UV transforms remain available.";
            case "Dense Moss": return "Selected within dense generated moss patches. Shares Moss world tiling, broad fill, saturation and macro variation. Dense moss has no separate normal map.";
            case "Moss": return "Fine Detail Contrast blends from Broad Fill Color to sampled moss textures. Normal and height relief fade from 12 to 45 world units.";
            case "Forest Floor Macro Variation": return "Shared broad tone variation for ordinary/mixed litter and a weaker contribution on moss.";
            case "Snow": return "Base Color and Texture Tint multiply. Near/far textures blend by camera distance; triplanar start/end use slope = 1 - abs(mesh normal Y), from 0 (flat) to 1 (vertical).";
            case "Lighting and Distance Blending": return "Distance noise softens snow near/far tiling transitions and traditional-grass transitions. It does not control rock or matching blade-ground fades.";
            case "Preview and Runtime Inputs": return "World Manager overrides Receive Shadows on generated terrain. Control maps and horizon-shadow data are assigned per chunk; the base material's values are shown read-only for diagnostics.";
            default: return null;
        }
    }

    private static void DrawProperty(MaterialEditor editor, MaterialProperty property, MaterialProperty[] properties)
    {
        string reason = TerrainMaterialInspectorRules.InactiveReason(property.name, properties);
        var label = TerrainMaterialInspectorRules.Label(property);
        if (reason != null) label.tooltip += "\nInactive: " + reason;
        using (new EditorGUI.DisabledScope(reason != null))
        {
            string keyword = TerrainMaterialInspectorRules.Keyword(property.name);
            if (keyword != null || property.name == "_ReceiveShadows")
            {
                bool mixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                bool value = EditorGUILayout.Toggle(label, property.floatValue > 0.5f);
                if (EditorGUI.EndChangeCheck())
                {
                    editor.RegisterPropertyChangeUndo(label.text);
                    property.floatValue = value ? 1f : 0f;
                    if (keyword != null)
                        foreach (UnityEngine.Object o in property.targets) SetKeyword((Material)o, keyword, value);
                    editor.PropertiesChanged();
                }
                EditorGUI.showMixedValue = mixed;
            }
            else if (property.propertyType == ShaderPropertyType.Texture)
            {
                editor.TexturePropertySingleLine(label, property);
                if (TerrainMaterialInspectorRules.HasUvTransform(property.name))
                {
                    string key = "FirstSettlers.TerrainMaterial.UV." + property.name;
                    bool open = EditorGUILayout.Foldout(SessionState.GetBool(key, false), "Per-map UV Tiling / Offset", true);
                    SessionState.SetBool(key, open);
                    if (open)
                    {
                        EditorGUI.indentLevel++;
                        editor.TextureScaleOffsetProperty(property);
                        EditorGUI.indentLevel--;
                    }
                }
            }
            else if (property.propertyType == ShaderPropertyType.Color)
            {
                // This opaque shader reads RGB only. Keep each saved alpha while
                // removing the nonfunctional alpha slider from the color picker.
                bool mixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = property.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                Color color = EditorGUI.ColorField(EditorGUILayout.GetControlRect(), label, property.colorValue,
                    true, false, (property.propertyFlags & ShaderPropertyFlags.HDR) != 0);
                if (EditorGUI.EndChangeCheck())
                {
                    editor.RegisterPropertyChangeUndo(label.text);
                    foreach (UnityEngine.Object o in property.targets)
                    {
                        var material = (Material)o;
                        color.a = material.GetColor(property.name).a;
                        material.SetColor(property.name, color);
                    }
                    editor.PropertiesChanged();
                }
                EditorGUI.showMixedValue = mixed;
            }
            else editor.ShaderProperty(property, label);
        }
    }

    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
    }

    public override void ValidateMaterial(Material material)
    {
        foreach (string name in new[] { "_RockDetail", "_GrassBladeGround" })
            if (material.HasProperty(name)) SetKeyword(material, TerrainMaterialInspectorRules.Keyword(name), material.GetFloat(name) > 0.5f);
    }
}
