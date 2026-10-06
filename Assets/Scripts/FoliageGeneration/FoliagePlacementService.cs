using UnityEngine;

/// <summary>Converts authoritative world-feature plans to instance data and registers detailed tree identities.</summary>
public sealed class FoliagePlacementService
{
    private readonly TreeSettings treeSettings;
    private readonly TreeRegistry treeRegistry;
    private readonly int worldSeed, chunkSize;
    private readonly float worldScale, meshHeightMultiplier;
    public FoliagePlacementService(WorldGenerationConfiguration generation, TreeSettings settings, TreeRegistry registry)
    { worldSeed = generation.Seed; chunkSize = generation.ChunkSize; worldScale = generation.WorldScale;
      meshHeightMultiplier = generation.MeshHeightMultiplier; treeSettings = settings; treeRegistry = registry; }
    public void PrepareDependencies(ChunkRecord record, FoliagePublicationKind kind)
    {
        if (kind != FoliagePublicationKind.LilyPad && kind != FoliagePublicationKind.Cattail) EnsureTreesGenerated(record);
        if (kind != FoliagePublicationKind.Flower && kind != FoliagePublicationKind.LilyPad && kind != FoliagePublicationKind.Cattail)
        { EnsureBushesGenerated(record); EnsureRocksGenerated(record); }
    }
    public void EnsureTreesGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.treeCubesGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateTreeCubesForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageTreeGeneration,
                stageStart);
        }
        treeRegistry?.RegisterChunk(record.ChunkCoord, record.FoliageData.treeCubeInstances,
            TreePlacementDetail.Detailed, record.FoliageData.TreeRevision);
    }

    public void EnsureBushesGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.bushesGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateBushesForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageBushGeneration,
                stageStart);
        }
    }

    public void EnsureRocksGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.rocksGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateRocksForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageRockGeneration,
                stageStart);
        }
    }
}
