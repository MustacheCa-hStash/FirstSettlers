using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Owns one deferred ground discovery job and a bounded, deduplicated FIFO of requests.</summary>
public sealed class FoliageDiscoveryScheduler : IDisposable
{
    private readonly GrassSettings grassSettings;
    private readonly FlowerSettings flowerSettings;
    private readonly LilyPadSettings lilyPadSettings;
    private readonly CattailSettings cattailSettings;
    private readonly CloverSettings cloverSettings;
    private readonly DandelionSettings dandelionSettings;
    private readonly int worldSeed, chunkSize;
    private readonly float worldScale, meshHeightMultiplier;
    private readonly TerrainWaterSettings waterSettings;
    private readonly FoliageRenderAssets assets;
    private readonly FoliagePlacementService placement;
    private readonly FoliagePublicationScheduler publicationScheduler;
    private readonly Func<ChunkRecord, ChunkCoord, FoliagePublicationKind, bool> stillWanted;
    private readonly List<GroundFoliageGenerationWorkItem> pendingGroundFoliageGenerationWork = new();
    private readonly HashSet<GroundFoliageGenerationWorkKey> queuedGroundFoliageGenerationWork = new();
    private IEnumerator<bool> activeGroundGeneration;
    private GroundFoliageGenerationWorkKey activeGroundKey;
    private ChunkRecord activeGroundRecord;
    private static readonly ProfilerMarker ProcessGroundFoliageGenerationMarker = new("FS.Streaming.Foliage.ProcessGroundGenerationQueue");
    public int PendingCount => pendingGroundFoliageGenerationWork.Count;
    public int ActiveCount => activeGroundGeneration != null ? 1 : 0;
    public FoliageDiscoveryScheduler(WorldGenerationConfiguration generation, WorldFoliageConfiguration settings,
        FoliageRenderAssets assets, FoliagePlacementService placement, FoliagePublicationScheduler publication,
        Func<ChunkRecord, ChunkCoord, FoliagePublicationKind, bool> stillWanted)
    {
        grassSettings = settings.Grass; flowerSettings = settings.Flowers; lilyPadSettings = settings.LilyPads;
        cattailSettings = settings.Cattails; cloverSettings = settings.Clover; dandelionSettings = settings.Dandelions;
        worldSeed = generation.Seed; chunkSize = generation.ChunkSize; worldScale = generation.WorldScale;
        meshHeightMultiplier = generation.MeshHeightMultiplier; waterSettings = generation.Water;
        this.assets = assets; this.placement = placement; publicationScheduler = publication; this.stillWanted = stillWanted;
    }
    public void Prune(IChunkLookup chunkManager, ChunkCoord viewerCoord)
    {
        queuedGroundFoliageGenerationWork.Clear();
        if (activeGroundGeneration != null) queuedGroundFoliageGenerationWork.Add(activeGroundKey);
        for (int i = pendingGroundFoliageGenerationWork.Count - 1; i >= 0; i--)
        {
            GroundFoliageGenerationWorkItem workItem = pendingGroundFoliageGenerationWork[i];
            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);

            if (record == null ||
                !FoliageWorkBudget.HasTerrainInputs(record) ||
                !stillWanted(record, viewerCoord, workItem.Key.GenerationType) ||
                !queuedGroundFoliageGenerationWork.Add(workItem.Key))
            {
                pendingGroundFoliageGenerationWork.RemoveAt(i);
            }
        }

    }
    public void Dispose()
    {
        activeGroundGeneration?.Dispose(); activeGroundGeneration = null; activeGroundRecord = null;
        pendingGroundFoliageGenerationWork.Clear(); queuedGroundFoliageGenerationWork.Clear();
    }
    private readonly struct GroundFoliageGenerationWorkItem
    {
        public readonly GroundFoliageGenerationWorkKey Key;

        public GroundFoliageGenerationWorkItem(GroundFoliageGenerationWorkKey key)
        {
            Key = key;
        }
    }

    private readonly struct GroundFoliageGenerationWorkKey : IEquatable<GroundFoliageGenerationWorkKey>
    {
        public readonly ChunkCoord ChunkCoord;
        public readonly FoliagePublicationKind GenerationType;

        public GroundFoliageGenerationWorkKey(ChunkCoord chunkCoord, FoliagePublicationKind generationType)
        {
            ChunkCoord = chunkCoord;
            GenerationType = generationType;
        }

        public bool Equals(GroundFoliageGenerationWorkKey other)
        {
            return ChunkCoord == other.ChunkCoord &&
                   GenerationType == other.GenerationType;
        }

        public override bool Equals(object obj)
        {
            return obj is GroundFoliageGenerationWorkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChunkCoord, GenerationType);
        }
    }
    public void Enqueue(ChunkRecord record, FoliagePublicationKind generationType)
    {
        if (record == null)
            return;

        GroundFoliageGenerationWorkKey key = new GroundFoliageGenerationWorkKey(record.ChunkCoord, generationType);
        if (!queuedGroundFoliageGenerationWork.Add(key))
            return;

        if (pendingGroundFoliageGenerationWork.Count >= Mathf.Max(1, grassSettings.maxQueuedGroundFoliageGenerationWork))
        {
            queuedGroundFoliageGenerationWork.Remove(key);
            return;
        }

        pendingGroundFoliageGenerationWork.Add(new GroundFoliageGenerationWorkItem(key));

    }

    public void Update(
        IChunkLookup chunkManager,
        ChunkCoord viewerCoord,
        long sharedBudgetStart,
        float sharedBudgetMs)
    {
        using var processGroundFoliageGenerationScope = ProcessGroundFoliageGenerationMarker.Auto();

        int maxGenerations = Mathf.Max(1, grassSettings.maxGroundFoliageGenerationsPerFrame);
        float budgetMs = Mathf.Max(0.05f, grassSettings.groundFoliageGenerationBudgetMsPerFrame);
        long frameStart = TerrainGenerationProfiler.GetTimestamp();
        int started = 0;
        // One resident discovery job bounds native memory and prevents worker flooding.
        // Always make one step of progress even when earlier foreground work used its budget.
        do
        {
            if (activeGroundGeneration == null)
            {
                if (pendingGroundFoliageGenerationWork.Count == 0 || started >= maxGenerations) break;
                var work = pendingGroundFoliageGenerationWork[0];
                pendingGroundFoliageGenerationWork.RemoveAt(0); // FIFO: distant requests cannot starve.
                queuedGroundFoliageGenerationWork.Remove(work.Key);
                var record = chunkManager.GetChunkRecord(work.Key.ChunkCoord);
                if (record == null || !FoliageWorkBudget.HasTerrainInputs(record) ||
                    !stillWanted(record, viewerCoord, work.Key.GenerationType)) continue;
                placement.PrepareDependencies(record, work.Key.GenerationType);
                activeGroundKey = work.Key;
                activeGroundRecord = record;
                switch (work.Key.GenerationType)
                {
                    case FoliagePublicationKind.Flower:
                        activeGroundGeneration = FoliageGenerator.GenerateFlowersIncrementally(record,
                            flowerSettings, worldSeed, chunkSize, worldScale, meshHeightMultiplier);
                        break;
                    case FoliagePublicationKind.LilyPad:
                        activeGroundGeneration = LilyPadGenerator.GenerateIncrementally(record,
                            lilyPadSettings, worldSeed, chunkSize, worldScale,
                            waterSettings.WaterLevel, waterSettings.SurfaceY);
                        break;
                    case FoliagePublicationKind.Cattail:
                        activeGroundGeneration = CattailGenerator.GenerateIncrementally(record,
                            cattailSettings, worldSeed, chunkSize, worldScale,
                            meshHeightMultiplier, waterSettings.WaterLevel);
                        break;
                    case FoliagePublicationKind.Clover:
                        activeGroundGeneration = FoliageGenerator.GenerateCloverIncrementally(record,
                            cloverSettings, assets.GetCloverRenderAssetCount(), worldSeed, chunkSize, worldScale, meshHeightMultiplier);
                        break;
                    default:
                        activeGroundGeneration = FoliageGenerator.GenerateDandelionsIncrementally(record,
                            dandelionSettings, worldSeed, chunkSize, worldScale, meshHeightMultiplier);
                        break;
                }
                queuedGroundFoliageGenerationWork.Add(activeGroundKey);
                started++;
            }
            if (activeGroundGeneration.MoveNext())
            {
                if (!activeGroundGeneration.Current) break; // Waiting: never Complete on the main thread.
            }
            else
            {
                activeGroundGeneration.Dispose();
                activeGroundGeneration = null;
                queuedGroundFoliageGenerationWork.Remove(activeGroundKey);
                var record = activeGroundRecord;
                activeGroundRecord = null;
                if (ReferenceEquals(chunkManager.GetChunkRecord(record.ChunkCoord), record))
                {
                    if (FoliagePublication.IsReady(record.FoliageData, activeGroundKey.GenerationType))
                    {
                        if (activeGroundKey.GenerationType == FoliagePublicationKind.Clover) record.FoliageData.ClearNearGrass();
                        publicationScheduler.Enqueue(record, activeGroundKey.GenerationType);
                    }
                }
            }
        } while (FoliageWorkBudget.HasRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs));

    }
}
