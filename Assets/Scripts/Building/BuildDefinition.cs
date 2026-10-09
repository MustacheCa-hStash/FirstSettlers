using UnityEngine;

public enum BuildPartKind { Wall, Floor, Foundation, Corner, Stair }
public enum WallPlacementMode { Panel, Infill, Surface }

/// <summary>Authored placement geometry is independent of mesh detail and runtime proxies.</summary>
[CreateAssetMenu(menuName = "First Settlers/Building/Component")]
public sealed class BuildDefinition : ScriptableObject
{
    public string contentId;
    public string displayName;
    public BuildPartKind kind;
    [Tooltip("Infill faces the viewer on open surfaces and extends parallel to wall ends instead of making a corner.")]
    public WallPlacementMode wallPlacementMode;
    public bool IsWallInfill => kind == BuildPartKind.Wall && wallPlacementMode == WallPlacementMode.Infill;
    public bool IsSurfaceWall => kind == BuildPartKind.Wall && wallPlacementMode == WallPlacementMode.Surface;
    public Vector3Int sizeUnits = new(14, 11, 1);
    public Vector3Int minimumUnits;
    [Min(0), Tooltip("Reserved space at each wall end within its placement bay, in quarter-metre units.")]
    public int wallEndInsetUnits = 1;
    public float WallEndInset => Mathf.Max(0, wallEndInsetUnits) * BuildGeometry.Unit;
    public float WallBaySpan => LocalBounds.size.x + 2 * WallEndInset;
    public Mesh mesh;
    [Tooltip("Independent convex walking hull for stairs. Other parts keep their logical BoxCollider.")]
    public Mesh collisionMesh;
    public Material material;
    public GameObject authoringPrefab;
    public float maxHealth = 100;
    public Bounds LocalBounds => new((Vector3)minimumUnits * BuildGeometry.Unit + (Vector3)sizeUnits * (BuildGeometry.Unit * .5f),
        (Vector3)sizeUnits * BuildGeometry.Unit);
}
