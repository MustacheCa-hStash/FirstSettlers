public static class FoliageWorkBudget
{
    public static bool HasRemaining(long localStart, float localMs, long sharedStart, float sharedMs) =>
        (localMs <= 0 || TerrainGenerationProfiler.GetElapsedMilliseconds(localStart) < localMs) &&
        (sharedMs <= 0 || TerrainGenerationProfiler.GetElapsedMilliseconds(sharedStart) < sharedMs);

    public static bool HasTerrainInputs(ChunkRecord record) => record != null && record.HeightMap != null &&
        record.SurfaceTypeMap != null && record.BiomeMap != null;
}
