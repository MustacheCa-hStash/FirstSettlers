using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TreeImpostorOutlineBaker
{
    public const string AssetPath = "Assets/Resources/Foliage/TreeImpostorOutlines.asset";
    public const string Output = ".utmp/tree-outlines";
    public static readonly int[] Counts = { 4, 8, 12, 16 };
    private const float GuardTexels = 2f;

    public static void RunBatch()
    {
        try { Bake(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Impostors/Bake Conservative Tree Outlines")]
    public static void Bake() => BakeSelected(AssetPath, Array.Empty<TreeImpostorOutlineLibrary.Entry>(),
        "RedMaple", "SugarMaple", "Oak", "Birch", "WhitePine", "Spruce");

    public static void BakeSelected(string assetPath, IEnumerable<TreeImpostorOutlineLibrary.Entry> baseEntries, params string[] names)
    {
        Directory.CreateDirectory(Output);
        var library = AssetDatabase.LoadAssetAtPath<TreeImpostorOutlineLibrary>(assetPath);
        if (library == null) { library = ScriptableObject.CreateInstance<TreeImpostorOutlineLibrary>(); AssetDatabase.CreateAsset(library, assetPath); }
        var entries = new List<TreeImpostorOutlineLibrary.Entry>(baseEntries);
        using var csv = new StreamWriter(Output + "/areas.csv");
        csv.WriteLine("species,frame,vertices,areaFraction");
        foreach (string name in names)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/M_Trees/{name}HQ/{name}HQ_OctaImpostor_M.mat");
            if (material == null) throw new InvalidOperationException("Missing HQ material: " + name);
            var albedo = (Texture2D)material.GetTexture("_AlbedoCoverage");
            var ids = (Texture2D)material.GetTexture("_MaterialIdAtlas");
            int frames = Mathf.RoundToInt(material.GetFloat("_FramesPerAxis")), width = albedo.width;
            int stride = width / frames, padding = Mathf.RoundToInt(material.GetFloat("_AtlasPadding"));
            int tile = stride - padding * 2;
            if (ids.width != width || ids.height != albedo.height) throw new InvalidOperationException("Outline source maps must have matching dimensions");
            var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Editor/TreeOutlineBake.compute");
            int kernel=compute.FindKernel("BuildOutlineRows"),rowCount=frames*frames*tile;
            var rows=new Vector2Int[rowCount];
            using(var buffer=new ComputeBuffer(rowCount,8))
            {
                compute.SetInt("_OutlineFrames",frames);compute.SetInt("_OutlineStride",stride);
                compute.SetInt("_OutlinePadding",padding);compute.SetInt("_OutlineTile",tile);
                compute.SetTexture(kernel,"_OutlineAlbedo",albedo);compute.SetTexture(kernel,"_OutlineIds",ids);
                compute.SetBuffer(kernel,"_OutlineRows",buffer);compute.Dispatch(kernel,(rowCount+63)/64,1,1);
                buffer.GetData(rows);
            }
            var entry = new TreeImpostorOutlineLibrary.Entry { albedo = albedo, materialIds = ids, frames = frames,
                width = width, height = albedo.height, padding = padding,
                inputHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(albedo)) + ":" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(ids)),
                shapes = new TreeImpostorOutlineLibrary.Shape[Counts.Length] };
            for (int q = 0; q < Counts.Length; q++) entry.shapes[q] = new TreeImpostorOutlineLibrary.Shape
                { vertices = Counts[q], uv = new Vector2[frames * frames * Counts[q]] };
            for (int frame = 0; frame < frames * frames; frame++)
            {
                var points = new List<Vector2>();
                int startX = frame % frames * stride + padding, startY = frame / frames * stride + padding;
                // Work from the actual imported BC7 texels, including compression artifacts.
                // Semantic IDs are read at mip zero by the runtime shader: their complete
                // texel cells bound surviving coverage even when albedo uses coarser mips.
                for (int y = 0; y < tile; y++)
                {
                    int first=rows[frame*tile+y].x,last=rows[frame*tile+y].y;
                    if (last < 0) continue;
                    float x0 = first / (float)tile, x1 = (last + 1f) / tile, y0 = y / (float)tile, y1 = (y + 1f) / tile;
                    points.Add(new Vector2(x0, y0)); points.Add(new Vector2(x0, y1));
                    points.Add(new Vector2(x1, y0)); points.Add(new Vector2(x1, y1));
                }
                for (int q = 0; q < Counts.Length; q++)
                {
                    var polygon = Fit(points, Counts[q], GuardTexels / tile);
                    foreach (var point in points) if (!Contains(polygon, point)) throw new InvalidOperationException("Outline clipped source coverage");
                    for (int v = 0; v < Counts[q]; v++) entry.shapes[q].uv[frame * Counts[q] + v] = polygon[Mathf.Min(v, polygon.Count - 1)];
                    float area = Area(polygon); entry.shapes[q].meanArea += area / (frames * frames);
                    csv.WriteLine($"{name},{frame},{Counts[q]},{area:F7}");
                }
            }
            entries.Add(entry);
            Debug.Log($"OUTLINE_BAKE {name}: area 4={entry.shapes[0].meanArea:F3}, 8={entry.shapes[1].meanArea:F3}, 12={entry.shapes[2].meanArea:F3}, 16={entry.shapes[3].meanArea:F3}");
        }
        library.entries = entries.ToArray(); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
        Debug.Log($"TREE_OUTLINE_BAKE_PASS: all imported coverage cells enclosed across four vertex budgets for {string.Join(", ", names)}.");
    }


    public static List<Vector2> Fit(List<Vector2> points, int count, float guard)
    {
        var polygon = new List<Vector2> { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        if (points.Count == 0) return polygon;
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2 / count;
            var normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            if (Mathf.Abs(normal.x) < .000001f) normal.x = 0;
            if (Mathf.Abs(normal.y) < .000001f) normal.y = 0;
            float support = float.MinValue;
            foreach (var point in points) support = Mathf.Max(support, Vector2.Dot(normal, point));
            support += guard * (Mathf.Abs(normal.x) + Mathf.Abs(normal.y));
            var result = new List<Vector2>();
            for (int v = 0; v < polygon.Count; v++)
            {
                Vector2 a = polygon[v], b = polygon[(v + 1) % polygon.Count];
                float da = Vector2.Dot(normal, a) - support, db = Vector2.Dot(normal, b) - support;
                if (da <= 0) result.Add(a);
                if ((da < 0 && db > 0) || (da > 0 && db < 0)) result.Add(Vector2.LerpUnclamped(a, b, da / (da - db)));
            }
            polygon = result;
        }
        for (int i = polygon.Count - 1; i >= 0 && polygon.Count > 3; i--)
            if ((polygon[i] - polygon[(i + 1) % polygon.Count]).sqrMagnitude < 1e-12f) polygon.RemoveAt(i);
        // An unusual precision/degeneracy case must lose optimization, never coverage.
        if (polygon.Count > count || polygon.Count < 3) return new List<Vector2> { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        return polygon;
    }

    public static bool Contains(List<Vector2> polygon, Vector2 p)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < -1e-6f) return false;
        }
        return true;
    }
    private static float Area(List<Vector2> polygon)
    {
        float twice = 0;
        for (int i = 0; i < polygon.Count; i++) { var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; twice += a.x * b.y - a.y * b.x; }
        return Mathf.Abs(twice) * .5f;
    }
}
