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
    [Range(0, 1.34f), Tooltip("Reduces fill light and leaf transmission inside crowns. Above 1 deepens partial shade.")]
    public float crownDarkness = .52f;
    [Range(0, 8), Tooltip("Reduces ambient light beneath overlapping canopies. Above 1 deepens partial shade.")]
    public float groundDarkness = .55f;
    [Range(0, 1), Tooltip("How much of the canopy ground shade also attenuates direct sunlight.")]
    public float groundDirectShade = 1f;
    [Range(.5f, 8.5f), Tooltip("Local direct-light multiplier for sun patches on the forest floor.")]
    public float groundSunStrength = 1f;
    [Range(1f, 2f), Tooltip("Daylight multiplier on sunlit grass and open terrain, including sand.")]
    public float openGroundSun = 1f;
    [Range(1f, 2f), Tooltip("Daylight sky-fill multiplier on grass and open terrain. Fades under forest cover.")]
    public float openGroundAmbient = 1f;
    [Range(0f, 1f), Tooltip("Directional ambient light on tree bark and crowns, derived from nearby canopy openings.")]
    public float directionalSkyFill;
    [Range(0f, 3f), Tooltip("Strength of sunlight-dependent transmission on the prototype oak and beech materials. Zero restores their previous leaf lighting.")]
    public float leafTransmission;
    [Range(0f, 3f), Tooltip("Strength of the short screen-space sun shadows around grass, stones and roots. Higher values are useful for judging coverage. Zero disables the pass unless Preview Ground Contact Mask is on.")]
    public float groundContactShadows;
    [Range(.15f, 3f), Tooltip("Maximum length of a ground contact shadow ray, in metres.")]
    public float groundContactLength = .7f;
    [Range(8f, 80f), Tooltip("Distance in metres at which ground contact shadows finish fading out. Higher values increase the area traced.")]
    public float groundContactRange = 24f;
    [Tooltip("Mark valid ground contact shadows in cyan in the Game view. This preview works with the shadow strength at zero.")]
    public bool previewGroundContactMask;
    [Range(0f, 2f), Tooltip("Direct sunlight removed by the soft forest shadow field beyond the main shadow cascades. Higher values deepen sparse coverage.")]
    public float distantForestShadows;
    [Range(140f, 650f), Tooltip("Outer fade distance in metres for projected forest shadows. Coverage also depends on the streamed trees and the bounds of the shadow field.")]
    public float distantForestShadowRange = 600f;
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
    static readonly int SkyId = Shader.PropertyToID("_FSForestSkyField");
    static readonly int DistantFieldId = Shader.PropertyToID("_FSDistantForestShadowField");
    static readonly int DistantBoundsId = Shader.PropertyToID("_FSDistantForestShadowBounds");
    static readonly int DistantProjectionId = Shader.PropertyToID("_FSDistantForestShadowProjection");
    readonly TreeRegistry registry;
    readonly float chunkSize, texelSize;
    readonly float[] crownRadii, crownHeights;
    readonly byte[] pixels = new byte[Resolution * Resolution];
    readonly Texture2D mask;
    Texture2D skyField;
    Color32[] skyPixels;
    Texture2D distantField;
    Vector2[] distantPixels;
    Vector2 distantOrigin, distantSlope;
    bool distantDirty = true;
    float nextDistantUpdate;
    Vector2Int origin;
    bool dirty = true;
    bool hadSkyField;
    float nextUpdate;
    public bool HasSkyField => skyField != null;

    public ForestCanopyShade(TreeRegistry registry, TreeSettings settings)
    {
        this.registry = registry;
        chunkSize = registry.ChunkWorldSize;
        texelSize = SpanChunks * chunkSize / Resolution;
        crownRadii = new float[Enum.GetValues(typeof(WorldFeatureVariant)).Length];
        crownHeights = new float[crownRadii.Length];
        foreach (var species in TreeSpeciesCatalog.All)
        {
            var prefab = species.BillboardPrefab(settings);
            var renderer = prefab ? prefab.GetComponentInChildren<MeshRenderer>() : null;
            var material = renderer ? renderer.sharedMaterial : null;
            if (material && material.HasProperty("_CaptureRadius"))
                crownRadii[(int)species.Variant] = material.GetFloat("_CaptureRadius") * .65f;
            if (material && material.HasProperty("_CaptureCenterLS"))
                crownHeights[(int)species.Variant] = material.GetVector("_CaptureCenterLS").y;
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
        if (coord.x < origin.x - 1 || coord.x > origin.x + SpanChunks ||
            coord.z < origin.y - 1 || coord.z > origin.y + SpanChunks) return;
        dirty = distantDirty = true;
    }

    public bool UpdateDistantShadows(Vector3 viewer, Vector3 sun)
    {
        if (sun.y <= .12f)
        {
            distantDirty = true;
            return false;
        }
        var slope = new Vector2(sun.x, sun.z) / Mathf.Max(.22f, sun.y);
        var projectedViewer = new Vector2(viewer.x, viewer.z) - slope * viewer.y;
        float span = texelSize * Resolution;
        var corner = projectedViewer - Vector2.one * (span * .5f);
        var nextOrigin = new Vector2(Mathf.Round(corner.x / texelSize), Mathf.Round(corner.y / texelSize)) * texelSize;
        bool recenter = (nextOrigin - distantOrigin).sqrMagnitude >= chunkSize * chunkSize;
        bool sunMoved = (slope - distantSlope).sqrMagnitude > .09f * .09f;
        if (distantField != null && (!distantDirty && !recenter && !sunMoved || Time.unscaledTime < nextDistantUpdate))
            return true;

        using var sample = DistantRebuildMarker.Auto();
        if (distantField == null)
        {
            distantField = new Texture2D(Resolution, Resolution, TextureFormat.RGFloat, false, true)
            {
                name = "Projected distant forest shadows",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            distantPixels = new Vector2[pixels.Length];
        }
        distantDirty = false;
        nextDistantUpdate = Time.unscaledTime + .65f;
        distantOrigin = nextOrigin;
        distantSlope = slope;
        Array.Clear(distantPixels, 0, distantPixels.Length);
        float slopeLength = slope.magnitude;
        Vector2 axis = slopeLength > .001f ? slope / slopeLength : Vector2.right;
        for (int z = origin.y - 1; z <= origin.y + SpanChunks; z++)
        for (int x = origin.x - 1; x <= origin.x + SpanChunks; x++)
        {
            var trees = registry.GetChunk(new ChunkCoord(x, z));
            for (int i = 0; i < trees.Count; i++)
                if (trees[i].State == TreeState.Standing) StampDistantShadow(trees[i], axis, slopeLength);
        }
        distantField.SetPixelData(distantPixels, 0);
        distantField.Apply(false, false);
        Shader.SetGlobalTexture(DistantFieldId, distantField);
        Shader.SetGlobalVector(DistantBoundsId, new Vector4(distantOrigin.x, distantOrigin.y, 1f / span, 0f));
        Shader.SetGlobalVector(DistantProjectionId, new Vector4(slope.x, slope.y, 0f, 0f));
        return true;
    }

    static readonly ProfilerMarker DistantRebuildMarker = new("FS.ForestCanopy.ProjectShadows");

    void StampDistantShadow(TreeRecord tree, Vector2 axis, float slopeLength)
    {
        var p = tree.Placement;
        float radius = CrownRadius(tree) * .74f;
        float height = crownHeights[(int)p.variant] * Mathf.Abs(p.localScale.y);
        if (height <= 0f) height = radius * 1.5f;
        float crownY = tree.WorldPosition.y + height;
        float top = crownY + Mathf.Max(2f, height * .45f);
        var projected = new Vector2(tree.WorldPosition.x, tree.WorldPosition.z) - distantSlope * crownY;
        var center = (projected - distantOrigin) / texelSize - Vector2.one * .5f;
        float major = (radius + slopeLength * height * .33f) / texelSize;
        float minor = radius / texelSize;
        Vector2 along = axis / major;
        Vector2 across = new Vector2(-axis.y, axis.x) / minor;
        float opacity = TreeSpeciesCatalog.IsGrassland(p.variant) ? .39f : .65f;
        int maxX = Mathf.Min(Resolution - 1, Mathf.CeilToInt(center.x + major));
        int maxZ = Mathf.Min(Resolution - 1, Mathf.CeilToInt(center.y + major));
        for (int z = Mathf.Max(0, Mathf.FloorToInt(center.y - major)); z <= maxZ; z++)
        for (int x = Mathf.Max(0, Mathf.FloorToInt(center.x - major)); x <= maxX; x++)
        {
            var offset = new Vector2(x, z) - center;
            float u = Vector2.Dot(offset, along), v = Vector2.Dot(offset, across);
            float falloff = Mathf.Clamp01(1f - (u * u + v * v));
            if (falloff <= 0f) continue;
            int index = z * Resolution + x;
            Vector2 old = distantPixels[index];
            float coverage = old.x + (1f - old.x) * opacity * falloff * falloff;
            float highest = old.x > 0f ? Mathf.Max(top, old.y / old.x) : top;
            distantPixels[index] = new Vector2(coverage, coverage * highest);
        }
    }

    public void Update(Vector3 viewer, bool buildSkyField)
    {
        if (buildSkyField && !hadSkyField) dirty = true;
        hadSkyField = buildSkyField;
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
        if (buildSkyField) UpdateSkyField();
        Shader.SetGlobalTexture(MaskId, mask);
        Shader.SetGlobalVector(BoundsId, new Vector4(origin.x * chunkSize, origin.y * chunkSize,
            1f / (SpanChunks * chunkSize), 0f));
    }

    void UpdateSkyField()
    {
        if (skyField == null)
        {
            skyField = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Forest directional sky field",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            skyPixels = new Color32[pixels.Length];
        }
        int near = Mathf.Clamp(Mathf.RoundToInt(9f / texelSize), 1, 16);
        int far = Mathf.Min(near * 2, 32);
        const int last = Resolution - 1;
        for (int z = 0; z < Resolution; z++)
        {
            int southNear = Mathf.Max(0, z - near) * Resolution;
            int northNear = Mathf.Min(last, z + near) * Resolution;
            int southFar = Mathf.Max(0, z - far) * Resolution;
            int northFar = Mathf.Min(last, z + far) * Resolution;
            int row = z * Resolution;
            for (int x = 0; x < Resolution; x++)
            {
                int west = (pixels[row + Mathf.Max(0, x - near)] * 2 + pixels[row + Mathf.Max(0, x - far)]) / 3;
                int east = (pixels[row + Mathf.Min(last, x + near)] * 2 + pixels[row + Mathf.Min(last, x + far)]) / 3;
                int south = (pixels[southNear + x] * 2 + pixels[southFar + x]) / 3;
                int north = (pixels[northNear + x] * 2 + pixels[northFar + x]) / 3;
                byte cover = pixels[row + x];
                skyPixels[row + x] = new Color32(cover, (byte)(128 + (west - east) / 2),
                    (byte)(128 + (south - north) / 2), (byte)((west + east + south + north) / 4));
            }
        }
        skyField.SetPixelData(skyPixels, 0);
        skyField.Apply(false, false);
        Shader.SetGlobalTexture(SkyId, skyField);
    }

    void Stamp(TreeRecord tree)
    {
        float radius = CrownRadius(tree) / texelSize;
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

    float CrownRadius(TreeRecord tree)
    {
        var p = tree.Placement;
        float authored = crownRadii[(int)p.variant] * Mathf.Max(p.localScale.x, p.localScale.z);
        return Mathf.Clamp(Mathf.Max(authored, tree.ExclusionRadiusWorld), 3.5f, 27f);
    }

    public void Dispose()
    {
        registry.ChunkChanged -= MarkDirty;
        UnityEngine.Object.Destroy(mask);
        if (skyField != null) UnityEngine.Object.Destroy(skyField);
        if (distantField != null) UnityEngine.Object.Destroy(distantField);
    }
}
