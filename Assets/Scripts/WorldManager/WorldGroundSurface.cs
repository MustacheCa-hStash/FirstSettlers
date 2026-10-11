using UnityEngine;

/// <summary>Explicit terrain role for placement queries; a streamed collider is not a persistent foundation.</summary>
public sealed class WorldGroundSurface : MonoBehaviour
{
    private static readonly System.Collections.Generic.List<WorldGroundSurface> active=new();
    public static System.Collections.Generic.IReadOnlyList<WorldGroundSurface> Active=>active;
    private void OnEnable(){if(!active.Contains(this))active.Add(this);}
    private void OnDisable()=>active.Remove(this);
    private void OnDestroy()=>active.Remove(this);
}
