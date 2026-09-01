using System.Diagnostics;
using System.Text;
using UnityEngine;

public enum TerrainGenerationProfileStage
{
    TerrainHeightField,
    TerrainClimateMoisture,
    TerrainClimateTemperature,
    TerrainBiomeMap,
    TerrainSurfaceMap,
    TerrainWaterStateMap,
    TerrainWorldFeaturePlan,
    TerrainGroundCoverMap,
    TerrainControlMapBuild,
    TerrainDataTotal,

    FarHeightGrid,
    FarSlopeGrid,
    FarSurfaceMap,
    FarMeshBuild,
    FarControlMapBuild,
    FarTerrainTotal,

    TerrainMeshBuild,
    LakeMeshBuild,
    RiverMeshBuild,
    LODMeshTotal,
    ColliderMeshBuild,

    MainTerrainControlMapTextureCreate,
    MainFarControlMapTextureCreate,
    MainFarTerrainMeshCreate,
    MainLODTerrainMeshCreate,
    MainLakeMeshCreate,
    MainRiverMeshCreate,
    MainColliderMeshCreate,
    MainProcessCompletedRequestsTotal,

    FoliageTotal,
    FoliageHandleSubChunkChanged,
    FoliageDrawVisibleEveryFrame,
    FoliageGrassSubChunkEnqueue,
    FoliageGrassSubChunkDiscovery,
    FoliageGrassRenderBatchBuild,
    FoliageGrassDraw,
    FoliageBillboardGrassGeneration,
    FoliageBillboardGrassBatchBuild,
    FoliageBillboardGrassDraw,
    FoliageFlowerGeneration,
    FoliageFlowerBatchBuild,
    FoliageFlowerDraw,
    FoliageTreeGeneration,
    FoliageTreeGameObjectRebuild,
    FoliageTreeBillboardBatchBuild,
    FoliageTreeBillboardDraw,
    FoliageBushGeneration,
    FoliageBushGameObjectRebuild,
    FoliageRockGeneration,
    FoliageRockGameObjectRebuild
}

public static class TerrainGenerationProfiler
{
    private sealed class StageAccumulator
    {
        public int Count;
        public double TotalMs;
        public double MaxMs;

        public void Add(double elapsedMs)
        {
            Count++;
            TotalMs += elapsedMs;

            if (elapsedMs > MaxMs)
                MaxMs = elapsedMs;
        }

        public void Reset()
        {
            Count = 0;
            TotalMs = 0d;
            MaxMs = 0d;
        }
    }

    private static readonly object SyncRoot = new object();
    private static readonly StageAccumulator[] Stages;
    private static readonly string[] StageLabels =
    {
        "terrain height field",
        "terrain moisture",
        "terrain temperature",
        "terrain biome map",
        "terrain surface map",
        "terrain water state",
        "terrain feature plan",
        "terrain ground cover",
        "terrain control maps",
        "terrain data total",

        "far height grid",
        "far slope grid",
        "far surface map",
        "far mesh build",
        "far control maps",
        "far terrain total",

        "terrain mesh build",
        "lake mesh build",
        "river mesh build",
        "lod mesh total",
        "collider mesh build",

        "main terrain control textures",
        "main far control textures",
        "main far mesh create",
        "main lod terrain mesh create",
        "main lake mesh create",
        "main river mesh create",
        "main collider mesh create",
        "main completed-request processing",

        "foliage total",
        "foliage handle subchunk changed",
        "foliage draw visible every frame",
        "foliage grass subchunk enqueue",
        "foliage grass subchunk discovery",
        "foliage grass render batch build",
        "foliage grass draw",
        "foliage billboard grass generation",
        "foliage billboard grass batch build",
        "foliage billboard grass draw",
        "foliage flower generation",
        "foliage flower batch build",
        "foliage flower draw",
        "foliage tree generation",
        "foliage tree gameobject rebuild",
        "foliage tree billboard batch build",
        "foliage tree billboard draw",
        "foliage bush generation",
        "foliage bush gameobject rebuild",
        "foliage rock generation",
        "foliage rock gameobject rebuild"
    };

    private static readonly StringBuilder Builder = new StringBuilder(2048);
    private static bool isEnabled = true;
    private static float nextLogTime;
    private static int activeTerrainDataJobs;
    private static int activeFarTerrainJobs;
    private static int activeMeshJobs;
    private static int activeColliderJobs;
    private static int queuedTerrainDataResults;
    private static int queuedFarTerrainResults;
    private static int queuedMeshResults;
    private static int queuedColliderResults;
    private static int pendingGrassSubChunkWork;
    private static int queuedGrassSubChunks;
    private static int dirtyGrassChunks;
    private static int queuedFoliageBatchRebuilds;
    private static int queuedTreeRepresentationRebuilds;

    static TerrainGenerationProfiler()
    {
        int stageCount = StageLabels.Length;
        Stages = new StageAccumulator[stageCount];

        for (int i = 0; i < stageCount; i++)
            Stages[i] = new StageAccumulator();
    }

    public static void SetEnabled(bool enabled)
    {
        lock (SyncRoot)
        {
            isEnabled = enabled;

            if (!enabled)
                ResetLocked();
        }
    }

    public static long GetTimestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    public static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
    }

    public static void Record(TerrainGenerationProfileStage stage, long startTimestamp)
    {
        if (!isEnabled)
            return;

        RecordMs(stage, GetElapsedMilliseconds(startTimestamp));
    }

    public static void RecordMs(TerrainGenerationProfileStage stage, double elapsedMs)
    {
        if (!isEnabled)
            return;

        int index = (int)stage;

        lock (SyncRoot)
        {
            if ((uint)index >= Stages.Length)
                return;

            Stages[index].Add(elapsedMs);
        }
    }

    public static void RecordQueueSnapshot(
        int terrainDataJobs,
        int farTerrainJobs,
        int meshJobs,
        int colliderJobs,
        int terrainDataResults,
        int farTerrainResults,
        int meshResults,
        int colliderResults)
    {
        if (!isEnabled)
            return;

        lock (SyncRoot)
        {
            activeTerrainDataJobs = terrainDataJobs;
            activeFarTerrainJobs = farTerrainJobs;
            activeMeshJobs = meshJobs;
            activeColliderJobs = colliderJobs;
            queuedTerrainDataResults = terrainDataResults;
            queuedFarTerrainResults = farTerrainResults;
            queuedMeshResults = meshResults;
            queuedColliderResults = colliderResults;
        }
    }

    public static void RecordFoliageQueueSnapshot(
        int pendingGrassWork,
        int queuedGrassKeys,
        int dirtyGrassChunkCount,
        int foliageBatchRebuildCount = 0,
        int treeRepresentationRebuildCount = -1)
    {
        if (!isEnabled)
            return;

        lock (SyncRoot)
        {
            pendingGrassSubChunkWork = pendingGrassWork;
            queuedGrassSubChunks = queuedGrassKeys;
            dirtyGrassChunks = dirtyGrassChunkCount;
            queuedFoliageBatchRebuilds = foliageBatchRebuildCount;
            if (treeRepresentationRebuildCount >= 0)
                queuedTreeRepresentationRebuilds = treeRepresentationRebuildCount;
        }
    }

    public static void LogSummaryIfDue(float unscaledTime, float intervalSeconds, bool resetAfterLog)
    {
        if (!isEnabled)
            return;

        float safeInterval = Mathf.Max(0.5f, intervalSeconds);

        if (unscaledTime < nextLogTime)
            return;

        nextLogTime = unscaledTime + safeInterval;
        string summary = BuildSummary(resetAfterLog);

        if (!string.IsNullOrEmpty(summary))
            UnityEngine.Debug.Log(summary);
    }

    private static string BuildSummary(bool resetAfterBuild)
    {
        lock (SyncRoot)
        {
            bool hasSamples = false;
            Builder.Clear();
            Builder.AppendLine("[TerrainGenerationProfiler]");
            Builder.Append("active jobs: terrain=");
            Builder.Append(activeTerrainDataJobs);
            Builder.Append(" far=");
            Builder.Append(activeFarTerrainJobs);
            Builder.Append(" mesh=");
            Builder.Append(activeMeshJobs);
            Builder.Append(" collider=");
            Builder.AppendLine(activeColliderJobs.ToString());
            Builder.Append("queued results: terrain=");
            Builder.Append(queuedTerrainDataResults);
            Builder.Append(" far=");
            Builder.Append(queuedFarTerrainResults);
            Builder.Append(" mesh=");
            Builder.Append(queuedMeshResults);
            Builder.Append(" collider=");
            Builder.AppendLine(queuedColliderResults.ToString());
            Builder.Append("foliage queues: pendingGrassSubChunks=");
            Builder.Append(pendingGrassSubChunkWork);
            Builder.Append(" queuedGrassKeys=");
            Builder.Append(queuedGrassSubChunks);
            Builder.Append(" dirtyGrassChunks=");
            Builder.Append(dirtyGrassChunks);
            Builder.Append(" queuedBatchRebuilds=");
            Builder.Append(queuedFoliageBatchRebuilds);
            Builder.Append(" queuedTreeRebuilds=");
            Builder.AppendLine(queuedTreeRepresentationRebuilds.ToString());
            Builder.AppendLine("stage | count | avg ms | max ms | total ms");

            for (int i = 0; i < Stages.Length; i++)
            {
                StageAccumulator stage = Stages[i];

                if (stage.Count == 0)
                    continue;

                hasSamples = true;
                double averageMs = stage.TotalMs / stage.Count;

                Builder.Append(StageLabels[i]);
                Builder.Append(" | ");
                Builder.Append(stage.Count);
                Builder.Append(" | ");
                Builder.Append(averageMs.ToString("0.###"));
                Builder.Append(" | ");
                Builder.Append(stage.MaxMs.ToString("0.###"));
                Builder.Append(" | ");
                Builder.AppendLine(stage.TotalMs.ToString("0.###"));
            }

            if (!hasSamples)
                return null;

            string summary = Builder.ToString();

            if (resetAfterBuild)
                ResetLocked();

            return summary;
        }
    }

    private static void ResetLocked()
    {
        for (int i = 0; i < Stages.Length; i++)
            Stages[i].Reset();
    }
}
