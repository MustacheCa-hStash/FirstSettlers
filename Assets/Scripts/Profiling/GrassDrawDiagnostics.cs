using System.Diagnostics;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

namespace FirstSettlers.Profiling
{
    // Main-thread event totals. Unity owns the native cells and flushes/resets them each frame.
    // Calls compile out of non-profiler players. No logging, formatted names or per-event allocations.
    public static unsafe class GrassDrawDiagnostics
    {
        // Explicit initialization attributes first-use registration to InitializeCounters when drawing
        // initializes first; resource events may also initialize these counters before the first draw.
        static GrassDrawDiagnostics() { }
        [Conditional("ENABLE_PROFILER")]
        public static void Initialize() { }
        public const string GpuChunksName = "FS Grass GPU Chunks";
        public const string FallbackChunksName = "FS Grass Fallback Chunks";
        public const string BuffersCreatedName = "FS Grass Buffers Created";
        public const string BuffersReleasedName = "FS Grass Buffers Released";
        public const string UploadsName = "FS Grass Buffer Uploads";
        public const string DispatchesName = "FS Grass Cull Dispatches";
        public const string IndirectDrawsName = "FS Grass Indirect Draw Calls";
        public const string FallbackDrawsName = "FS Grass Fallback Draw Calls";
        public const string FallbackCandidatesName = "FS Grass Fallback Candidate Visits";
#if ENABLE_PROFILER
        private static readonly int* GpuChunks = Create(GpuChunksName);
        private static readonly int* FallbackChunks = Create(FallbackChunksName);
        private static readonly int* BuffersCreated = Create(BuffersCreatedName);
        private static readonly int* BuffersReleased = Create(BuffersReleasedName);
        private static readonly int* Uploads = Create(UploadsName);
        private static readonly int* Dispatches = Create(DispatchesName);
        private static readonly int* IndirectDraws = Create(IndirectDrawsName);
        private static readonly int* FallbackDraws = Create(FallbackDrawsName);
        private static readonly int* FallbackCandidates = Create(FallbackCandidatesName);
        private static int* Create(string name) => (int*)ProfilerUnsafeUtility.CreateCounterValue(
            out _, name, ProfilerCategory.Scripts, MarkerFlags.Counter,
            (byte)ProfilerMarkerDataType.Int32, (byte)ProfilerMarkerDataUnit.Count, sizeof(int),
            ProfilerCounterOptions.FlushOnEndOfFrame | ProfilerCounterOptions.ResetToZeroOnFlush);

        private static void Add(int* counter, int count = 1)
        {
            if (counter != null) *counter += count;
        }
#endif
        [Conditional("ENABLE_PROFILER")]
        public static void CountChunk(bool gpu)
        {
#if ENABLE_PROFILER
            if (gpu) Add(GpuChunks); else Add(FallbackChunks);
#endif
        }

        [Conditional("ENABLE_PROFILER")]
        public static void CountBufferCreated()
        {
#if ENABLE_PROFILER
            Add(BuffersCreated);
#endif
        }

        [Conditional("ENABLE_PROFILER")]
        public static void CountBufferReleased()
        {
#if ENABLE_PROFILER
            Add(BuffersReleased);
#endif
        }

        [Conditional("ENABLE_PROFILER")]
        public static void CountUpload()
        {
#if ENABLE_PROFILER
            Add(Uploads);
#endif
        }

        [Conditional("ENABLE_PROFILER")]
        public static void CountDispatch()
        {
#if ENABLE_PROFILER
            Add(Dispatches);
#endif
        }

        [Conditional("ENABLE_PROFILER")]
        public static void CountIndirectDraw()
        {
#if ENABLE_PROFILER
            Add(IndirectDraws);
#endif
        }

        [Conditional("ENABLE_PROFILER")]
        public static void CountFallbackDraw()
        {
#if ENABLE_PROFILER
            Add(FallbackDraws);
#endif
        }

        [Conditional("ENABLE_PROFILER")]
        public static void CountFallbackCandidates(int count)
        {
#if ENABLE_PROFILER
            Add(FallbackCandidates, count);
#endif
        }
    }
}
