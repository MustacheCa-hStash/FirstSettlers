using UnityEngine;

/// <summary>Physics roles. Indices match ProjectSettings/TagManager.asset; target identity belongs to components/data.</summary>
public static class GameplayLayers
{
    public const int WorldSolid = 8;
    public const int Player = 9;
    public const int QueryOnly = 10;

    // All solid surfaces support the same movement queries, regardless of object type or breakability.
    public const int SolidSurfaceMask = 1 << WorldSolid;

    // Apply on creation, not every frame. Trigger volumes keep their explicitly authored role.
    public static void AssignPhysicalColliders(GameObject hierarchy, int layer)
    {
        foreach (var collider in hierarchy.GetComponentsInChildren<Collider>(true))
            if (!collider.isTrigger) collider.gameObject.layer = layer;
    }
}
