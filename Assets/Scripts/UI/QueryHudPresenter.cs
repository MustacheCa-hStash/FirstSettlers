using UnityEngine;

/// <summary>Consumes the local player's final, obstruction-filtered query after PlayerQuery (order 100).</summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class QueryHudPresenter : MonoBehaviour
{
    [SerializeField] private PlayerQuery query;
    [SerializeField] private QueryHudView view;
    [SerializeField, Min(0f), Tooltip("Maximum tree label distance from the view ray origin to the trunk or canopy hit.")]
    private float treePreviewDistance = 1.5f;
    private LocalPlayerInput input;
    public PlayerQuery Query => query;
    public QueryHudView View => view;

    private void Start() => input = query != null ? query.GetComponentInParent<CharacterMotor>()?.GetComponentInChildren<LocalPlayerInput>() : null;

    private void LateUpdate() => Refresh();
    private void OnDisable() { if (view != null) view.Hide(); }

    public void Refresh()
    {
        if (view == null || !view.isActiveAndEnabled) return;
        var hit = query != null ? query.Current : default;
        if (!isActiveAndEnabled || (input != null && !input.WorldInteractionAllowed) || query == null || !query.isActiveAndEnabled ||
            !hit.HasTarget || (hit.Target.Source is TreeGameplayProxy && hit.Distance > treePreviewDistance))
        {
            view.Hide();
            return;
        }
        view.Show(new QueryHudData(hit.Target));
    }
}
