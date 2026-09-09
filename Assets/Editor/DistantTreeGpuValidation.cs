using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

public static class DistantTreeGpuValidation
{
    [MenuItem("Tools/Terrain/Validate Distant Tree Compute (correctness only)")]
    public static void Run()
    {
        Require(Marshal.SizeOf<DistantTreeGpuBatch.SourceInstance>() == 112, "Source buffer stride mismatch.");
        Require(Marshal.SizeOf<DistantTreeGpuBatch.VisibleInstance>() == 96, "Visible buffer stride mismatch.");
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/DistantTreeCompact.compute");
        Require(DistantTreeGpuBatch.IsSupported(compute), "Compute/indirect drawing is unavailable or kernels are missing.");
        ValidateShaders();
        ValidateBounds();
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.forward }, triangles = new[] { 0, 1, 2 } };
        mesh.RecalculateBounds();
        var material = new Material(Shader.Find("Custom/TreeBillboardInstancedSimpleLit")) { enableInstancing = true };
        var cameraObject = new GameObject("Distant tree compute validation") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        var settings = new TreeSettings { distantTreeDensityAware = false, distantTreeDensity = 1,
            distantTreeProtectedCount = 0, distantTreeDepthOrdering = true, distantTreeDepthBands = 16 };
        var planes = new Vector4[6];
        // Axis-aligned test frustum: x/y in [-10,10], z in [0,200].
        planes[0] = new Vector4(1, 0, 0, 10); planes[1] = new Vector4(-1, 0, 0, 10);
        planes[2] = new Vector4(0, 1, 0, 10); planes[3] = new Vector4(0, -1, 0, 10);
        planes[4] = new Vector4(0, 0, 1, 0); planes[5] = new Vector4(0, 0, -1, 200);
        var slots = new List<int>();
        using (var batch = new DistantTreeGpuBatch(compute, mesh, material))
        try
        {
            int expected = 0;
            for (int i = 0; i < 1300; i++)
            {
                float x = i % 11 == 0 ? 100f : 0f;
                slots.Add(batch.Register(Matrix4x4.Translate(new Vector3(x, 0, 20 + i % 100)),
                    new Vector4(i, 0.5f, 0.25f, 1), new Vector4(0.5f, 10, 0, 0), 1));
                if (x == 0) expected++;
            }
            void Draw(float transition)
            {
                batch.BeginFrame();
                foreach (int slot in slots) batch.Submit(slot, 2, 1, transition, new Vector4(0.5f, 10, 0, 0));
                batch.Draw(settings, camera, Vector3.zero, planes, 200, 10, 5, 1);
            }
            Draw(0.75f);
            var visible = batch.ReadVisibleForValidation();
            Require(visible.Length == expected && visible.Length > 1023, "Culling/count failed across 1023 instances.");
            int previousBand = -1;
            var seen = new HashSet<int>();
            foreach (var item in visible)
            {
                int id = Mathf.RoundToInt(item.Tint.x);
                Require(seen.Add(id) && id % 11 != 0, "Compaction duplicated an instance or retained a culled instance.");
                Require(Mathf.Abs(item.ObjectToWorld.m13 - 2) < 0.001f && item.Fade.y == 0.75f,
                    "Instance seating/fade data mismatch.");
                int band = Mathf.Min(15, (int)(item.ObjectToWorld.m23 / 200f * 16));
                Require(band >= previousBand, "Compaction lost front-to-back depth bands.");
                previousBand = band;
            }
            settings.distantTreeDensity = 0;
            Draw(1);
            Require(batch.ReadVisibleForValidation().Length == 0, "Distance thinning failed.");
            // Grow resident storage after density has reached zero: old state must survive.
            for (int i = 1300; i < 2100; i++)
                slots.Add(batch.Register(Matrix4x4.Translate(new Vector3(0, 0, 30)), Vector4.one,
                    new Vector4(0.5f, 10, 0, 0), 0));
            Draw(1);
            Require(batch.ReadVisibleForValidation().Length == 0, "Buffer growth reset density or exposed new instances.");
            settings.distantTreeDensity = 1;
            settings.distantTreeDepthOrdering = false;
            Draw(0);
            Require(batch.ReadVisibleForValidation().Length == 0, "Zero handoff retained visible instances.");
            Draw(1);
            Require(batch.ReadVisibleForValidation().Length == expected + 800, "Recovery after zero visibility failed.");
            int released = slots[1];
            batch.Release(released); slots.RemoveAt(1);
            int reused = batch.Register(Matrix4x4.Translate(new Vector3(100, 0, 30)), Vector4.one,
                new Vector4(0.5f, 10, 0, 0), 1);
            Require(reused == released, "Released resident slot was not reused.");
            slots.Add(reused);
            Draw(1);
            Require(batch.ReadVisibleForValidation().Length == expected + 799, "Released slot retained stale geometry.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
        Debug.Log("Distant tree compute correctness passed: shader variants, scaled bounds, >1023 instances, culling, ordering, tint/fade, thinning, capacity growth and slot reuse. No benchmark run.");
    }

    private static void ValidateShaders()
    {
        bool previous = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders" }))
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                var material = new Material(shader) { enableInstancing = true };
                try
                {
                    if (material.GetTag("DistantTreeIndirect", false, "False") != "True") continue;
                    foreach (string variant in new[] { "", "INSTANCING_ON", "PROCEDURAL_INSTANCING_ON" })
                    {
                        material.DisableKeyword("INSTANCING_ON"); material.DisableKeyword("PROCEDURAL_INSTANCING_ON");
                        if (variant.Length > 0) material.EnableKeyword(variant);
                        ShaderUtil.CompilePass(material, 0, true);
                        if (shader.name == "Custom/SpruceBillboardVariationSimpleLitCutout")
                        {
                            material.EnableKeyword("SPRUCE_FAR_SIMPLE");
                            ShaderUtil.CompilePass(material, 0, true);
                            material.DisableKeyword("SPRUCE_FAR_SIMPLE");
                        }
                    }
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,
                            shader.name + ": " + message.message);
                }
                finally { UnityEngine.Object.DestroyImmediate(material); }
            }
        }
        finally { ShaderUtil.allowAsyncCompilation = previous; }
    }

    private static void ValidateBounds()
    {
        var local = new Bounds(new Vector3(2, 4, -3), new Vector3(3, 9, 2));
        var transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(25, 60, -12), new Vector3(-2, 5, 3));
        Vector4 envelope = DistantTreeGpuBatch.CalculateBounds(local, transform);
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3((i & 1) == 0 ? local.min.x : local.max.x,
                (i & 2) == 0 ? local.min.y : local.max.y, (i & 4) == 0 ? local.min.z : local.max.z);
            Vector3 world = transform.MultiplyPoint3x4(p);
            Require(Mathf.Abs(world.x) <= envelope.x + 0.001f && Mathf.Abs(world.z) <= envelope.x + 0.001f &&
                world.y >= envelope.y - 0.001f && world.y <= envelope.z + 0.001f, "Scaled/offset tree escaped bounds.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
