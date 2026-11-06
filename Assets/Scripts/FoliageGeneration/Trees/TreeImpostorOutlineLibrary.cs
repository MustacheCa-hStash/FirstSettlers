using System;
using UnityEngine;

public enum TreeImpostorOutlineQuality { Quad = 0, Four = 4, Eight = 8, Twelve = 12, Sixteen = 16 }

// Offline-derived conservative convex outlines in the exact UV domain used by the atlas shader.
// Every view has the same topology; only its vertex positions change with the selected frame.
public sealed class TreeImpostorOutlineLibrary : ScriptableObject
{
    [Serializable] public sealed class Shape
    {
        public int vertices;
        public Vector2[] uv;
        public float meanArea;
    }
    [Serializable] public sealed class Entry
    {
        public Texture2D albedo, materialIds;
        public int frames, width, height;
        public float padding;
        public string inputHash;
        public Shape[] shapes;
    }
    public Entry[] entries;

    public Entry Find(Material material)
    {
        if (entries == null || material == null || !material.HasProperty("_FramesPerAxis")) return null;
        var albedo = material.GetTexture("_AlbedoCoverage");
        var ids = material.GetTexture("_MaterialIdAtlas");
        foreach (var entry in entries)
            if (entry.albedo == albedo && entry.materialIds == ids && albedo != null &&
                albedo.width == entry.width && albedo.height == entry.height &&
                Mathf.RoundToInt(material.GetFloat("_FramesPerAxis")) == entry.frames &&
                material.GetFloat("_AtlasPadding") == entry.padding)
            {
#if UNITY_EDITOR
                string hash = UnityEditor.AssetDatabase.GetAssetDependencyHash(UnityEditor.AssetDatabase.GetAssetPath(albedo)) + ":" +
                    UnityEditor.AssetDatabase.GetAssetDependencyHash(UnityEditor.AssetDatabase.GetAssetPath(ids));
                if (entry.inputHash != hash) return null;
#endif
                return entry;
            }
        return null;
    }
}

public sealed class TreeImpostorOutlineBinding : IDisposable
{
    private ComputeBuffer buffer;
    private int vertices;

    public void Bind(Material material)
    {
        material.SetBuffer("_TreeOutlineUVs", buffer);
        material.SetInt("_TreeOutlineVertexCount", vertices);
        material.EnableKeyword("TREE_IMPOSTOR_OUTLINE");
    }

    public static TreeImpostorOutlineBinding Apply(Material material, Mesh mesh,
        TreeImpostorOutlineLibrary library, TreeImpostorOutlineQuality quality)
    {
        if (quality == TreeImpostorOutlineQuality.Quad || library == null ||
            !SystemInfo.supportsComputeShaders || SystemInfo.graphicsShaderLevel < 45) return null;
        if (material.shader.name != "Custom/TreeHQOctaImpostor" && material.shader.name != "Custom/SpruceOctaImpostor") return null;
        var entry = library.Find(material);
        if (entry == null) return null;
        foreach (var shape in entry.shapes)
        {
            int count = (int)quality;
            if (shape.vertices != count || shape.uv == null || shape.uv.Length != entry.frames * entry.frames * count) continue;
            var binding = new TreeImpostorOutlineBinding { buffer = new ComputeBuffer(shape.uv.Length, 8), vertices = count };
            binding.buffer.SetData(shape.uv);
            Bounds bounds = mesh.bounds;
            var triangles = new int[(count - 2) * 3];
            for (int i = 0; i < count - 2; i++) { triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = i + 2; }
            mesh.Clear(); mesh.vertices = new Vector3[count]; mesh.uv = new Vector2[count]; mesh.triangles = triangles;
            // Keep the original conservative camera-facing/wind bounds, not the placeholder vertices.
            mesh.bounds = bounds;
            binding.Bind(material);
            return binding;
        }
        return null;
    }

    public void Dispose() { buffer?.Release(); buffer = null; }
}
