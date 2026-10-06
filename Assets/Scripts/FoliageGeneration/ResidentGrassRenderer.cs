using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

// One resident arena per chunk, with fixed subchunk slots and separate biome/LOD draws.
// Uploading a completed subchunk never repacks/reuploads its neighbors.
public sealed class ResidentGrassRenderer : IDisposable
{
    private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("FS.Streaming.Grass.UploadOneTile");
    private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("FS.Streaming.Grass.PrepareUploadInstances");
    private static readonly ProfilerMarker SetDataMarker = new ProfilerMarker("FS.Streaming.Grass.UploadSetData");
    private static readonly ProfilerMarker ResizeMarker = new ProfilerMarker("FS.Streaming.Grass.ReleaseForSlotGrowth");
#if UNITY_EDITOR
    public int SlotSizeForValidation => slotSize;
    public void CullAllForValidation(GrassSettings settings, Mesh[] meshes, Material[] materials,
        Vector4[] planes, Vector3 viewer, float subSize, float radius, float edge, Vector4 density, float farDensity)
    {
        EnsureBuffers();
        if (metadataDirty) { slots.SetData(metadata); metadataDirty = false; }
        LastCullDispatchCount = 0;
        CullAll(settings, meshes[0], materials[0], meshes[1], materials[1], meshes[2], materials[2],
            meshes[3], materials[3], planes, viewer, subSize, radius, edge, density, farDensity);
    }
    public static int GpuChunks, FallbackChunks;
#endif
    private int slotSize;
    private readonly GrassRenderUtility.Instance[][] cpuSlots;
    private readonly Vector4[] metadata;
    private readonly int[] forestCounts;
    private int forestCount, meadowCount;
    private readonly Matrix4x4[] fallbackMatrices = new Matrix4x4[1023];
    private readonly Vector4[] fallbackData = new Vector4[1023];
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private ComputeBuffer sources, slots;
    private readonly ComputeBuffer[] visible = new ComputeBuffer[4], arguments = new ComputeBuffer[4];
    private bool metadataDirty;
    private readonly Mesh[] argumentMeshes = new Mesh[4];
    private readonly Mesh[] channelMeshes = new Mesh[4];
    private readonly Material[] channelMaterials = new Material[4];
    private readonly Vector4[] centers = new Vector4[4], extents = new Vector4[4], winds = new Vector4[4];
    private ComputeBuffer unusedVisible;
    public int LastCullDispatchCount { get; private set; }
    private Bounds bounds;
    private bool hasBounds;
    public ResidentGrassRenderer(int subCount, int cellsPerAxis, int subAxis)
    {
        slotSize = (Mathf.CeilToInt((float)cellsPerAxis / subAxis) + 3);
        slotSize *= slotSize;
        cpuSlots = new GrassRenderUtility.Instance[subCount][];
        metadata = new Vector4[subCount];
        forestCounts = new int[subCount];
    }
    public bool HasSlot(int index) => cpuSlots[index] != null;
    public void Upload(int index, List<FoliageInstanceData> candidates, Matrix4x4 localToWorld, Mesh near, Mesh far,
        Mesh forestNear = null, Mesh forestFar = null)
    {
        using (UploadMarker.Auto())
        {
        if (candidates.Count > slotSize) { using (ResizeMarker.Auto()) ReleaseBuffers(); slotSize = Mathf.NextPowerOfTwo(candidates.Count); }
        GrassRenderUtility.Instance[] upload;
        using (PrepareMarker.Auto())
        {
        upload = new GrassRenderUtility.Instance[candidates.Count];
        forestCount -= forestCounts[index];
        meadowCount -= (int)metadata[index].x - forestCounts[index];
        forestCounts[index] = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var matrix = localToWorld * Matrix4x4.TRS(c.localPosition, c.localRotation, c.localScale);
            upload[i] = new GrassRenderUtility.Instance { ObjectToWorld = matrix,
                Data = new Vector4(c.forestBlend, (c.selectionRank & 0xffffffu) / 16777216f,
                    GrassStreamingPolicy.UnitRank(c.selectionRank), GrassStreamingPolicy.RepresentationRank(c.selectionRank)) };
            bool forest = c.forestBlend >= 0.5f;
            if (forest) forestCounts[index]++;
            Mesh candidateNear = forest && forestNear != null ? forestNear : near;
            Mesh candidateFar = forest && forestFar != null ? forestFar : far;
            if (candidateNear != null) IncludeBounds(GrassRenderUtility.TransformBounds(candidateNear.bounds, matrix));
            if (candidateFar != null) IncludeBounds(GrassRenderUtility.TransformBounds(candidateFar.bounds, matrix));
        }
        // CPU copy remains available for hardware/material fallback and buffer recreation.
        cpuSlots[index] = upload;
        metadata[index].x = upload.Length;
        forestCount += forestCounts[index];
        meadowCount += upload.Length - forestCounts[index];
        metadataDirty = true;
        }
        if (sources != null && upload.Length > 0)
        {
            using (SetDataMarker.Auto()) sources.SetData(upload, 0, index * slotSize, upload.Length);
        }
        }
    }
    private void IncludeBounds(Bounds b) { if (!hasBounds) { bounds = b; hasBounds = true; } else bounds.Encapsulate(b); }
    public void SetBlend(int index, float blend)
    {
        if (metadata[index].y == blend) return;
        metadata[index].y = blend; metadataDirty = true;
    }
    public void Draw(GrassSettings settings, Mesh near, Material nearMaterial, Mesh far, Material farMaterial,
        Camera camera, Vector4[] planes, Vector3 viewer, float subSize, float outerRadius, float edgeWidth,
        Mesh forestNear = null, Material forestNearMaterial = null, Mesh forestFar = null, Material forestFarMaterial = null)
    {
        if (!hasBounds) return;
        LastCullDispatchCount = 0;
        bool splitForest = (forestNear != null && forestNearMaterial != null) || (forestFar != null && forestFarMaterial != null);
        Vector4 density = new Vector4(Mathf.Clamp01(settings.densityRadius3), Mathf.Clamp01(settings.densityRadius6),
            Mathf.Clamp01(settings.densityRadius10), Mathf.Clamp01(settings.densityBeyond10));
        float farDensity = Mathf.Clamp01(settings.billboardCoverage);
        Prepare(settings);
        bool gpu = settings.gpuIndirectRendering && settings.grassCompactShader != null &&
            settings.grassCompactShader.HasKernel("CullResidentGrassAll") &&
            Supports(near, nearMaterial, settings) && Supports(far, farMaterial, settings) &&
            (!splitForest || (Supports(forestNear, forestNearMaterial, settings) && Supports(forestFar, forestFarMaterial, settings)));
#if UNITY_EDITOR
        if (gpu) GpuChunks++; else FallbackChunks++;
#endif
        if (gpu)
        {
            EnsureBuffers();
            if (metadataDirty) { slots.SetData(metadata); metadataDirty = false; }
            CullAll(settings, near, nearMaterial, far, farMaterial, forestNear, forestNearMaterial,
                forestFar, forestFarMaterial, camera != null ? planes : null, viewer, subSize,
                outerRadius, edgeWidth, density, farDensity);
        }
        else
        {
            ReleaseBuffers();
        }
        for (int biome = 0; biome < (splitForest ? 2 : 1); biome++)
        {
            if (splitForest && (biome == 0 ? meadowCount : forestCount) == 0) continue;
            bool useForestNear = biome == 1 && forestNear != null && forestNearMaterial != null;
            bool useForestFar = biome == 1 && forestFar != null && forestFarMaterial != null;
            Mesh n = useForestNear ? forestNear : near, f = useForestFar ? forestFar : far;
            Material nm = useForestNear ? forestNearMaterial : nearMaterial, fm = useForestFar ? forestFarMaterial : farMaterial;
            bool nearReady = n != null && nm != null, farReady = f != null && fm != null;
            for (int lod = 0; lod < 2; lod++)
            {
                if (lod == 0 ? !nearReady : !farReady) continue;
                Mesh mesh = lod == 0 ? n : f;
                Material material = lod == 0 ? nm : fm;
                int force = nearReady && farReady ? -1 : lod;
                int filter = splitForest ? biome : -1;
                if (gpu) DrawGpu(biome * 2 + lod, settings, mesh, material, camera);
                else DrawFallback(lod, filter, mesh, material, camera, viewer, subSize, outerRadius, edgeWidth, density, farDensity, force, settings.receiveGrassShadows);
            }
        }
    }
    private static bool Supports(Mesh mesh, Material material, GrassSettings settings) =>
        mesh == null || material == null || GrassRenderUtility.IsSupported(settings.grassCompactShader, material);
    private void Prepare(GrassSettings s)
    {
        properties.Clear();
        properties.SetColor("_ForestDarkGrassColor", s.forestDarkGrassColor);
        properties.SetColor("_ForestMidGrassColor", s.forestMidGrassColor);
        properties.SetColor("_ForestLightGrassColor", s.forestLightGrassColor);
        properties.SetFloat("_ReceiveShadows", s.receiveGrassShadows ? 1 : 0);
        properties.SetFloat("_RenderFadeEnabled", 0); properties.SetFloat("_RenderFadeProgress", 1);
    }
    private void EnsureBuffers()
    {
        if (sources != null) return;
        int capacity = slotSize * cpuSlots.Length;
        sources = new ComputeBuffer(capacity, 80);
        slots = new ComputeBuffer(cpuSlots.Length, 16);
        for (int i = 0; i < cpuSlots.Length; i++)
            if (cpuSlots[i] != null && cpuSlots[i].Length > 0) sources.SetData(cpuSlots[i], 0, i * slotSize, cpuSlots[i].Length);
        metadataDirty = true;
    }
    private readonly uint[] drawArgs = new uint[5];

    private void CullAll(GrassSettings settings, Mesh near, Material nearMaterial, Mesh far, Material farMaterial,
        Mesh forestNear, Material forestNearMaterial, Mesh forestFar, Material forestFarMaterial,
        Vector4[] planes, Vector3 viewer, float subSize, float radius, float edge, Vector4 density, float farDensity, int forceOverride = -1)
    {
        bool split = (forestNear != null && forestNearMaterial != null) || (forestFar != null && forestFarMaterial != null);
        channelMeshes[0] = near; channelMaterials[0] = nearMaterial;
        channelMeshes[1] = far; channelMaterials[1] = farMaterial;
        channelMeshes[2] = forestNear != null && forestNearMaterial != null ? forestNear : near;
        channelMaterials[2] = forestNear != null && forestNearMaterial != null ? forestNearMaterial : nearMaterial;
        channelMeshes[3] = forestFar != null && forestFarMaterial != null ? forestFar : far;
        channelMaterials[3] = forestFar != null && forestFarMaterial != null ? forestFarMaterial : farMaterial;
        int mask = 0;
        Vector4 forces = new Vector4(-1, -1, 0, 0);
        for (int biome = 0; biome < (split ? 2 : 1); biome++)
        {
            bool n = channelMeshes[biome * 2] != null && channelMaterials[biome * 2] != null;
            bool f = channelMeshes[biome * 2 + 1] != null && channelMaterials[biome * 2 + 1] != null;
            forces[biome] = forceOverride >= 0 ? forceOverride : n && f ? -1 : n ? 0 : 1;
            if (split && (biome == 0 ? meadowCount : forestCount) == 0) continue;
            for (int lod = 0; lod < 2; lod++)
            {
                int channel = biome * 2 + lod;
                if (lod == 0 ? !n : !f) continue;
                mask |= 1 << channel;
                Mesh mesh = channelMeshes[channel];
                centers[channel] = mesh.bounds.center; extents[channel] = mesh.bounds.extents;
                winds[channel] = GrassRenderUtility.WindPadding(channelMaterials[channel]);
                if (visible[channel] == null)
                {
                    visible[channel] = new ComputeBuffer(slotSize * cpuSlots.Length, 4, ComputeBufferType.Append);
                    arguments[channel] = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
                }
                if (argumentMeshes[channel] != mesh)
                {
                    drawArgs[0] = mesh.GetIndexCount(0); drawArgs[1] = 0;
                    drawArgs[2] = mesh.GetIndexStart(0); drawArgs[3] = (uint)mesh.GetBaseVertex(0);
                    arguments[channel].SetData(drawArgs); argumentMeshes[channel] = mesh;
                }
            }
        }
        unusedVisible ??= new ComputeBuffer(1, 4, ComputeBufferType.Append);
        for (int i = 0; i < 4; i++) visible[i]?.SetCounterValue(0);
        var shader = settings.grassCompactShader;
        int kernel = shader.FindKernel("CullResidentGrassAll");
        shader.SetInt("_ResidentSlotSize", slotSize); shader.SetInt("_ResidentSlotCount", cpuSlots.Length);
        shader.SetInt("_ResidentSplitForest", split ? 1 : 0); shader.SetInt("_ResidentChannels", mask);
        shader.SetVector("_ResidentForces", forces);
        shader.SetVector("_ResidentViewer", viewer); shader.SetVector("_ResidentDensities", density);
        shader.SetVector("_ResidentDistances", new Vector4(subSize, radius, edge, farDensity));
        shader.SetInt("_GrassFrustumEnabled", planes != null ? 1 : 0);
        if (planes != null) shader.SetVectorArray("_GrassFrustum", planes);
        shader.SetVectorArray("_ResidentCenters", centers); shader.SetVectorArray("_ResidentExtents", extents);
        shader.SetVectorArray("_ResidentWind", winds);
        shader.SetBuffer(kernel, "_GrassInstances", sources); shader.SetBuffer(kernel, "_ResidentSlots", slots);
        shader.SetBuffer(kernel, "_ResidentMeadowNear", visible[0] ?? unusedVisible);
        shader.SetBuffer(kernel, "_ResidentMeadowFar", visible[1] ?? unusedVisible);
        shader.SetBuffer(kernel, "_ResidentForestNear", visible[2] ?? unusedVisible);
        shader.SetBuffer(kernel, "_ResidentForestFar", visible[3] ?? unusedVisible);
        if (mask != 0) { shader.Dispatch(kernel, (slotSize * cpuSlots.Length + 63) / 64, 1, 1); LastCullDispatchCount++; }
        for (int i = 0; i < 4; i++)
            if (visible[i] != null) ComputeBuffer.CopyCount(visible[i], arguments[i], 4);
    }
    private void DrawGpu(int channel, GrassSettings settings, Mesh mesh, Material material, Camera camera)
    {
        Vector3 wind = GrassRenderUtility.WindPadding(material);
        properties.SetBuffer("_GrassInstances", sources); properties.SetBuffer("_GrassVisibleIndices", visible[channel]);
        Bounds b = bounds; b.Expand(wind * 2);
        Graphics.DrawMeshInstancedIndirect(mesh, 0, material, b, arguments[channel], 0, properties,
            ShadowCastingMode.Off, settings.receiveGrassShadows, 0, camera, LightProbeUsage.Off);
    }
    private void DrawFallback(int representation, int forest, Mesh mesh, Material material, Camera camera, Vector3 viewer,
        float subSize, float radius, float edge, Vector4 density, float farDensity, int force, bool shadows)
    {
        int count = 0;
        for (int slot = 0; slot < cpuSlots.Length; slot++)
        {
            if (cpuSlots[slot] == null) continue;
            foreach (var instance in cpuSlots[slot])
            {
                if (forest >= 0 && (instance.Data.x >= 0.5f ? 1 : 0) != forest) continue;
                Vector3 p = instance.ObjectToWorld.GetColumn(3);
                float distance = new Vector2(p.x - viewer.x, p.z - viewer.z).magnitude;
                int selected = GrassStreamingPolicy.Select(instance.Data.z, instance.Data.w, distance, subSize, density,
                    farDensity, force < 0 ? metadata[slot].y : force, radius, edge);
                if (selected != representation) continue;
                fallbackMatrices[count] = instance.ObjectToWorld; fallbackData[count++] = instance.Data;
                if (count == 1023) { DrawFallbackBatch(mesh, material, camera, count, shadows); count = 0; }
            }
        }
        if (count > 0) DrawFallbackBatch(mesh, material, camera, count, shadows);
    }
    private void DrawFallbackBatch(Mesh mesh, Material material, Camera camera, int count, bool shadows)
    {
        properties.SetVectorArray("_GrassInstanceData", fallbackData);
        Graphics.DrawMeshInstanced(mesh, 0, material, fallbackMatrices, count, properties, ShadowCastingMode.Off, shadows, 0, camera, LightProbeUsage.Off);
    }
    private void ReleaseBuffers()
    {
        sources?.Release(); slots?.Release();
        unusedVisible?.Release(); unusedVisible = null;
        for (int i = 0; i < 4; i++) { visible[i]?.Release(); arguments[i]?.Release(); visible[i] = arguments[i] = null; argumentMeshes[i] = null; }
        sources = slots = null;
    }
    public void Dispose() => ReleaseBuffers();
#if UNITY_EDITOR
    // Dispatch/readback only: validates selection without rendering a scene or creating a camera.
    public uint[] CullForValidation(int biome, int lod, GrassSettings settings, Mesh mesh, Material material,
        Vector3 viewer, float subSize, float radius, float edge, Vector4 density, float farDensity, int force = -1)
    {
        EnsureBuffers();
        if (metadataDirty) { slots.SetData(metadata); metadataDirty = false; }
        int channel = (biome < 0 ? 0 : biome) * 2 + lod;
        CullAll(settings, mesh, material, mesh, material, biome >= 0 ? mesh : null,
            biome >= 0 ? material : null, biome >= 0 ? mesh : null, biome >= 0 ? material : null,
            null, viewer, subSize, radius, edge, density, farDensity, force);
        return ReadVisibleForValidation(channel);
    }
    public GrassRenderUtility.Instance[] ReadSlotForValidation(int index)
    {
        EnsureBuffers(); var result = new GrassRenderUtility.Instance[cpuSlots[index].Length];
        if (result.Length > 0) sources.GetData(result, 0, index * slotSize, result.Length);
        return result;
    }
    public uint[] ReadVisibleForValidation(int representation)
    {
        var args = new uint[5]; arguments[representation].GetData(args); var result = new uint[args[1]];
        if (result.Length > 0) visible[representation].GetData(result, 0, 0, result.Length); return result;
    }
#endif
}
