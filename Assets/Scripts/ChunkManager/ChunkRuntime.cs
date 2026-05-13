using UnityEngine;

public class ChunkRuntime
{
    private ChunkRecord chunkRecord;
    private GameObject root;
    private bool visible;

    private MeshFilter terrainMeshFilter;
    private MeshRenderer terrainMeshRenderer;
    private Material runtimeTerrainMaterial;

    private GameObject lakeRoot;
    private MeshFilter lakeMeshFilter;
    private MeshRenderer lakeMeshRenderer;

    private GameObject riverRoot;
    private MeshFilter riverMeshFilter;
    private MeshRenderer riverMeshRenderer;

    private Material runtimeWaterMaterial;
    private MeshCollider terrainMeshCollider;

    private ChunkFoliageRuntime foliageRuntime;

    private int currentLOD = -1;

    public ChunkRecord ChunkRecord => chunkRecord;
    public GameObject Root => root;
    public Transform RootTransform => root != null ? root.transform : null;
    public bool IsVisible => visible;
    public int CurrentLOD => currentLOD;
    public ChunkFoliageRuntime FoliageRuntime {
        get => foliageRuntime;
        set => foliageRuntime = value;
    }

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

        lakeRoot = new GameObject("Lake");
        lakeRoot.transform.SetParent(root.transform, false);
        lakeRoot.transform.localPosition = Vector3.zero;
        lakeRoot.transform.localRotation = Quaternion.identity;
        lakeRoot.transform.localScale = Vector3.one;

        lakeMeshFilter = lakeRoot.AddComponent<MeshFilter>();
        lakeMeshRenderer = lakeRoot.AddComponent<MeshRenderer>();

        riverRoot = new GameObject("River");
        riverRoot.transform.SetParent(root.transform, false);
        riverRoot.transform.localPosition = Vector3.zero;
        riverRoot.transform.localRotation = Quaternion.identity;
        riverRoot.transform.localScale = Vector3.one;

        riverMeshFilter = riverRoot.AddComponent<MeshFilter>();
        riverMeshRenderer = riverRoot.AddComponent<MeshRenderer>();

        runtimeWaterMaterial = new Material(waterMaterial);
        lakeMeshRenderer.material = runtimeWaterMaterial;
        riverMeshRenderer.material = runtimeWaterMaterial;

        lakeRoot.SetActive(false);
        riverRoot.SetActive(false);

        SetVisible(false);
        chunkRecord.SetActiveRuntime(this);
    }

    public void SetControlMaps(Texture2D[] controlMaps)
    {
        if (runtimeTerrainMaterial == null || controlMaps == null)
            return;

        if (controlMaps.Length > 0)
            runtimeTerrainMaterial.SetTexture("_ControlMap0", controlMaps[0]);

        if (controlMaps.Length > 1)
            runtimeTerrainMaterial.SetTexture("_ControlMap1", controlMaps[1]);
    }

    public void SetMeshes(Mesh terrainMesh, Mesh lakeMesh, Mesh riverMesh, int lod)
    {
        if (terrainMeshFilter.sharedMesh != terrainMesh)
            terrainMeshFilter.sharedMesh = terrainMesh;

        bool hasRenderableLake = lakeMesh != null && lakeMesh.vertexCount > 0;
        bool hasRenderableRiver = riverMesh != null && riverMesh.vertexCount > 0;

        if (hasRenderableLake)
        {
            if (lakeMeshFilter.sharedMesh != lakeMesh)
                lakeMeshFilter.sharedMesh = lakeMesh;

            if (!lakeRoot.activeSelf)
                lakeRoot.SetActive(true);
        }
        else
        {
            lakeMeshFilter.sharedMesh = null;

            if (lakeRoot.activeSelf)
                lakeRoot.SetActive(false);
        }

        if (hasRenderableRiver)
        {
            if (riverMeshFilter.sharedMesh != riverMesh)
                riverMeshFilter.sharedMesh = riverMesh;

            if (!riverRoot.activeSelf)
                riverRoot.SetActive(true);
        }
        else
        {
            riverMeshFilter.sharedMesh = null;
        
            if (riverRoot.activeSelf)
                riverRoot.SetActive(false);
        }

        currentLOD = lod;
    }

    public void ApplyCollider(Mesh colliderMesh)
    {
        if (terrainMeshCollider == null)
            terrainMeshCollider = root.AddComponent<MeshCollider>();

        if (terrainMeshCollider.sharedMesh != null)
            terrainMeshCollider.sharedMesh = null;

        terrainMeshCollider.sharedMesh = colliderMesh;
    }

    public void RemoveCollider()
    {
        if (terrainMeshCollider == null)
            return;

        terrainMeshCollider.sharedMesh = null;
        Object.Destroy(terrainMeshCollider);
        terrainMeshCollider = null;
    }

    public bool HasCollider()
    {
        return terrainMeshCollider != null && terrainMeshCollider.sharedMesh != null;
    }

    public void ClearMeshes()
    {
        terrainMeshFilter.sharedMesh = null;
        lakeMeshFilter.sharedMesh = null;
        riverMeshFilter.sharedMesh = null;

        if (lakeRoot != null)
            lakeRoot.SetActive(false);

        if (riverRoot != null)
            riverRoot.SetActive(false);

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

        RemoveCollider();
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

        lakeRoot = null;
        riverRoot = null;
        terrainMeshFilter = null;
        terrainMeshRenderer = null;
        lakeMeshFilter = null;
        lakeMeshRenderer = null;
        riverMeshFilter = null;
        riverMeshRenderer = null;
        terrainMeshCollider = null;
        chunkRecord = null;
        currentLOD = -1;
    }

}
