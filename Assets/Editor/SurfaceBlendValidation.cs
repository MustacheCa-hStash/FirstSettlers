using System;
using UnityEditor;
using UnityEngine;

public static class SurfaceBlendValidation
{
    [MenuItem("Tools/Terrain/Validate Surface Blending")]
    public static void Run()
    {
        foreach (SurfaceType left in Enum.GetValues(typeof(SurfaceType)))
        {
            foreach (SurfaceType right in Enum.GetValues(typeof(SurfaceType)))
                ValidatePair(left, right);
        }
        ValidateSharedEdge();
        Shader shader = Shader.Find("Custom/StylizedTerrainURP");
        Require(shader != null, "Terrain shader is missing.");
        var material = new Material(shader);
        try
        {
            ShaderUtil.CompilePass(material, 0, true);
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
        }
        finally { UnityEngine.Object.DestroyImmediate(material); }
        Debug.Log("Surface blending validation passed: all 49 surface pairs, normalized weights, unchanged labels, shared chunk edges, and terrain shader compilation.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void ValidatePair(SurfaceType left, SurfaceType right)
    {
        var labels = new SurfaceType[11, 9];
        var cover = new GroundCoverType[11, 9];
        for (int x = 0; x < 11; x++)
            for (int z = 0; z < 9; z++)
                labels[x, z] = x < 5 ? left : right;
        ControlMapPixelData data = TerrainControlMapBuilder.BuildRaw(labels, cover);
        Require(data.Width == 9 && data.Height == 7, "Padded map has incorrect mesh dimensions.");
        for (int x = 0; x < data.Width; x++)
        {
            for (int z = 0; z < data.Height; z++)
            {
                float total = 0f;
                foreach (SurfaceType surface in Enum.GetValues(typeof(SurfaceType)))
                    total += Weight(data, x, z, surface);
                Require(Mathf.Abs(total - 1f) < 0.012f, "Surface weights lose brightness at a boundary.");
                Require(labels[x + 1, z + 1] == (x + 1 < 5 ? left : right), "Blending modified gameplay labels.");
            }
        }
        if (left != right)
        {
            Require(Weight(data, 0, 3, left) == 1f, "Pure surface interior changed.");
            float a = Weight(data, 3, 3, right), b = Weight(data, 4, 3, right);
            Require(a > 0f && a < b && b < 1f, "Material boundary did not become a gradual transition.");
        }
    }

    private static void ValidateSharedEdge()
    {
        const int size = 8;
        var maps = new ControlMapPixelData[2];
        for (int chunk = 0; chunk < 2; chunk++)
        {
            var labels = new SurfaceType[size + 3, size + 3];
            var cover = new GroundCoverType[size + 3, size + 3];
            for (int x = 0; x < size + 3; x++)
                for (int z = 0; z < size + 3; z++)
                    labels[x, z] = (SurfaceType)(((x - 1 + chunk * size + z) % 7 + 7) % 7);
            maps[chunk] = TerrainControlMapBuilder.BuildRaw(labels, cover);
        }
        for (int z = 0; z <= size; z++)
            for (int map = 0; map < 3; map++)
                Require(maps[0].Maps[map][z * (size + 1) + size].Equals(maps[1].Maps[map][z * (size + 1)]), "Surface blending creates a chunk seam.");
    }

    private static float Weight(ControlMapPixelData data, int x, int z, SurfaceType surface)
    {
        int index = (int)surface;
        Color color = data.Maps[index / 4][z * data.Width + x];
        return color[index % 4];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
