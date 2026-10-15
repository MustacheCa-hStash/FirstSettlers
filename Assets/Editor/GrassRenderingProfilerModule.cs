using System;
using FirstSettlers.Profiling;
using Unity.Profiling;
using Unity.Profiling.Editor;

/// <summary>Per-frame grass path and resource events beside the CPU marker breakdown.</summary>
[Serializable]
[ProfilerModuleMetadata("First Settlers Grass")]
public sealed class GrassRenderingProfilerModule : ProfilerModule
{
    private static readonly ProfilerCounterDescriptor[] Counters =
    {
        new(GrassDrawDiagnostics.GpuChunksName, ProfilerCategory.Scripts),
        new(GrassDrawDiagnostics.FallbackChunksName, ProfilerCategory.Scripts),
        new(GrassDrawDiagnostics.BuffersCreatedName, ProfilerCategory.Scripts),
        new(GrassDrawDiagnostics.BuffersReleasedName, ProfilerCategory.Scripts),
        new(GrassDrawDiagnostics.UploadsName, ProfilerCategory.Scripts),
        new(GrassDrawDiagnostics.DispatchesName, ProfilerCategory.Scripts),
        new(GrassDrawDiagnostics.IndirectDrawsName, ProfilerCategory.Scripts),
        new(GrassDrawDiagnostics.FallbackDrawsName, ProfilerCategory.Scripts),
        new(GrassDrawDiagnostics.FallbackCandidatesName, ProfilerCategory.Scripts)
    };

    public GrassRenderingProfilerModule() : base(Counters, ProfilerModuleChartType.Line,
        autoEnabledCategoryNames: new[] { "Scripts" }) { }
}
