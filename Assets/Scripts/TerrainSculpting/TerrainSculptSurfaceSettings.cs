using UnityEngine;

/// <summary>Independent excavation appearance. Does not change landscape rock/cliff materials.</summary>
[CreateAssetMenu(menuName="First Settlers/Terrain/Excavated stone appearance")]
public sealed class TerrainSculptSurfaceSettings:ScriptableObject
{
    [Tooltip("Repeat-wrapped stone albedo. Replacing this does not affect exterior rock/cliff textures.")]
    public Texture2D albedo;
    public Texture2D normal;
    public Color tint=Color.white;
    [Min(.001f),Tooltip("World-space repeats per metre. Higher values make the texture smaller.")]
    public float repeatsPerMetre=.65f;
    [Range(0,2)] public float normalStrength=.45f;
    internal void Apply(MaterialPropertyBlock properties)
    {
        properties.SetTexture("_ExcavationAlbedo",albedo!=null?albedo:Texture2D.whiteTexture);
        if(normal!=null)properties.SetTexture("_ExcavationNormal",normal);
        properties.SetColor("_ExcavationTint",tint);
        properties.SetFloat("_ExcavationTiling",Mathf.Max(.001f,repeatsPerMetre));
        properties.SetFloat("_ExcavationNormalStrength",normal!=null?normalStrength:0);
    }
    // A short transition at the entrance preserves the original skin. Deep upward-facing
    // floors and downward-facing ceilings both become stone; slope is not the classifier.
    internal static float Exposure(double depth,double spacing)
    {
        double t=System.Math.Clamp((depth-spacing*.1)/(spacing*.9),0,1);
        return (float)(t*t*(3-2*t));
    }
}
