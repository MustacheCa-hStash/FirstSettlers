using System;
using UnityEditor;
using UnityEngine;

public static class ButterflyValidation
{
    [MenuItem("Tools/Terrain/Validate Ambient Life")]
    public static void Run()
    {
        ValidateDailyResidents();
        ValidateBeeGroups();
        ValidateChunkHandoff();
        ValidateGroundAndWater();
        Debug.Log("Ambient life validation passed: butterfly residents, bee groups, chunk handoff, terrain height, and water exclusion.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void ValidateDailyResidents()
    {
        var settings = new ButterflySettings { spawnChance = 1f, maxPerChunk = 3 };
        var data = new ChunkButterflyData();
        data.Hotspots.Add(new ButterflyHotspot(new Vector3(1f, 2f, 1f), 8));
        data.Hotspots.Add(new ButterflyHotspot(new Vector3(5f, 3f, 1f), 12));
        data.Hotspots.Add(new ButterflyHotspot(new Vector3(1f, 4f, 5f), 9));
        ChunkCoord coord = new(7, -4);

        AmbientLifeManager.GenerateDailyRoster(settings, 12345, coord, data, 12);
        Require(data.Residents.Count > 0 && data.Residents.Count <= 3, "Daily population exceeded its cap or disappeared from guaranteed habitat.");
        ButterflyResident[] first = data.Residents.ToArray();
        AmbientLifeManager.GenerateDailyRoster(settings, 12345, coord, data, 12);
        Require(data.Residents.Count == first.Length, "Returning to a chunk changed the same-day count.");
        for (int i = 0; i < first.Length; i++)
        {
            ButterflyResident current = data.Residents[i];
            Require(current.Id == first[i].Id && current.HomeHotspotIndex == first[i].HomeHotspotIndex &&
                    Mathf.Approximately(current.PreferredHeight, first[i].PreferredHeight),
                "Returning to a chunk changed a same-day resident.");
            Require(current.HomeHotspotIndex >= 0 && current.HomeHotspotIndex < data.Hotspots.Count,
                "A resident selected an invalid hotspot.");
        }
        AmbientLifeManager.GenerateDailyRoster(settings, 12345, coord, data, 13);
        Require(data.RosterDay == 13 && data.Residents.Count > 0 && data.Residents[0].Id != first[0].Id,
            "A new day did not refresh the population.");
        data.Hotspots.Clear();
        AmbientLifeManager.GenerateDailyRoster(settings, 12345, coord, data, 14);
        Require(data.Residents.Count == 0, "A chunk without flowers kept residents.");
    }

    private static void ValidateGroundAndWater()
    {
        const int chunkSize = 2;
        const int mapSize = chunkSize + 3;
        float[,] heights = new float[mapSize, mapSize];
        WaterState[,] water = new WaterState[mapSize, mapSize];
        for (int x = 0; x < mapSize; x++)
            for (int z = 0; z < mapSize; z++)
                heights[x, z] = 1f;
        heights[1, 1] = 1f;
        heights[2, 1] = 2f;
        heights[1, 2] = 3f;
        heights[2, 2] = 4f;

        ChunkCoord coord = new(1, 0);
        var record = new ChunkRecord(coord);
        int version = record.BeginTerrainDataRequest();
        try
        {
            Require(record.TryCompleteTerrainDataRequest(version, heights, new float[mapSize, mapSize],
                new float[mapSize, mapSize], new float[mapSize, mapSize], new BiomeType[mapSize, mapSize],
                new SurfaceType[mapSize, mapSize], water, new GroundCoverType[mapSize, mapSize],
                new WorldFeaturePlan(mapSize, mapSize), new float[mapSize, mapSize], Array.Empty<Texture2D>()),
                "Synthetic terrain was rejected.");

            Require(ButterflyTerrainSampler.TrySampleDryGround(record, coord, 2.5f, 0.5f,
                chunkSize, 1f, 2f, 3f, 0.2f, out float groundY), "Dry terrain was rejected.");
            Require(Mathf.Abs(groundY - 5f) < 0.0001f,
                "Padded bilinear sampling or the chunk's world origin is incorrect.");

            water[1, 1] = WaterState.Shallow;
            Require(!ButterflyTerrainSampler.TrySampleDryGround(record, coord, 2.1f, 0.1f,
                chunkSize, 1f, 2f, 3f, 0.2f, out _), "Shallow water was treated as dry land.");
            water[1, 1] = WaterState.Dry;
            Require(!ButterflyTerrainSampler.TrySampleDryGround(record, coord, 2.5f, 0.5f,
                chunkSize, 1f, 2f, 5f, 0.2f, out _), "Terrain below the global water Y was accepted.");
        }
        finally
        {
            record.Dispose();
        }
    }

    private static void ValidateBeeGroups()
    {
        var settings = new BeeSettings { spawnChance = 1f, threeBeeChance = 0f };
        var data = new ChunkButterflyData();
        for (int i = 0; i < 3; i++)
            data.Hotspots.Add(new ButterflyHotspot(new Vector3(i * 2f, 3f, 0f), 8));
        ChunkCoord coord = new(7, -4);
        AmbientLifeManager.GenerateDailyRoster(settings, 12345, coord, data, 12);
        Require(data.Residents.Count == 2, "An occupied bee chunk did not start with two bees.");
        uint firstId = data.Residents[0].Id;
        AmbientLifeManager.GenerateDailyRoster(settings, 12345, coord, data, 12);
        Require(data.Residents.Count == 2 && data.Residents[0].Id == firstId,
            "Returning to a bee chunk changed its same-day residents.");
        settings.threeBeeChance = 1f;
        AmbientLifeManager.GenerateDailyRoster(settings, 12345, coord, data, 13);
        Require(data.Residents.Count == 3, "An occupied three-bee chunk did not get three residents.");
        settings.spawnChance = 0f;
        AmbientLifeManager.GenerateDailyRoster(settings, 12345, coord, data, 14);
        Require(data.Residents.Count == 0, "A zero bee group chance produced residents.");
        settings.spawnChance = 1f;
        data.Hotspots.Clear();
        AmbientLifeManager.GenerateDailyRoster(settings, 12345, coord, data, 15);
        Require(data.Residents.Count == 0, "A flowerless chunk produced bees.");
    }

    private static void ValidateChunkHandoff()
    {
        var settings = new ButterflySettings { spawnChance = 1f, maxPerChunk = 2 };
        var source = new ChunkButterflyData();
        var destination = new ChunkButterflyData();
        var third = new ChunkButterflyData();
        source.Hotspots.Add(new ButterflyHotspot(new Vector3(1f, 3f, 1f), 10));
        source.Hotspots.Add(new ButterflyHotspot(new Vector3(2f, 3f, 2f), 10));
        source.Hotspots.Add(new ButterflyHotspot(new Vector3(3f, 3f, 3f), 10));
        AmbientLifeManager.GenerateDailyRoster(settings, 5, new ChunkCoord(0, 0), source, 4);
        AmbientLifeManager.GenerateDailyRoster(settings, 5, new ChunkCoord(1, 0), destination, 4);
        AmbientLifeManager.GenerateDailyRoster(settings, 5, new ChunkCoord(2, 0), third, 4);
        uint id = source.Residents[0].Id;
        Vector3 firstHome = new(12f, 4f, 2f);
        Require(AmbientLifeManager.MoveResident(source, destination, id, firstHome),
            "A boundary crossing failed to transfer its resident.");
        Require(!source.Residents.Exists(r => r.Id == id) &&
                destination.Residents.Exists(r => r.Id == id && r.HomeWorldPosition == firstHome),
            "A boundary crossing duplicated or lost its resident.");
        AmbientLifeManager.GenerateDailyRoster(settings, 5, new ChunkCoord(0, 0), source, 4);
        AmbientLifeManager.GenerateDailyRoster(settings, 5, new ChunkCoord(1, 0), destination, 4);
        Require(!source.Residents.Exists(r => r.Id == id) && destination.Residents.Exists(r => r.Id == id),
            "A same-day habitat rebuild undid a boundary crossing.");
        Require(AmbientLifeManager.MoveResident(destination, third, id, new Vector3(22f, 4f, 2f)) &&
                !destination.Residents.Exists(r => r.Id == id) && third.Residents.Exists(r => r.Id == id),
            "A migrant could not cross a second chunk boundary.");
        AmbientLifeManager.GenerateDailyRoster(settings, 5, new ChunkCoord(2, 0), third, 5);
        Require(!third.Residents.Exists(r => r.Id == id), "A previous day's migrant remained in the chunk.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
