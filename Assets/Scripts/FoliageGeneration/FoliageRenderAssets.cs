using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Resolves authored mesh/material bindings and applies them to a newly created ground runtime.</summary>
public sealed class FoliageRenderAssets
{
    private readonly GrassSettings grassSettings;
    private readonly FlowerSettings flowerSettings;
    private readonly LilyPadSettings lilyPadSettings;
    private readonly CattailSettings cattailSettings;
    private readonly CloverSettings cloverSettings;
    private readonly DandelionSettings dandelionSettings;
    private readonly TreeSettings treeSettings;
    public FoliageRenderAssets(WorldFoliageConfiguration settings)
    {
        grassSettings = settings.Grass; flowerSettings = settings.Flowers; lilyPadSettings = settings.LilyPads;
        cattailSettings = settings.Cattails; cloverSettings = settings.Clover; dandelionSettings = settings.Dandelions; treeSettings = settings.Trees;
        ResolveGrassRenderAssets(); ResolveFlowerRenderAssets(); ResolveLilyPadRenderAssets(); ResolveCattailRenderAssets();
        ResolveCloverRenderAssets(); ResolveDandelionRenderAssets(); ResolveTreeRenderAssets();
    }
    public void ApplyTo(ChunkFoliageRuntime runtime)
    {
        runtime.flowerMesh = flowerMesh;
        runtime.flowerMaterial = flowerMaterial;
        runtime.tallFlowerMesh = tallFlowerMesh;
        runtime.tallFlowerMaterial = tallFlowerMaterial;
        runtime.daisyWeedMesh = daisyWeedMesh;
        runtime.daisyWeedMaterial = daisyWeedMaterial;
        runtime.flowerPetalColorPropertyId = flowerPetalColorPropertyId;
        runtime.lilyPadMesh = lilyPadMesh;
        runtime.lilyPadMaterial = lilyPadMaterial;
        runtime.cattailMesh = cattailMesh;
        runtime.cattailMaterial = cattailMaterial;

        runtime.cloverRenderData = cloverRenderData;
        runtime.receiveCloverShadows = cloverSettings != null && cloverSettings.receiveCloverShadows;
        runtime.cloverInstanceDataPropertyId = cloverInstanceDataPropertyId;

        runtime.dandelionMesh = dandelionMesh;
        runtime.dandelionMaterial = dandelionMaterial;
        runtime.receiveDandelionShadows = dandelionSettings != null && dandelionSettings.receiveDandelionShadows;
        runtime.dandelionInstanceDataPropertyId = dandelionInstanceDataPropertyId;

        if (treeSettings != null)
        {
            runtime.blueberryBushPrefab = treeSettings.blueberryBushPrefab;
            runtime.raspberryBushPrefab = treeSettings.raspberryBushPrefab;
            runtime.strawberryBushPrefab = treeSettings.strawberryBushPrefab;
            runtime.blackberryBushPrefab = treeSettings.blackberryBushPrefab;
            runtime.fallbackBushPrefab = treeSettings.fallbackBushPrefab;
            runtime.forestRockPrefabs = treeSettings.forestRockPrefabs;
            runtime.forestRockFallbackPrefab = treeSettings.forestRockFallbackPrefab;
            runtime.grasslandRockPrefabs = treeSettings.grasslandRockPrefabs;
            runtime.grasslandRockFallbackPrefab = treeSettings.grasslandRockFallbackPrefab;
            runtime.grasslandLargeRockPrefabs = treeSettings.grasslandLargeRockPrefabs;
            runtime.grasslandLargeRockFallbackPrefab = treeSettings.grasslandLargeRockFallbackPrefab;

        }
    }
    private Mesh grassMesh;
    private Mesh forestGrassMesh, forestFarGrassMesh;
    private Material forestGrassMaterial, forestFarGrassMaterial;
    private Material grassMaterial;
    private Mesh billboardGrassMesh;
    private Material billboardGrassMaterial;
    private Mesh flowerMesh;
    private Material flowerMaterial;
    private Mesh tallFlowerMesh;
    private Material tallFlowerMaterial;
    private Mesh daisyWeedMesh;
    private Material daisyWeedMaterial;
    private int flowerPetalColorPropertyId;
    private Mesh lilyPadMesh;
    private Material lilyPadMaterial;
    private Matrix4x4 lilyPadMeshLocalMatrix = Matrix4x4.identity;
    private Mesh cattailMesh;
    private Material cattailMaterial;
    private Matrix4x4 cattailMeshLocalMatrix = Matrix4x4.identity;
    private CloverRenderData[] cloverRenderData;
    private int cloverInstanceDataPropertyId;
    private float cloverMeshRadius;
    private Mesh dandelionMesh;
    private Material dandelionMaterial;
    private int dandelionInstanceDataPropertyId;
    public Mesh GrassMesh => grassMesh;
    public Mesh ForestGrassMesh => forestGrassMesh;
    public Mesh ForestFarGrassMesh => forestFarGrassMesh;
    public Material ForestGrassMaterial => forestGrassMaterial;
    public Material ForestFarGrassMaterial => forestFarGrassMaterial;
    public Material GrassMaterial => grassMaterial;
    public Mesh BillboardGrassMesh => billboardGrassMesh;
    public Material BillboardGrassMaterial => billboardGrassMaterial;
    public Mesh FlowerMesh => flowerMesh;
    public Material FlowerMaterial => flowerMaterial;
    public Mesh TallFlowerMesh => tallFlowerMesh;
    public Material TallFlowerMaterial => tallFlowerMaterial;
    public Mesh DaisyWeedMesh => daisyWeedMesh;
    public Material DaisyWeedMaterial => daisyWeedMaterial;
    public int FlowerPetalColorPropertyId => flowerPetalColorPropertyId;
    public Mesh LilyPadMesh => lilyPadMesh;
    public Material LilyPadMaterial => lilyPadMaterial;
    public Matrix4x4 LilyPadMeshLocalMatrix => lilyPadMeshLocalMatrix;
    public Mesh CattailMesh => cattailMesh;
    public Material CattailMaterial => cattailMaterial;
    public Matrix4x4 CattailMeshLocalMatrix => cattailMeshLocalMatrix;
    public CloverRenderData[] CloverRenderData => cloverRenderData;
    public int CloverInstanceDataPropertyId => cloverInstanceDataPropertyId;
    public float CloverMeshRadius => cloverMeshRadius;
    public Mesh DandelionMesh => dandelionMesh;
    public Material DandelionMaterial => dandelionMaterial;
    public int DandelionInstanceDataPropertyId => dandelionInstanceDataPropertyId;

    private void ResolveGrassRenderAssets()
    {
        ResolveForestGrassAsset(grassSettings.forestGrassPrefab, "Foliage/ForestGrassTuft_LOD0", out forestGrassMesh, out forestGrassMaterial);
        ResolveForestGrassAsset(grassSettings.forestBillboardGrassPrefab, "Foliage/ForestGrassTuft_LOD1", out forestFarGrassMesh, out forestFarGrassMaterial);

        if (grassSettings.grassPrefab == null)
        {
            Debug.LogError("Grass prefab is missing.");
        }
        else
        {
            MeshFilter meshFilter = grassSettings.grassPrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = grassSettings.grassPrefab.GetComponentInChildren<MeshRenderer>();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("Grass prefab missing MeshFilter or mesh.");
            }
            else
            {
                grassMesh = meshFilter.sharedMesh;
            }

            if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                Debug.LogError("Grass prefab missing MeshRenderer or material.");
            }
            else
            {
                grassMaterial = meshRenderer.sharedMaterial;
                grassMaterial.enableInstancing = true;
            }
        }

        if (grassSettings.billboardGrassPrefab == null)
        {
            Debug.LogError("Billboard grass prefab is missing.");
        }
        else
        {
            MeshFilter meshFilter = grassSettings.billboardGrassPrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = grassSettings.billboardGrassPrefab.GetComponentInChildren<MeshRenderer>();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("Billboard grass prefab missing MeshFilter or mesh.");
            }
            else
            {
                billboardGrassMesh = meshFilter.sharedMesh;
            }

            if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                Debug.LogError("Billboard grass prefab missing MeshRenderer or material.");
            }
            else
            {
                billboardGrassMaterial = meshRenderer.sharedMaterial;
                billboardGrassMaterial.enableInstancing = true;
            }
        }
    }

    public static void ResolveForestGrassAsset(GameObject prefab, string resource, out Mesh mesh, out Material material)
    {
        prefab = prefab != null ? prefab : Resources.Load<GameObject>(resource);
        mesh = prefab != null ? prefab.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
        material = prefab != null ? prefab.GetComponentInChildren<MeshRenderer>()?.sharedMaterial : null;
        if (mesh == null || material == null)
            Debug.LogWarning($"Forest grass asset {resource} is incomplete; using the standard grass asset for that distance.");
        else material.enableInstancing = true;
    }

    private void ResolveFlowerRenderAssets()
    {
        if (!IsFlowerSystemEnabled())
            return;

        string petalColorPropertyName = string.IsNullOrEmpty(flowerSettings.flowerPetalColorPropertyName)
            ? "_FlowerPetalColor"
            : flowerSettings.flowerPetalColorPropertyName;

        flowerPetalColorPropertyId = Shader.PropertyToID(petalColorPropertyName);

        if (flowerSettings.flowerPrefab == null && flowerSettings.tallFlowerPrefab == null && flowerSettings.daisyWeedPrefab == null)
        {
            Debug.LogWarning("Flower prefabs are missing. Flowers will not render until one is assigned.");
            return;
        }

        if (flowerSettings.flowerPrefab != null)
        {
            MeshFilter meshFilter = flowerSettings.flowerPrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = flowerSettings.flowerPrefab.GetComponentInChildren<MeshRenderer>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                Debug.LogError("Flower prefab missing MeshFilter or mesh.");
            else
                flowerMesh = meshFilter.sharedMesh;

            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                flowerMaterial = meshRenderer.sharedMaterial;
                flowerMaterial.enableInstancing = true;
            }
            else
                Debug.LogError("Flower prefab missing MeshRenderer or material.");
        }

        if (flowerSettings.tallFlowerPrefab != null)
        {
            MeshFilter meshFilter = flowerSettings.tallFlowerPrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = flowerSettings.tallFlowerPrefab.GetComponentInChildren<MeshRenderer>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                Debug.LogError("Tall flower prefab missing MeshFilter or mesh.");
            else
                tallFlowerMesh = meshFilter.sharedMesh;

            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                tallFlowerMaterial = meshRenderer.sharedMaterial;
                tallFlowerMaterial.enableInstancing = true;
            }
            else
                Debug.LogError("Tall flower prefab missing MeshRenderer or material.");
        }
    }

    private void ResolveLilyPadRenderAssets()
    {
        if (lilyPadSettings == null || lilyPadSettings.lilyPadPrefab == null)
            return;

        MeshFilter filter = lilyPadSettings.lilyPadPrefab.GetComponentInChildren<MeshFilter>();
        MeshRenderer renderer = lilyPadSettings.lilyPadPrefab.GetComponentInChildren<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || renderer == null || renderer.sharedMaterial == null)
        {
            Debug.LogError("Lily pad prefab needs a MeshFilter and MeshRenderer with a material.");
            return;
        }

        lilyPadMesh = filter.sharedMesh;
        lilyPadMaterial = renderer.sharedMaterial;
        lilyPadMeshLocalMatrix = lilyPadSettings.lilyPadPrefab.transform.worldToLocalMatrix *
            filter.transform.localToWorldMatrix;
        lilyPadMaterial.enableInstancing = true;
    }

    private void ResolveCattailRenderAssets()
    {
        if (cattailSettings == null || cattailSettings.cattailPrefab == null)
            return;

        MeshFilter filter = cattailSettings.cattailPrefab.GetComponentInChildren<MeshFilter>();
        MeshRenderer renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
        if (filter == null || filter.sharedMesh == null || renderer == null || renderer.sharedMaterial == null)
        {
            Debug.LogError("Cattail prefab needs one child with a MeshFilter and MeshRenderer with a material.");
            return;
        }
        if (filter.sharedMesh.subMeshCount != 1 || renderer.sharedMaterials.Length != 1)
        {
            Debug.LogError("Cattail clump needs one combined mesh and one material for instanced rendering.");
            return;
        }

        cattailMesh = filter.sharedMesh;
        cattailMaterial = renderer.sharedMaterial;
        cattailMeshLocalMatrix = cattailSettings.cattailPrefab.transform.worldToLocalMatrix *
            filter.transform.localToWorldMatrix;
        cattailMaterial.enableInstancing = true;
    }

    private void ResolveCloverRenderAssets()
    {
        string instanceDataPropertyName = cloverSettings == null ||
                                          string.IsNullOrEmpty(cloverSettings.cloverInstanceDataPropertyName)
            ? "_CloverInstanceData"
            : cloverSettings.cloverInstanceDataPropertyName;

        cloverInstanceDataPropertyId = Shader.PropertyToID(instanceDataPropertyName);

        if (!IsCloverSystemEnabled())
        {
            cloverRenderData = Array.Empty<CloverRenderData>();
            return;
        }

        List<GameObject> prefabs = new List<GameObject>();
        if (cloverSettings.cloverClumpPrefabs != null)
        {
            for (int i = 0; i < cloverSettings.cloverClumpPrefabs.Length; i++)
            {
                if (cloverSettings.cloverClumpPrefabs[i] != null)
                    prefabs.Add(cloverSettings.cloverClumpPrefabs[i]);
            }
        }

        if (flowerSettings.daisyWeedPrefab != null)
        {
            MeshFilter meshFilter = flowerSettings.daisyWeedPrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = flowerSettings.daisyWeedPrefab.GetComponentInChildren<MeshRenderer>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                Debug.LogError("Daisy weed prefab missing MeshFilter or mesh.");
            else
                daisyWeedMesh = meshFilter.sharedMesh;

            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                daisyWeedMaterial = meshRenderer.sharedMaterial;
                daisyWeedMaterial.enableInstancing = true;
            }
            else
                Debug.LogError("Daisy weed prefab missing MeshRenderer or material.");
        }

        if (prefabs.Count == 0 && cloverSettings.cloverClumpPrefab != null)
            prefabs.Add(cloverSettings.cloverClumpPrefab);

        if (prefabs.Count == 0)
        {
            Debug.LogWarning("Clover is enabled but no clover clump prefab is assigned.");
            cloverRenderData = Array.Empty<CloverRenderData>();
            return;
        }

        cloverRenderData = new CloverRenderData[prefabs.Count];
        cloverMeshRadius=0;
        for (int i = 0; i < prefabs.Count; i++)
        {
            cloverRenderData[i] = ResolveCloverRenderData(prefabs[i], $"clover clump prefab {i}");
            var mesh=cloverRenderData[i].mesh;
            if(mesh!=null)cloverMeshRadius=Mathf.Max(cloverMeshRadius,mesh.bounds.extents.magnitude+mesh.bounds.center.magnitude);
        }
    }

    private CloverRenderData ResolveCloverRenderData(GameObject prefab, string label)
    {
        if (prefab == null)
            return new CloverRenderData(null, null);

        MeshFilter meshFilter = prefab.GetComponentInChildren<MeshFilter>();
        MeshRenderer meshRenderer = prefab.GetComponentInChildren<MeshRenderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError($"{label} must have a MeshFilter with a mesh.");
            return new CloverRenderData(null, null);
        }

        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            Debug.LogError($"{label} must have a MeshRenderer with one shared material.");
            return new CloverRenderData(null, null);
        }

        meshRenderer.sharedMaterial.enableInstancing = true;
        return new CloverRenderData(meshFilter.sharedMesh, meshRenderer.sharedMaterial);
    }

    private void ResolveDandelionRenderAssets()
    {
        string instanceDataPropertyName = dandelionSettings == null ||
                                          string.IsNullOrEmpty(dandelionSettings.dandelionInstanceDataPropertyName)
            ? "_DandelionInstanceData"
            : dandelionSettings.dandelionInstanceDataPropertyName;

        dandelionInstanceDataPropertyId = Shader.PropertyToID(instanceDataPropertyName);

        if (!IsDandelionSystemEnabled())
            return;

        if (dandelionSettings.dandelionPrefab == null)
        {
            Debug.LogWarning("Dandelions are enabled but no dandelion prefab is assigned.");
            return;
        }

        CloverRenderData renderData = ResolveCloverRenderData(dandelionSettings.dandelionPrefab, "dandelion prefab");
        dandelionMesh = renderData.mesh;
        dandelionMaterial = renderData.material;
    }

    private void ResolveTreeRenderAssets()
    {
        if (treeSettings == null) return;
        foreach (var species in TreeSpeciesCatalog.All)
            if (species.NearPrefab(treeSettings) == null)
                Debug.LogWarning(species.DisplayName + " prefab is missing and no habitat fallback is assigned.");
        WarnMissingBushPrefab(treeSettings.blueberryBushPrefab, "Blueberry");
        WarnMissingBushPrefab(treeSettings.raspberryBushPrefab, "Raspberry");
        WarnMissingBushPrefab(treeSettings.strawberryBushPrefab, "Strawberry");
        WarnMissingBushPrefab(treeSettings.blackberryBushPrefab, "Blackberry");
    }

    private void WarnMissingBushPrefab(GameObject prefab, string label)
    {
        if (prefab == null && treeSettings.fallbackBushPrefab == null)
        {
            Debug.LogWarning($"{label} bush prefab is missing and no fallback bush prefab is assigned.");
        }
    }

    private bool IsFlowerSystemEnabled()
    {
        return flowerSettings != null && flowerSettings.enableFlowers;
    }

    private bool IsLilyPadSystemEnabled()
    {
        return lilyPadSettings != null && lilyPadSettings.enableLilyPads;
    }

    private bool IsCattailSystemEnabled()
    {
        return cattailSettings != null && cattailSettings.enableCattails;
    }

    private bool IsCloverSystemEnabled()
    {
        return cloverSettings != null && cloverSettings.enableClover;
    }

    private bool IsDandelionSystemEnabled()
    {
        return dandelionSettings != null && dandelionSettings.enableDandelions;
    }

    public bool HasFlowerRenderAssets()
    {
        return (flowerMesh != null && flowerMaterial != null) ||
               (tallFlowerMesh != null && tallFlowerMaterial != null) ||
               (daisyWeedMesh != null && daisyWeedMaterial != null);
    }

    public bool HasLilyPadRenderAssets()
    {
        return lilyPadMesh != null && lilyPadMaterial != null;
    }

    public bool HasCattailRenderAssets()
    {
        return cattailMesh != null && cattailMaterial != null;
    }

    public bool HasCloverRenderAssets()
    {
        if (cloverRenderData == null)
            return false;

        for (int i = 0; i < cloverRenderData.Length; i++)
        {
            if (cloverRenderData[i].mesh != null && cloverRenderData[i].material != null)
                return true;
        }

        return false;
    }

    public bool HasDandelionRenderAssets()
    {
        return dandelionMesh != null && dandelionMaterial != null;
    }

    public int GetCloverRenderAssetCount()
    {
        return cloverRenderData != null ? cloverRenderData.Length : 0;
    }
}
