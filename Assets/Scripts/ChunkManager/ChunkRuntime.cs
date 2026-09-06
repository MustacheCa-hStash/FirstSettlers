using UnityEngine;
using UnityEngine.Rendering;

public class ChunkRuntime
{
    private ChunkRecord chunkRecord;
    private GameObject root;
    private bool visible;
    private bool renderVisible = true;
    private bool foliageRenderVisible = true;
    private bool foliageShadowCasterVisible = true;

    private MeshFilter terrainMeshFilter;
    private MeshRenderer terrainMeshRenderer;
    private Material runtimeTerrainMaterial;

    private GameObject waterRoot;
    private MeshFilter waterMeshFilter;
    private MeshRenderer waterMeshRenderer;

    private Material runtimeWaterMaterial;
    private MeshCollider terrainMeshCollider;

    private ChunkFoliageRuntime foliageRuntime;

    private int currentLOD = -1;

    public ChunkRecord ChunkRecord => chunkRecord;
    public GameObject Root => root;
    public Transform RootTransform => root != null ? root.transform : null;
    public bool IsVisible => visible;
    public bool IsRenderVisible => renderVisible;
    public bool IsFoliageRenderVisible => foliageRenderVisible;
    public bool IsFoliageShadowCasterVisible => foliageShadowCasterVisible;
    public int CurrentLOD => currentLOD;
    public ChunkFoliageRuntime FoliageRuntime {
        get => foliageRuntime;
        set => foliageRuntime = value;
    }

    public ChunkRuntime(ChunkRecord chunkRecord, int chunkSize, float worldScale, Transform parent,
        Material terrainMaterial, Material waterMaterial, bool terrainReceiveShadows)
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
        if (runtimeTerrainMaterial.HasProperty("_ReceiveShadows"))
            runtimeTerrainMaterial.SetFloat("_ReceiveShadows", terrainReceiveShadows ? 1f : 0f);

        terrainMeshRenderer.material = runtimeTerrainMaterial;
        terrainMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        terrainMeshRenderer.receiveShadows = terrainReceiveShadows;

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

    public void SetControlMaps(Texture2D[] controlMaps)
    {
        if (runtimeTerrainMaterial == null || controlMaps == null)
            return;

        if (controlMaps.Length > 0)
            runtimeTerrainMaterial.SetTexture("_ControlMap0", controlMaps[0]);

        if (controlMaps.Length > 1)
            runtimeTerrainMaterial.SetTexture("_ControlMap1", controlMaps[1]);

        if (controlMaps.Length > 2)
            runtimeTerrainMaterial.SetTexture("_ControlMap2", controlMaps[2]);
    }

    public void SetMeshes(Mesh terrainMesh, Mesh waterMesh, int lod)
    {
        if (terrainMeshFilter.sharedMesh != terrainMesh)
            terrainMeshFilter.sharedMesh = terrainMesh;

        bool hasWater = waterMesh != null && waterMesh.vertexCount > 0;
        waterMeshFilter.sharedMesh = hasWater ? waterMesh : null;
        waterRoot.SetActive(hasWater);

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
        waterMeshFilter.sharedMesh = null;

        if (waterRoot != null)
            waterRoot.SetActive(false);

        currentLOD = -1;
    }

    public bool IsShowingLOD(int lod)
    {
        return currentLOD == lod;
    }

    public void AccumulateRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        stats.VisibleChunkCount++;
        stats.AddLOD(currentLOD);

        Mesh terrainMesh = terrainMeshFilter != null ? terrainMeshFilter.sharedMesh : null;
        if (terrainMesh != null)
        {
            stats.VisibleChunkWithTerrainMeshCount++;
            stats.Terrain.AddMesh(terrainMesh);
        }

        Mesh waterMesh = waterMeshFilter != null ? waterMeshFilter.sharedMesh : null;
        if (waterRoot != null && waterRoot.activeSelf && waterMesh != null)
            stats.Water.AddMesh(waterMesh);


    }

    public void SetRenderVisible(bool visible)
    {
        if (renderVisible == visible)
            return;

        renderVisible = visible;

        if (terrainMeshRenderer != null)
            terrainMeshRenderer.enabled = visible;

        if (waterMeshRenderer != null)
            waterMeshRenderer.enabled = visible;

    }

    public void SetFoliageRenderVisible(bool visible)
    {
        if (foliageRenderVisible == visible)
            return;

        foliageRenderVisible = visible;
        foliageRuntime?.SetRenderVisible(visible);
    }

    public void SetFoliageShadowCasterVisible(bool visible)
    {
        if (foliageShadowCasterVisible == visible)
            return;

        foliageShadowCasterVisible = visible;
        foliageRuntime?.SetShadowCasterVisible(visible);
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

        waterRoot = null;
        terrainMeshFilter = null;
        terrainMeshRenderer = null;
        waterMeshFilter = null;
        waterMeshRenderer = null;
        terrainMeshCollider = null;
        chunkRecord = null;
        currentLOD = -1;
    }

}
