using System.Collections.Generic;
using UnityEngine;

public class ChunkFoliageRuntime
{
    public Transform root;
    public readonly List<GameObject> grassObjects = new List<GameObject>();

    public bool IsCreated => root != null;

    public void ClearSpawnedObjects()
    {
        for (int i = 0; i < grassObjects.Count; i++)
        {
            if (grassObjects[i] != null)
            {
                Object.Destroy(grassObjects[i]);
            }
        }

        grassObjects.Clear();
    }

    public void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.gameObject.SetActive(visible);
        }
    }
}