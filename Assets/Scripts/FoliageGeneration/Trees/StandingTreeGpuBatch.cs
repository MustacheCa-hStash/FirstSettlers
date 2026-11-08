using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// One source per standing tree and one indirect command per mesh part. The
// world still owns placement, loading, and the mesh-to-billboard handoff.
internal sealed class StandingTreeGpuBatch : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Source
    {
        public Matrix4x4 ObjectToWorld;
        public Vector4 Leaves, Bark, Appearance, Fade;
    }

    internal sealed class Draw
    {
        public Mesh Mesh;
        public Material Material;
        public int Submesh, Level;
        public bool Trunk;
        public Matrix4x4 Local;
        public readonly MaterialPropertyBlock Properties = new();
    }

    private const string Kernel = "SelectStandingTreeLods";
    private const int ArgumentSize = 5;
    private readonly ComputeShader shader;
    private readonly int kernel, levels;
    private readonly Bounds localBounds;
    private readonly float referenceScale;
    private readonly Vector4 thresholds, halfWidths, firstCommand, commandCount;
    private readonly Draw[] draws;
    private readonly uint[] template;
    private readonly List<Source> pending = new();
    private ComputeBuffer sources, inverses, visible, args;
    private int capacity;
    private Bounds bounds;
    private ChunkCoord? lastChunk;
    private float nextSample;
    private bool samplePending, disposed;
    private RenderGeometryStats sampledStats;

    public int CandidateCount => pending.Count;
    public RenderGeometryStats SampledStats => sampledStats;

    public static bool IsSupported(ComputeShader compute) =>
        compute != null && SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing &&
        SystemInfo.supportsIndirectArgumentsBuffer && SystemInfo.graphicsShaderLevel >= 45 && compute.HasKernel(Kernel);

    public StandingTreeGpuBatch(ComputeShader shader, Bounds localBounds, float referenceScale,
        float[] thresholds, float[] halfWidths, Draw[] draws)
    {
        this.shader = shader;
        kernel = shader.FindKernel(Kernel);
        this.localBounds = localBounds;
        this.referenceScale = referenceScale;
        this.draws = draws;
        levels = thresholds.Length;
        template = new uint[draws.Length * ArgumentSize];
        for (int level = 0; level < levels; ++level)
        {
            this.thresholds[level] = thresholds[level];
            this.halfWidths[level] = halfWidths[level];
        }
        for (int i = 0; i < draws.Length; ++i)
        {
            var draw = draws[i];
            if (commandCount[draw.Level] == 0) firstCommand[draw.Level] = i;
            commandCount[draw.Level]++;
            int offset = i * ArgumentSize;
            template[offset] = draw.Mesh.GetIndexCount(draw.Submesh);
            template[offset + 2] = draw.Mesh.GetIndexStart(draw.Submesh);
            template[offset + 3] = draw.Mesh.GetBaseVertex(draw.Submesh);
            var props = draw.Properties;
            props.SetFloat("_StandingTreeEnabled", 1f);
            props.SetFloat("_StandingTreeHasPartTransform", draw.Local == Matrix4x4.identity ? 0f : 1f);
            props.SetMatrix("_StandingTreePartLocal", draw.Local);
            props.SetMatrix("_StandingTreePartInverse", draw.Local.inverse);
        }
        args = new ComputeBuffer(template.Length, sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    public void BeginFrame()
    {
        pending.Clear();
        lastChunk = null;
    }

    public void Submit(ChunkCoord chunk, TreeInstanceData tree, Matrix4x4 matrix, float handoff, float load,
        Bounds? regionBounds)
    {
        var scale = tree.localScale;
        float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        bool alphaShadows = StandingTreeRenderer.AlwaysUsesAlphaShadows(tree);
        if (!regionBounds.HasValue || lastChunk != chunk)
        {
            var instanceBounds = regionBounds ?? new Bounds(matrix.MultiplyPoint3x4(localBounds.center),
                Vector3.one * (2f * localBounds.extents.magnitude * maxScale));
            if (pending.Count == 0) bounds = instanceBounds;
            else bounds.Encapsulate(instanceBounds);
            lastChunk = chunk;
        }
        pending.Add(new Source
        {
            ObjectToWorld = matrix, Leaves = (Color)tree.leafTint, Bark = (Color)tree.barkTint,
            Appearance = new Vector4(tree.snowCoverage, alphaShadows ? 1f : 0f, maxScale,
                alphaShadows ? 0f : StandingTreeRenderer.AlphaShadowRadiusScaleSquared(tree.id)),
            Fade = new Vector4(handoff, 1, load, 0)
        });
    }

    public int Render(Camera camera, Vector3 eye, float screenHeight, bool orthographic, float forestAlphaShadowDistanceSquared,
        bool castShadows, bool receiveShadows, Vector4[] frustum, bool enableMeshLods)
    {
        int count = pending.Count;
        if (count == 0) return 0;
        EnsureCapacity(count);
        sources.SetData(pending, 0, 0, count);
        args.SetData(template);
        shader.SetInt("_StandingTreeCount", count);
        shader.SetInt("_StandingTreeCapacity", capacity);
        shader.SetInt("_StandingTreeLevels", enableMeshLods ? levels : 1);
        shader.SetVector("_StandingTreeFirstCommand", firstCommand);
        shader.SetVector("_StandingTreeCommandCount", commandCount);
        shader.SetVector("_StandingTreeThresholds", thresholds);
        shader.SetVector("_StandingTreeHalfWidths", halfWidths);
        Vector3 center = localBounds.center;
        shader.SetVector("_StandingTreeCenterRadius", new Vector4(center.x, center.y, center.z, localBounds.extents.magnitude));
        shader.SetVector("_StandingTreeEyeHeight", new Vector4(eye.x, eye.y, eye.z, localBounds.size.y * referenceScale * screenHeight));
        shader.SetFloat("_StandingTreeForestAlphaDistanceSq", forestAlphaShadowDistanceSquared);
        shader.SetInt("_StandingTreeOrthographic", orthographic ? 1 : 0);
        // A camera-frustum reject is only safe when the instance can't cast a shadow.
        shader.SetInt("_StandingTreeCull", frustum != null ? 1 : 0);
        if (frustum != null) shader.SetVectorArray("_StandingTreePlanes", frustum);
        shader.SetBuffer(kernel, "_StandingTreeSources", sources);
        shader.SetBuffer(kernel, "_StandingTreeInverses", inverses);
        shader.SetBuffer(kernel, "_StandingTreeVisible", visible);
        shader.SetBuffer(kernel, "_StandingTreeArgs", args);
        shader.Dispatch(kernel, (count + 63) / 64, 1, 1);
        int drawCount = enableMeshLods ? draws.Length : (int)commandCount.x;
        for (int i = 0; i < drawCount; ++i)
        {
            var draw = draws[i];
            Graphics.DrawMeshInstancedIndirect(draw.Mesh, draw.Submesh, draw.Material, bounds, args,
                i * ArgumentSize * sizeof(uint), draw.Properties,
                castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows && draw.Trunk, 0, camera, LightProbeUsage.Off);
        }

        // The diagnostics HUD can use a recent sample. Keep the rendering
        // path free from synchronous readback and per-frame polling.
        if (Application.isPlaying && SystemInfo.supportsAsyncGPUReadback &&
            !samplePending && Time.unscaledTime >= nextSample)
        {
            samplePending = true;
            nextSample = Time.unscaledTime + .5f;
            AsyncGPUReadback.Request(args, request =>
            {
                samplePending = false;
                if (!disposed && !request.hasError)
                    sampledStats = ReadStats(request.GetData<uint>());
            });
        }
        pending.Clear();
        lastChunk = null;
        return drawCount;
    }

    private RenderGeometryStats ReadStats(Unity.Collections.NativeArray<uint> data)
    {
        var stats = new RenderGeometryStats();
        for (int i = 0; i < draws.Length; ++i)
        {
            long count = data[i * ArgumentSize + 1];
            stats.instances += count;
            stats.vertices += count * draws[i].Mesh.vertexCount;
            stats.triangles += count * (template[i * ArgumentSize] / 3);
        }
        return stats;
    }

    private void EnsureCapacity(int count)
    {
        if (count <= capacity) return;
        sources?.Release();
        inverses?.Release();
        visible?.Release();
        capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, count));
        sources = new ComputeBuffer(capacity, Marshal.SizeOf<Source>());
        inverses = new ComputeBuffer(capacity, 64);
        visible = new ComputeBuffer(capacity * levels, 32);
        foreach (var draw in draws)
        {
            var props = draw.Properties;
            props.SetBuffer("_StandingTreeSources", sources);
            props.SetBuffer("_StandingTreeInverses", inverses);
            props.SetBuffer("_StandingTreeVisible", visible);
            props.SetInt("_StandingTreeVisibleStart", draw.Level * capacity);
        }
    }

    public void Dispose()
    {
        disposed = true;
        args.Release();
        sources?.Release();
        inverses?.Release();
        visible?.Release();
    }
}
