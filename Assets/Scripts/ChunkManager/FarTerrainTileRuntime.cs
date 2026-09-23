using UnityEngine;
using UnityEngine.Rendering;

public class FarTerrainTileRuntime
{
    private FarTerrainTileRecord record;
    private GameObject root;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material runtimeMaterial;
    private GameObject waterRoot;
    private MeshFilter waterMeshFilter;
    private MeshRenderer waterMeshRenderer;
    private bool visible;
    private bool renderVisible = true;

    public bool IsVisible => visible;
    public bool HasTerrainMesh => meshFilter != null && meshFilter.sharedMesh != null;

    public FarTerrainTileRuntime(
        FarTerrainTileRecord record,
        int tileWorldChunkSize,
        float worldScale,
        Transform parent,
        Material terrainMaterial,
        bool terrainReceiveShadows, Material waterMaterial = null)
    {
        CreateObjects(terrainMaterial, terrainReceiveShadows, waterMaterial);
        Reinitialize(record, tileWorldChunkSize, worldScale, parent, terrainReceiveShadows);
    }

    private void CreateObjects(Material terrainMaterial, bool terrainReceiveShadows, Material waterMaterial)
    {
        root = new GameObject("FarTile_Runtime");
        meshFilter = root.AddComponent<MeshFilter>();
        meshRenderer = root.AddComponent<MeshRenderer>();
        runtimeMaterial = new Material(terrainMaterial);
        ForestFloorMaterialOptions.DisableMissingMaps(runtimeMaterial);
        meshRenderer.material = runtimeMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;

        waterRoot = new GameObject("Water");
        waterRoot.transform.SetParent(root.transform, false);
        waterMeshFilter = waterRoot.AddComponent<MeshFilter>();
        waterMeshRenderer = waterRoot.AddComponent<MeshRenderer>();
        waterMeshRenderer.sharedMaterial = waterMaterial;
        waterMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        waterMeshRenderer.receiveShadows = false;
        waterRoot.SetActive(false);

        ConfigureRenderSettings(terrainReceiveShadows);
        SetVisible(false);
    }

    public void Reinitialize(
        FarTerrainTileRecord record,
        int tileWorldChunkSize,
        float worldScale,
        Transform parent,
        bool terrainReceiveShadows)
    {
        this.record = record;
        ChunkCoord tileCoord = record.TileCoord;
        Vector3 worldPosition = new Vector3(
            (tileCoord.x * tileWorldChunkSize + tileWorldChunkSize * 0.5f) * worldScale,
            0f,
            (tileCoord.z * tileWorldChunkSize + tileWorldChunkSize * 0.5f) * worldScale);

        root.name = $"FarPatch_{record.SizeInChunks}_{tileCoord.x}_{tileCoord.z}";
        root.transform.SetParent(parent, false);
        root.transform.position = worldPosition;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        ConfigureRenderSettings(terrainReceiveShadows);
        visible = false;
        renderVisible = false;
        SetMesh(null, null);
        SetRenderVisible(true);
    }

    private void ConfigureRenderSettings(bool terrainReceiveShadows)
    {
        if (runtimeMaterial != null && runtimeMaterial.HasProperty("_ReceiveShadows"))
            runtimeMaterial.SetFloat("_ReceiveShadows", terrainReceiveShadows ? 1f : 0f);

        if (meshRenderer != null)
        {
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = terrainReceiveShadows;
        }
    }

    public void SetControlMaps(Texture2D[] controlMaps)
    {
        if (runtimeMaterial == null || controlMaps == null)
            return;

        if (controlMaps.Length > 0)
            runtimeMaterial.SetTexture("_ControlMap0", controlMaps[0]);

        if (controlMaps.Length > 1)
            runtimeMaterial.SetTexture("_ControlMap1", controlMaps[1]);

        if (controlMaps.Length > 2)
            runtimeMaterial.SetTexture("_ControlMap2", controlMaps[2]);
    }

    public void SetMesh(Mesh mesh, Mesh waterMesh = null)
    {
        if (meshFilter && meshFilter.sharedMesh != mesh)
            meshFilter.sharedMesh = mesh;
        if (waterMeshFilter)
        {
            waterMeshFilter.sharedMesh = waterMesh;
            waterMeshFilter.gameObject.SetActive(waterMesh != null && waterMesh.vertexCount > 0);
        }
    }

    public void SetRenderVisible(bool renderVisible)
    {
        if (this.renderVisible == renderVisible)
            return;

        this.renderVisible = renderVisible;

        if (meshRenderer)
            meshRenderer.enabled = renderVisible;
        if (waterMeshRenderer)
            waterMeshRenderer.enabled = renderVisible;
    }

    public void SetVisible(bool nextVisible)
    {
        visible = nextVisible;

        if (root)
            root.SetActive(nextVisible);
    }

    public bool IsShowingMesh(Mesh mesh)
    {
        return meshFilter != null && meshFilter.sharedMesh == mesh;
    }

    public void AccumulateRenderStats(ref WorldRenderStatsDebugInfo stats)
    {
        if (!visible || !renderVisible)
            return;

        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        stats.VisibleChunkCount++;
        stats.VisibleChunkWithTerrainMeshCount++;
        stats.AddLOD(5);
        stats.Terrain.AddMesh(meshFilter.sharedMesh);
        if (waterMeshFilter.sharedMesh != null && waterMeshFilter.gameObject.activeSelf)
            stats.Water.AddMesh(waterMeshFilter.sharedMesh);
    }

    public void DestroyRuntime()
    {
        visible = false;

        SetMesh(null, null);

        if (runtimeMaterial)
        {
            Object.Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }

        if (root)
        {
            Object.Destroy(root);
            root = null;
        }

        waterMeshFilter = null;
        waterMeshRenderer = null;
        waterRoot = null;
        meshFilter = null;
        meshRenderer = null;
        record = null;
    }

    public void ReleaseToPool(Transform poolParent)
    {
        visible = false;
        renderVisible = false;
        SetMesh(null, null);
        SetRenderVisible(true);
        ResetControlMaps();

        if (root)
        {
            root.name = "FarTile_Pooled";
            root.transform.SetParent(poolParent, false);
            root.SetActive(false);
        }

        record = null;
    }

    private void ResetControlMaps()
    {
        if (runtimeMaterial == null)
            return;

        if (runtimeMaterial.HasProperty("_ControlMap0"))
            runtimeMaterial.SetTexture("_ControlMap0", null);
        if (runtimeMaterial.HasProperty("_ControlMap1"))
            runtimeMaterial.SetTexture("_ControlMap1", null);
        if (runtimeMaterial.HasProperty("_ControlMap2"))
            runtimeMaterial.SetTexture("_ControlMap2", null);
    }
}
