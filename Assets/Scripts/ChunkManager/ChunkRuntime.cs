using UnityEngine;

public class ChunkRuntime
{
    private ChunkRecord chunkRecord;
    private GameObject root;
    private bool visible;

    private Renderer renderer;
    private Material runtimeMaterial;

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

        renderer = root.GetComponent<Renderer>();
        runtimeMaterial = new Material(renderer.sharedMaterial);
        renderer.material = runtimeMaterial;

        ApplyHeightMapPreview(chunkRecord);
        SetVisible(false);
        chunkRecord.SetActiveRuntime(this);
    }

    private void ApplyHeightMapPreview(ChunkRecord chunkRecord)
    {
        float[,] heightMap = chunkRecord.HeightMap;
        
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        Texture2D texture = new Texture2D(width, height);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color[] colorMap = new Color[width * height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                colorMap[z * width + x] = Color.Lerp(Color.black, Color.white, heightMap[x, z]);
            }
        }
        texture.SetPixels(colorMap);
        texture.Apply();

        runtimeMaterial.mainTexture = texture;
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
