using System.Collections.Generic;
using UnityEngine;

/// <summary>Explicit collider templates on a visual tree prefab. The visual prefab is never instantiated by gameplay.</summary>
[DisallowMultipleComponent]
public sealed class TreeGameplayAuthoring : MonoBehaviour
{
    [Tooltip("Assign only the physical trunk colliders. Box, Capsule and Sphere are supported. Children and compound trunks are allowed. Source enabled state is ignored; runtime proxies enable these as solid colliders.")]
    [SerializeField] private Collider[] physicalTrunkColliders = System.Array.Empty<Collider>();

    public IReadOnlyList<Collider> PhysicalTrunkColliders => physicalTrunkColliders ?? System.Array.Empty<Collider>();
}
