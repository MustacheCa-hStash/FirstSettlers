using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LeafClusterValidation
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Set(ChunkRecord record, string name, object value) =>
        typeof(ChunkRecord).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(record, value);

    private static ChunkRecord Record(ChunkCoord coord, int size, BiomeType biome = BiomeType.Forest,
        SurfaceType surface = SurfaceType.Grass, float slope = 0, float river = 0, float grass = 0.08f)
    {
        int length = size + 3;
        var record = new ChunkRecord(coord);
        var heights = new float[length, length]; var slopes = new float[length, length];
        var rivers = new float[length, length]; var biomes = new BiomeType[length, length];
        var surfaces = new SurfaceType[length, length];
        var plan = new WorldFeaturePlan(length, length);
        plan.ForestStructure.EnsureFloorEcologyMap(length, length);
        for (int x = 0; x < length; x++) for (int z = 0; z < length; z++)
        {
            heights[x, z] = 1; slopes[x, z] = slope; rivers[x, z] = river;
            biomes[x, z] = biome; surfaces[x, z] = surface;
            plan.ForestStructure.FloorEcologyMap[x, z] = new float4(grass, 0.1f, 0.1f, 0.3f);
        }
        Set(record, "heightMap", heights); Set(record, "slopeMap", slopes); Set(record, "riverMaskMap", rivers);
        Set(record, "biomeMap", biomes); Set(record, "surfaceTypeMap", surfaces); Set(record, "worldFeaturePlan", plan);
        return record;
    }
    private static LeafClusterGeneration Generate(ChunkRecord record, int size, LeafClusterSettings options = null, int slice = 37, float scale = 1)
    {
        var generation = new LeafClusterGeneration(record, options ?? new LeafClusterSettings(), 1937, size, scale, 10);
        generation.Step(1);
        Check(generation.Complete || generation.VisitedCells == 1, "A one-cell slice consumed the whole chunk.");
        while (!generation.Complete) generation.Step(slice);
        return generation;
    }
    [MenuItem("Tools/Terrain/Validate Forest Leaf Clusters")]
    public static void Run()
    {
        const int size = 32;
        var options = new LeafClusterSettings();
        using var record = Record(new ChunkCoord(-2, 3), size);
        var a = Generate(record, size, options); var b = Generate(record, size, options, 4096);
        var previous=Generate(record,size,new LeafClusterSettings{placementMultiplier=6});
        Check(a.Instances.Count>previous.Instances.Count*1.7f && a.Instances.Count<previous.Instances.Count*2.3f,
            "Full-density nearby litter did not roughly double its previous candidate population.");
        Check(a.Instances.Count == b.Instances.Count, "Slicing changed placement count.");
        for (int i = 0; i < a.Instances.Count; i++)
        {
            Check(a.Instances[i].position == b.Instances[i].position && a.Instances[i].rank == b.Instances[i].rank,
                "Slice boundaries changed placement.");
            Check(Math.Abs(a.Instances[i].position.y - 10.006f) < 0.0001f, "Cluster was not seated on the terrain.");
        }
        using var meadow = Record(new ChunkCoord(-2, 3), size, BiomeType.Grassland);
        using var rock = Record(new ChunkCoord(-2, 3), size, surface: SurfaceType.Rock);
        using var steep = Record(new ChunkCoord(-2, 3), size, slope: 40);
        using var water = Record(new ChunkCoord(-2, 3), size, river: 0.8f);
        using var opening = Record(new ChunkCoord(-2, 3), size, grass: 0.8f);
        Check(Generate(meadow, size).Instances.Count == 0 && Generate(rock, size).Instances.Count == 0 &&
            Generate(steep, size).Instances.Count == 0 && Generate(water, size).Instances.Count == 0,
            "Leaves escaped forest/surface/slope/water filters.");
        Check(Generate(opening, size).Instances.Count < a.Instances.Count, "Dense vegetation did not reduce litter clutter.");
        Check(a.Matches(record, options), "Fresh cache was invalid.");
        Set(record, "heightMap", new float[size + 3, size + 3]);
        Check(!a.Matches(record, options), "Replaced terrain maps retained stale instances.");
        var fresh = Generate(record, size, options);
        Check(fresh.Matches(record, options), "Fresh replacement cache was invalid.");
        options.density *= 0.5f;
        Check(!fresh.Matches(record, options), "Changed placement settings retained stale instances.");

        // Splitting the same negative-coordinate region into four chunks must preserve
        // every world-space candidate, including cells straddling chunk boundaries.
        using var wholeRecord = Record(new ChunkCoord(-1, -1), size * 2);
        var whole = Generate(wholeRecord, size * 2);
        var split = new Dictionary<uint, Vector3>();
        for (int x = -2; x < 0; x++) for (int z = -2; z < 0; z++)
        {
            using var part = Record(new ChunkCoord(x, z), size);
            foreach (var instance in Generate(part, size).Instances)
                Check(split.TryAdd(instance.rank, instance.position), "Chunk seam duplicated a candidate.");
        }
        Check(split.Count == whole.Instances.Count, "Chunk boundaries changed litter density.");
        foreach (var instance in whole.Instances)
            Check(split.TryGetValue(instance.rank, out var position) && position == instance.position, "Chunk seam moved a candidate.");

        using var blockedRecord = Record(new ChunkCoord(0, 0), size);
        blockedRecord.WorldFeaturePlan.Placements.Add(new WorldFeaturePlacement(WorldFeatureType.Boulder,
            WorldFeatureVariant.Boulder, 16, 16, Quaternion.identity, Vector3.one, 6, 6));
        foreach (var instance in Generate(blockedRecord, size).Instances)
            Check(Vector2.Distance(new Vector2(instance.position.x, instance.position.z), new Vector2(16, 16)) >= 6.4f,
                "Leaf cluster intersected a boulder footprint.");
        using var scaledRecord = Record(new ChunkCoord(0, 0), size);
        var scaled = Generate(scaledRecord, size, scale: 2);
        Check(scaled.Instances.Count > 0 && Math.Abs(scaled.Instances[0].position.y - 20.006f) < 0.0001f,
            "World scale changed mesh seating.");
        float[,] cell = { { 0, 0, 0 }, { 0, 0, 2 }, { 0, 1, 8 } };
        Check(Math.Abs(LeafClusterGeneration.SampleTerrain(cell, new Vector2(0.25f, 0.75f)) - 3) < 0.001f,
            "Height sampling did not match the terrain triangle.");
        Check(Math.Abs(LeafClusterGeneration.SampleTerrain(cell, new Vector2(0.75f, 0.25f)) - 2.5f) < 0.001f,
            "Second terrain triangle height was incorrect.");
        using var tilted = Record(new ChunkCoord(0, 0), size);
        for (int x = 0; x < size + 3; x++) for (int z = 0; z < size + 3; z++) tilted.HeightMap[x,z] = x * 0.015f + z * 0.005f;
        var tiltedLeaves = Generate(tilted, size);
        Check(tiltedLeaves.Instances.Count > 0, "Gentle slope lost all litter.");
        foreach (var leaf in tiltedLeaves.Instances)
            Check(Vector3.Angle(leaf.rotation * Vector3.up, new Vector3(-0.15f,1,-0.05f).normalized) < 0.1f,
                "Cluster did not follow the actual terrain normal.");
        using var cliff = Record(new ChunkCoord(0, 0), size);
        for (int x = 0; x < size + 3; x++) for (int z = 0; z < size + 3; z++) cliff.HeightMap[x,z] = x;
        Check(Generate(cliff, size).Instances.Count == 0, "Smoothed slope map allowed leaves on an actual cliff.");
        Plane[] planes = { new Plane(Vector3.right, 0) };
        Check(!LeafClusterSystem.InsideFrustum(new Vector3(-2, 0, 0), 0.5f, planes) &&
            LeafClusterSystem.InsideFrustum(new Vector3(-0.2f, 0, 0), 0.5f, planes), "Frustum sphere clipping failed.");
        ValidateAssets();
        Debug.Log($"LEAF CLUSTER PASS: {whole.Instances.Count} sparse clusters in a 64x64 fixture; deterministic slices, negative-coordinate seams, filters, blocker footprints, map/settings invalidation, triangle seating, world scale, frustum culling, and asset/shader settings.");
    }

    private static void ValidateAssets()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeafClusterPrefabBuilder.PrefabPath);
        Check(prefab != null && Resources.Load<GameObject>("Foliage/LeafCluster") == prefab, "Default prefab is not available to runtime builds.");
        Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        MeshRenderer renderer = prefab.GetComponent<MeshRenderer>();
        Check(mesh != null && mesh.triangles.Length / 3 == 72 && mesh.subMeshCount == 1, "Leaf mesh budget changed.");
        var scatter=new List<Vector4>();mesh.GetUVs(1,scatter);
        Check(scatter.Count==mesh.vertexCount,"Leaf mesh is missing per-leaf origin/identity data.");
        var identities=new HashSet<int>();foreach(var entry in scatter) {Check(entry.w==1,"Leaf variation metadata is invalid.");identities.Add((int)entry.z);}
        Check(identities.Count==9,"Leaves lost their independent identities.");
        var parameters=new HashSet<Vector4>();for(uint rank=0;rank<64;rank++) parameters.Add(LeafClusterSystem.ScatterParams(rank));
        Check(parameters.Count==64,"Leaf instances repeated their scatter parameters.");
        Check(mesh.bounds.size.y > 0.008f && mesh.bounds.size.y < 0.05f && mesh.bounds.size.x > .8f && mesh.bounds.size.z < 1f,
            "Leaves lost their close-range curled silhouette.");
        Check(Math.Abs(mesh.bounds.min.y) < 0.0001f, "Prefab pivot is not seated at the lowest leaf.");
        Check(prefab.GetComponentsInChildren<Collider>().Length == 0 && renderer.shadowCastingMode == ShadowCastingMode.Off,
            "Tiny litter added colliders or shadow casters.");
        Material material = renderer.sharedMaterial;
        Check(material.GetFloat("_UseAtlasColor") == 0, "Painted detail returned within leaf silhouettes.");
        GameObject far = Resources.Load<GameObject>("Foliage/LeafScatter_LOD1");
        Check(far != null && far.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3 == 16,
            "Distant leaf scatter is unavailable or too expensive.");
        Check(material != null && material.enableInstancing && material.shader.name == "FirstSettlers/Leaf Cluster Instanced",
            "Leaf material does not support the instanced rendering path.");
        Check(!ShaderUtil.ShaderHasError(material.shader), "Leaf shader failed import.");
        var importer = AssetImporter.GetAtPath(LeafClusterPrefabBuilder.TexturePath) as TextureImporter;
        Check(importer != null && importer.alphaIsTransparency && importer.mipmapEnabled && importer.mipMapsPreserveCoverage &&
            !importer.isReadable && importer.maxTextureSize == 1024, "Leaf atlas import settings are incorrect.");
        Check(material.GetTexture("_BaseMap") != null && material.FindPass("DepthOnly") >= 0 && material.FindPass("DepthNormals") >= 0,
            "Leaf texture/depth passes are missing.");
    }
    public static void BuildAndRunBatch()
    {
        try
        {
            LeafClusterPrefabBuilder.Build();
            Run();
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
