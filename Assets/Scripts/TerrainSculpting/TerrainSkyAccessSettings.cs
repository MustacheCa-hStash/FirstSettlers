using UnityEngine;

/// <summary>Live ambient controls. Geometry/visibility cache spacing remains 2 metres.</summary>
[CreateAssetMenu(menuName="First Settlers/Terrain/Cave sky access")]
public sealed class TerrainSkyAccessSettings:ScriptableObject
{
    public bool enabled=true;
    [Range(0,1),Tooltip("Fraction of ordinary ambient retained in a sealed cave. 0 removes ambient; 1 keeps outdoor ambient. Does not dim direct lights.")]
    public float caveAmbientMultiplier=.025f;
    [Range(.25f,4),Tooltip("Higher values darken partial sky exposure around entrances. Full outdoor exposure remains unchanged.")]
    public float skyResponse=1.5f;
    [Range(0,2),Tooltip("Seconds to blend newly computed sky access. Brightness sliders apply immediately without rebuilding probes.")]
    public float blendSeconds=.35f;
}
