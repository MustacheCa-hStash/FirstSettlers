using System;
using Unity.Profiling;
using UnityEngine;

[Serializable]
public sealed class ForestLightingSettings
{
    public bool enabled;
    public Color leafColor = new(.34f, .41f, .28f);
    [Range(0, 1)] public float leafPaletteBlend = .82f;
    public Color barkColor = new(.37f, .34f, .30f);
    [Range(0, 1)] public float barkPaletteBlend = .83f;
    [Range(0, 1), Tooltip("Reduces fill light and leaf transmission inside crowns.")]
    public float crownDarkness = .52f;
    [Range(0, 1), Tooltip("Reduces outdoor light beneath overlapping canopies.")]
    public float groundDarkness = .55f;
}

// A coarse, world-aligned canopy mask. Its source is standing placements, independent
// of the camera-facing billboards retained for the current frame.
internal sealed class ForestCanopyShade : IDisposable
{
    const int Resolution = 256;
    const int SpanChunks = 32;
    const int RecenterChunks = 4;
    static readonly ProfilerMarker RebuildMarker = new("FS.ForestCanopy.Rebuild");
    static readonly int BoundsId = Shader.PropertyToID("_FSForestCanopyBounds");
    static readonly int MaskId = Shader.PropertyToID("_FSForestCanopyMask");
    readonly TreeRegistry registry;
    readonly float chunkSize, texelSize;
    readonly float[] crownRadii;
    readonly byte[] pixels = new byte[Resolution * Resolution];
    readonly Texture2D mask;
    Vector2Int origin;
    bool dirty = true;
    float nextUpdate;

    public ForestCanopyShade(TreeRegistry registry, TreeSettings settings)
    {
        this.registry = registry;
        chunkSize = registry.ChunkWorldSize;
        texelSize = SpanChunks * chunkSize / Resolution;
        crownRadii = new float[Enum.GetValues(typeof(WorldFeatureVariant)).Length];
        foreach (var species in TreeSpeciesCatalog.All)
        {
            var prefab = species.BillboardPrefab(settings);
            var renderer = prefab ? prefab.GetComponentInChildren<MeshRenderer>() : null;
            var material = renderer ? renderer.sharedMaterial : null;
            if (material && material.HasProperty("_CaptureRadius"))
                crownRadii[(int)species.Variant] = material.GetFloat("_CaptureRadius") * .65f;
        }
        mask = new Texture2D(Resolution, Resolution, TextureFormat.R8, false, true)
        {
            name = "Forest canopy shade",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        registry.ChunkChanged += MarkDirty;
    }

    void MarkDirty(ChunkCoord coord)
    {
        if (coord.x >= origin.x - 1 && coord.x <= origin.x + SpanChunks &&
            coord.z >= origin.y - 1 && coord.z <= origin.y + SpanChunks)
            dirty = true;
    }

    public void Update(Vector3 viewer)
    {
        var center = new Vector2Int(Mathf.FloorToInt(viewer.x / (chunkSize * RecenterChunks)) * RecenterChunks,
            Mathf.FloorToInt(viewer.z / (chunkSize * RecenterChunks)) * RecenterChunks);
        var nextOrigin = center - Vector2Int.one * (SpanChunks / 2);
        if ((!dirty && origin == nextOrigin) || Time.unscaledTime < nextUpdate) return;
        using var sample = RebuildMarker.Auto();
        nextUpdate = Time.unscaledTime + .3f;
        origin = nextOrigin;
        dirty = false;
        Array.Clear(pixels, 0, pixels.Length);
        for (int z = origin.y - 1; z <= origin.y + SpanChunks; z++)
        for (int x = origin.x - 1; x <= origin.x + SpanChunks; x++)
        {
            var trees = registry.GetChunk(new ChunkCoord(x, z));
            for (int i = 0; i < trees.Count; i++)
            {
                var tree = trees[i];
                if (tree.State == TreeState.Standing) Stamp(tree);
            }
        }
        mask.SetPixelData(pixels, 0);
        mask.Apply(false, false);
        Shader.SetGlobalTexture(MaskId, mask);
        Shader.SetGlobalVector(BoundsId, new Vector4(origin.x * chunkSize, origin.y * chunkSize,
            1f / (SpanChunks * chunkSize), 0f));
    }

    void Stamp(TreeRecord tree)
    {
        var placement = tree.Placement;
        float authoredRadius = crownRadii[(int)placement.variant] * Mathf.Max(placement.localScale.x, placement.localScale.z);
        float radius = Mathf.Clamp(Mathf.Max(authoredRadius, tree.ExclusionRadiusWorld), 3.5f, 27f) / texelSize;
        float cx = (tree.WorldPosition.x - origin.x * chunkSize) / texelSize - .5f;
        float cz = (tree.WorldPosition.z - origin.y * chunkSize) / texelSize - .5f;
        int maxX = Mathf.Min(Resolution - 1, Mathf.CeilToInt(cx + radius));
        int maxZ = Mathf.Min(Resolution - 1, Mathf.CeilToInt(cz + radius));
        for (int z = Mathf.Max(0, Mathf.FloorToInt(cz - radius)); z <= maxZ; z++)
        for (int x = Mathf.Max(0, Mathf.FloorToInt(cx - radius)); x <= maxX; x++)
        {
            float dx = x - cx, dz = z - cz;
            float weight = 1f - Mathf.Clamp01((dx * dx + dz * dz) / (radius * radius));
            int stamp = Mathf.RoundToInt(weight * weight * 220);
            int index = z * Resolution + x;
            pixels[index] = (byte)(255 - (255 - pixels[index]) * (255 - stamp) / 255);
        }
    }

    public void Dispose()
    {
        registry.ChunkChanged -= MarkDirty;
        UnityEngine.Object.Destroy(mask);
    }
}
