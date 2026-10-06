using UnityEngine;
using Unity.Profiling;

public enum PlayerQueryHitKind
{
    None,
    Solid,
    QueryOnly
}

/// <summary>Closest geometry plus optional target metadata. A solid hit can be a named, breakable target.</summary>
public readonly struct PlayerQueryResult
{
    public RaycastHit Hit { get; }
    public PlayerQueryHitKind Kind { get; }
    public bool HasHit
    {
        get
        {
            var collider = Hit.collider;
            return Kind != PlayerQueryHitKind.None && collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
        }
    }
    public Collider Collider => Hit.collider;
    public Vector3 Point => Hit.point;
    public Vector3 Normal => Hit.normal;
    public float Distance => Hit.distance;
    public QueryTargetInfo Target { get; }
    public bool HasTarget => HasHit && Target.IsValid;

    internal PlayerQueryResult(RaycastHit hit, PlayerQueryHitKind kind)
    {
        Hit = hit;
        Kind = kind;
        QueryTarget.TryResolve(hit.collider, out var target);
        Target = target;
    }
}

/// <summary>Local view query and target resolution. Does not read input, move transforms or execute interactions.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class PlayerQuery : MonoBehaviour
{
    private static readonly ProfilerMarker QueryMarker = new("FS.PlayerQuery.Sample");
    [SerializeField] private Camera viewCamera;
    [Tooltip("Query reach in world units, measured from the center ray's origin on the camera near plane.")]
    [SerializeField, Min(0f)] private float maxDistance = 5f;

    public Camera ViewCamera => viewCamera;
    public float MaxDistance => maxDistance;
    public PlayerQueryResult Current { get; private set; }
    public PlayerQueryResult SolidHit { get; private set; }
    public PlayerQueryResult QueryOnlyHit { get; private set; }
    public Ray ViewRay { get; private set; }
    public bool HasViewRay { get; private set; }

    private void LateUpdate() => Sample();

    /// <summary>Refresh from the final view pose. The local player samples once per rendered frame.</summary>
    public void Sample()
    {
        using var sample = QueryMarker.Auto();
        Current = default;
        SolidHit = default;
        QueryOnlyHit = default;
        HasViewRay = false;
        ViewRay = default;
        if (!isActiveAndEnabled || viewCamera == null || !viewCamera.isActiveAndEnabled || maxDistance <= 0f)
            return;

        ViewRay = viewCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        HasViewRay = true;

        // Separate casts allow query triggers while ignoring incidental triggers on physical objects.
        // Closest-hit casts need no allocating hit list or bounded buffer that can truncate the nearest hit.
        bool hasSolid = Physics.Raycast(ViewRay, out var solid, maxDistance,
            GameplayLayers.SolidSurfaceMask, QueryTriggerInteraction.Ignore);
        bool hasQuery = Physics.Raycast(ViewRay, out var query, maxDistance,
            1 << GameplayLayers.QueryOnly, QueryTriggerInteraction.Collide);

        if (hasSolid) SolidHit = new PlayerQueryResult(solid, PlayerQueryHitKind.Solid);
        if (hasQuery) QueryOnlyHit = new PlayerQueryResult(query, PlayerQueryHitKind.QueryOnly);

        // Solid wins an exact tie. A wall or terrain blocks query shapes behind it.
        if (hasQuery && (!hasSolid || query.distance < solid.distance))
            Current = QueryOnlyHit;
        else if (hasSolid)
            Current = SolidHit;
    }

    private void OnDisable()
    {
        Current = default;
        SolidHit = default;
        QueryOnlyHit = default;
        HasViewRay = false;
        ViewRay = default;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !HasViewRay)
            return;
        bool hasHit = Current.HasHit;
        Gizmos.color = hasHit ? Color.green : Color.yellow;
        Vector3 end = hasHit ? Current.Point : ViewRay.GetPoint(maxDistance);
        Gizmos.DrawLine(ViewRay.origin, end);
        if (hasHit)
            Gizmos.DrawWireSphere(end, .04f);
    }
}
