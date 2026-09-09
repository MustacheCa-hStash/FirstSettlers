using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

public static class GrassIndirectValidation
{
    [MenuItem("Tools/Terrain/Validate Grass Compute (correctness only)")]
    public static void Run()
    {
        ValidateCounts();
        Require(Marshal.SizeOf<GrassIndirectRenderer.Instance>() == 80, "Grass buffer stride mismatch.");
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/GrassCompact.compute");
        var material = new Material(Shader.Find("Custom/GrassInstancedTerrainTint")) { enableInstancing = true };
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
        mesh.RecalculateBounds();
        var cameraObject = new GameObject("Grass compute validation") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        var properties = new MaterialPropertyBlock();
        properties.SetFloat("_RenderFadeProgress", 1);
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            Require(GrassIndirectRenderer.IsSupported(compute, material), "Grass compute or material support is unavailable.");
            ShaderUtil.allowAsyncCompilation = false;
            foreach (string variant in new[] { "", "INSTANCING_ON", "PROCEDURAL_INSTANCING_ON" })
                foreach (bool fade in new[] { false, true })
                {
                    material.DisableKeyword("INSTANCING_ON"); material.DisableKeyword("PROCEDURAL_INSTANCING_ON");
                    material.DisableKeyword("_BILLBOARD_RENDER_FADE_ON");
                    if (variant.Length > 0) material.EnableKeyword(variant);
                    if (fade) material.EnableKeyword("_BILLBOARD_RENDER_FADE_ON");
                    ShaderUtil.CompilePass(material, 0, true);
                }
            foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            material.DisableKeyword("PROCEDURAL_INSTANCING_ON"); material.DisableKeyword("INSTANCING_ON");
            material.DisableKeyword("_BILLBOARD_RENDER_FADE_ON");

            using (var batch = new GrassIndirectRenderer.Batch(compute, mesh, material))
            {
                int revision = 0;
                foreach (bool billboard in new[] { false, true })
                {
                    var previousIds = new HashSet<int>();
                    foreach (float density in new[] { 0f, 0.01f, 0.35f, 0.7f, 1f })
                    {
                        var selection = SelectRankPrefixes(density, billboard);
                        batch.Upload(selection, ++revision);
                        batch.Draw(properties, null, null, false);
                        uint[] visible = batch.ReadVisibleForValidation();
                        var sources = batch.ReadSourcesForValidation();
                        Require(visible.Length == sources.Length, "Unculled grass lost selected clumps.");
                        var ids = new HashSet<int>();
                        for (int i = 0; i < visible.Length; i++)
                        {
                            Require(visible[i] == i, "Compaction changed rank-prefix ordering.");
                            ids.Add((int)sources[i].Data.z);
                            Require(sources[i].Data.x == 0.25f && sources[i].Data.y == (sources[i].Data.z % 97) / 97f,
                                "Forest tint or rank-derived wind phase changed.");
                        }
                        Require(previousIds.IsSubsetOf(ids), "Increasing density replaced existing clumps.");
                        previousIds = ids;
                    }
                    Require(previousIds.Count == 2400, "Full density did not retain all clumps across 1023 boundaries.");
                }

                // The preceding loop also grows buffers from tiny to >2048, then
                // replaces them with smaller density prefixes using the same capacity.
                var full = SelectRankPrefixes(1, false);
                batch.Upload(full, ++revision);
                Vector4[] planes = {
                    new Vector4(1, 0, 0, 10), new Vector4(-1, 0, 0, 10),
                    new Vector4(0, 1, 0, 10), new Vector4(0, -1, 0, 10),
                    new Vector4(0, 0, 1, 0), new Vector4(0, 0, -1, 200)
                };
                batch.Draw(properties, camera, planes, false);
                var seen = new HashSet<uint>();
                foreach (uint index in batch.ReadVisibleForValidation())
                {
                    Require(index % 11 != 0 && seen.Add(index), "Frustum compaction retained an outside or duplicate clump.");
                }
                Require(seen.Count == 2400 - 219, "Per-clump frustum count mismatch.");

                // Geometry outside the frustum can still sway inside it.
                var windy = new List<GrassRenderBatch> { new GrassRenderBatch(
                    new[] { Matrix4x4.Translate(new Vector3(10.5f, 0, 20)) }, new[] { Vector4.one }) };
                batch.Upload(windy, ++revision);
                material.SetFloat("_WindStrength", 0); material.SetFloat("_WindFlutterStrength", 0);
                batch.Draw(properties, camera, planes, false);
                Require(batch.ReadVisibleForValidation().Length == 0, "Outside static clump was not culled.");
                material.SetFloat("_WindStrength", 1);
                batch.Draw(properties, camera, planes, false);
                Require(batch.ReadVisibleForValidation().Length == 1, "Wind bounds culled a swaying clump.");
                batch.Upload(new List<GrassRenderBatch>(), ++revision);
                batch.Draw(properties, camera, planes, false);
                Require(batch.ReadVisibleForValidation().Length == 0, "Empty replacement retained stale clumps.");
                batch.Dispose();
                batch.Upload(full, ++revision);
                batch.Draw(properties, null, null, false);
                Require(batch.ReadVisibleForValidation().Length == 2400, "Re-enable after disposal did not rebuild buffers.");
            }
            Require(!GrassIndirectRenderer.IsSupported(null, material), "Unassigned compute did not select fallback.");
            ValidateBounds();
            Debug.Log("Grass compute correctness passed: nested rank prefixes, deterministic counts, shader variants, >1023 clumps, culling/order, instance data, wind bounds, replacement and disposal. No benchmarks run.");
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = previousAsync;
            UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static List<GrassRenderBatch> SelectRankPrefixes(float density, bool billboard)
    {
        // Four independently rank-sorted buckets. Identical rank ties remain in
        // their supplied order; density only changes the length of each prefix.
        var matrices = new List<Matrix4x4>(); var data = new List<Vector4>();
        for (int bucket = 0; bucket < 4; bucket++)
        {
            int count = billboard ? FoliageManager.GetStochasticGrassRenderCount(600, density, 0.37f)
                : FoliageManager.GetNearGrassRenderCount(600, density);
            for (int rank = 0; rank < count; rank++)
            {
                int id = bucket * 600 + rank;
                matrices.Add(Matrix4x4.Translate(new Vector3(id % 11 == 0 ? 100 : 0, 0, 20 + id % 100)));
                data.Add(new Vector4(0.25f, (id % 97) / 97f, id, 0));
            }
        }
        var result = new List<GrassRenderBatch>();
        for (int start = 0; start < matrices.Count; start += 1023)
        {
            int count = Mathf.Min(1023, matrices.Count - start);
            result.Add(new GrassRenderBatch(matrices.GetRange(start, count).ToArray(), data.GetRange(start, count).ToArray()));
        }
        return result;
    }

    private static void ValidateCounts()
    {
        foreach (int total in new[] { 0, 1, 7, 125, 600 })
            foreach (float sample in new[] { 0f, 0.37f, 0.99999f })
            {
                int previousNear = 0, previousFar = 0;
                for (int percent = 0; percent <= 100; percent++)
                {
                    float density = percent / 100f;
                    int near = FoliageManager.GetNearGrassRenderCount(total, density);
                    int far = FoliageManager.GetStochasticGrassRenderCount(total, density, sample);
                    Require(near >= previousNear && far >= previousFar && near <= total && far <= total,
                        "Density prefix counts are not monotonic/bounded.");
                    if (percent == 0) Require(near == 0 && far == 0, "Zero density retained grass.");
                    if (percent == 100) Require(near == total && far == total, "Full density dropped grass.");
                    previousNear = near; previousFar = far;
                }
            }
        Require(FoliageManager.GetNearGrassRenderCount(1, 0.001f) == 1, "Near minimum-one rule changed.");
        Require(FoliageManager.GetStochasticGrassRenderCount(7, 0.5f, 0.49f) == 4 &&
            FoliageManager.GetStochasticGrassRenderCount(7, 0.5f, 0.5f) == 3, "Billboard rounding boundary changed.");
    }

    private static void ValidateBounds()
    {
        var local = new Bounds(new Vector3(1, 3, -2), new Vector3(4, 5, 3));
        var transform = Matrix4x4.TRS(new Vector3(8, -2, 3), Quaternion.Euler(12, 65, 21), new Vector3(-2, 4, 3));
        Bounds bounds = GrassIndirectRenderer.Batch.TransformBounds(local, transform);
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
