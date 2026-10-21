using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

public static class FoliageExclusionValidation
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    [MenuItem("Tools/Foliage/Validate Grass Exclusion Profiles")]
    public static void Run()
    {
        foreach (var shape in RockPresetSetup.Names)
        {
            FoliageExclusionProfile shared = null;
            foreach (var finish in new[] { "Bare", "Moss", "Snow" })
            {
                var prefab = Resources.Load<GameObject>("Rocks/" + shape + "_" + finish);
                var profile = prefab.GetComponent<FoliageExclusionZone>()?.profile;
                Check(profile != null, "Missing rock profile: " + prefab.name);
                Check(profile.HasRockHull, "Rock contact hull was not baked: " + prefab.name);
                Check(shared == null || shared == profile, "Rock finishes should share a footprint.");
                shared = profile;
            }
        }
        foreach (var name in new[] { "CloverClump", "CloverClump_v01", "CloverClump_v02_A",
            "CloverClump_v02_B_Flowering", "CloverClump_v02_Edge", "CloverSingle_v02" })
            Check(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab")
                .GetComponent<FoliageExclusionZone>()?.profile != null, "Missing clover profile: " + name);
        ValidateGeneration();
        ValidateHullContact();
        Debug.Log("FOLIAGE_EXCLUSION_PASS: 24 prefab bindings, baked contact hulls, terrain-height contact, scale/rotation/embed, near/whole-chunk grass parity, tile-edge overlap, zero/fallback radii and per-prefab clover fade.");
    }

    private static void ValidateGeneration()
    {
        var prefab = new GameObject("exclusion fixture");
        var profile = ScriptableObject.CreateInstance<FoliageExclusionProfile>();
        prefab.AddComponent<FoliageExclusionZone>().profile = profile;
        var record = BillboardGrassStreamingValidation.CreateRecord();
        try
        {
            profile.grassRadius = 1.5f; profile.grassFadePadding = .4f;
            Check(profile.ScaledZone(new Vector3(-2f, 1f, 3f)) == new Vector2(4.5f, 1.2f), "Nonuniform/mirrored scale lost.");
            var biomes = new BiomeType[19,19];
            for (int x = 0; x < 19; x++) for (int z = 0; z < 19; z++) biomes[x,z] = BiomeType.Grassland;
            Set(record, "biomeMap", biomes);
            Set(record, "slopeMap", new float[19,19]);
            Set(record, "nativeTerrainData", new ChunkRecord.NativeTerrainData(record.HeightMap, record.SlopeMap,
                record.BiomeMap, record.SurfaceTypeMap, record.WaterStateMap, record.GroundCoverMap, record.RiverMaskMap));
            var plan = new WorldFeaturePlan(19,19);
            plan.Placements.Add(new WorldFeaturePlacement(WorldFeatureType.Boulder, WorldFeatureVariant.GrasslandLargeBoulder,
                7f, 8f, Quaternion.identity, Vector3.one * 2f, 1f, 1f));
            Set(record, "worldFeaturePlan", plan);
            var trees = new TreeSettings { grasslandLargeRockFallbackPrefab = prefab, rockGrassExclusionRadius = 0f };
            FoliageGenerator.GenerateRocksForChunk(record, trees, 1234, 16, .3f, 10);
            Check(record.FoliageData.rockInstances.Single().grassExclusionRadius == 3f,
                "Selected fallback prefab radius was not scaled, or worldScale was applied twice.");
            // Put a small and a large zone on opposite sides of a tile boundary. The global fallback is zero.
            record.FoliageData.rockInstances.Clear();
            record.FoliageData.rockInstances.Add(new RockInstanceData(new Vector3(-1f,0,0), Quaternion.identity,
                Vector3.one, WorldFeatureVariant.Boulder, 0, 3f));
            record.FoliageData.rockInstances.Add(new RockInstanceData(new Vector3(5f,0,0), Quaternion.identity,
                Vector3.one, WorldFeatureVariant.Boulder, 0, .4f));
            var grass = new GrassSettings { cellsPerAxis = 32, subChunksPerChunk = 2, cellJitter = .8f };
            CloverSettings blending = null;
            void ComparePaths()
            {
                FoliageGenerator.GenerateGrassForChunk(record, grass, blending, trees, 1234, 16, 1, 10);
                FoliageGenerator.GenerateBillboardGrassForChunk(record, grass, blending, trees, 1234, 16, 1, 10);
                var near = record.FoliageData.nearGrassInstancesBySubChunk.Cast<System.Collections.Generic.List<FoliageInstanceData>>()
                    .SelectMany(list => list).Select(p => p.localPosition).ToHashSet();
                var whole = record.FoliageData.billboardGrassInstances.Select(p => p.localPosition).ToHashSet();
                Check(near.Count > 0 && near.SetEquals(whole), "Per-tile and whole-chunk exclusion results disagree.");
                foreach (var rock in record.FoliageData.rockInstances)
                {
                    using var planes = PlaneBuffer(rock);
                    var zone = RockGrassExclusion.Create(rock, trees.rockGrassExclusionRadius, 0);
                    foreach (var position in near)
                        Check(!zone.Contains(position, planes), "Grass leaked into an instance's exclusion zone.");
                }
            }
            ComparePaths();
            record.FoliageData.rockInstances.Clear();
            var actualRock = new RockInstanceData(new Vector3(-1f,0,0), Quaternion.Euler(0,37,0),
                new Vector3(1.4f,1.2f,1.1f), WorldFeatureVariant.GrasslandLargeBoulder, 0, 0f);
            FoliageExclusionZone.SnapshotRockHull(Resources.Load<GameObject>("Rocks/LongBoulder_Bare"), ref actualRock);
            record.FoliageData.rockInstances.Add(actualRock);
            ComparePaths(); // The zero compatibility radius must not replace the baked hull.
            record.FoliageData.rockInstances.Clear();
            record.FoliageData.rockInstances.Add(new RockInstanceData(Vector3.zero, Quaternion.identity,
                Vector3.one, WorldFeatureVariant.Boulder, 0, 0f));
            trees.rockGrassExclusionRadius = 100f;
            ComparePaths(); // An explicit zero must override the global fallback.
            record.FoliageData.rockInstances[0] = new RockInstanceData(Vector3.zero, Quaternion.identity,
                Vector3.one, WorldFeatureVariant.Boulder, 0);
            trees.rockGrassExclusionRadius = 2f;
            ComparePaths();

            record.FoliageData.rockInstances.Clear(); plan.Placements.Clear();
            var clover = new CloverSettings { cloverClumpPrefabs = new[] { null, prefab }, uniformScaleRange = Vector2.one * 2f,
                patchCellSize = 4f, patchSpawnChance = 1f, patchNoiseThreshold = 0f, patchRadiusRange = Vector2.one,
                minClumpsPerPatch = 5, maxClumpsPerPatch = 5, grassInfluenceRadius = 99f, grassFadePadding = 99f };
            Check(clover.GetPrefab(0) == prefab, "Null slots changed the rendered clover index.");
            FoliageGenerator.GenerateCloverForChunk(record, clover, 1, 1234, 16, 1f, 10f);
            Check(record.FoliageData.cloverInstances.Count > 0, "Clover fixture produced no instances.");
            foreach (var clump in record.FoliageData.cloverInstances)
            {
                Check(clump.grassInfluenceRadius == 3f && clump.grassFadePadding == .8f, "Clover ignored prefab profile or instance scale.");
            }
            // A deliberately huge legacy fade must not override this prefab's .2-unit fade.
            record.FoliageData.cloverInstances.Clear();
            record.FoliageData.cloverInstances.Add(new CloverInstanceData(Vector3.zero, Quaternion.identity,
                Vector3.one, 0, .5f, 0, .2f));
            blending = new CloverSettings { grassDensityInsidePatch = 0f, grassFadePadding = 99f };
            ComparePaths();
            foreach (var bucket in record.FoliageData.nearGrassInstancesBySubChunk)
                foreach (var candidate in bucket)
                    Check(new Vector2(candidate.localPosition.x, candidate.localPosition.z).sqrMagnitude >= .25f,
                        "Clover core did not suppress grass at zero retention.");
        }
        finally { record.Dispose(); UnityEngine.Object.DestroyImmediate(prefab); UnityEngine.Object.DestroyImmediate(profile); }
    }

    private static NativeArray<float4> PlaneBuffer(RockInstanceData rock)
    {
        var result = new NativeArray<float4>(rock.grassExclusionHull?.Length ?? 0, Allocator.Temp);
        for (int i = 0; i < result.Length; i++)
        {
            var p = rock.grassExclusionHull[i];
            result[i] = new float4(p.x,p.y,p.z,p.w);
        }
        return result;
    }

    private static void ValidateHullContact()
    {
        // A known tapered hull: half-width .3 at its base, 1.5 at its top.
        var rock = new RockInstanceData(new Vector3(-1,4,2), Quaternion.Euler(23,37,0), new Vector3(-2,3,1.5f),
            WorldFeatureVariant.Boulder, 0, 100f)
        {
            grassExclusionHull = new[] { new Vector4(0,-1,0,0), new Vector4(0,1,0,-2),
                new Vector4(1,-.6f,0,-.3f).normalized, new Vector4(-1,-.6f,0,-.3f).normalized,
                new Vector4(0,-.4f,1,-.2f).normalized, new Vector4(0,-.4f,-1,-.2f).normalized },
            grassHullBounds = new Bounds(new Vector3(0,1,0),new Vector3(3,2,2)),
            groundEmbed = .08f
        };
        using (var planes = PlaneBuffer(rock))
        {
            var zone = RockGrassExclusion.Create(rock, 0f, 0);
            var position = rock.localPosition - Vector3.up * (rock.groundEmbed * Mathf.Abs(rock.localScale.y));
            var matrix = Matrix4x4.TRS(position, rock.localRotation, rock.localScale);
            Check(zone.Contains(matrix.MultiplyPoint3x4(new Vector3(.1f,.08f,0)), planes), "Tapered base contact missed.");
            Check(!zone.Contains(matrix.MultiplyPoint3x4(new Vector3(.8f,.08f,0)), planes), "Upper rock silhouette still clears grass beside the base.");
            Check(zone.Contains(matrix.MultiplyPoint3x4(new Vector3(1.2f,1.7f,0)), planes), "Terrain-height query ignored higher hull contact.");
        }
        foreach (var shape in RockPresetSetup.Names)
        {
            var prefab = Resources.Load<GameObject>("Rocks/"+shape+"_Bare");
            rock = new RockInstanceData(Vector3.zero,Quaternion.identity,Vector3.one,WorldFeatureVariant.Boulder,0);
            FoliageExclusionZone.SnapshotRockHull(prefab,ref rock);
            using var planes = PlaneBuffer(rock);
            var zone = RockGrassExclusion.Create(rock,0f,0);
            float oldRadius = prefab.GetComponent<FoliageExclusionZone>().profile.grassRadius;
            int oldCount=0, contactCount=0, restored=0;
            const float step=.025f;
            for(float x=-zone.radius;x<=zone.radius;x+=step)
                for(float z=-zone.radius;z<=zone.radius;z+=step)
                {
                    bool old=x*x+z*z < oldRadius*oldRadius;
                    bool contact=zone.Contains(new float3(x,0,z),planes);
                    if(old)oldCount++;
                    if(contact)contactCount++;
                    if(old&&!contact)restored++;
                }
            Check(contactCount>0 && restored>0, "Hull footprint did not restore grass outside contact: "+shape);
            Debug.Log($"ROCK_CONTACT_AREA {shape}: circle={oldCount*step*step:F3}, contact={contactCount*step*step:F3} square units; restored={restored*step*step:F3}");
        }
    }

    public static void SetupAndRunBatch()
    {
        try { FoliageExclusionProfileAuthoring.Setup(); Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
