using UnityEngine;

/// <summary>Identity for a pooled gameplay body. No rendering, input or per-tree update logic.</summary>
[DisallowMultipleComponent]
public sealed class TreeGameplayProxy : MonoBehaviour
{
    public TreeId Id { get; private set; }
    public WorldFeatureVariant Variant { get; private set; }
    public bool IsBound => Id.IsValid;

    internal bool Bind(TreeRecord record)
    {
        bool changed = Id != record.Id || transform.position != record.WorldPosition ||
            transform.rotation != record.Placement.localRotation || transform.localScale != record.Placement.localScale;
        Id = record.Id;
        Variant = record.Placement.variant;
        if (changed)
        {
            transform.SetPositionAndRotation(record.WorldPosition, record.Placement.localRotation);
            transform.localScale = record.Placement.localScale;
        }
        return changed;
    }
    internal void Unbind() { Id = default; Variant = WorldFeatureVariant.None; }
}
