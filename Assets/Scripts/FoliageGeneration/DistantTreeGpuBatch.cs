using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Resident tree records are uploaded only when a manifest or its ecology changes.
// Each frame submits chunk/species regions, not individual trees. Sources change
// only on placement, ecology or grounding edits; compute owns distance/handoff.
public sealed class DistantTreeGpuBatch : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SourceInstance
    {
        public Matrix4x4 ObjectToWorld;
        public Vector4 Tint, Ecology, Bounds; // priority, protection rank, crowding, exposure
        public Vector4 State; // grounded height, initial density, near-mesh support (-1 = explicit handoff), unused
        public uint Region, Generation;
        private Vector2 padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RegionInstance
    {
        public Vector4 State; // load fade, explicit handoff, billboard visibility, unused
        public uint Frame;
        private Vector3 padding;
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
    private readonly Stack<int> freeRegions = new Stack<int>();
    private readonly List<int> dirtySources = new List<int>();
    private readonly List<int> dirtyRegions = new List<int>();
    private SourceInstance[] sources = Array.Empty<SourceInstance>();
    private bool[] sourceDirty = Array.Empty<bool>();
    private int[] activeSlots = Array.Empty<int>(), activeIndices = Array.Empty<int>();
    private RegionInstance[] regions = Array.Empty<RegionInstance>();
    private bool[] regionDirty = Array.Empty<bool>();
    private ComputeBuffer sourceBuffer, regionBuffer, indexBuffer, densityBuffer, evaluatedBuffer;
    private ComputeBuffer orderBuffer, groupBuffer, visibleBuffer, argsBuffer;
    private int count, activeCount, capacity, regionCount, regionCapacity;
    private int dirtyIndexFirst = int.MaxValue, dirtyIndexLast = -1;
    private uint frame, generation;
    private Bounds drawBounds;
    // Conservative region candidates; compute rejects near-only, hidden and thinned trees.
    public int SubmittedCount { get; private set; }
    public int CandidateCount => activeCount;
    public int UploadedSourceCount { get; private set; }
    public int UploadedRegionCount { get; private set; }

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

    // Explicit handoff adapter for isolated render fixtures. World rendering uses shared regions.
    public int Register(Matrix4x4 transform, Vector4 tint, Vector4 ecology, float initialDensity)
        => Register(transform, tint, ecology, initialDensity, CreateRegion(), -1f);

    public int CreateRegion()
    {
        int region = freeRegions.Count > 0 ? freeRegions.Pop() : regionCount++;
        if (regions.Length < regionCount)
        {
            Array.Resize(ref regions, Mathf.NextPowerOfTwo(Mathf.Max(16, regionCount)));
            Array.Resize(ref regionDirty, regions.Length);
        }
        regions[region] = default;
        MarkRegionDirty(region);
        return region;
    }

    // Release the region after releasing its trees. Region handles are local to this batch.
    public void ReleaseRegion(int region)
    {
        regions[region] = default;
        MarkRegionDirty(region);
        freeRegions.Push(region);
    }

    public int Register(Matrix4x4 transform, Vector4 tint, Vector4 ecology, float initialDensity,
        int region, float nearMeshSupport)
    {
        int slot = freeSlots.Count > 0 ? freeSlots.Pop() : count++;
        if (sources.Length < count)
        {
            int size = Mathf.NextPowerOfTwo(Mathf.Max(64, count));
            Array.Resize(ref sources, size);
            Array.Resize(ref sourceDirty, size);
            Array.Resize(ref activeSlots, size);
            Array.Resize(ref activeIndices, size);
        }
        sources[slot] = new SourceInstance
        {
            ObjectToWorld = transform, Tint = tint, Ecology = ecology,
            Bounds = CalculateBounds(mesh.bounds, transform, material),
            State = new Vector4(transform.m13, Mathf.Clamp01(initialDensity), nearMeshSupport, 0f),
            Region = (uint)region, Generation = ++generation
        };
        activeIndices[slot] = activeCount;
        activeSlots[activeCount] = slot;
        MarkIndexDirty(activeCount++);
        MarkDirty(slot);
        return slot;
    }

    public void Release(int slot)
    {
        if (slot < 0) return;
        if (sources[slot].State.z < 0f) ReleaseRegion((int)sources[slot].Region);
        int index = activeIndices[slot];
        int last = activeSlots[--activeCount];
        activeSlots[index] = last;
        activeIndices[last] = index;
        if (index < activeCount) MarkIndexDirty(index);
        freeSlots.Push(slot);
    }

    public void BeginFrame()
    {
        SubmittedCount = 0;
        UploadedSourceCount = UploadedRegionCount = 0;
        frame++;
    }

    public void Submit(int slot, float height, float loadFade, float handoffFade, Vector4 ecology)
    {
        UpdateSource(slot, height, ecology);
        var m = sources[slot].ObjectToWorld;
        var b = sources[slot].Bounds;
        var bounds = new Bounds(new Vector3(m.m03, height + (b.y + b.z) * .5f, m.m23),
            new Vector3(b.x * 2f, b.z - b.y, b.x * 2f));
        SubmitRegion((int)sources[slot].Region, loadFade, bounds, 1, true, handoffFade);
    }

    public void UpdateSource(int slot, float height, Vector4 ecology)
    {
        if (sources[slot].Ecology != ecology || sources[slot].State.x != height)
        {
            sources[slot].Ecology = ecology;
            sources[slot].State.x = height;
            MarkDirty(slot);
        }
    }

    public void SubmitRegion(int region, float loadFade, Bounds bounds, int treeCount,
        bool visible, float explicitHandoff = 1f)
    {
        regions[region] = new RegionInstance
        {
            State = new Vector4(loadFade, explicitHandoff, visible ? 1f : 0f, 0f), Frame = frame
        };
        MarkRegionDirty(region);
        if (SubmittedCount == 0) drawBounds = bounds;
        else drawBounds.Encapsulate(bounds);
        SubmittedCount += treeCount;
    }

    public static Bounds RenderingBounds(Bounds local, Material material)
    {
        if ((material.shader.name == "Custom/SpruceOctaImpostor" || material.shader.name == "Custom/TreeHQOctaImpostor"))
        {
            // The selected capture's projected root is anchored to the tree
            // origin. Include both the rotating square and its root-to-center
            // displacement, rather than bounding the old center-pivot quad.
            Vector3 center = material.GetVector("_CaptureCenterLS");
            float radius = Mathf.Abs(material.GetFloat("_CaptureRadius")) * Mathf.Sqrt(2f) + center.magnitude;
            return new Bounds(Vector3.zero, Vector3.one * (radius * 2f));
        }
        float horizontal = Mathf.Max(Mathf.Abs(local.min.x), Mathf.Abs(local.max.x),
            Mathf.Abs(local.min.z), Mathf.Abs(local.max.z));
        return new Bounds(new Vector3(0f, local.center.y, 0f),
            new Vector3(horizontal * 2f, local.size.y, horizontal * 2f));
    }

    // Includes both rotated fixed-plane geometry and upright camera-facing cards,
    // non-uniform scale, and mesh offsets. Bounds are relative to the tree origin.
    public static Vector4 CalculateBounds(Bounds local, Matrix4x4 transform, Material material = null)
    {
        if (material != null && (material.shader.name == "Custom/SpruceOctaImpostor" || material.shader.name == "Custom/TreeHQOctaImpostor"))
        {
            Vector3 captureCenter = material.GetVector("_CaptureCenterLS");
            // Match the shader: its billboard uses the X-column length as a
            // uniform radius, even if an override supplies non-uniform TRS.
            float r = (Mathf.Abs(material.GetFloat("_CaptureRadius")) * Mathf.Sqrt(2f) + captureCenter.magnitude) *
                ((Vector3)transform.GetColumn(0)).magnitude;
            // Wind is disabled by the distant manager. Include its world-space
            // displacement when this helper is used with a standalone material.
            float captureRadius = Mathf.Max(Mathf.Abs(material.GetFloat("_CaptureRadius")), 0.0001f);
            float windWeight = 0.5f + captureCenter.magnitude / (2f * captureRadius);
            r += Mathf.Abs(material.GetFloat("_WindStrength")) * windWeight * new Vector3(1f, 0f, 0.35f).magnitude;
            return new Vector4(r, -r, r, 0);
        }
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
        if (sourceDirty[slot]) return;
        sourceDirty[slot] = true;
        dirtySources.Add(slot);
    }

    private void MarkRegionDirty(int region)
    {
        if (regionDirty[region]) return;
        regionDirty[region] = true;
        dirtyRegions.Add(region);
    }

    private void MarkIndexDirty(int index)
    {
        dirtyIndexFirst = Mathf.Min(dirtyIndexFirst, index);
        dirtyIndexLast = Mathf.Max(dirtyIndexLast, index);
    }

    public void Draw(TreeSettings settings, Camera camera, Vector3 viewer, Vector4[] frustum,
        float maxDistance, float outerWidth, float thinStart, float fadeStep,
        float meshStart = 0f, float meshEnd = 1f)
    {
        if (SubmittedCount == 0) return;
        EnsureCapacity();
        dirtySources.Sort();
        for (int i = 0; i < dirtySources.Count;)
        {
            int first = dirtySources[i], last = first;
            sourceDirty[first] = false;
            while (++i < dirtySources.Count && dirtySources[i] == last + 1)
            { last = dirtySources[i]; sourceDirty[last] = false; }
            sourceBuffer.SetData(sources, first, first, last - first + 1);
            UploadedSourceCount += last - first + 1;
        }
        dirtySources.Clear();
        if (dirtyIndexLast >= dirtyIndexFirst)
        {
            int last = Mathf.Min(activeCount - 1, dirtyIndexLast);
            if (last >= dirtyIndexFirst)
                indexBuffer.SetData(activeSlots, dirtyIndexFirst, dirtyIndexFirst, last - dirtyIndexFirst + 1);
            dirtyIndexFirst = int.MaxValue; dirtyIndexLast = -1;
        }
        dirtyRegions.Sort();
        for (int i = 0; i < dirtyRegions.Count;)
        {
            int first = dirtyRegions[i], last = first;
            regionDirty[first] = false;
            while (++i < dirtyRegions.Count && dirtyRegions[i] == last + 1)
            { last = dirtyRegions[i]; regionDirty[last] = false; }
            regionBuffer.SetData(regions, first, first, last - first + 1);
            UploadedRegionCount += last - first + 1;
        }
        dirtyRegions.Clear();
        int groups = (activeCount + 63) / 64;
        shader.SetInt("_DistantTreeFrame", unchecked((int)frame));
        shader.SetVector("_DistantTreeHandoff", new Vector4(meshStart, meshEnd, 0f, 0f));
        shader.SetInt("_DistantTreeCandidateCount", activeCount);
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
        shader.SetBuffer(cullKernel, "_DistantTreeRegions", regionBuffer);
        shader.SetBuffer(cullKernel, "_DistantTreeActiveSlots", indexBuffer);
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
        properties.SetFloat("_DistantTreeEnabled", 1f);
        properties.SetFloat("_DistantTreeBillboard", 1f);
        properties.SetBuffer("_DistantTreeInstances", visibleBuffer);
        Graphics.DrawMeshInstancedIndirect(mesh, 0, material, drawBounds, argsBuffer, 0,
            properties, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
    }

    private void EnsureCapacity()
    {
        if (regionCapacity < regionCount)
        {
            regionCapacity = Mathf.NextPowerOfTwo(Mathf.Max(16, regionCount));
            regionBuffer?.Release();
            regionBuffer = new ComputeBuffer(regionCapacity, Marshal.SizeOf<RegionInstance>());
            for (int i = 0; i < regionCount; i++) MarkRegionDirty(i);
        }
        if (capacity >= count) return;
        int previousCapacity = capacity;
        capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, count));
        ComputeBuffer previousDensity = densityBuffer;
        sourceBuffer?.Release(); indexBuffer?.Release(); evaluatedBuffer?.Release();
        orderBuffer?.Release(); groupBuffer?.Release(); visibleBuffer?.Release(); argsBuffer?.Release();
        sourceBuffer = new ComputeBuffer(capacity, Marshal.SizeOf<SourceInstance>());
        indexBuffer = new ComputeBuffer(capacity, 4);
        densityBuffer = new ComputeBuffer(capacity, 8);
        // Never depend on driver-specific initial buffer contents for generation matching.
        densityBuffer.SetData(new Vector2[capacity]);
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
        for (int i = 0; i < count; i++) MarkDirty(i);
        dirtyIndexFirst = 0; dirtyIndexLast = activeCount - 1;
    }

    public void Dispose()
    {
        sourceBuffer?.Release(); regionBuffer?.Release(); indexBuffer?.Release(); densityBuffer?.Release(); evaluatedBuffer?.Release();
        orderBuffer?.Release(); groupBuffer?.Release(); visibleBuffer?.Release(); argsBuffer?.Release();
        sourceBuffer = regionBuffer = indexBuffer = densityBuffer = evaluatedBuffer = null;
        orderBuffer = groupBuffer = visibleBuffer = argsBuffer = null;
        capacity = 0;
    }

#if UNITY_EDITOR
    // Explicit correctness checks only. Never read GPU counts during normal rendering.
    public VisibleInstance[] ReadVisibleForValidation()
    {
        if (SubmittedCount == 0 || argsBuffer == null) return Array.Empty<VisibleInstance>();
        var args = new uint[5];
        argsBuffer.GetData(args);
        var result = new VisibleInstance[args[1]];
        if (result.Length > 0) visibleBuffer.GetData(result, 0, 0, result.Length);
        return result;
    }
#endif
}
