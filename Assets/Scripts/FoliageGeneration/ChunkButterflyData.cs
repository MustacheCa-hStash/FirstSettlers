using System.Collections.Generic;
using UnityEngine;

public readonly struct ButterflyHotspot
{
    public readonly Vector3 WorldPosition;
    public readonly int FlowerCount;

    public ButterflyHotspot(Vector3 worldPosition, int flowerCount)
    {
        WorldPosition = worldPosition;
        FlowerCount = flowerCount;
    }
}

public readonly struct ButterflyResident
{
    public readonly uint Id;
    public readonly int HomeHotspotIndex;
    public readonly Vector3 HomeWorldPosition;
    public readonly float PreferredHeight;
    public readonly float Scale;

    public ButterflyResident(uint id, int homeHotspotIndex, Vector3 homeWorldPosition,
        float preferredHeight, float scale)
    {
        Id = id;
        HomeHotspotIndex = homeHotspotIndex;
        HomeWorldPosition = homeWorldPosition;
        PreferredHeight = preferredHeight;
        Scale = scale;
    }
}

public sealed class ChunkButterflyData
{
    public ChunkFoliageData FlowerSource;
    public float[,] TerrainSource;
    public int FlowersRevision = -1;
    public int SettingsSignature;
    public int RosterDay = int.MinValue;
    public readonly List<ButterflyHotspot> Hotspots = new();
    public readonly List<ButterflyResident> Residents = new();
    public readonly List<ButterflyResident> Migrants = new();
    public readonly HashSet<uint> DepartedIds = new();
}
