using UnityEngine;

public class ChunkRuntime
{
    private ChunkRecord chunkRecord;
    private GameObject root;
    private bool visible;

    public ChunkRecord ChunkRecord => chunkRecord;
    public GameObject Root => root;
    public bool IsVisible => visible;

    public ChunkRuntime(ChunkRecord chunkRecord, int chunkSize, Transform parent)
    {
        this.chunkRecord = chunkRecord;

        ChunkCoord chunkCoord = chunkRecord.ChunkCoord;
        Vector3 worldPosition = new Vector3(chunkCoord.x * chunkSize + chunkSize * 0.5f, 0f, chunkCoord.z * chunkSize + chunkSize * 0.5f);

        root = GameObject.CreatePrimitive(PrimitiveType.Plane);
        root.name = $"Chunk_{chunkCoord.x}_{chunkCoord.z}";
        root.transform.position = worldPosition;
        root.transform.localScale = new Vector3(chunkSize / 10f, 1f, chunkSize / 10f);
        root.transform.parent = parent;

        Renderer renderer = root.GetComponent<Renderer>();
        float hue = Mathf.Abs((chunkCoord.x * 73856093 ^ chunkCoord.z * 19349663)) % 1000 / 1000f;
        Color color = Color.HSVToRGB(hue, 0.6f, 0.9f);
        renderer.material.color = color;

        SetVisible(false);

        chunkRecord.SetActiveRuntime(this);
    }

    public void SetVisible(bool visible)
    {
        this.visible = visible;
        root.SetActive(visible);
    }
    public void DestroyRuntime()
    {
        chunkRecord.ClearActiveRuntime(this);
        Object.Destroy(root);
    }

}
