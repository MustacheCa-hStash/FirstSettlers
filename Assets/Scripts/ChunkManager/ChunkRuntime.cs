using UnityEngine;
using UnityEngine.Rendering;

public class ChunkRuntime
{
    private ChunkRecord chunkRecord;
    private GameObject root;
    private bool visible;
    private bool renderVisible = true;
    private bool terrainHandoffHidden;
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
    public bool HasTerrainMesh => terrainMeshFilter != null && terrainMeshFilter.sharedMesh != null;
    public bool IsRenderVisible => renderVisible && !terrainHandoffHidden;
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
        CreateObjects(terrainMaterial, waterMaterial, terrainReceiveShadows);
        Reinitialize(chunkRecord, chunkSize, worldScale, parent, terrainReceiveShadows);
    }

    private void CreateObjects(Material terrainMaterial, Material waterMaterial, bool terrainReceiveShadows)
    {
        root = new GameObject("Chunk_Runtime");
        terrainMeshFilter = root.AddComponent<MeshFilter>();
        terrainMeshRenderer = root.AddComponent<MeshRenderer>();

        runtimeTerrainMaterial = new Material(terrainMaterial);
        ForestFloorMaterialOptions.DisableMissingMaps(runtimeTerrainMaterial);
        terrainMeshRenderer.material = runtimeTerrainMaterial;
        terrainMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;

        waterRoot = new GameObject("Water");
        waterRoot.transform.SetParent(root.transform, false);
        waterRoot.transform.localPosition = Vector3.zero;
        waterRoot.transform.localRotation = Quaternion.identity;
        waterRoot.transform.localScale = Vector3.one;

        waterMeshFilter = waterRoot.AddComponent<MeshFilter>();
        waterMeshRenderer = waterRoot.AddComponent<MeshRenderer>();

        runtimeWaterMaterial = new Material(waterMaterial);
        waterMeshRenderer.material = runtimeWaterMaterial;
        waterMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        waterMeshRenderer.receiveShadows = false;

        ConfigureRenderSettings(terrainReceiveShadows);
        waterRoot.SetActive(false);

        SetVisible(false);
    }

    public void Reinitialize(ChunkRecord chunkRecord, int chunkSize, float worldScale, Transform parent,
        bool terrainReceiveShadows)
    {
        this.chunkRecord = chunkRecord;

        ChunkCoord chunkCoord = chunkRecord.ChunkCoord;
        Vector3 worldPosition = new Vector3(
            (chunkCoord.x * chunkSize + chunkSize * 0.5f) * worldScale,
            0f,
            (chunkCoord.z * chunkSize + chunkSize * 0.5f) * worldScale
        );

        root.name = $"Chunk_{chunkCoord.x}_{chunkCoord.z}";
        root.transform.SetParent(parent, false);
        root.transform.position = worldPosition;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        ConfigureRenderSettings(terrainReceiveShadows);
        ResetRenderState();
        ClearMeshes();
        if (foliageRuntime != null && foliageRuntime.root != null)
        {
            foliageRuntime.root.gameObject.name = $"Foliage_{chunkCoord.x}_{chunkCoord.z}";
            foliageRuntime.root.SetParent(root.transform, false);
            foliageRuntime.root.localPosition = Vector3.zero;
            foliageRuntime.root.localRotation = Quaternion.identity;
            foliageRuntime.root.localScale = Vector3.one;
        }
        chunkRecord.SetActiveRuntime(this);
    }

    private void ConfigureRenderSettings(bool terrainReceiveShadows)
    {
        if (runtimeTerrainMaterial != null && runtimeTerrainMaterial.HasProperty("_ReceiveShadows"))
            runtimeTerrainMaterial.SetFloat("_ReceiveShadows", terrainReceiveShadows ? 1f : 0f);

        if (terrainMeshRenderer != null)
        {
            terrainMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            terrainMeshRenderer.receiveShadows = terrainReceiveShadows;
        }
    }

    private void ResetRenderState()
    {
        visible = false;
        renderVisible = true;
        terrainHandoffHidden = false;
        foliageRenderVisible = true;
        foliageShadowCasterVisible = true;
        currentLOD = -1;

        if (terrainMeshRenderer != null)
            terrainMeshRenderer.enabled = true;

        if (waterMeshRenderer != null)
            waterMeshRenderer.enabled = true;
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

        if (terrainMeshCollider)
        {
            terrainMeshCollider.sharedMesh = null;
            Object.Destroy(terrainMeshCollider);
        }

        terrainMeshCollider = null;
    }

    public bool HasCollider()
    {
        return terrainMeshCollider != null && terrainMeshCollider.sharedMesh != null;
    }

    public void ClearMeshes()
    {
        if (terrainMeshFilter)
            terrainMeshFilter.sharedMesh = null;

        if (waterMeshFilter)
            waterMeshFilter.sharedMesh = null;

        if (waterRoot)
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
            terrainMeshRenderer.enabled = visible && !terrainHandoffHidden;

        if (waterMeshRenderer != null)
            waterMeshRenderer.enabled = visible && !terrainHandoffHidden;

    }

    // Visibility refreshes cannot accidentally reveal a replacement underneath an
    // outgoing macro tile. Both terrain and water switch together at handoff.
    public void SetTerrainHandoffHidden(bool hidden)
    {
        if (terrainHandoffHidden == hidden) return;
        terrainHandoffHidden = hidden;
        if (terrainMeshRenderer != null) terrainMeshRenderer.enabled = renderVisible && !hidden;
        if (waterMeshRenderer != null) waterMeshRenderer.enabled = renderVisible && !hidden;
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
        if (root)
            root.SetActive(visible);
    }
    public void ReleaseToPool(Transform poolParent)
    {
        chunkRecord?.ClearActiveRuntime(this);

        RemoveCollider();
        ClearMeshes();
        visible = false;
        ResetControlMaps();

        if (foliageRuntime != null)
        {
            foliageRuntime.ClearCachedBatches();
            foliageRuntime.SetVisible(false);
            if (foliageRuntime.root)
                foliageRuntime.root.gameObject.name = "Foliage_Pooled";
        }

        chunkRecord = null;
        if (root)
        {
            root.name = "Chunk_Pooled";
            root.transform.SetParent(poolParent, false);
            root.SetActive(false);
        }
    }

    public void DestroyRuntime()
    {
        chunkRecord?.ClearActiveRuntime(this);

        RemoveCollider();
        ClearMeshes();
        visible = false;

        if (foliageRuntime != null)
        {
            foliageRuntime.ClearCachedBatches();
            if (foliageRuntime.root)
            {
                Object.Destroy(foliageRuntime.root.gameObject);
                foliageRuntime.root = null;
            }
            foliageRuntime = null;
        }

        if (runtimeTerrainMaterial)
        {
            Object.Destroy(runtimeTerrainMaterial);
            runtimeTerrainMaterial = null;
        }

        if (runtimeWaterMaterial)
        {
            Object.Destroy(runtimeWaterMaterial);
            runtimeWaterMaterial = null;
        }

        if (root)
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

    private void ResetControlMaps()
    {
        if (runtimeTerrainMaterial == null)
            return;

        if (runtimeTerrainMaterial.HasProperty("_ControlMap0"))
            runtimeTerrainMaterial.SetTexture("_ControlMap0", null);
        if (runtimeTerrainMaterial.HasProperty("_ControlMap1"))
            runtimeTerrainMaterial.SetTexture("_ControlMap1", null);
        if (runtimeTerrainMaterial.HasProperty("_ControlMap2"))
            runtimeTerrainMaterial.SetTexture("_ControlMap2", null);
    }

}

internal static class ForestFloorMaterialOptions
{
    public static void DisableMissingMaps(Material material)
    {
        DisableIfMissing(material, "_LeafLitterAO", "_LeafLitterAOStrength");
        DisableIfMissing(material, "_LeafLitterHeight", "_LeafLitterHeightStrength");
        DisableIfMissing(material, "_BareDirtAO", "_BareDirtAOStrength");
        DisableIfMissing(material, "_BareDirtHeight", "_BareDirtHeightStrength");
        DisableIfMissing(material, "_MossAO", "_MossAOStrength");
        DisableIfMissing(material, "_MossHeight", "_MossHeightStrength");
        DisableIfMissing(material, "_MixedForestFloorHeight", "_MixedForestFloorHeightStrength");
        DisableIfMissing(material, "_DenseMossAO", "_DenseMossAOStrength");
        DisableIfMissing(material, "_DenseMossHeight", "_DenseMossHeightStrength");
    }

    private static void DisableIfMissing(Material material, string textureProperty, string strengthProperty)
    {
        if (material.HasProperty(textureProperty) && material.HasProperty(strengthProperty) &&
            material.GetTexture(textureProperty) == null)
            material.SetFloat(strengthProperty, 0f);
    }
}
