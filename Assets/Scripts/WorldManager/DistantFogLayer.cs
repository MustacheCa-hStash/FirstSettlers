using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-owned settings for the distant, world-height fog bank. Does not change streaming or culling.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class DistantFogLayer : MonoBehaviour
{
    public enum Resolution { Full = 1, Half = 2, Quarter = 4 }

    [Header("Distance (world units)")]
    [Min(0)] public float startDistance = 600;
    [Min(1)] public float distanceFeather = 200;
    [Min(1)] public float maxDistance = 5000;
    [Tooltip("Extinction per world unit. Larger values hide distant low terrain sooner.")]
    [Range(0.0001f, 0.04f)] public float density = 0.006f;

    [Header("Elevation (absolute world Y)")]
    public float bottomHeight = -45;
    public float topHeight = 85;
    [Min(0.1f)] public float heightFeather = 25;
    [Min(0)] public float billowHeight = 20;
    [Tooltip("Fog on visible surfaces fades out between these heights, even if fog lies in front of them. Sky uses the volume itself.")]
    public Vector2 clearHeights = new Vector2(110, 150);

    [Header("Cloud shape and movement")]
    public Texture3D noiseTexture;
    [Min(1)] public float noiseTileSize = 640;
    [Range(0, 1)] public float noiseStrength = 0.8f;
    [Range(0, 1)] public float detailStrength = 0.2f;
    [Tooltip("World units per second. Noise stays anchored to the world as the camera moves.")]
    public Vector3 wind = new Vector3(2, 0, 0.7f);
    [Range(0, 1)] public float shading = 0.3f;

    [Header("Color")]
    public bool followDayNightColor = true;
    public Color fogColor = new Color(0.52f, 0.66f, 0.95f);

    [Header("Quality")]
    public Resolution resolution = Resolution.Half;
    [Range(8, 96)] public int steps = 32;
    [Tooltip("Stable spatial dithering breaks up sampling bands without temporal ghosting.")]
    [Range(0, 1)] public float dithering = 0.7f;
    public bool showInSceneView = true;

    [Header("Water")]
    [Tooltip("Reads the world's shared water height. Coverage comes from actual water meshes, so dry land is unaffected.")]
    public WorldManager world;
    public bool fogWater = true;
    public float fallbackWaterHeight = 14.4f;

    static readonly List<DistantFogLayer> Layers = new List<DistantFogLayer>();
    Texture3D generatedNoise;

    public float WaterHeight => world != null ? world.GlobalWaterHeight : fallbackWaterHeight;
    public Texture3D Noise
    {
        get
        {
            if (noiseTexture != null) return noiseTexture;
            if (generatedNoise == null) generatedNoise = CreateNoiseTexture(64);
            return generatedNoise;
        }
    }

    void OnEnable() { if (!Layers.Contains(this)) Layers.Add(this); }
    void OnDisable() { Layers.Remove(this); }
    void OnDestroy()
    {
        Layers.Remove(this);
        if (generatedNoise != null)
        {
            if (Application.isPlaying) Destroy(generatedNoise); else DestroyImmediate(generatedNoise);
        }
    }

    void OnValidate()
    {
        startDistance = Mathf.Max(0, startDistance);
        distanceFeather = Mathf.Max(1, distanceFeather);
        maxDistance = Mathf.Max(startDistance + 1, maxDistance);
        topHeight = Mathf.Max(bottomHeight + 1, topHeight);
        heightFeather = Mathf.Max(0.1f, heightFeather);
        noiseTileSize = Mathf.Max(1, noiseTileSize);
        clearHeights.y = Mathf.Max(clearHeights.x + 0.1f, clearHeights.y);
        steps = Mathf.Clamp(steps, 8, 96);
    }

    public static DistantFogLayer ForCamera(Camera camera)
    {
        Scene scene = camera.cameraType == CameraType.SceneView ? SceneManager.GetActiveScene() : camera.gameObject.scene;
        for (int i = Layers.Count - 1; i >= 0; i--)
        {
            var layer = Layers[i];
            if (layer == null) { Layers.RemoveAt(i); continue; }
            if (layer.isActiveAndEnabled && layer.gameObject.scene == scene &&
                (camera.cameraType != CameraType.SceneView || layer.showInSceneView)) return layer;
        }
        return null;
    }

    // Two periodic value-noise octaves packed into RG: one texture lookup per march step.
    // Baking once avoids procedural-noise arithmetic in every fragment and every frame.
    public static Texture3D CreateNoiseTexture(int size)
    {
        var pixels = new Color32[size * size * size];
        for (int z = 0; z < size; z++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector3 p = new Vector3(x, y, z) / size;
                    pixels[x + size * (y + size * z)] = new Color32(
                        (byte)(255 * PeriodicNoise(p, 4)), (byte)(255 * PeriodicNoise(p, 11)), 0, 255);
                }
        var texture = new Texture3D(size, size, size, TextureFormat.RGBA32, true)
        {
            name = "Distant Fog Periodic Noise", wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    static float PeriodicNoise(Vector3 p, int cells)
    {
        p *= cells;
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        float tx = Smooth(p.x - x), ty = Smooth(p.y - y), tz = Smooth(p.z - z);
        float a = Mathf.Lerp(Hash(x, y, z, cells), Hash(x + 1, y, z, cells), tx);
        float b = Mathf.Lerp(Hash(x, y + 1, z, cells), Hash(x + 1, y + 1, z, cells), tx);
        float c = Mathf.Lerp(Hash(x, y, z + 1, cells), Hash(x + 1, y, z + 1, cells), tx);
        float d = Mathf.Lerp(Hash(x, y + 1, z + 1, cells), Hash(x + 1, y + 1, z + 1, cells), tx);
        return Mathf.Lerp(Mathf.Lerp(a, b, ty), Mathf.Lerp(c, d, ty), tz);
    }
    static float Smooth(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    static float Hash(int x, int y, int z, int cells)
    {
        unchecked
        {
            uint h = (uint)(x % cells) * 73856093u ^ (uint)(y % cells) * 19349663u ^ (uint)(z % cells) * 83492791u ^ 0x6A09E667u;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0x00FFFFFFu) / 16777215f;
        }
    }
}
