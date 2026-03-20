using UnityEngine;

public class ChunkRuntime
{
    private ChunkRecord chunkRecord;
    private GameObject root;
    private bool visible;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material runtimeMaterial;

    private int currentLOD = -1;

    public ChunkRecord ChunkRecord => chunkRecord;
    public GameObject Root => root;
    public bool IsVisible => visible;
    public int CurrentLOD => currentLOD;

    public ChunkRuntime(ChunkRecord chunkRecord, int chunkSize, float worldScale, Transform parent, Material baseMaterial)
    {
        this.chunkRecord = chunkRecord;

        ChunkCoord chunkCoord = chunkRecord.ChunkCoord;
        Vector3 worldPosition = new Vector3(
            (chunkCoord.x * chunkSize + chunkSize * 0.5f) * worldScale,
            0f,
            (chunkCoord.z * chunkSize + chunkSize * 0.5f) * worldScale
        );

        root = new GameObject($"Chunk_{chunkCoord.x}_{chunkCoord.z}");
        root.transform.position = worldPosition;
        root.transform.parent = parent;

        meshFilter = root.AddComponent<MeshFilter>();
        meshRenderer = root.AddComponent<MeshRenderer>();

        runtimeMaterial = new Material(baseMaterial);
        meshRenderer.material = runtimeMaterial;

        SetVisible(false);
        chunkRecord.SetActiveRuntime(this);
    }

    public void SetMesh(Mesh mesh, int lod)
    {
        if (meshFilter.sharedMesh == mesh && currentLOD == lod)
            return;

        meshFilter.sharedMesh = mesh;
        currentLOD = lod;
    }

    public void ClearMesh()
    {
        meshFilter.sharedMesh = null;
        currentLOD = -1;
    }

    public bool IsShowingLOD(int lod)
    {
        return currentLOD == lod;
    }

    public void SetVisible(bool visible)
    {
        this.visible = visible;
        root.SetActive(visible);
    }
    public void DestroyRuntime()
    {
        chunkRecord.ClearActiveRuntime(this);

        ClearMesh();
        visible = false;

        if (runtimeMaterial != null)
        {
            Object.Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }

        if (root != null)
        {
            Object.Destroy(root);
            root = null;
        }

        meshFilter = null;
        meshRenderer = null;
        chunkRecord = null;
        currentLOD = -1;
    }

}
