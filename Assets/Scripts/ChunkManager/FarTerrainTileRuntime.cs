using UnityEngine;

public class FarTerrainTileRuntime
{
    private readonly FarTerrainTileRecord record;
    private GameObject root;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material runtimeMaterial;
    private bool visible;
    private bool renderVisible = true;

    public bool IsVisible => visible;

    public FarTerrainTileRuntime(
        FarTerrainTileRecord record,
        int tileWorldChunkSize,
        float worldScale,
        Transform parent,
        Material terrainMaterial)
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
        meshRenderer.material = runtimeMaterial;

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

    public void SetMesh(Mesh mesh)
    {
        if (meshFilter != null && meshFilter.sharedMesh != mesh)
            meshFilter.sharedMesh = mesh;
    }

    public void SetRenderVisible(bool renderVisible)
    {
        if (this.renderVisible == renderVisible)
            return;

        this.renderVisible = renderVisible;

        if (meshRenderer != null)
            meshRenderer.enabled = renderVisible;
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

        meshFilter = null;
        meshRenderer = null;
    }
}
