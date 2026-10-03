using System;
using UnityEditor;
using UnityEngine;

// Shader data, not artwork. Two texels hold each leaf's static transform/tone
// and visibility threshold for all 8192 existing scatter seeds.
public static class LeafScatterVariationBuilder
{
    public const string Path = "Assets/Resources/Foliage/LeafScatterVariation.asset";
    public const int Width = 256, Height = 576;
    [MenuItem("Tools/Foliage/Bake Leaf Scatter Variation")]
    public static Texture2D Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Bake scatter data in Edit mode.");
        var colors = new Color[Width * Height];
        for (int seed = 1; seed <= 8192; seed++) for (int leaf = 0; leaf < 9; leaf++)
        {
            float angle = (Random(seed,leaf,3)-.5f)*6.2831853f;
            int index = ((seed-1)*9+leaf)*2;
            colors[index] = new Color(Mathf.Cos(angle), Mathf.Sin(angle), Mathf.Lerp(.6f,1.4f,Random(seed,leaf,7)),
                Mathf.Lerp(.88f,1.06f,Random(seed,leaf,19)));
            colors[index+1] = new Color((Random(seed,leaf,11)-.5f)*.28f,(Random(seed,leaf,13)-.5f)*.28f,
                leaf == 0 ? -1 : Random(seed,leaf,17),0);
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
        if (texture == null)
        {
            texture = new Texture2D(Width,Height,TextureFormat.RGBAFloat,false,true) { name = "LeafScatterVariation" };
            AssetDatabase.CreateAsset(texture,Path);
        }
        else texture.Reinitialize(Width,Height,TextureFormat.RGBAFloat,false);
        texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels(colors); texture.Apply(false,false); EditorUtility.SetDirty(texture);
        AssetDatabase.SaveAssets(); return texture;
    }
    static float Random(float seed, float leaf, float salt)
    {
        float value = Mathf.Sin(seed*.754877666f+leaf*12.9898f+salt*78.233f)*43758.5453f;
        // Round-off near integers must not produce an out-of-range threshold.
        // Keep the baked equivalent of HLSL frac in [0,1).
        return Mathf.Clamp(value-Mathf.Floor(value),0f,.99999994f);
    }
}
