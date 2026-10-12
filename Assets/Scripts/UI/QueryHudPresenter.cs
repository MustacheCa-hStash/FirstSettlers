using UnityEngine;

/// <summary>Consumes the local player's final, obstruction-filtered query after PlayerQuery (order 100).</summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class QueryHudPresenter : MonoBehaviour
{
    [SerializeField] private PlayerQuery query;
    [SerializeField] private QueryHudView view;
    private LocalPlayerInput input;
    public PlayerQuery Query => query;
    public QueryHudView View => view;

    private void Start() => input = query != null ? query.GetComponentInParent<CharacterMotor>()?.GetComponentInChildren<LocalPlayerInput>() : null;

    private void LateUpdate() => Refresh();
    private void OnDisable() { if (view != null) view.Hide(); }

    public void Refresh()
    {
        if (view == null || !view.isActiveAndEnabled) return;
        if (!isActiveAndEnabled || (input != null && !input.WorldInteractionAllowed) || query == null || !query.isActiveAndEnabled || !query.Current.HasTarget)
        {
            view.Hide();
            return;
        }
        view.Show(new QueryHudData(query.Current.Target));
    }
}
