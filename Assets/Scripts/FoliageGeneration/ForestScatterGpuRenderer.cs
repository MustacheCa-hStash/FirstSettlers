using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Stable records stay resident. Only appended generation results and changed
// chunk visibility/ranges are uploaded; compute chooses all LOD lists at once.
public sealed class ForestScatterGpuRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Source
    {
        public Matrix4x4 Transform;
        public Vector4 Tint, Scatter, Sphere, Selection; // near LOD rank, chunk slot, density rank, coarse LOD rank
    }
    sealed class Tile
    {
        public int Slot, Start, Capacity, Count;
        public Bounds Bounds;
        public LeafClusterGeneration Generation;
    }
    readonly ComputeShader compute;
    readonly int kernel;
    readonly Dictionary<ChunkCoord, Tile> tiles = new Dictionary<ChunkCoord, Tile>();
    readonly Dictionary<int, Stack<int>> freeBlocks = new Dictionary<int, Stack<int>>();
    readonly Stack<int> freeSlots = new Stack<int>();
    Source[] records = Array.Empty<Source>();
    Vector4[] metadata = Array.Empty<Vector4>();
    readonly Vector4[] planes = new Vector4[6];
    readonly uint[] args = new uint[5];
    readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    ComputeBuffer sources, chunks, unusedCoarse;
    readonly ComputeBuffer[] visible, arguments;
    int highWater, slotCount, gpuCapacity, gpuSlotCapacity;
    bool metadataDirty;
    public int ResidentInstances { get; private set; }
    public int SourceUploads { get; private set; }
    public int DrawCalls { get; private set; }
    public static bool Supports(ComputeShader shader, Material near, Material far, Material coarse = null) =>
        shader != null && SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing &&
        SystemInfo.supportsIndirectArgumentsBuffer && SystemInfo.graphicsShaderLevel >= 45 &&
        shader.HasKernel("CullForestScatter") && Compatible(near) && (far == null || Compatible(far)) && (coarse == null || Compatible(coarse));
    static bool Compatible(Material material) => material != null && string.Equals(
        material.GetTag("ForestScatterIndirect", false, "False"), "True", StringComparison.OrdinalIgnoreCase);
    public ForestScatterGpuRenderer(ComputeShader shader,int lodCount=2)
    {
        compute = shader; kernel = shader.FindKernel("CullForestScatter");
        visible=new ComputeBuffer[Mathf.Clamp(lodCount,2,3)];arguments=new ComputeBuffer[visible.Length];
    }

    public void Remove(ChunkCoord coord)
    {
        if (!tiles.TryGetValue(coord, out var tile)) return;
        tiles.Remove(coord); ResidentInstances -= tile.Count;
        metadata[tile.Slot] = Vector4.zero; metadataDirty = true;
        freeSlots.Push(tile.Slot); Free(tile.Start, tile.Capacity);
    }
    void Free(int start, int capacity)
    {
        if (!freeBlocks.TryGetValue(capacity, out var blocks)) freeBlocks.Add(capacity, blocks = new Stack<int>());
        blocks.Push(start);
    }
    int Allocate(int capacity)
    {
        if (freeBlocks.TryGetValue(capacity, out var blocks) && blocks.Count > 0) return blocks.Pop();
        int start = highWater; highWater += capacity;
        if (records.Length < highWater) Array.Resize(ref records, Mathf.NextPowerOfTwo(highWater));
        return start;
    }
    public void Sync(ChunkCoord coord, LeafClusterGeneration generation, bool active, float radius, bool fern)
    {
        if (tiles.TryGetValue(coord, out var tile) && !ReferenceEquals(tile.Generation, generation)) { Remove(coord); tile = null; }
        int count = generation.Instances.Count;
        if (tile == null)
        {
            int slot = freeSlots.Count > 0 ? freeSlots.Pop() : slotCount++;
            if (metadata.Length < slotCount) Array.Resize(ref metadata, Mathf.NextPowerOfTwo(Mathf.Max(16, slotCount)));
            tile = new Tile { Slot = slot, Generation = generation, Capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, count)) };
            tile.Start = Allocate(tile.Capacity); tiles.Add(coord, tile);
        }
        int from = tile.Count;
        if (count > tile.Capacity)
        {
            int capacity = Mathf.NextPowerOfTwo(count), start = Allocate(capacity);
            Array.Copy(records, tile.Start, records, start, tile.Count);
            Free(tile.Start, tile.Capacity); tile.Start = start; tile.Capacity = capacity;
            from = 0; // Relocated stable records are uploaded once with the new tail.
        }
        for (int i = tile.Count; i < count; i++)
        {
            var instance = generation.Instances[i];
            records[tile.Start + i] = new Source {
                Transform = Matrix4x4.TRS(instance.position, instance.rotation, Vector3.one * instance.scale),
                Tint = instance.tint, Scatter = fern ? Vector4.zero : LeafClusterSystem.ScatterParams(instance.rank),
                Sphere = new Vector4(instance.position.x, instance.position.y, instance.position.z, radius * instance.scale),
                Selection = new Vector4(LeafClusterGeneration.Unit(instance.rank, 193), tile.Slot,
                    LeafClusterGeneration.Unit(instance.rank,277),LeafClusterGeneration.Unit(instance.rank,307)) };
            var b = new Bounds(instance.position, Vector3.one * (2 * radius * instance.scale));
            if (i == 0) tile.Bounds = b; else tile.Bounds.Encapsulate(b);
        }
        ResidentInstances += count - tile.Count; tile.Count = count;
        bool rebuilt = EnsureBuffers();
        if (!rebuilt && count > from)
        {
            sources.SetData(records, tile.Start + from, tile.Start + from, count - from); SourceUploads++;
        }
        var state = new Vector4(tile.Start, count, active ? 1 : 0, 0);
        if (metadata[tile.Slot] != state) { metadata[tile.Slot] = state; metadataDirty = true; }
    }
    bool EnsureBuffers()
    {
        bool rebuilt = false;
        if (gpuCapacity < records.Length)
        {
            sources?.Release(); sources = new ComputeBuffer(records.Length, Marshal.SizeOf<Source>());
            sources.SetData(records); SourceUploads++; gpuCapacity = records.Length; rebuilt = true;
            if(visible.Length==2 && unusedCoarse==null)unusedCoarse=new ComputeBuffer(1,4,ComputeBufferType.Append);
            for (int i = 0; i < visible.Length; i++)
            {
                visible[i]?.Release(); visible[i] = new ComputeBuffer(gpuCapacity, 4, ComputeBufferType.Append);
                if (arguments[i] == null) arguments[i] = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
            }
        }
        if (gpuSlotCapacity < metadata.Length)
        {
            chunks?.Release(); chunks = new ComputeBuffer(metadata.Length, 16);
            gpuSlotCapacity = metadata.Length; metadataDirty = true;
        }
        return rebuilt;
    }
    public void Draw(Mesh near, Material nearMaterial, Matrix4x4 nearLocal, Mesh far, Material farMaterial,
        Matrix4x4 farLocal, Camera camera, Plane[] frustum, Vector3 viewer, float range, float fade, float lodStart, float lodEnd,
        Mesh coarse=null,Material coarseMaterial=null,Matrix4x4? coarseLocal=null,float coarseStart=40,float coarseEnd=55,
        float densityRingSize=0,Vector4 densityRings=default)
    {
        DrawCalls = 0;
        if (sources == null) return;
        for (int i = 0; i < visible.Length; i++) visible[i].SetCounterValue(0);
        if (ResidentInstances == 0) { ClearCounts(); return; }
        bool hasFar = far != null && farMaterial != null;
        bool hasCoarse=hasFar && visible.Length==3 && coarse!=null && coarseMaterial!=null;
        Bounds bounds = default; bool any = false;
        foreach (var tile in tiles.Values)
        {
            if (tile.Count == 0 || metadata[tile.Slot].z == 0) continue;
            if (!any) { bounds = tile.Bounds; any = true; } else bounds.Encapsulate(tile.Bounds);
        }
        if (!any) { ClearCounts(); return; }
        if (metadataDirty) { chunks.SetData(metadata); metadataDirty = false; }
        for (int i = 0; i < 6; i++) planes[i] = new Vector4(frustum[i].normal.x, frustum[i].normal.y, frustum[i].normal.z, frustum[i].distance);
        compute.SetInt("_ForestSourceCount", highWater); compute.SetInt("_ForestChunkCount", slotCount);
        compute.SetInt("_ForestFrustumEnabled", camera != null ? 1 : 0); compute.SetInt("_ForestHasFar", hasFar ? 1 : 0);
        compute.SetInt("_ForestHasCoarse",hasCoarse?1:0);
        compute.SetVectorArray("_ForestFrustum", planes); compute.SetVector("_ForestViewer", viewer);
        compute.SetVector("_ForestDistances", new Vector4(range, lodStart, Mathf.Max(lodStart + .01f, lodEnd), 0));
        compute.SetVector("_ForestCoarseDistances",new Vector4(coarseStart,Mathf.Max(coarseStart+.01f,coarseEnd),Mathf.Max(.001f,densityRingSize),0));
        compute.SetVector("_ForestDensityRings",densityRingSize>0?densityRings:Vector4.one);
        compute.SetBuffer(kernel, "_ForestSources", sources); compute.SetBuffer(kernel, "_ForestChunks", chunks);
        compute.SetBuffer(kernel, "_ForestNear", visible[0]); compute.SetBuffer(kernel, "_ForestFar", visible[1]);
        compute.SetBuffer(kernel,"_ForestCoarse",visible.Length==3?visible[2]:unusedCoarse);
        compute.Dispatch(kernel, (highWater + 63) / 64, 1, 1);
        int lodCount=hasCoarse?3:hasFar?2:1;
        for (int lod = 0; lod < lodCount; lod++)
        {
            Mesh mesh = lod == 0 ? near : lod==1?far:coarse; Material material = lod == 0 ? nearMaterial : lod==1?farMaterial:coarseMaterial;
            args[0] = mesh.GetIndexCount(0); args[1] = 0; args[2] = mesh.GetIndexStart(0); args[3] = (uint)mesh.GetBaseVertex(0); args[4] = 0;
            arguments[lod].SetData(args); ComputeBuffer.CopyCount(visible[lod], arguments[lod], 4);
            properties.Clear(); properties.SetBuffer("_ForestSources", sources); properties.SetBuffer("_ForestVisible", visible[lod]);
            properties.SetMatrix("_ForestMeshLocal", lod == 0 ? nearLocal : lod==1?farLocal:coarseLocal??Matrix4x4.identity);
            properties.SetFloat("_FadeStart", Mathf.Max(0, range - fade)); properties.SetFloat("_FadeEnd", range);
            properties.SetVector("_LeafViewer", new Vector4(viewer.x, viewer.y, viewer.z, 1));
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, arguments[lod], 0, properties,
                ShadowCastingMode.Off, true, 0, camera, LightProbeUsage.Off); DrawCalls++;
        }
        for(int lod=lodCount;lod<visible.Length;lod++) ComputeBuffer.CopyCount(visible[lod],arguments[lod],4);
    }
    void ClearCounts() { for (int i = 0; i < visible.Length; i++) ComputeBuffer.CopyCount(visible[i], arguments[i], 4); }
#if UNITY_EDITOR
    // Readback is only used by explicit correctness checks, never runtime frames.
    public uint[] ReadVisible(int lod)
    {
        var values = new uint[5]; arguments[lod].GetData(values);
        var result = new uint[values[1]]; if (result.Length > 0) visible[lod].GetData(result, 0, 0, result.Length); return result;
    }
    public Source ReadSource(uint index) => records[index];
#endif
    public void Dispose()
    {
        sources?.Release(); chunks?.Release(); unusedCoarse?.Release();
        for (int i = 0; i < visible.Length; i++) { visible[i]?.Release(); arguments[i]?.Release(); }
        tiles.Clear(); freeBlocks.Clear(); freeSlots.Clear();
    }
}
