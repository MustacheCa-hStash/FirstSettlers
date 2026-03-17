using UnityEngine;

public class ChunkRuntime
{
    private ChunkRecord chunkRecord;
    private float meshHeightMultiplier;
    private GameObject root;
    private bool visible;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material runtimeMaterial;

    public ChunkRecord ChunkRecord => chunkRecord;
    public GameObject Root => root;
    public bool IsVisible => visible;

    public ChunkRuntime(ChunkRecord chunkRecord, int chunkSize, Transform parent, float meshHeightMultiplier, Material baseMaterial)
    {
        this.chunkRecord = chunkRecord;
        this.meshHeightMultiplier = meshHeightMultiplier;

        ChunkCoord chunkCoord = chunkRecord.ChunkCoord;
        Vector3 worldPosition = new Vector3(chunkCoord.x * chunkSize + chunkSize * 0.5f, 0f, chunkCoord.z * chunkSize + chunkSize * 0.5f);

        root = new GameObject($"Chunk_{chunkCoord.x}_{chunkCoord.z}");
        root.transform.position = worldPosition;
        root.transform.parent = parent;

        meshFilter = root.AddComponent<MeshFilter>();
        meshRenderer = root.AddComponent<MeshRenderer>();

        runtimeMaterial = new Material(baseMaterial);
        meshRenderer.material = runtimeMaterial;

        RebuildMesh();
        SetVisible(false);
        chunkRecord.SetActiveRuntime(this);
    }

    private void ApplyTerrainMesh()
    {
        float[,] heightMap = chunkRecord.HeightMap;
        MeshData meshData = MeshGenerator.GenerateTerrainMesh(heightMap, meshHeightMultiplier);
        meshFilter.mesh = meshData.CreateMesh();
    }
    public void RebuildMesh()
    {
        ApplyTerrainMesh();
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
