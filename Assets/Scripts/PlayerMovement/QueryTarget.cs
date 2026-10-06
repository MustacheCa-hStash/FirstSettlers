using System;
using UnityEngine;

[Flags]
public enum QueryTargetCapabilities
{
    None = 0,
    Breakable = 1
}

/// <summary>Identity/metadata snapshot. No interaction commands or physics-layer-based object types.</summary>
public readonly struct QueryTargetInfo
{
    private readonly uint identityVersion;
    public QueryTarget Source { get; }
    public string DisplayName { get; }
    public Sprite Icon { get; }
    public WorldObjectDefinition Definition { get; }
    public string Description { get; }
    public QueryTargetCapabilities Capabilities { get; }
    public object Data { get; }
    public bool IsValid => Source != null && Source.IsQueryAvailable && Source.IdentityVersion == identityVersion;

    public QueryTargetInfo(QueryTarget source, string displayName, Sprite icon,
        QueryTargetCapabilities capabilities, object data, WorldObjectDefinition definition = null)
    {
        Source = source;
        identityVersion = source != null ? source.IdentityVersion : 0;
        Definition = definition;
        DisplayName = definition != null && !string.IsNullOrWhiteSpace(definition.DisplayName)
            ? definition.DisplayName : displayName;
        Icon = definition != null && definition.Icon != null ? definition.Icon : icon;
        Description = definition != null ? definition.ShortDescription : string.Empty;
        Capabilities = capabilities;
        Data = data;
    }

    public bool HasCapability(QueryTargetCapabilities capability) => IsValid && (Capabilities & capability) == capability;
    public bool TryGetData<T>(out T data) where T : class
    {
        data = IsValid ? Data as T : null;
        return data != null;
    }
}

/// <summary>Target provider on the collider object or an ancestor. No per-object Update is needed.</summary>
public abstract class QueryTarget : MonoBehaviour
{
    internal uint IdentityVersion { get; private set; }
    public bool IsQueryAvailable => isActiveAndEnabled && IsAvailable;
    protected virtual bool IsAvailable => true;
    public abstract bool TryGetInfo(out QueryTargetInfo info);

    // Call when rebinding/unbinding a pooled provider so old snapshots cannot identify the new occupant.
    protected void InvalidateIdentity() { unchecked { IdentityVersion++; } }

    public static bool TryResolve(Collider collider, out QueryTargetInfo info)
    {
        info = default;
        if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
            return false;
        for (Transform node = collider.transform; node != null; node = node.parent)
            if (node.TryGetComponent<QueryTarget>(out var target))
            {
                // The nearest provider owns this shape; a disabled provider does not fall through to another target.
                if (target.IsQueryAvailable && target.TryGetInfo(out var resolved) && resolved.IsValid)
                {
                    info = resolved;
                    return true;
                }
                return false;
            }
        return false;
    }
}
