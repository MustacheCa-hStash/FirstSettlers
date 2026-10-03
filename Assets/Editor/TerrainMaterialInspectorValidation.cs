using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainMaterialInspectorValidation
{
    private const string MaterialPath = "Assets/Materials/M_Terrain/M_TerrainBase.mat";
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static MaterialProperty[] Properties(params Material[] materials) => MaterialEditor.GetMaterialProperties(materials);

    [MenuItem("Tools/Terrain/Validate Terrain Material Inspector")]
    public static void Run()
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Check(source != null && source.shader.name == "Custom/StylizedTerrainURP", "Terrain base material is missing or uses the wrong shader.");
        var properties = Properties(source);
        var grouped = new HashSet<string>();
        foreach (var section in TerrainMaterialInspector.Sections)
            for (int i = 1; i < section.Length; i++)
            {
                Check(source.HasProperty(section[i]), "Missing grouped property: " + section[i]);
                Check(grouped.Add(section[i]), "Duplicate grouped property: " + section[i]);
            }
        string before = EditorJsonUtility.ToJson(source);
        int transforms = 0;
        foreach (var p in properties)
        {
            if (p.name.StartsWith("unity_", StringComparison.Ordinal)) continue;
            Check(grouped.Contains(p.name), "Ungrouped terrain property: " + p.name);
            TerrainMaterialInspectorRules.Label(p);
            TerrainMaterialInspectorRules.InactiveReason(p.name, properties);
            if (p.propertyType == ShaderPropertyType.Texture)
            {
                bool supported = TerrainMaterialInspectorRules.HasUvTransform(p.name);
                bool hiddenTransform = (p.propertyFlags & ShaderPropertyFlags.NoScaleOffset) != 0;
                Check(supported != hiddenTransform, "Incorrect texture-transform flags: " + p.name);
                if (supported) transforms++;
            }
            if (TerrainMaterialInspectorRules.IsRuntimeInput(p.name))
            {
                Check((p.propertyFlags & ShaderPropertyFlags.HideInInspector) != 0, "Runtime input exposed as a regular control: " + p.name);
                Check(TerrainMaterialInspectorRules.InactiveReason(p.name, properties) != null, "Runtime diagnostic is editable: " + p.name);
            }
        }
        var gui = new TerrainMaterialInspector();
        gui.ValidateMaterial(source);
        Check(before == EditorJsonUtility.ToJson(source), "Inspecting/validating the base material changed its saved settings.");
        var clone = new Material(source);
        var other = new Material(source);
        try
        {
            void Active(string name, bool expected)
            {
                Check((TerrainMaterialInspectorRules.InactiveReason(name, Properties(clone)) == null) == expected,
                    "Unexpected active state: " + name);
            }
            void Mode(string name, bool enabled)
            {
                clone.SetFloat(name, enabled ? 1f : 0f); gui.ValidateMaterial(clone);
                Check(clone.IsKeywordEnabled(TerrainMaterialInspectorRules.Keyword(name)) == enabled, "Toggle/keyword mismatch: " + name);
            }
            Mode("_GrassBladeGround", true);
            Active("_GrassAlbedo", false); Active("_GrassTilingNear", false);
            Active("_GrassSurfaceMap", true); Active("_GrassGroundTiling", true); Active("_GrassNormalStrength", true);
            Mode("_GrassBladeGround", false);
            Active("_GrassAlbedo", true); Active("_GrassTilingNear", true); Active("_GrassSurfaceMap", false);
            Mode("_RockDetail", false);
            Active("_RockTiling", false); Active("_RockAverageAlbedo", false);
            Active("_RockColor", true); Active("_CliffColor", true);
            Mode("_RockDetail", true); Active("_RockTiling", true);
            clone.SetTexture("_LeafLitterAO", null); Active("_LeafLitterAOStrength", false); Active("_LeafLitterAO", true);
            clone.SetTexture("_LeafLitterAO", Texture2D.whiteTexture); Active("_LeafLitterAOStrength", true);
            Mode("_GrassBladeGround", true);
            clone.SetFloat("_GrassGroundHeightDepth", 0); Active("_GrassGroundHeightFadeEnd", false); Active("_GrassGroundHeightDepth", true);
            clone.SetFloat("_GrassGroundHeightDepth", .01f); Active("_GrassGroundHeightFadeEnd", true);
            clone.SetFloat("_RockNormalStrength", 0); clone.SetFloat("_CliffNormalStrength", 0);
            Active("_RockNormalFadeEnd", false); Active("_RockNormalStrength", true);
            other.SetFloat("_GrassBladeGround", 0); gui.ValidateMaterial(other);
            var mixed = Properties(clone, other);
            Check(Array.Find(mixed, p => p.name == "_GrassBladeGround").hasMixedValue, "Mixed grass mode not established.");
            Check(TerrainMaterialInspectorRules.InactiveReason("_GrassAlbedo", mixed) == null &&
                TerrainMaterialInspectorRules.InactiveReason("_GrassSurfaceMap", mixed) == null, "Mixed selection locks a working grass mode.");
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); UnityEngine.Object.DestroyImmediate(other); }
        Check(!ShaderUtil.ShaderHasError(source.shader), "Terrain shader has import errors.");
        Debug.Log($"TERRAIN MATERIAL INSPECTOR PASS: {grouped.Count} properties grouped once; {transforms} working UV transforms retained; runtime inputs read-only; mode, optional-map and mixed-selection dependencies passed.");
    }

    // BeforeTerrain.mat is copied into the disposable project before launching.
    private static void ValidatePreservation()
    {
        var before = AssetDatabase.LoadAssetAtPath<Material>("Assets/Editor/TerrainInspectorFixtures/BeforeTerrain.mat");
        var after = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Check(before != null, "Missing original-material validation fixture.");
        foreach (var p in Properties(after))
        {
            bool same;
            switch (p.propertyType)
            {
                case ShaderPropertyType.Texture:
                    same = before.GetTexture(p.name) == after.GetTexture(p.name) &&
                        before.GetTextureScale(p.name) == after.GetTextureScale(p.name) &&
                        before.GetTextureOffset(p.name) == after.GetTextureOffset(p.name); break;
                case ShaderPropertyType.Color: same = before.GetColor(p.name) == after.GetColor(p.name); break;
                case ShaderPropertyType.Vector: same = before.GetVector(p.name) == after.GetVector(p.name); break;
                case ShaderPropertyType.Int: same = before.GetInteger(p.name) == after.GetInteger(p.name); break;
                default: same = before.GetFloat(p.name) == after.GetFloat(p.name); break;
            }
            Check(same, "Cleanup changed a working value/reference/UV transform: " + p.name);
        }
        foreach (string keyword in new[] { "_ROCK_DETAIL", "_GRASS_BLADE_GROUND" })
            Check(before.IsKeywordEnabled(keyword) == after.IsKeywordEnabled(keyword), "Cleanup changed feature mode: " + keyword);
        Check(before.renderQueue == after.renderQueue, "Cleanup changed render queue.");
        Debug.Log("TERRAIN MATERIAL PRESERVATION PASS: all shader values, texture references, UV transforms, feature keywords and render queue match the original material.");
    }

    public static void RunBatch()
    {
        try { Run(); ValidatePreservation(); CliffTextureValidation.Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
