using UnityEngine;
using UnityEngine.Rendering;

public class FarTerrainTileRuntime
{
    private readonly FarTerrainTileRecord record;
    private GameObject root;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material runtimeMaterial;
    private MeshFilter waterMeshFilter;
    private MeshRenderer waterMeshRenderer;
    private bool visible;
    private bool renderVisible = true;

    public bool IsVisible => visible;

    public FarTerrainTileRuntime(
        FarTerrainTileRecord record,
        int tileWorldChunkSize,
        float worldScale,
        Transform parent,
        Material terrainMaterial,
        bool terrainReceiveShadows, Material waterMaterial = null)
    {
        this.record = record;
        ChunkCoord tileCoord = record.TileCoord;
        Vector3 worldPosition = new Vector3(
            (tileCoord.x * tileWorldChunkSize + tileWorldChunkSize * 0.5f) * worldScale,
            0f,
            (tileCoord.z * tileWorldChunkSize + tileWorldChunkSize * 0.5f) * worldScale);

        root = new GameObject($"FarTile_{tileCoord.x}_{tileCoord.z}");
        root.transform.position = worldPosition;
        root.transform.parent = parent;

        meshFilter = root.AddComponent<MeshFilter>();
        meshRenderer = root.AddComponent<MeshRenderer>();
        runtimeMaterial = new Material(terrainMaterial);
        if (runtimeMaterial.HasProperty("_ReceiveShadows"))
            runtimeMaterial.SetFloat("_ReceiveShadows", terrainReceiveShadows ? 1f : 0f);

        meshRenderer.material = runtimeMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = terrainReceiveShadows;

        var waterRoot = new GameObject("Water");
        waterRoot.transform.SetParent(root.transform, false);
        waterMeshFilter = waterRoot.AddComponent<MeshFilter>();
        waterMeshRenderer = waterRoot.AddComponent<MeshRenderer>();
        waterMeshRenderer.sharedMaterial = waterMaterial;
        waterMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        waterMeshRenderer.receiveShadows = false;
        waterRoot.SetActive(false);

        SetVisible(false);
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
        if (meshFilter != null && meshFilter.sharedMesh != mesh)
            meshFilter.sharedMesh = mesh;
        waterMeshFilter.sharedMesh = waterMesh;
        waterMeshFilter.gameObject.SetActive(waterMesh != null && waterMesh.vertexCount > 0);
    }

    public void SetRenderVisible(bool renderVisible)
    {
        if (this.renderVisible == renderVisible)
            return;

        this.renderVisible = renderVisible;

        if (meshRenderer != null)
            meshRenderer.enabled = renderVisible;
        if (waterMeshRenderer != null)
            waterMeshRenderer.enabled = renderVisible;
    }

    public void SetVisible(bool nextVisible)
    {
        visible = nextVisible;

        if (root != null)
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

        waterMeshFilter = null;
        waterMeshRenderer = null;
        meshFilter = null;
        meshRenderer = null;
    }
}
