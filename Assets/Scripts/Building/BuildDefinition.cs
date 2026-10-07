using UnityEngine;

public enum BuildPartKind { Wall, Floor, Foundation }

/// <summary>Authored placement geometry is independent of mesh detail and runtime proxies.</summary>
[CreateAssetMenu(menuName = "First Settlers/Building/Component")]
public sealed class BuildDefinition : ScriptableObject
{
    public string contentId;
    public string displayName;
    public BuildPartKind kind;
    public Vector3Int sizeUnits = new(16, 11, 1);
    public Vector3Int minimumUnits;
    public Mesh mesh;
    public Material material;
    public GameObject authoringPrefab;
    public float maxHealth = 100;
    public Bounds LocalBounds => new((Vector3)minimumUnits * BuildGeometry.Unit + (Vector3)sizeUnits * (BuildGeometry.Unit * .5f),
        (Vector3)sizeUnits * BuildGeometry.Unit);
}
