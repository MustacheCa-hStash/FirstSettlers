using UnityEngine;

public class ChunkRuntime
{
    private ChunkRecord chunkRecord;
    private GameObject root;
    private bool visible;

    private MeshFilter terrainMeshFilter;
    private MeshRenderer terrainMeshRenderer;
    private Material runtimeTerrainMaterial;

    private GameObject waterRoot;
    private MeshFilter waterMeshFilter;
    private MeshRenderer waterMeshRenderer;
    private Material runtimeWaterMaterial;

    private int currentLOD = -1;

    public ChunkRecord ChunkRecord => chunkRecord;
    public GameObject Root => root;
    public bool IsVisible => visible;
    public int CurrentLOD => currentLOD;

    public ChunkRuntime(ChunkRecord chunkRecord, int chunkSize, float worldScale, Transform parent,
        Material terrainMaterial, Material waterMaterial)
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

        terrainMeshFilter = root.AddComponent<MeshFilter>();
        terrainMeshRenderer = root.AddComponent<MeshRenderer>();

        runtimeTerrainMaterial = new Material(terrainMaterial);
        terrainMeshRenderer.material = runtimeTerrainMaterial;

        waterRoot = new GameObject("Water");
        waterRoot.transform.SetParent(root.transform, false);
        waterRoot.transform.localPosition = Vector3.zero;
        waterRoot.transform.localRotation = Quaternion.identity;
        waterRoot.transform.localScale = Vector3.one;

        waterMeshFilter = waterRoot.AddComponent<MeshFilter>();
        waterMeshRenderer = waterRoot.AddComponent<MeshRenderer>();

        runtimeWaterMaterial = new Material(waterMaterial);
        waterMeshRenderer.material = runtimeWaterMaterial;

        waterRoot.SetActive(false);

        SetVisible(false);
        chunkRecord.SetActiveRuntime(this);
    }

    public void SetMeshes(Mesh terrainMesh, Mesh waterMesh, int lod)
    {
        if (terrainMeshFilter.sharedMesh != terrainMesh)
            terrainMeshFilter.sharedMesh = terrainMesh;

        //bool hasRenderableWater = waterMesh != null && waterMesh.vertexCount > 0;

        //if (hasRenderableWater)
        //{
        //    if (waterMeshFilter.sharedMesh != waterMesh)
        //        waterMeshFilter.sharedMesh = waterMesh;

        //    if (!waterRoot.activeSelf)
        //        waterRoot.SetActive(true);
        //}
        //else
        //{
        //    waterMeshFilter.sharedMesh = null;
        //
        //    if (waterRoot.activeSelf)
        //        waterRoot.SetActive(false);
        //}

        currentLOD = lod;
    }

    public void ClearMeshes()
    {
        terrainMeshFilter.sharedMesh = null;
        waterMeshFilter.sharedMesh = null;

        if (waterRoot != null)
            waterRoot.SetActive(false);

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

        ClearMeshes();
        visible = false;

        if (runtimeTerrainMaterial != null)
        {
            Object.Destroy(runtimeTerrainMaterial);
            runtimeTerrainMaterial = null;
        }

        if (runtimeWaterMaterial != null)
        {
            Object.Destroy(runtimeWaterMaterial);
            runtimeWaterMaterial = null;
        }

        if (root != null)
        {
            Object.Destroy(root);
            root = null;
        }

        waterRoot = null;
        terrainMeshFilter = null;
        terrainMeshRenderer = null;
        waterMeshFilter = null;
        waterMeshRenderer = null;
        chunkRecord = null;
        currentLOD = -1;
    }

}
