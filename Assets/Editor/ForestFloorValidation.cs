using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Burst;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public static class ForestFloorValidation
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [MenuItem("Tools/Terrain/Validate Forest Floor")]
    public static void Run()
    {
        ValidatePolicy();
        ValidateRockEdges();
        ValidateFieldsAndMaterials();
        ValidatePlacement();
        ValidateFarGeneration();
        ValidateLitterMaterial();
        Debug.Log("FOREST FLOOR PASS: continuous bounded density, opening/dry/slope/water response, " +
            "X/Z seams, detailed/far policy agreement, litter beneath grass, deterministic near/far placement, " +
            "zero/full density, and grassland preservation.");
    }

    private static void ValidatePolicy()
    {
        double interior = 0, opening = 0, openingOutsideMoss=0;
        int openingSamples=0;
        for (int i = 0; i < 4096; i++)
        {
            float2 position = new float2(i % 64 * 3.1f - 70, i / 64 * 2.7f - 100);
            float4 closed = ForestFloorPolicy.Evaluate(position, 1937, 0.72f, 4, 0, 0.8f, 0);
            float4 gap = ForestFloorPolicy.Evaluate(position, 1937, 0.72f, 4, 0, 0.05f, 1);
            Check(math.all(math.isfinite(closed)) && math.all(closed >= 0) && math.all(closed <= 1), "Invalid ecology sample.");
            Check(gap.x >= closed.x, "Openings did not support more grass outside moss exclusions.");
            Check(ForestFloorPolicy.Evaluate(position, 1937, 0.1f, 4, 0).x == 0, "Dry ground retained grass.");
            Check(ForestFloorPolicy.Evaluate(position, 1937, 0.72f, 50, 0).x == 0, "Steep ground retained grass.");
            Check(ForestFloorPolicy.Evaluate(position, 1937, 0.72f, 4, 0.8f).x == 0, "River retained grass.");
            float4 nearby = ForestFloorPolicy.Evaluate(position + new float2(0.01f, 0), 1937, 0.72f, 4, 0, 0.8f, 0);
            var delta=math.abs(closed-nearby);
            Check(math.cmax(delta) < 0.005f, "Substrate or grass habitat field contains an abrupt step.");
            Check(math.abs(closed.x * ForestFloorPolicy.GrassRetention(closed.z) -
                nearby.x * ForestFloorPolicy.GrassRetention(nearby.z)) < .005f,
                "Effective grass density contains an abrupt moss step.");
            Check(ForestFloorPolicy.ControlWeights(gap).y > 0.85f, "Opening erased the litter substrate.");
            interior += closed.x; opening += gap.x;
            if(!ForestFloorPolicy.MossBlocksVegetation(gap.z)) {openingOutsideMoss+=gap.x;openingSamples++;}
        }
        Check(interior / 4096 < 0.2 && openingSamples>0 && openingOutsideMoss/openingSamples > 0.45,
            "Forest density outside hard moss exclusions drifted.");
        Debug.Log($"FOREST FLOOR density: closed={interior / 4096:P1}, opening={opening / 4096:P1} of meadow candidates, before object exclusions.");
    }

    private static void ValidateRockEdges()
    {
        foreach (float slope in new[] { 44.9f, 45f, 47f, 49.9f, 50f })
        {
            BiomeType biome = BiomeClassifier.Classify(1f, 0.8f, 0.5f, slope, 0, 0, 0.24f);
            Check(biome == BiomeType.Forest, "Wet rock-edge band became a full-density meadow.");
            Check(SurfaceTypeClassifier.Classify(1f, slope, 0, biome, 0.24f) == SurfaceType.Grass,
                "Forest floor did not reach the rock boundary.");
            Check(ForestFloorPolicy.Evaluate(new float2(12, 37), 1937, 0.8f, slope, 0).x < 0.025f,
                "Grass density did not taper at the rock edge.");
        }
        Check(BiomeClassifier.Classify(1, 0.8f, 0.5f, 50.1f, 0, 0, 0.24f) == BiomeType.Rock,
            "Rock boundary moved.");
        Check(BiomeClassifier.Classify(1, 0.5f, 0.5f, 47f, 0, 0, 0.24f) == BiomeType.Grassland,
            "Genuine dry meadow slope changed.");
    }

    private static WorldFeaturePlan Plan(ChunkCoord coord, int size, out BiomeType[,] biomes,
        out SurfaceType[,] surfaces, out GroundCoverType[,] covers)
    {
        int length = size + 3;
        biomes = new BiomeType[length, length]; surfaces = new SurfaceType[length, length];
        covers = new GroundCoverType[length, length];
        var moisture = new float[length, length]; var temperature = new float[length, length];
        for (int x = 0; x < length; x++)
            for (int z = 0; z < length; z++)
            {
                biomes[x, z] = BiomeType.Forest; surfaces[x, z] = SurfaceType.Grass;
                covers[x, z] = (x + z) % 2 == 0 ? GroundCoverType.DarkGrass : GroundCoverType.LeafLitter;
                moisture[x, z] = 0.72f; temperature[x, z] = 0.5f;
            }
        return WorldFeaturePlanGenerator.Generate(coord, size, 1937, biomes, surfaces, moisture, temperature,
            new float[length, length], new float[length, length], new WorldFeatureGenerationSettings());
    }

    private static void ValidateFieldsAndMaterials()
    {
        const int size = 128;
        var coord = new ChunkCoord(-2, 3);
        WorldFeaturePlan plan = Plan(coord, size, out var biomes, out var surfaces, out var covers);
        WorldFeaturePlan east = Plan(new ChunkCoord(-1, 3), size, out _, out _, out _);
        WorldFeaturePlan north = Plan(new ChunkCoord(-2, 4), size, out _, out _, out _);
        var floor = plan.ForestStructure.FloorEcologyMap;
        for (int z = 0; z < size + 3; z++)
            Check(math.cmax(math.abs(floor[size + 1, z] - east.ForestStructure.FloorEcologyMap[1, z])) < 1e-6f, "X seam differs.");
        for (int x = 0; x < size + 3; x++)
            Check(math.cmax(math.abs(floor[x, size + 1] - north.ForestStructure.FloorEcologyMap[x, 1])) < 1e-6f, "Z seam differs.");
        for (int x = 1; x <= size; x += 7)
            for (int z = 1; z <= size; z += 7)
            {
                var shared = ForestFloorPolicy.Evaluate(new float2(coord.x * size + x - 1, coord.z * size + z - 1),
                    1937, 0.72f, 0, 0);
                Check(math.cmax(math.abs(shared - floor[x, z])) < 1e-6f, "Detailed/far floor policy differs.");
            }

        var mixedLabels = TerrainControlMapBuilder.BuildRaw(surfaces, covers, forestStructure: plan.ForestStructure, biomeMap: biomes);
        for (int x = 0; x < size + 3; x++)
            for (int z = 0; z < size + 3; z++) covers[x, z] = GroundCoverType.LeafLitter;
        var litterLabels = TerrainControlMapBuilder.BuildRaw(surfaces, covers, forestStructure: plan.ForestStructure, biomeMap: biomes);
        for (int map = 0; map < 3; map++)
            for (int i = 0; i < mixedLabels.Maps[map].Length; i++)
                Check(mixedLabels.Maps[map][i].Equals(litterLabels.Maps[map][i]), "Gameplay categories leaked into forest material islands.");
        for (int i = 0; i < mixedLabels.Maps[2].Length; i++)
            Check(mixedLabels.Maps[2][i].g >= 216, "Forest litter floor vanished under vegetation.");

        for (int x = 0; x < size + 3; x++)
            for (int z = 0; z < size + 3; z++) { biomes[x, z] = BiomeType.Grassland; covers[x, z] = GroundCoverType.Default; }
        var original = TerrainControlMapBuilder.BuildRaw(surfaces, covers);
        var unchanged = TerrainControlMapBuilder.BuildRaw(surfaces, covers, forestStructure: plan.ForestStructure, biomeMap: biomes);
        for (int map = 0; map < 3; map++)
            for (int i = 0; i < original.Maps[map].Length; i++)
                Check(original.Maps[map][i].Equals(unchanged.Maps[map][i]), "Grassland material changed.");
    }

    private static ChunkRecord Record(float density, BiomeType biome = BiomeType.Forest)
    {
        var record = BillboardGrassStreamingValidation.CreateRecord();
        var plan = new WorldFeaturePlan(19, 19);
        plan.ForestStructure.EnsureFloorEcologyMap(19, 19);
        for (int x = 0; x < 19; x++)
            for (int z = 0; z < 19; z++)
            {
                record.BiomeMap[x, z] = biome;
                record.GroundCoverMap[x, z] = biome == BiomeType.Forest ? GroundCoverType.LeafLitter : GroundCoverType.Default;
                plan.ForestStructure.FloorEcologyMap[x, z] = new float4(density, 0, 0, 0.3f);
            }
        Set(record, "worldFeaturePlan", plan);
        Set(record, "slopeMap", new float[19, 19]);
        Set(record, "nativeTerrainData", new ChunkRecord.NativeTerrainData(record.HeightMap, record.SlopeMap,
            record.BiomeMap, record.SurfaceTypeMap, null, record.GroundCoverMap, null, plan.ForestStructure.FloorEcologyMap));
        return record;
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void ValidateFarGeneration()
    {
        // Run the actual Burst control-map jobs for both distant terrain representations.
        foreach (bool macro in new[] { false, true })
        {
            var tile = FarTerrainGenerator.Generate(new ChunkCoord(-2, 3), 1, macro ? 512 : 128,
                1937, 60f, 10f, 0.3f, 9, 16, 0, 0.24f, isMacroTile: macro);
            Check(tile.ControlMapsRawData.Width == 16 && tile.ControlMapsRawData.Height == 16,
                "Far terrain control-map generation failed.");
            Check(tile.TerrainMeshData != null && tile.HeightGrid != null, "Far terrain mesh generation failed.");
        }
    }

    private static void ValidateLitterMaterial()
    {
        const string normalPath = "Assets/Textures/Ground/Forest/leaf-fall3-normal-unity.png";
        Material terrain = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat");
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        Check(terrain != null && normal != null && terrain.GetTexture("_LeafLitterNormal") == normal,
            "Ordinary litter normal map is not assigned.");
        var importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        Check(importer != null && importer.textureType == TextureImporterType.NormalMap && !importer.sRGBTexture,
            "Litter normal must be imported as a linear normal map.");
        Check(terrain.GetFloat("_ForestFloorMacroStrength") > 0 && terrain.GetFloat("_ForestFloorMacroStrength") <= 0.15f,
            "Litter macro variation is missing or too strong.");
        Check(terrain.GetFloat("_LeafLitterNormalFadeEnd") > terrain.GetFloat("_LeafLitterNormalFadeStart"),
            "Litter detail has no valid distance fade.");
        Check(!ShaderUtil.ShaderHasError(terrain.shader), "Terrain shader import has errors.");
    }

    private static void ValidatePlacement()
    {
        var settings = new GrassSettings { cellsPerAxis = 64, subChunksPerChunk = 4 };
        foreach (float density in new[] { 0f, 0.15f, 1f })
        {
            using var record = Record(density);
            FoliageGenerator.GenerateGrassForChunk(record, settings, null, null, 1937, 16, 1, 10);
            var ranks = new HashSet<uint>();
            foreach (var bucket in record.FoliageData.nearGrassInstancesBySubChunk)
                foreach (var instance in bucket)
                {
                    Check(instance.forestBlend == 1, "Litter-grown grass lost forest tint.");
                    Check(ranks.Add(instance.selectionRank), "Subchunks duplicated forest grass.");
                }
            FoliageGenerator.GenerateBillboardGrassForChunk(record, settings, null, null, 1937, 16, 1, 10);
            Check(record.FoliageData.billboardGrassInstances.Count == ranks.Count, "Near/far forest counts differ.");
            foreach (var instance in record.FoliageData.billboardGrassInstances)
                Check(ranks.Contains(instance.selectionRank), "Near/far forest selection differs.");
            if (density == 0) Check(ranks.Count == 0, "Zero density retained plants.");
            if (density == 1) Check(ranks.Count == 4096, "Full density lost candidates.");
            if (density == 0.15f) Check(ranks.Count > 400 && ranks.Count < 850, "Interior budget was not applied.");
            using var repeated = Record(density);
            FoliageGenerator.GenerateBillboardGrassForChunk(repeated, settings, null, null, 1937, 16, 1, 10);
            Check(repeated.FoliageData.billboardGrassInstances.Count == ranks.Count, "Forest placement was not deterministic.");
            foreach (var instance in repeated.FoliageData.billboardGrassInstances)
                Check(ranks.Contains(instance.selectionRank), "Repeated forest ranks differ.");

            // Exercise the owned-array fallback used by standalone billboard discovery.
            repeated.NativeData.Dispose();
            Set(repeated, "nativeTerrainData", null);
            repeated.FoliageData.ClearBillboards();
            FoliageGenerator.GenerateBillboardGrassForChunk(repeated, settings, null, null, 1937, 16, 1, 10);
            Check(repeated.FoliageData.billboardGrassInstances.Count == ranks.Count, "Fallback discovery lost the forest density field.");
            foreach (var instance in repeated.FoliageData.billboardGrassInstances)
                Check(ranks.Contains(instance.selectionRank), "Fallback forest ranks differ.");
        }
        using var grassland = Record(0, BiomeType.Grassland);
        FoliageGenerator.GenerateGrassForChunk(grassland, settings, null, null, 1937, 16, 1, 10);
        Check(grassland.FoliageData.GetTotalNearGrassInstanceCount() == 4096, "Forest density reduced grassland grass.");
    }

    public static void RunBatch()
    {
        try
        {
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            Run();
            TerrainSlopeValidation.Validate();
            BillboardGrassStreamingValidation.Run();
            GroundFoliageStreamingValidation.Run();
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
