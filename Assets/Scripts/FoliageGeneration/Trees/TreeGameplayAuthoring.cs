using System.Collections.Generic;
using UnityEngine;

/// <summary>Explicit collider templates on a visual tree prefab. The visual prefab is never instantiated by gameplay.</summary>
[DisallowMultipleComponent]
public sealed class TreeGameplayAuthoring : MonoBehaviour
{
    [Tooltip("Assign only the physical trunk colliders. Box, Capsule and Sphere are supported. Children and compound trunks are allowed. Source enabled state is ignored; runtime proxies enable these as solid colliders.")]
    [SerializeField] private Collider[] physicalTrunkColliders = System.Array.Empty<Collider>();

    [Tooltip("Identification-only canopy shapes on separate child GameObjects. Box, Capsule, Sphere and convex MeshColliders with a shared mesh are supported. Runtime forces QueryOnly triggers, independent of source enabled state.")]
    [SerializeField] private Collider[] queryCanopyColliders = System.Array.Empty<Collider>();

    [Tooltip("Shared datacard for this tree type. Its name, icon and description take precedence over legacy display fields.")]
    [SerializeField] private WorldObjectDefinition queryDefinition;

    [Tooltip("Fallback query display name when no datacard name is assigned. Leave blank to use the generated tree species name.")]
    [SerializeField] private string queryDisplayName;
    [SerializeField] private Sprite queryIcon;

    public IReadOnlyList<Collider> PhysicalTrunkColliders => physicalTrunkColliders ?? System.Array.Empty<Collider>();
    public IReadOnlyList<Collider> QueryCanopyColliders => queryCanopyColliders ?? System.Array.Empty<Collider>();
    public string QueryDisplayName => queryDisplayName;
    public Sprite QueryIcon => queryIcon;
    public WorldObjectDefinition QueryDefinition => queryDefinition;
}
