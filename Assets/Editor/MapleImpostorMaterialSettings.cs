using UnityEditor;
using UnityEngine;

public static class MapleImpostorMaterialSettings
{
    public static void Apply(Material impostor, string leafMaterialPath, float paletteMode)
    {
        var leaves = AssetDatabase.LoadAssetAtPath<Material>(leafMaterialPath);
        if (leaves == null)
        {
            Debug.LogError("Missing 3D maple leaf material: " + leafMaterialPath);
            return;
        }
        Apply(impostor, leaves, paletteMode);
    }

    public static void Apply(Material impostor, Material leaves, float paletteMode)
    {
        // Treat the 3D leaf material as the palette's source of truth when
        // rebuilding a proxy or recapturing its atlas.
        foreach (string property in new[] { "_SummerLeafColor", "_AutumnYellowColor", "_AutumnOrangeColor", "_AutumnRedColor", "_TreeLeafTint" })
            if (leaves.HasProperty(property)) impostor.SetColor(property, leaves.GetColor(property));
        foreach (string property in new[] { "_SeasonAutumnAmount", "_AutumnVariationStrength", "_TreeTintStrength" })
            if (leaves.HasProperty(property)) impostor.SetFloat(property, leaves.GetFloat(property));
        impostor.SetFloat("_SeasonPaletteMode", paletteMode);
        impostor.SetFloat("_UseSeasonPalette", 1f);
        EditorUtility.SetDirty(impostor);
    }
}
