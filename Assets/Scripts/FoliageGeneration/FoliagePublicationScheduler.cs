using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Owns the FIFO of final batch/object publications and one cancellable staging operation.</summary>
public sealed class FoliagePublicationScheduler : IDisposable
{
    private readonly GrassSettings grassSettings;
    private readonly FoliageRenderAssets assets;
    private readonly Func<ChunkRecord, ChunkCoord, FoliagePublicationKind, bool> stillWanted;
    private readonly List<FoliageBatchWorkItem> pendingFoliageBatchWork = new();
    private readonly HashSet<FoliageBatchWorkKey> queuedFoliageBatchWork = new();
    private FoliagePublication activePublication;
    private FoliageBatchWorkKey activePublicationKey;
    private static readonly ProfilerMarker ProcessFoliageBatchQueueMarker = new("FS.Streaming.Foliage.ProcessBatchQueue");
    private static readonly ProfilerMarker PublicationSliceMarker = new("FS.Streaming.Foliage.PublishSlice");
    private static readonly ProfilerMarker CreateMarker = new("FS.Streaming.Foliage.Publication.CreateWork");
    private static readonly ProfilerMarker PreflightMarker = new("FS.Streaming.Foliage.Publication.Preflight");
    private static readonly ProfilerMarker ReadAssetsMarker = new("FS.Streaming.Foliage.Publication.Setup.ReadRenderAssets");
    private static readonly ProfilerMarker ConstructMarker = new("FS.Streaming.Foliage.Publication.Setup.AllocateAndConstruct");
    public int PendingCount => pendingFoliageBatchWork.Count;
    public int ActiveCount => activePublication != null ? 1 : 0;
    public FoliagePublicationScheduler(GrassSettings budgets, FoliageRenderAssets assets,
        Func<ChunkRecord, ChunkCoord, FoliagePublicationKind, bool> stillWanted)
    { grassSettings = budgets; this.assets = assets; this.stillWanted = stillWanted; }
    public void Prune(IChunkLookup chunkManager, ChunkCoord viewerCoord)
    {
        queuedFoliageBatchWork.Clear();
        if (activePublication != null) queuedFoliageBatchWork.Add(activePublicationKey);
        for (int i = pendingFoliageBatchWork.Count - 1; i >= 0; i--)
        {
            FoliageBatchWorkItem workItem = pendingFoliageBatchWork[i];
            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null ||
                runtime == null ||
                runtime.FoliageRuntime == null ||
                !FoliageWorkBudget.HasTerrainInputs(record) ||
                !stillWanted(record, viewerCoord, workItem.Key.WorkType) ||
                !queuedFoliageBatchWork.Add(workItem.Key))
            {
                pendingFoliageBatchWork.RemoveAt(i);
            }
        }

    }
    public void Dispose()
    {
        activePublication?.Dispose(); activePublication = null;
        pendingFoliageBatchWork.Clear(); queuedFoliageBatchWork.Clear();
    }

    internal void ForgetChunk(ChunkCoord coord)
    {
        if (activePublication != null && activePublication.Record.ChunkCoord == coord)
        {
            activePublication.Dispose(); activePublication = null;
        }
        pendingFoliageBatchWork.RemoveAll(item => item.Key.ChunkCoord == coord);
        queuedFoliageBatchWork.RemoveWhere(key => key.ChunkCoord == coord);
    }
    private readonly struct FoliageBatchWorkItem
    {
        public readonly FoliageBatchWorkKey Key;

        public FoliageBatchWorkItem(FoliageBatchWorkKey key)
        {
            Key = key;
        }
    }

    private readonly struct FoliageBatchWorkKey : IEquatable<FoliageBatchWorkKey>
    {
        public readonly ChunkCoord ChunkCoord;
        public readonly FoliagePublicationKind WorkType;

        public FoliageBatchWorkKey(ChunkCoord chunkCoord, FoliagePublicationKind workType)
        {
            ChunkCoord = chunkCoord;
            WorkType = workType;
        }

        public bool Equals(FoliageBatchWorkKey other)
        {
            return ChunkCoord == other.ChunkCoord &&
                   WorkType == other.WorkType;
        }

        public override bool Equals(object obj)
        {
            return obj is FoliageBatchWorkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChunkCoord, WorkType);
        }
    }
    public void Enqueue(ChunkRecord record, FoliagePublicationKind workType)
    {
        if (record == null)
            return;

        FoliageBatchWorkKey key = new FoliageBatchWorkKey(record.ChunkCoord, workType);
        if (!queuedFoliageBatchWork.Add(key))
            return;

        if (pendingFoliageBatchWork.Count >= Mathf.Max(1, grassSettings.maxQueuedRenderBatchWork))
        {
            queuedFoliageBatchWork.Remove(key);
            return;
        }

        pendingFoliageBatchWork.Add(new FoliageBatchWorkItem(key));

    }

    public void Update(IChunkLookup chunkManager, ChunkCoord viewerCoord, long sharedBudgetStart, float sharedBudgetMs, WorldStreamingBudget frameBudget = null)
    {
        if (frameBudget != null && !frameBudget.HasRemaining) return;
        using var scope = ProcessFoliageBatchQueueMarker.Auto();
        long started = TerrainGenerationProfiler.GetTimestamp();
        float budget = Mathf.Max(0f, grassSettings.renderBatchRebuildBudgetMsPerFrame);
        int completed = 0, attempted = 0;
        int cap = Mathf.Max(1, grassSettings.maxRenderBatchRebuildsPerFrame);
        // Advance one bounded slice once admitted by the world allowance. The owner rotates first service.
        do
        {
            if (activePublication == null)
            {
                if (pendingFoliageBatchWork.Count == 0 || attempted++ >= cap) break;
                var item = pendingFoliageBatchWork[0]; pendingFoliageBatchWork.RemoveAt(0);
                var record = chunkManager.GetChunkRecord(item.Key.ChunkCoord);
                var runtime = chunkManager.GetChunkRuntime(record);
                if (record == null || runtime?.FoliageRuntime == null || !FoliageWorkBudget.HasTerrainInputs(record) ||
                    !stillWanted(record, viewerCoord, item.Key.WorkType) ||
                    !FoliagePublication.IsReady(record.FoliageData, item.Key.WorkType))
                { queuedFoliageBatchWork.Remove(item.Key); continue; }
                activePublicationKey = item.Key;
                var local = item.Key.WorkType == FoliagePublicationKind.LilyPad ? assets.LilyPadMeshLocalMatrix :
                    item.Key.WorkType == FoliagePublicationKind.Cattail ? assets.CattailMeshLocalMatrix : Matrix4x4.identity;
                using (CreateMarker.Auto())
                {
                    int cloverCount;
                    using (ReadAssetsMarker.Auto()) cloverCount = assets.GetCloverRenderAssetCount();
                    // Includes object allocation/type initialization as well as the constructor's child scopes.
                    using (ConstructMarker.Auto())
                        activePublication = new FoliagePublication(record, runtime, item.Key.WorkType, cloverCount, local);
                }
            }
            var work = activePublication;
            ChunkRecord currentRecord;
            ChunkRuntime currentRuntime;
            bool current;
            using (PreflightMarker.Auto())
            {
                currentRecord = chunkManager.GetChunkRecord(work.Record.ChunkCoord);
                currentRuntime = chunkManager.GetChunkRuntime(currentRecord);
                current = work.Matches(currentRecord, currentRuntime, assets.GetCloverRenderAssetCount()) &&
                    stillWanted(currentRecord, viewerCoord, activePublicationKey.WorkType);
            }
            bool finished;
            try
            {
                using var slice = PublicationSliceMarker.Auto();
                finished = !current || work.Advance();
            }
            catch
            {
                work.Dispose(); activePublication = null;
                queuedFoliageBatchWork.Remove(activePublicationKey); throw;
            }
            if (finished)
            {
                work.Dispose(); activePublication = null; queuedFoliageBatchWork.Remove(activePublicationKey);
                if (!current && currentRecord != null && currentRuntime?.FoliageRuntime != null &&
                    stillWanted(currentRecord, viewerCoord, activePublicationKey.WorkType))
                    Enqueue(currentRecord, activePublicationKey.WorkType);
                if (++completed >= cap) break;
            }
        } while ((frameBudget == null || frameBudget.HasRemaining) && FoliageWorkBudget.HasRemaining(started, budget, sharedBudgetStart, sharedBudgetMs));

    }
}
