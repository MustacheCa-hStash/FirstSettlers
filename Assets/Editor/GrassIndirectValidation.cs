using System;
using UnityEditor;
using UnityEngine;
public static class GrassIndirectValidation
{
    [MenuItem("Tools/Terrain/Validate Grass Compute (correctness only)")]
    public static void Run() { ValidateBounds(); ResidentGrassCullingValidation.Run(); }
    public static void RunBatch() { try { Run(); EditorApplication.Exit(0); } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }
    private static void ValidateBounds()
    {
        var local = new Bounds(new Vector3(1, 3, -2), new Vector3(4, 5, 3));
        var transform = Matrix4x4.TRS(new Vector3(8, -2, 3), Quaternion.Euler(12, 65, 21), new Vector3(-2, 4, 3));
        Bounds bounds = GrassRenderUtility.TransformBounds(local, transform);
        bounds.Expand(0.001f);
        for (int i = 0; i < 8; i++)
            Require(bounds.Contains(transform.MultiplyPoint3x4(new Vector3(
                (i & 1) == 0 ? local.min.x : local.max.x, (i & 2) == 0 ? local.min.y : local.max.y,
                (i & 4) == 0 ? local.min.z : local.max.z))), "Scaled/offset grass escaped its bounds.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
