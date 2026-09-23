using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class TreeImpostorAmbientAtlasBakerWindow : EditorWindow
{
    [SerializeField] private TreeImpostorCaptureSetup setup;
    [SerializeField] private Texture2D surfaceAtlas;
    [SerializeField] private Texture2D depthAtlas;
    [SerializeField, Range(4, 24)] private int sourceFramesPerAxis = 12;
    [SerializeField, Range(1, 8)] private int sourceTilePadding = 2;
    [SerializeField, Range(64, 192)] private int voxelResolution = 128;
    [SerializeField, Range(64, 256)] private int ambientTileResolution = 128;
    [SerializeField, Range(4, 16)] private int aoRays = 8;
    [SerializeField, Range(4, 24)] private int skyRays = 12;
    [SerializeField, Range(8, 32)] private int raySteps = 16;
    [SerializeField, Range(0.05f, 0.35f)] private float aoRadiusFraction = 0.15f;
    [SerializeField, Range(0.01f, 0.25f)] private float leafVoxelDensity = 0.08f;
    [SerializeField, Range(1, 8)] private int paddingPixels = 2;
    [SerializeField] private string outputFolder = "Assets/Textures/Trees/Impostors/Spruce";

    private float[] volume;
    private Vector3 boundsMin;
    private Vector3 boundsSize;
    private Texture2D readableLeafTexture;
    private bool ownsReadableLeafTexture;

    [MenuItem("Tools/Impostors/Bake Spruce Ambient Atlas")]
    private static void Open() => GetWindow<TreeImpostorAmbientAtlasBakerWindow>("Ambient Atlas Bake");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Spruce AO / Sky Visibility / Bent Normal", EditorStyles.boldLabel);
        setup = (TreeImpostorCaptureSetup)EditorGUILayout.ObjectField("Capture Setup", setup, typeof(TreeImpostorCaptureSetup), true);
        surfaceAtlas = (Texture2D)EditorGUILayout.ObjectField("Surface Atlas", surfaceAtlas, typeof(Texture2D), false);
        depthAtlas = (Texture2D)EditorGUILayout.ObjectField("Depth Atlas", depthAtlas, typeof(Texture2D), false);
        sourceFramesPerAxis = EditorGUILayout.IntSlider("Source Frames Per Axis", sourceFramesPerAxis, 4, 24);
        sourceTilePadding = EditorGUILayout.IntSlider("Source Tile Padding", sourceTilePadding, 1, 8);
        voxelResolution = EditorGUILayout.IntPopup("Voxel Resolution", voxelResolution, new[] { "64", "96", "128", "160", "192" }, new[] { 64, 96, 128, 160, 192 });
        ambientTileResolution = EditorGUILayout.IntPopup("Ambient Tile Resolution", ambientTileResolution, new[] { "64", "128", "256" }, new[] { 64, 128, 256 });
        aoRays = EditorGUILayout.IntSlider("AO Rays", aoRays, 4, 16);
        skyRays = EditorGUILayout.IntSlider("Sky Rays", skyRays, 4, 24);
        raySteps = EditorGUILayout.IntSlider("Ray Steps", raySteps, 8, 32);
        aoRadiusFraction = EditorGUILayout.Slider("AO Radius / Tree Height", aoRadiusFraction, 0.05f, 0.35f);
        leafVoxelDensity = EditorGUILayout.Slider("Leaf Voxel Density", leafVoxelDensity, 0.01f, 0.25f);
        paddingPixels = EditorGUILayout.IntSlider("Tile Padding", paddingPixels, 1, 8);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        EditorGUILayout.HelpBox("This is an offline CPU bake. Start at 128³ voxels and 128-pixel ambient tiles. The output is low-frequency by design and may take several minutes.", MessageType.Info);

        using (new EditorGUI.DisabledScope(setup == null || surfaceAtlas == null || depthAtlas == null))
        {
            if (GUILayout.Button("Bake AO + Sky Visibility + Bent Normal"))
                Bake();
        }
    }

    private void Bake()
    {
        if (!ValidateInputs(out string error))
        {
            Debug.LogError(error, setup);
            return;
        }

        try
        {
            setup.RecalculateBounds(out _);
            boundsMin = setup.LocalBoundsMin;
            boundsSize = setup.LocalBoundsMax - setup.LocalBoundsMin;
            BuildDensityVolume();
            BakeAtlas();
            AssetDatabase.Refresh();
            ConfigureOutputImporter();
            Debug.Log("Spruce ambient atlas bake completed.", setup);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, setup);
        }
        finally
        {
            if (ownsReadableLeafTexture && readableLeafTexture != null)
                DestroyImmediate(readableLeafTexture);
            readableLeafTexture = null;
            ownsReadableLeafTexture = false;
            volume = null;
            EditorUtility.ClearProgressBar();
        }
    }

    private bool ValidateInputs(out string error)
    {
        error = null;
        if (setup == null || setup.TreeRoot == null || setup.LeafMaterial == null || setup.BarkMaterial == null)
        {
            error = "Assign a complete TreeImpostorCaptureSetup.";
            return false;
        }
        if (!surfaceAtlas.isReadable || !depthAtlas.isReadable)
        {
            error = "Surface and Depth atlases must be Read/Write Enabled. Re-run semantic capture or enable Read/Write in their import settings.";
            return false;
        }
        if (surfaceAtlas.width != surfaceAtlas.height || depthAtlas.width != depthAtlas.height)
        {
            error = "Surface and Depth atlases must be square.";
            return false;
        }
        if (surfaceAtlas.width != depthAtlas.width || surfaceAtlas.width % sourceFramesPerAxis != 0)
        {
            error = "Surface and Depth atlases must have matching dimensions divisible by Source Frames Per Axis.";
            return false;
        }
        if (surfaceAtlas.width / sourceFramesPerAxis <= sourceTilePadding * 2)
        {
            error = "Source Tile Padding is too large for the selected atlas layout.";
            return false;
        }
        if (!outputFolder.StartsWith("Assets/", StringComparison.Ordinal))
        {
            error = "Output Folder must be inside Assets/.";
            return false;
        }
        return true;
    }

    private void BuildDensityVolume()
    {
        EditorUtility.DisplayProgressBar("Spruce Ambient Bake", "Preparing alpha-aware leaf texture", 0f);
        readableLeafTexture = GetReadableTexture(setup.LeafMaterial.GetTexture("_BaseMap") as Texture2D, out ownsReadableLeafTexture);
        volume = new float[voxelResolution * voxelResolution * voxelResolution];

        MeshFilter[] filters = setup.TreeRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
        {
            MeshFilter filter = filters[filterIndex];
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || filter.sharedMesh == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            Material[] materials = renderer.sharedMaterials;
            for (int subMesh = 0; subMesh < mesh.subMeshCount && subMesh < materials.Length; subMesh++)
            {
                Material material = materials[subMesh];
                bool isLeaf = material == setup.LeafMaterial;
                bool isBark = material == setup.BarkMaterial;
                if (!isLeaf && !isBark)
                    continue;

                int[] triangles = mesh.GetTriangles(subMesh);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = ToRootLocal(filter.transform.TransformPoint(vertices[triangles[i]]));
                    Vector3 b = ToRootLocal(filter.transform.TransformPoint(vertices[triangles[i + 1]]));
                    Vector3 c = ToRootLocal(filter.transform.TransformPoint(vertices[triangles[i + 2]]));
                    Vector2 uvA = uv.Length > triangles[i] ? uv[triangles[i]] : Vector2.zero;
                    Vector2 uvB = uv.Length > triangles[i + 1] ? uv[triangles[i + 1]] : Vector2.zero;
                    Vector2 uvC = uv.Length > triangles[i + 2] ? uv[triangles[i + 2]] : Vector2.zero;
                    VoxelizeTriangle(a, b, c, uvA, uvB, uvC, isLeaf);
                }
            }
            EditorUtility.DisplayProgressBar("Spruce Ambient Bake", "Voxelizing spruce geometry", (filterIndex + 1f) / Mathf.Max(1, filters.Length) * 0.2f);
        }
    }

    private void VoxelizeTriangle(Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC, bool isLeaf)
    {
        float longestEdge = Mathf.Max((a - b).magnitude, Mathf.Max((a - c).magnitude, (b - c).magnitude));
        float voxelWorldSize = Mathf.Max(boundsSize.x, Mathf.Max(boundsSize.y, boundsSize.z)) / voxelResolution;
        int samples = Mathf.Clamp(Mathf.CeilToInt(longestEdge / Mathf.Max(voxelWorldSize * 1.5f, 0.001f)), 1, 8);
        float cutoff = setup.LeafMaterial.GetFloat("_Cutoff");
        Vector2 scale = setup.LeafMaterial.GetTextureScale("_BaseMap");
        Vector2 offset = setup.LeafMaterial.GetTextureOffset("_BaseMap");

        for (int row = 0; row < samples; row++)
        for (int column = 0; column < samples - row; column++)
        {
            float v = (row + 0.333f) / samples;
            float w = (column + 0.333f) / samples;
            float u = 1f - v - w;
            Vector3 position = a * u + b * v + c * w;
            float density = 1f;
            if (isLeaf && readableLeafTexture != null)
            {
                Vector2 textureUv = (uvA * u + uvB * v + uvC * w) * scale + offset;
                float alpha = readableLeafTexture.GetPixelBilinear(textureUv.x, textureUv.y).a;
                if (alpha < cutoff) continue;
                density = alpha * leafVoxelDensity;
            }
            AddDensity(position, density);
        }
    }

    private void AddDensity(Vector3 position, float amount)
    {
        Vector3 normalized = new Vector3(
            (position.x - boundsMin.x) / Mathf.Max(boundsSize.x, 0.0001f),
            (position.y - boundsMin.y) / Mathf.Max(boundsSize.y, 0.0001f),
            (position.z - boundsMin.z) / Mathf.Max(boundsSize.z, 0.0001f));
        int x = Mathf.RoundToInt(normalized.x * (voxelResolution - 1));
        int y = Mathf.RoundToInt(normalized.y * (voxelResolution - 1));
        int z = Mathf.RoundToInt(normalized.z * (voxelResolution - 1));
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int sx = x + dx, sy = y + dy, sz = z + dz;
            if (sx < 0 || sy < 0 || sz < 0 || sx >= voxelResolution || sy >= voxelResolution || sz >= voxelResolution) continue;
            float weight = dx == 0 && dy == 0 && dz == 0 ? 1f : 0.25f;
            int index = sx + voxelResolution * (sy + voxelResolution * sz);
            volume[index] = Mathf.Min(1f, volume[index] + amount * weight);
        }
    }

    private void BakeAtlas()
    {
        int sourceStride = surfaceAtlas.width / sourceFramesPerAxis;
        int frames = sourceFramesPerAxis;
        int sourceTileResolution = sourceStride - sourceTilePadding * 2;
        int outputStride = ambientTileResolution + paddingPixels * 2;
        var output = new Texture2D(frames * outputStride, frames * outputStride, TextureFormat.RGBA32, false, true);
        Color[] surface = surfaceAtlas.GetPixels();
        Color[] depth = depthAtlas.GetPixels();
        float radius = setup.CaptureRadius;
        int total = frames * frames;

        for (int tileY = 0; tileY < frames; tileY++)
        for (int tileX = 0; tileX < frames; tileX++)
        {
            Vector3 viewDirection = OctDecode(new Vector2((tileX + 0.5f) / frames * 2f - 1f, (tileY + 0.5f) / frames * 2f - 1f));
            Quaternion rotation = CameraRotation(viewDirection);
            for (int y = 0; y < ambientTileResolution; y++)
            for (int x = 0; x < ambientTileResolution; x++)
            {
                int sourceX = Mathf.Clamp(Mathf.FloorToInt((x + 0.5f) / ambientTileResolution * sourceTileResolution), 0, sourceTileResolution - 1);
                int sourceY = Mathf.Clamp(Mathf.FloorToInt((y + 0.5f) / ambientTileResolution * sourceTileResolution), 0, sourceTileResolution - 1);
                int sourceIndex = (tileY * sourceStride + sourceTilePadding + sourceY) * surfaceAtlas.width + tileX * sourceStride + sourceTilePadding + sourceX;
                Color depthPixel = depth[sourceIndex];
                Color result = Color.clear;
                if (depthPixel.a > 0.5f)
                {
                    Vector3 position = ReconstructPosition(viewDirection, rotation, x, y, depthPixel.r, radius);
                    Color normalPixel = surface[sourceIndex];
                    Vector3 normal = OctDecode(new Vector2(normalPixel.r * 2f - 1f, normalPixel.g * 2f - 1f));
                    float ao = TraceAo(position, normal, tileX * 193 + tileY * 97 + x * 11 + y);
                    GetSkyData(position, tileX * 131 + tileY * 67 + x * 7 + y, out float skyVisibility, out Vector3 bentNormal);
                    Vector2 bent = OctEncode(bentNormal);
                    result = new Color(bent.x, bent.y, skyVisibility, ao);
                }
                output.SetPixel(tileX * outputStride + paddingPixels + x, tileY * outputStride + paddingPixels + y, result);
            }
            CopyPadding(output, tileX * outputStride + paddingPixels, tileY * outputStride + paddingPixels, ambientTileResolution, paddingPixels);
            EditorUtility.DisplayProgressBar("Spruce Ambient Bake", "Tracing ambient visibility", 0.2f + 0.8f * ((tileY * frames + tileX + 1f) / total));
        }

        output.Apply(false, false);
        Directory.CreateDirectory(Path.GetFullPath(outputFolder));
        File.WriteAllBytes(Path.GetFullPath(Path.Combine(outputFolder, "Spruce_Octa_Ambient.png")), output.EncodeToPNG());
        string metadata = "{\n" +
            $"  \"framesPerAxis\": {frames},\n" +
            $"  \"tileResolution\": {ambientTileResolution},\n" +
            $"  \"paddingPixels\": {paddingPixels},\n" +
            "  \"encoding\": \"RG octahedral local bent normal; B sky visibility; A ambient occlusion\"\n}";
        File.WriteAllText(Path.GetFullPath(Path.Combine(outputFolder, "Spruce_Octa_Ambient_Metadata.json")), metadata);
        DestroyImmediate(output);
    }

    private Vector3 ReconstructPosition(Vector3 viewDirection, Quaternion rotation, int x, int y, float normalizedDepth, float radius)
    {
        float eyeDepth = Mathf.Lerp(radius * 1.25f, radius * 3.75f, normalizedDepth);
        float planeX = ((x + 0.5f) / ambientTileResolution - 0.5f) * radius * 2f;
        float planeY = ((y + 0.5f) / ambientTileResolution - 0.5f) * radius * 2f;
        Vector3 cameraLocal = setup.CaptureCenterLocal + viewDirection * radius * 2.5f;
        return cameraLocal + rotation * Vector3.right * planeX + rotation * Vector3.up * planeY + rotation * Vector3.forward * eyeDepth;
    }

    private Quaternion CameraRotation(Vector3 viewDirection)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(viewDirection, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
        return Quaternion.LookRotation(-viewDirection, up);
    }

    private float TraceAo(Vector3 position, Vector3 normal, int seed)
    {
        float visibility = 0f;
        float radius = boundsSize.y * aoRadiusFraction;
        for (int ray = 0; ray < aoRays; ray++)
        {
            Vector3 direction = Hemisphere(normal, ray, aoRays, seed);
            visibility += TraceVisibility(position + normal * 0.01f, direction, radius);
        }
        return 1f - visibility / aoRays;
    }

    private void GetSkyData(Vector3 position, int seed, out float skyVisibility, out Vector3 bentNormal)
    {
        float visibility = 0f;
        Vector3 directionSum = Vector3.zero;
        float distance = boundsSize.magnitude;
        for (int ray = 0; ray < skyRays; ray++)
        {
            Vector3 direction = UpperHemisphere(ray, skyRays, seed);
            float transmittance = TraceVisibility(position + direction * 0.01f, direction, distance);
            visibility += transmittance;
            directionSum += direction * transmittance;
        }
        skyVisibility = visibility / skyRays;
        bentNormal = directionSum.sqrMagnitude > 0.0001f ? directionSum.normalized : Vector3.up;
    }

    private float TraceVisibility(Vector3 start, Vector3 direction, float maxDistance)
    {
        float stepDistance = maxDistance / raySteps;
        float transmittance = 1f;
        for (int step = 0; step < raySteps; step++)
        {
            Vector3 point = start + direction * ((step + 0.5f) * stepDistance);
            if (!InsideBounds(point)) break;
            float density = SampleDensity(point);
            transmittance *= 1f - Mathf.Clamp01(density * 0.85f);
            if (transmittance < 0.01f) return 0f;
        }
        return transmittance;
    }

    private bool InsideBounds(Vector3 point) => point.x >= boundsMin.x && point.y >= boundsMin.y && point.z >= boundsMin.z && point.x <= boundsMin.x + boundsSize.x && point.y <= boundsMin.y + boundsSize.y && point.z <= boundsMin.z + boundsSize.z;

    private float SampleDensity(Vector3 point)
    {
        Vector3 p = new Vector3((point.x - boundsMin.x) / boundsSize.x, (point.y - boundsMin.y) / boundsSize.y, (point.z - boundsMin.z) / boundsSize.z) * (voxelResolution - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, voxelResolution - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, voxelResolution - 1), z0 = Mathf.Clamp(Mathf.FloorToInt(p.z), 0, voxelResolution - 1);
        int x1 = Mathf.Min(x0 + 1, voxelResolution - 1), y1 = Mathf.Min(y0 + 1, voxelResolution - 1), z1 = Mathf.Min(z0 + 1, voxelResolution - 1);
        float tx = p.x - x0, ty = p.y - y0, tz = p.z - z0;
        return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(V(x0,y0,z0), V(x1,y0,z0), tx), Mathf.Lerp(V(x0,y1,z0), V(x1,y1,z0), tx), ty), Mathf.Lerp(Mathf.Lerp(V(x0,y0,z1), V(x1,y0,z1), tx), Mathf.Lerp(V(x0,y1,z1), V(x1,y1,z1), tx), ty), tz);
    }

    private float V(int x, int y, int z) => volume[x + voxelResolution * (y + voxelResolution * z)];
    private Vector3 ToRootLocal(Vector3 world) => setup.TreeRoot.InverseTransformPoint(world);

    private static Vector3 Hemisphere(Vector3 normal, int index, int count, int seed)
    {
        float u = Frac((index + 0.5f) / count + Hash(seed));
        float v = Frac((index * 0.61803398875f) + Hash(seed * 17));
        float phi = 2f * Mathf.PI * v, z = u, r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        Vector3 tangent = Vector3.Cross(Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right, normal).normalized;
        return (tangent * (Mathf.Cos(phi) * r) + Vector3.Cross(normal, tangent) * (Mathf.Sin(phi) * r) + normal * z).normalized;
    }

    private static Vector3 UpperHemisphere(int index, int count, int seed)
    {
        float u = Frac((index + 0.5f) / count + Hash(seed));
        float v = Frac(index * 0.61803398875f + Hash(seed * 29));
        float phi = 2f * Mathf.PI * v, y = u, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        return new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
    }

    private static Vector2 OctEncode(Vector3 n)
    {
        n /= Mathf.Max(Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z), 0.0001f);
        Vector2 e = new Vector2(n.x, n.y);
        if (n.z < 0f) e = new Vector2((1f - Mathf.Abs(e.y)) * Mathf.Sign(e.x), (1f - Mathf.Abs(e.x)) * Mathf.Sign(e.y));
        return e * 0.5f + Vector2.one * 0.5f;
    }

    private static Vector3 OctDecode(Vector2 e)
    {
        Vector3 n = new Vector3(e.x, e.y, 1f - Mathf.Abs(e.x) - Mathf.Abs(e.y));
        if (n.z < 0f) { float x = (1f - Mathf.Abs(n.y)) * Mathf.Sign(n.x); n.y = (1f - Mathf.Abs(n.x)) * Mathf.Sign(n.y); n.x = x; }
        return n.normalized;
    }

    private static float Hash(int value) => Frac(Mathf.Sin(value * 12.9898f) * 43758.5453f);
    private static float Frac(float value) => value - Mathf.Floor(value);

    private Texture2D GetReadableTexture(Texture2D source, out bool ownsCopy)
    {
        ownsCopy = false;
        if (source == null || source.isReadable) return source;
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);
        RenderTexture old = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        copy.Apply();
        RenderTexture.active = old;
        RenderTexture.ReleaseTemporary(rt);
        ownsCopy = true;
        return copy;
    }

    private static void CopyPadding(Texture2D texture, int x, int y, int size, int padding)
    {
        for (int p = 1; p <= padding; p++)
        for (int i = 0; i < size; i++)
        {
            texture.SetPixel(x + i, y - p, texture.GetPixel(x + i, y));
            texture.SetPixel(x + i, y + size - 1 + p, texture.GetPixel(x + i, y + size - 1));
            texture.SetPixel(x - p, y + i, texture.GetPixel(x, y + i));
            texture.SetPixel(x + size - 1 + p, y + i, texture.GetPixel(x + size - 1, y + i));
        }
    }

    private void ConfigureOutputImporter()
    {
        string path = Path.Combine(outputFolder, "Spruce_Octa_Ambient.png").Replace('\\', '/');
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }
    }
}
