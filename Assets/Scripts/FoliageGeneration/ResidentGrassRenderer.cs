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
    public static int GpuChunks, FallbackChunks;
#endif
    private int slotSize;
    private readonly GrassIndirectRenderer.Instance[][] cpuSlots;
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
    private Bounds bounds;
    private bool hasBounds;
    public ResidentGrassRenderer(int subCount, int cellsPerAxis, int subAxis)
    {
        slotSize = (Mathf.CeilToInt((float)cellsPerAxis / subAxis) + 3);
        slotSize *= slotSize;
        cpuSlots = new GrassIndirectRenderer.Instance[subCount][];
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
        GrassIndirectRenderer.Instance[] upload;
        using (PrepareMarker.Auto())
        {
        upload = new GrassIndirectRenderer.Instance[candidates.Count];
        forestCount -= forestCounts[index];
        meadowCount -= (int)metadata[index].x - forestCounts[index];
        forestCounts[index] = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var matrix = localToWorld * Matrix4x4.TRS(c.localPosition, c.localRotation, c.localScale);
            upload[i] = new GrassIndirectRenderer.Instance { ObjectToWorld = matrix,
                Data = new Vector4(c.forestBlend, (c.selectionRank & 0xffffffu) / 16777216f,
                    GrassStreamingPolicy.UnitRank(c.selectionRank), GrassStreamingPolicy.RepresentationRank(c.selectionRank)) };
            bool forest = c.forestBlend >= 0.5f;
            if (forest) forestCounts[index]++;
            Mesh candidateNear = forest && forestNear != null ? forestNear : near;
            Mesh candidateFar = forest && forestFar != null ? forestFar : far;
            if (candidateNear != null) IncludeBounds(GrassIndirectRenderer.Batch.TransformBounds(candidateNear.bounds, matrix));
            if (candidateFar != null) IncludeBounds(GrassIndirectRenderer.Batch.TransformBounds(candidateFar.bounds, matrix));
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
        bool splitForest = (forestNear != null && forestNearMaterial != null) || (forestFar != null && forestFarMaterial != null);
        Vector4 density = new Vector4(Mathf.Clamp01(settings.densityRadius3), Mathf.Clamp01(settings.densityRadius6),
            Mathf.Clamp01(settings.densityRadius10), Mathf.Clamp01(settings.densityBeyond10));
        float farDensity = Mathf.Clamp01(settings.billboardCoverage);
        Prepare(settings);
        bool gpu = settings.gpuIndirectRendering && settings.grassCompactShader != null &&
            settings.grassCompactShader.HasKernel("CullResidentGrass") &&
            Supports(near, nearMaterial, settings) && Supports(far, farMaterial, settings) &&
            (!splitForest || (Supports(forestNear, forestNearMaterial, settings) && Supports(forestFar, forestFarMaterial, settings)));
#if UNITY_EDITOR
        if (gpu) GpuChunks++; else FallbackChunks++;
#endif
        if (gpu)
        {
            EnsureBuffers();
            if (metadataDirty) { slots.SetData(metadata); metadataDirty = false; }
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
                if (gpu) DrawGpu(biome * 2 + lod, lod, filter, settings, mesh, material, camera, planes, viewer, subSize, outerRadius, edgeWidth, density, farDensity, force);
                else DrawFallback(lod, filter, mesh, material, camera, viewer, subSize, outerRadius, edgeWidth, density, farDensity, force, settings.receiveGrassShadows);
            }
        }
    }
    private static bool Supports(Mesh mesh, Material material, GrassSettings settings) =>
        mesh == null || material == null || GrassIndirectRenderer.IsSupported(settings.grassCompactShader, material);
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
    private void CullGpu(int channel, int representation, int forest, GrassSettings settings, Mesh mesh, Material material, Camera camera,
        Vector4[] planes, Vector3 viewer, float subSize, float radius, float edge, Vector4 density, float farDensity, int force)
    {
        if (visible[channel] == null)
        {
            visible[channel] = new ComputeBuffer(slotSize * cpuSlots.Length, 4, ComputeBufferType.Append);
            arguments[channel] = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
        }
        var shader = settings.grassCompactShader;
        int kernel = shader.FindKernel("CullResidentGrass");
        shader.SetInt("_ResidentSlotSize", slotSize); shader.SetInt("_ResidentSlotCount", cpuSlots.Length);
        shader.SetInt("_ResidentRepresentation", representation); shader.SetInt("_ResidentForce", force);
        shader.SetInt("_ResidentForest", forest);
        shader.SetVector("_ResidentViewer", viewer); shader.SetVector("_ResidentDensities", density);
        shader.SetVector("_ResidentDistances", new Vector4(subSize, radius, edge, farDensity));
        shader.SetInt("_GrassFrustumEnabled", camera != null && planes != null ? 1 : 0);
        if (planes != null) shader.SetVectorArray("_GrassFrustum", planes);
        Vector3 wind = GrassIndirectRenderer.Batch.WindPadding(material);
        shader.SetVector("_GrassMeshCenter", mesh.bounds.center); shader.SetVector("_GrassMeshExtents", mesh.bounds.extents);
        shader.SetVector("_GrassWindPadding", wind);
        visible[channel].SetCounterValue(0);
        shader.SetBuffer(kernel, "_GrassInstances", sources); shader.SetBuffer(kernel, "_ResidentSlots", slots);
        shader.SetBuffer(kernel, "_ResidentVisible", visible[channel]);
        shader.Dispatch(kernel, (slotSize * cpuSlots.Length + 63) / 64, 1, 1);
        drawArgs[0] = mesh.GetIndexCount(0); drawArgs[1] = 0; drawArgs[2] = mesh.GetIndexStart(0); drawArgs[3] = (uint)mesh.GetBaseVertex(0);
        if (argumentMeshes[channel] != mesh)
        {
            arguments[channel].SetData(drawArgs);
            argumentMeshes[channel] = mesh;
        }
        ComputeBuffer.CopyCount(visible[channel], arguments[channel], 4);
    }
    private void DrawGpu(int channel, int representation, int forest, GrassSettings settings, Mesh mesh, Material material, Camera camera,
        Vector4[] planes, Vector3 viewer, float subSize, float radius, float edge, Vector4 density, float farDensity, int force)
    {
        CullGpu(channel, representation, forest, settings, mesh, material, camera, planes, viewer, subSize, radius, edge, density, farDensity, force);
        Vector3 wind = GrassIndirectRenderer.Batch.WindPadding(material);
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
        CullGpu(channel, lod, biome, settings, mesh, material, null, null, viewer, subSize, radius, edge, density, farDensity, force);
        return ReadVisibleForValidation(channel);
    }
    public GrassIndirectRenderer.Instance[] ReadSlotForValidation(int index)
    {
        EnsureBuffers(); var result = new GrassIndirectRenderer.Instance[cpuSlots[index].Length];
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
