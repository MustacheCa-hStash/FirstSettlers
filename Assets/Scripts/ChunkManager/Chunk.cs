using UnityEngine;

public class Chunk
{
    private ChunkCoord chunkCoord;
    private GameObject root;
    private bool visible;

    public Chunk(ChunkCoord chunkCoord, int chunkSize, Transform parent)
    {
        this.chunkCoord = chunkCoord;
        Vector3 worldPosition = new Vector3(chunkCoord.x * chunkSize, 0f, chunkCoord.z * chunkSize);
        root = GameObject.CreatePrimitive(PrimitiveType.Plane);
        root.name = $"Chunk_{chunkCoord.x}_{chunkCoord.z}";
        root.transform.position = worldPosition;
        root.transform.localScale = new Vector3(chunkSize / 10f, 1f, chunkSize / 10f);
        root.transform.parent = parent;

        //these four lines are color visualization settings
        Renderer renderer = root.GetComponent<Renderer>();
        float hue = Mathf.Abs((chunkCoord.x * 73856093 ^ chunkCoord.z * 19349663)) % 1000 / 1000f;
        Color color = Color.HSVToRGB(hue, 0.6f, 0.9f);
        renderer.material.color = color;

        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        this.visible = visible;
        root.SetActive(visible);
    }

    public bool IsVisible() { return visible; }
}
