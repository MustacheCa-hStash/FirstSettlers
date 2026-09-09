using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Resident tree records are uploaded only when a manifest or its ecology changes.
// Each frame uploads just height/load/handoff state; compute selects and orders trees.
public sealed class DistantTreeGpuBatch : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SourceInstance
    {
        public Matrix4x4 ObjectToWorld;
        public Vector4 Tint, Ecology, Bounds; // priority, protection rank, crowding, exposure
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VisibleInstance
    {
        public Matrix4x4 ObjectToWorld;
        public Vector4 Tint, Fade;
    }

    private readonly ComputeShader shader;
    private readonly int cullKernel, prefixKernel, scatterKernel, copyKernel;
    private readonly Mesh mesh;
    private readonly Material material;
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private readonly Stack<int> freeSlots = new Stack<int>();
    private SourceInstance[] sources = Array.Empty<SourceInstance>();
    private Vector4[] states = Array.Empty<Vector4>();
    private ComputeBuffer sourceBuffer, stateBuffer, densityBuffer, evaluatedBuffer;
    private ComputeBuffer orderBuffer, groupBuffer, visibleBuffer, argsBuffer;
    private int count, capacity, dirtyFirst = int.MaxValue, dirtyLast = -1;
    private Bounds drawBounds;
    public int SubmittedCount { get; private set; }

    public static bool IsSupported(ComputeShader compute)
    {
        return compute != null && SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing &&
            SystemInfo.supportsIndirectArgumentsBuffer && SystemInfo.graphicsShaderLevel >= 45 &&
            compute.HasKernel("CompactDistantTrees") && compute.HasKernel("PrefixDistantTrees") &&
            compute.HasKernel("ScatterDistantTrees") && compute.HasKernel("CopyDistantTreeDensity");
    }

    public DistantTreeGpuBatch(ComputeShader compute, Mesh mesh, Material material)
    {
        shader = compute;
        this.mesh = mesh;
        this.material = material;
        cullKernel = shader.FindKernel("CompactDistantTrees");
        prefixKernel = shader.FindKernel("PrefixDistantTrees");
        scatterKernel = shader.FindKernel("ScatterDistantTrees");
        copyKernel = shader.FindKernel("CopyDistantTreeDensity");
    }

    public int Register(Matrix4x4 transform, Vector4 tint, Vector4 ecology, float initialDensity)
    {
        int slot = freeSlots.Count > 0 ? freeSlots.Pop() : count++;
        if (sources.Length < count)
        {
            int size = Mathf.NextPowerOfTwo(Mathf.Max(64, count));
            Array.Resize(ref sources, size);
            Array.Resize(ref states, size);
        }
        sources[slot] = new SourceInstance
        {
            ObjectToWorld = transform, Tint = tint, Ecology = ecology,
            Bounds = CalculateBounds(mesh.bounds, transform)
        };
        // Positive w resets GPU density on the first dispatch, including reused slots.
        states[slot].w = 1f + Mathf.Clamp01(initialDensity);
        MarkDirty(slot);
        return slot;
    }

    public void Release(int slot)
    {
        if (slot < 0) return;
        states[slot] = Vector4.zero;
        freeSlots.Push(slot);
    }

    public void BeginFrame()
    {
        SubmittedCount = 0;
        for (int i = 0; i < count; i++) states[i].z = 0f;
    }

    public void Submit(int slot, float height, float loadFade, float handoffFade, Vector4 ecology)
    {
        if (sources[slot].Ecology != ecology)
        {
            sources[slot].Ecology = ecology;
            MarkDirty(slot);
        }
        states[slot] = new Vector4(height, loadFade, handoffFade, states[slot].w);
        Vector4 b = sources[slot].Bounds;
        Matrix4x4 m = sources[slot].ObjectToWorld;
        Vector3 low = new Vector3(m.m03 - b.x, height + b.y, m.m23 - b.x);
        Vector3 high = new Vector3(m.m03 + b.x, height + b.z, m.m23 + b.x);
        if (SubmittedCount++ == 0) drawBounds = new Bounds((low + high) * 0.5f, high - low);
        else { drawBounds.Encapsulate(low); drawBounds.Encapsulate(high); }
    }

    // Includes both rotated fixed-plane geometry and upright camera-facing cards,
    // non-uniform scale, and mesh offsets. Bounds are relative to the tree origin.
    public static Vector4 CalculateBounds(Bounds local, Matrix4x4 transform)
    {
        Vector3 center = transform.MultiplyVector(local.center);
        Vector3 e = local.extents;
        Vector3 extents = new Vector3(
            Mathf.Abs(transform.m00) * e.x + Mathf.Abs(transform.m01) * e.y + Mathf.Abs(transform.m02) * e.z,
            Mathf.Abs(transform.m10) * e.x + Mathf.Abs(transform.m11) * e.y + Mathf.Abs(transform.m12) * e.z,
            Mathf.Abs(transform.m20) * e.x + Mathf.Abs(transform.m21) * e.y + Mathf.Abs(transform.m22) * e.z);
        float sy = ((Vector3)transform.GetColumn(1)).magnitude;
        float sz = ((Vector3)transform.GetColumn(2)).magnitude;
        float cardRadius = Mathf.Max(Mathf.Abs(local.min.x), Mathf.Abs(local.max.x),
            Mathf.Abs(local.min.z), Mathf.Abs(local.max.z)) * sz;
        float radius = Mathf.Max(cardRadius, Mathf.Abs(center.x) + extents.x, Mathf.Abs(center.z) + extents.z);
        return new Vector4(radius, Mathf.Min(center.y - extents.y, local.min.y * sy),
            Mathf.Max(center.y + extents.y, local.max.y * sy), 0f);
    }

    private void MarkDirty(int slot)
    {
        dirtyFirst = Mathf.Min(dirtyFirst, slot);
        dirtyLast = Mathf.Max(dirtyLast, slot);
    }

    public void Draw(TreeSettings settings, Camera camera, Vector3 viewer, Vector4[] frustum,
        float maxDistance, float outerWidth, float thinStart, float fadeStep)
    {
        if (SubmittedCount == 0) return;
        EnsureCapacity();
        if (dirtyLast >= dirtyFirst)
        {
            sourceBuffer.SetData(sources, dirtyFirst, dirtyFirst, dirtyLast - dirtyFirst + 1);
            dirtyFirst = int.MaxValue; dirtyLast = -1;
        }
        stateBuffer.SetData(states, 0, 0, count);
        int groups = (count + 63) / 64;
        shader.SetInt("_DistantTreeCandidateCount", count);
        shader.SetInt("_DistantTreeGroupCount", groups);
        shader.SetInt("_DistantTreeBandCount", camera != null && settings.distantTreeDepthOrdering
            ? Mathf.Clamp(settings.distantTreeDepthBands, 4, 64) : 1);
        shader.SetInt("_DistantTreeFrustumEnabled", camera != null ? 1 : 0);
        shader.SetVectorArray("_DistantTreeFrustum", frustum);
        shader.SetVector("_DistantTreeViewer", viewer);
        shader.SetVector("_DistantTreeCamera", camera != null ? camera.transform.position : viewer);
        shader.SetVector("_DistantTreeForward", camera != null ? camera.transform.forward : Vector3.forward);
        shader.SetVector("_DistantTreeRange", new Vector4(maxDistance, outerWidth, thinStart, fadeStep));
        shader.SetVector("_DistantTreeDensitySettings", new Vector4(settings.distantTreeDensity,
            settings.distantTreeCrowdingThreshold, settings.distantTreeEdgeProtection,
            settings.distantTreeDensityAware ? 1f : 0f));
        shader.SetInt("_DistantTreeProtectedCount", Mathf.Max(0, settings.distantTreeProtectedCount));
        // Depth bands span the visible range relative to the camera, including camera/viewer offsets.
        shader.SetFloat("_DistantTreeDepthRange", Mathf.Max(1f, maxDistance +
            (camera != null ? Vector3.Distance(camera.transform.position, viewer) : 0f)));
        shader.SetBuffer(cullKernel, "_DistantTreeSources", sourceBuffer);
        shader.SetBuffer(cullKernel, "_DistantTreeStates", stateBuffer);
        shader.SetBuffer(cullKernel, "_DistantTreeDensity", densityBuffer);
        shader.SetBuffer(cullKernel, "_DistantTreeEvaluated", evaluatedBuffer);
        shader.SetBuffer(cullKernel, "_DistantTreeOrder", orderBuffer);
        shader.SetBuffer(cullKernel, "_DistantTreeGroups", groupBuffer);
        shader.Dispatch(cullKernel, groups, 1, 1);
        shader.SetBuffer(prefixKernel, "_DistantTreeGroups", groupBuffer);
        shader.SetBuffer(prefixKernel, "_DistantTreeArgs", argsBuffer);
        shader.Dispatch(prefixKernel, 1, 1, 1);
        shader.SetBuffer(scatterKernel, "_DistantTreeEvaluated", evaluatedBuffer);
        shader.SetBuffer(scatterKernel, "_DistantTreeOrder", orderBuffer);
        shader.SetBuffer(scatterKernel, "_DistantTreeGroups", groupBuffer);
        shader.SetBuffer(scatterKernel, "_DistantTreeVisible", visibleBuffer);
        shader.Dispatch(scatterKernel, groups, 1, 1);
        for (int i = 0; i < count; i++) states[i].w = 0f;
        properties.SetFloat("_DistantTreeEnabled", 1f);
        properties.SetFloat("_DistantTreeBillboard", 1f);
        properties.SetBuffer("_DistantTreeInstances", visibleBuffer);
        Graphics.DrawMeshInstancedIndirect(mesh, 0, material, drawBounds, argsBuffer, 0,
            properties, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
    }

    private void EnsureCapacity()
    {
        if (capacity >= count) return;
        int previousCapacity = capacity;
        capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, count));
        ComputeBuffer previousDensity = densityBuffer;
        sourceBuffer?.Release(); stateBuffer?.Release(); evaluatedBuffer?.Release();
        orderBuffer?.Release(); groupBuffer?.Release(); visibleBuffer?.Release(); argsBuffer?.Release();
        sourceBuffer = new ComputeBuffer(capacity, Marshal.SizeOf<SourceInstance>());
        stateBuffer = new ComputeBuffer(capacity, 16);
        densityBuffer = new ComputeBuffer(capacity, 4);
        evaluatedBuffer = new ComputeBuffer(capacity, Marshal.SizeOf<VisibleInstance>());
        orderBuffer = new ComputeBuffer(capacity, 8);
        groupBuffer = new ComputeBuffer(((capacity + 63) / 64) * 64, 4);
        visibleBuffer = new ComputeBuffer(capacity, Marshal.SizeOf<VisibleInstance>());
        argsBuffer = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(new uint[] { mesh.GetIndexCount(0), 0, mesh.GetIndexStart(0),
            (uint)mesh.GetBaseVertex(0), 0 });
        if (previousDensity != null)
        {
            shader.SetInt("_DistantTreePreviousCapacity", previousCapacity);
            shader.SetBuffer(copyKernel, "_DistantTreeOldDensity", previousDensity);
            shader.SetBuffer(copyKernel, "_DistantTreeDensity", densityBuffer);
            shader.Dispatch(copyKernel, (previousCapacity + 63) / 64, 1, 1);
            previousDensity.Release();
        }
        dirtyFirst = 0; dirtyLast = count - 1;
    }

    public void Dispose()
    {
        sourceBuffer?.Release(); stateBuffer?.Release(); densityBuffer?.Release(); evaluatedBuffer?.Release();
        orderBuffer?.Release(); groupBuffer?.Release(); visibleBuffer?.Release(); argsBuffer?.Release();
        sourceBuffer = stateBuffer = densityBuffer = evaluatedBuffer = null;
        orderBuffer = groupBuffer = visibleBuffer = argsBuffer = null;
        capacity = 0;
    }

#if UNITY_EDITOR
    // Explicit correctness checks only. Never read GPU counts during normal rendering.
    public VisibleInstance[] ReadVisibleForValidation()
    {
        var args = new uint[5];
        argsBuffer.GetData(args);
        var result = new VisibleInstance[args[1]];
        if (result.Length > 0) visibleBuffer.GetData(result, 0, 0, result.Length);
        return result;
    }
#endif
}
