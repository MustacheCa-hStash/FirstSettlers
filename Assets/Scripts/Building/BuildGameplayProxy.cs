using UnityEngine;

/// <summary>Pooled collision and query identity only. The visible mesh stays instanced.</summary>
public sealed class BuildGameplayProxy : QueryTarget
{
    public BuildPieceRecord Record { get; private set; }
    public BoxCollider Shape { get; private set; }
    protected override bool IsAvailable => Record != null;
    public void Bind(BuildPieceRecord record)
    {
        if (Record != record) InvalidateIdentity();
        Record = record;
        gameObject.layer = GameplayLayers.WorldSolid;
        Shape ??= gameObject.AddComponent<BoxCollider>();
        Shape.center = record.Definition.LocalBounds.center;
        Shape.size = record.Definition.LocalBounds.size;
        Shape.isTrigger = false;
        transform.SetPositionAndRotation(record.Origin, BuildGeometry.Rotation(record.WorldYawStep));
        transform.localScale = Vector3.one;
        Shape.enabled = true;
        gameObject.SetActive(true);
    }
    public void Unbind()
    {
        InvalidateIdentity(); Record = null;
        if (Shape != null) Shape.enabled = false;
        gameObject.SetActive(false);
    }
    public override bool TryGetInfo(out QueryTargetInfo info)
    {
        info = default;
        if (!IsQueryAvailable) return false;
        info = new QueryTargetInfo(this, Record.Definition.displayName, null, QueryTargetCapabilities.Breakable, Record);
        return true;
    }
}
