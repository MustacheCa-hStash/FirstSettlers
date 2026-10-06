using UnityEngine;

/// <summary>Per-instance query metadata. Generated placement is not a persistent/network rock ID.</summary>
public sealed class RockQueryData
{
    public RockInstanceData? Placement { get; }
    public bool IsGenerated => Placement.HasValue;
    public RockQueryData(RockInstanceData? placement) { Placement = placement; }
}

/// <summary>Author on a rock root to identify all descendant physical/query colliders.</summary>
[DisallowMultipleComponent]
public sealed class RockQueryTarget : QueryTarget
{
    [SerializeField] private string displayName = "Rock";
    [SerializeField] private Sprite icon;
    [SerializeField] private WorldObjectDefinition definition;
    [SerializeField] private bool breakable = true;
    private RockQueryData data;

    public RockQueryData Data => data;
    private void Awake() => data ??= new RockQueryData(null);

    public void Initialize(RockInstanceData placement)
    {
        InvalidateIdentity();
        data = new RockQueryData(placement);
    }

    public override bool TryGetInfo(out QueryTargetInfo info)
    {
        info = default;
        if (!IsQueryAvailable) return false;
        // Supports directly placed scene rocks and Edit Mode validation without a generated placement.
        data ??= new RockQueryData(null);
        info = new QueryTargetInfo(this, string.IsNullOrWhiteSpace(displayName) ? "Rock" : displayName,
            icon, breakable ? QueryTargetCapabilities.Breakable : QueryTargetCapabilities.None, data, definition);
        return true;
    }
}
