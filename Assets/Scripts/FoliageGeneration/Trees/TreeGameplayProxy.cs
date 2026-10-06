using UnityEngine;
using System.Collections.Generic;

/// <summary>Identity for a pooled gameplay body. No rendering, input or per-tree update logic.</summary>
[DisallowMultipleComponent]
public sealed class TreeGameplayProxy : QueryTarget
{
    public TreeId Id { get; private set; }
    public WorldFeatureVariant Variant { get; private set; }
    public bool IsBound => Id.IsValid;
    public TreeRecord Record { get; private set; }
    private string displayNameOverride;
    private Sprite queryIcon;
    private WorldObjectDefinition queryDefinition;
    private Collider[] physicalTrunkColliders = System.Array.Empty<Collider>();
    private Collider[] queryCanopyColliders = System.Array.Empty<Collider>();
    public IReadOnlyList<Collider> PhysicalTrunkColliders => physicalTrunkColliders;
    public IReadOnlyList<Collider> QueryCanopyColliders => queryCanopyColliders;
    public bool PhysicalTrunkActive { get; private set; }
    public bool QueryCanopyActive { get; private set; }

    internal void ConfigureColliders(Collider[] physical, Collider[] canopy)
    {
        physicalTrunkColliders = physical;
        queryCanopyColliders = canopy;
    }

    internal bool SetColliderRoles(bool physical, bool canopy)
    {
        bool changed = PhysicalTrunkActive != physical || QueryCanopyActive != canopy;
        if (PhysicalTrunkActive != physical)
            foreach (var collider in physicalTrunkColliders) collider.enabled = physical;
        if (QueryCanopyActive != canopy)
            foreach (var collider in queryCanopyColliders) collider.enabled = canopy;
        PhysicalTrunkActive = physical;
        QueryCanopyActive = canopy;
        return changed;
    }
    protected override bool IsAvailable => IsBound && Record != null && Record.State == TreeState.Standing;

    internal void ConfigureQuery(string displayName, Sprite icon, WorldObjectDefinition definition = null)
    {
        displayNameOverride = displayName;
        queryIcon = icon;
        queryDefinition = definition;
    }

    public override bool TryGetInfo(out QueryTargetInfo info)
    {
        info = default;
        if (!IsQueryAvailable) return false;
        info = new QueryTargetInfo(this, string.IsNullOrWhiteSpace(displayNameOverride) ? TreeSpeciesCatalog.DisplayName(Variant) : displayNameOverride,
            queryIcon, QueryTargetCapabilities.Breakable, Record, queryDefinition);
        return true;
    }

    internal bool Bind(TreeRecord record)
    {
        if (!ReferenceEquals(Record, record)) InvalidateIdentity();
        Record = record;
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
    internal void Unbind()
    {
        SetColliderRoles(false, false);
        InvalidateIdentity();
        Record = null; Id = default; Variant = WorldFeatureVariant.None;
    }
}
