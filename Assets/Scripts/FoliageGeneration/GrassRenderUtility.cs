using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Shared resident grass GPU layout, capability routing and conservative bounds.</summary>
public static class GrassRenderUtility
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Instance
    {
        public Matrix4x4 ObjectToWorld;
        public Vector4 Data;
    }
    public static bool IsSupported(ComputeShader shader, Material material) =>
        shader != null && material != null && material.enableInstancing &&
        string.Equals(material.GetTag("GrassIndirect", false, "False"), "True", StringComparison.OrdinalIgnoreCase) &&
        SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing &&
        SystemInfo.supportsIndirectArgumentsBuffer && SystemInfo.graphicsShaderLevel >= 45 &&
        shader.HasKernel("CullResidentGrassAll");
    public static Bounds TransformBounds(Bounds local, Matrix4x4 transform)
    {
        Vector3 e = local.extents;
        var extents = new Vector3(
            Mathf.Abs(transform.m00) * e.x + Mathf.Abs(transform.m01) * e.y + Mathf.Abs(transform.m02) * e.z,
            Mathf.Abs(transform.m10) * e.x + Mathf.Abs(transform.m11) * e.y + Mathf.Abs(transform.m12) * e.z,
            Mathf.Abs(transform.m20) * e.x + Mathf.Abs(transform.m21) * e.y + Mathf.Abs(transform.m22) * e.z);
        return new Bounds(transform.MultiplyPoint3x4(local.center), extents * 2);
    }
    public static Vector3 WindPadding(Material material)
    {
        float bend = material.HasProperty("_WindStrength") ? Mathf.Abs(material.GetFloat("_WindStrength")) : 0;
        float flutter = material.HasProperty("_WindFlutterStrength") ? Mathf.Abs(material.GetFloat("_WindFlutterStrength")) : 0;
        return new Vector3(bend + flutter, bend * 0.18f, bend + flutter);
    }
}
