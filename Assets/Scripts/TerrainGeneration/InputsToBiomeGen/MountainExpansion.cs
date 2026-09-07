using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public struct MountainExpansionAnchor
{
    public float2 Position;
    public float Radius;
}

public static partial class HeightMapGenerator
{
    private static readonly object MountainAnchorLock = new object();
    private static readonly Dictionary<(int, float, int, int), MountainExpansionAnchor> MountainAnchorCache = new();

    public static MountainExpansionAnchor[] GetMountainAnchors(
        float2 minimum, float2 maximum, float sampleScale, TerrainHeightSamplingContext context)
    {
        if (context.MountainHorizontalScale <= 1f)
            return Array.Empty<MountainExpansionAnchor>();

        sampleScale = math.max(sampleScale, 0.0001f);
        float cellSize = sampleScale * 3f;
        float reach = cellSize * context.MountainHorizontalScale;
        int2 first = (int2)math.floor((minimum - reach) / cellSize);
        int2 last = (int2)math.floor((maximum + reach) / cellSize);
        var anchors = new List<MountainExpansionAnchor>();
        for (int x = first.x; x <= last.x; x++)
        {
            for (int z = first.y; z <= last.y; z++)
            {
                var key = (context.RiverSeed, sampleScale, x, z);
                MountainExpansionAnchor anchor;
                bool cached;
                lock (MountainAnchorLock)
                    cached = MountainAnchorCache.TryGetValue(key, out anchor);
                if (!cached)
                {
                    anchor = FindMountainAnchor(new float2(x, z) * cellSize, cellSize, sampleScale, context);
                    lock (MountainAnchorLock)
                    {
                        // Cache eviction affects only search cost, never the deterministic anchor result.
                        if (MountainAnchorCache.Count >= 1024) MountainAnchorCache.Clear();
                        MountainAnchorCache[key] = anchor;
                    }
                }
                float2 nearest = math.clamp(anchor.Position, minimum, maximum);
                if (anchor.Radius > 0f && math.distancesq(nearest, anchor.Position) < reach * reach)
                    anchors.Add(anchor);
            }
        }
        return anchors.ToArray();
    }

    private static MountainExpansionAnchor FindMountainAnchor(
        float2 minimum, float cellSize, float sampleScale, TerrainHeightSamplingContext context)
    {
        float2 best = minimum;
        float highest = 0f;
        float step = cellSize / 8f;
        float Value(float2 p)
        {
            float mask = SampleMountainMask(p.x / (sampleScale * 6f), p.y / (sampleScale * 6f), context.MountainMaskOffsets);
            float terrain = SampleMountainTerrain(p.x / (sampleScale * 3f), p.y / (sampleScale * 3f), context.MountainTerrainOffsets);
            float height = terrain * math.pow(math.smoothstep(0.12f, 0.9f, mask), 1.8f) * 45f;
            var offsets = context.MountainRuggedOffsets;
            return height + SampleMountainDetail(p, sampleScale, 0.5f, height, TerrainWaterSettings.DefaultWaterLevel,
                new float2(offsets[0].x, offsets[0].y), new float2(offsets[1].x, offsets[1].y), new float2(offsets[2].x, offsets[2].y));
        }
        for (int x = 0; x <= 8; x++)
            for (int z = 0; z <= 8; z++)
            {
                float2 p = minimum + new float2(x, z) * step;
                float value = Value(p);
                if (value > highest) { highest = value; best = p; }
            }
        if (highest < 1.35f) return default;
        for (int iteration = 0; iteration < 10; iteration++)
        {
            float2 center = best;
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    float2 p = math.clamp(center + new float2(x, z) * step, minimum, minimum + cellSize);
                    float value = Value(p);
                    if (value > highest) { highest = value; best = p; }
                }
            step *= 0.5f;
        }
        // A cell ending on another mountain's rising flank must not invent a summit there.
        if (math.any(best <= minimum + step) || math.any(best >= minimum + cellSize - step))
            return default;
        return new MountainExpansionAnchor { Position = best, Radius = cellSize };
    }

    public static float2 MountainExpansionSource(float2 position, MountainExpansionAnchor anchor, float width)
    {
        float distance = math.distance(position, anchor.Position);
        float edge = distance / (anchor.Radius * width);
        // Exact summit-centered stretching in the interior; smoothly return to identity at support edges.
        float influence = 1f - math.smoothstep(0.65f, 1f, edge);
        return anchor.Position + (position - anchor.Position) / math.lerp(1f, width, influence);
    }


    private static MountainShape SampleMountainShape(float2 p, float scale, float baseLand, float waterLevel,
        Vector2[] maskOffsets, Vector2[] terrainOffsets, Vector2[] offsets)
    {
        float mask = SampleMountainMask(p.x / (scale * 6f), p.y / (scale * 6f), maskOffsets);
        float weight = math.pow(math.smoothstep(0.12f, 0.9f, mask), 1.8f);
        float terrain = SampleMountainTerrain(p.x / (scale * 3f), p.y / (scale * 3f), terrainOffsets);
        float contribution = terrain * weight;
        float height = contribution * 45f;
        float detail = SampleMountainDetail(p, scale, baseLand, height, waterLevel, new float2(offsets[0].x, offsets[0].y), new float2(offsets[1].x, offsets[1].y), new float2(offsets[2].x, offsets[2].y));
        return new MountainShape { Mask = mask, Weight = weight, Contribution = contribution, Relief = height + detail };
    }

    private static MountainShape SampleExpandedMountain(float2 p, float scale, float baseLand, float waterLevel,
        Vector2[] maskOffsets, Vector2[] terrainOffsets, Vector2[] offsets,
        float width, MountainExpansionAnchor[] anchors)
    {
        MountainShape result = SampleMountainShape(p, scale, baseLand, waterLevel, maskOffsets, terrainOffsets, offsets);
        if (width <= 1f || anchors == null) return result;
        for (int i = 0; i < anchors.Length; i++)
        {
            MountainExpansionAnchor anchor = anchors[i];
            float reach = anchor.Radius * width;
            if (math.distancesq(p, anchor.Position) >= reach * reach) continue;
            float2 source = MountainExpansionSource(p, anchor, width);
            MergeMountain(ref result, SampleMountainShape(source, scale, baseLand, waterLevel, maskOffsets, terrainOffsets, offsets));
        }
        return result;
    }

    private static MountainShape SampleMountainShape(float2 p, float scale, float baseLand, float waterLevel,
        NativeArray<float2> maskOffsets, NativeArray<float2> terrainOffsets, NativeArray<float2> offsets)
    {
        float mask = SampleMountainMask(p.x / (scale * 6f), p.y / (scale * 6f), maskOffsets);
        float weight = math.pow(math.smoothstep(0.12f, 0.9f, mask), 1.8f);
        float terrain = SampleMountainTerrain(p.x / (scale * 3f), p.y / (scale * 3f), terrainOffsets);
        float contribution = terrain * weight;
        float height = contribution * 45f;
        float detail = SampleMountainDetail(p, scale, baseLand, height, waterLevel, offsets[0], offsets[1], offsets[2]);
        return new MountainShape { Mask = mask, Weight = weight, Contribution = contribution, Relief = height + detail };
    }

    private static MountainShape SampleExpandedMountain(float2 p, float scale, float baseLand, float waterLevel,
        NativeArray<float2> maskOffsets, NativeArray<float2> terrainOffsets, NativeArray<float2> offsets,
        float width, NativeArray<MountainExpansionAnchor> anchors)
    {
        MountainShape result = SampleMountainShape(p, scale, baseLand, waterLevel, maskOffsets, terrainOffsets, offsets);
        if (width <= 1f || !anchors.IsCreated) return result;
        for (int i = 0; i < anchors.Length; i++)
        {
            MountainExpansionAnchor anchor = anchors[i];
            float reach = anchor.Radius * width;
            if (math.distancesq(p, anchor.Position) >= reach * reach) continue;
            float2 source = MountainExpansionSource(p, anchor, width);
            MergeMountain(ref result, SampleMountainShape(source, scale, baseLand, waterLevel, maskOffsets, terrainOffsets, offsets));
        }
        return result;
    }

    private struct MountainShape
    {
        public float Mask, Weight, Contribution, Relief;
    }

    private static void MergeMountain(ref MountainShape result, MountainShape candidate)
    {
        // Max-union broadens terrain without stacking mountain heights at overlaps.
        result.Relief = math.max(result.Relief, candidate.Relief);
        result.Contribution = math.max(result.Contribution, candidate.Contribution);
        result.Mask = math.max(result.Mask, candidate.Mask);
        result.Weight = math.max(result.Weight, candidate.Weight);
    }
}
