using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ResidentGrassCullingValidation
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        Check(Marshal.SizeOf<GrassRenderUtility.Instance>() == 80, "Grass source stride changed.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/GrassCompact.compute");
        var material = new Material(Shader.Find("Custom/GrassInstancedTerrainTint")) { enableInstancing = true };
        var meshes = new Mesh[4]; var materials = new[] { material, material, material, material };
        var settings = new GrassSettings { grassCompactShader = shader };
        var candidates = new List<FoliageInstanceData>();
        for (uint i = 0; i < 2300; i++)
            candidates.Add(new FoliageInstanceData(new Vector3((i % 41) - 20f, 0, 5),
                Quaternion.Euler(0, i % 90, 0), Vector3.one, i * 1867373u, i % 2 == 0 ? 0 : 1));
        try
        {
            Check(GrassRenderUtility.IsSupported(shader, material), "Resident grass compute unavailable.");
            for (int i = 0; i < 4; i++)
            {
                meshes[i] = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
                meshes[i].bounds = new Bounds(new Vector3(i * 3f, 0, 0), Vector3.one * (i + 1));
            }
            var planes = new[] { new Vector4(1,0,0,10), new Vector4(-1,0,0,10), new Vector4(0,1,0,10),
                new Vector4(0,-1,0,10), new Vector4(0,0,1,10), new Vector4(0,0,-1,10) };
            using var renderer = new ResidentGrassRenderer(2, 8, 2);
            renderer.Upload(0, candidates, Matrix4x4.identity, meshes[0], meshes[1], meshes[2], meshes[3]);
            renderer.Upload(1, new List<FoliageInstanceData>(), Matrix4x4.identity, meshes[0], meshes[1]);
            foreach (float blend in new[] { 0f, .3f, 1f })
                foreach (Vector4 density in new[] { Vector4.one, new Vector4(1,.6f,.4f,.2f), Vector4.zero })
                {
                    renderer.SetBlend(0, blend);
                    renderer.CullAllForValidation(settings, meshes, materials, planes, Vector3.zero, 3, 100, 10, density, .7f);
                    Check(renderer.LastCullDispatchCount == 1, "Grass issued multiple selection dispatches.");
                    var seen = new HashSet<uint>();
                    for (int channel = 0; channel < 4; channel++)
                    {
                        var expected = new HashSet<uint>();
                        for (int i = 0; i < candidates.Count; i++)
                        {
                            var candidate = candidates[i];
                            int biome = candidate.forestBlend >= .5f ? 1 : 0;
                            int lod = GrassStreamingPolicy.Select(GrassStreamingPolicy.UnitRank(candidate.selectionRank),
                                GrassStreamingPolicy.RepresentationRank(candidate.selectionRank),
                                new Vector2(candidate.localPosition.x, candidate.localPosition.z).magnitude,
                                3, density, .7f, blend, 100, 10);
                            if (lod < 0 || biome * 2 + lod != channel) continue;
                            var matrix = Matrix4x4.TRS(candidate.localPosition, candidate.localRotation, candidate.localScale);
                            Bounds bounds = GrassRenderUtility.TransformBounds(meshes[channel].bounds, matrix);
                            bounds.Expand(GrassRenderUtility.WindPadding(material) * 2);
                            bool visible = true;
                            foreach (var p in planes)
                            {
                                var n = new Vector3(p.x, p.y, p.z);
                                visible &= Vector3.Dot(n, bounds.center) + p.w + Vector3.Dot(new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z)), bounds.extents) >= 0;
                            }
                            if (visible) expected.Add((uint)i);
                        }
                        var actual = renderer.ReadVisibleForValidation(channel);
                        Check(expected.SetEquals(actual), "Shared culling disagreed with CPU biome/LOD/mesh bounds selection.");
                        foreach (uint id in actual) Check(seen.Add(id), "An instance entered multiple draw lists.");
                    }
                }
            renderer.SetBlend(0, 1);
            // A missing far asset must force all eligible candidates to the surviving near mesh.
            renderer.CullAllForValidation(settings, new[] { meshes[0], null, meshes[2], null },
                new[] { material, null, material, null }, null, Vector3.zero, 3, 100, 10, Vector4.one, 1);
            Check(renderer.ReadVisibleForValidation(0).Length + renderer.ReadVisibleForValidation(2).Length == candidates.Count,
                "Missing far asset lost resident grass.");
            Check(renderer.ReadVisibleForValidation(1).Length == 0 && renderer.ReadVisibleForValidation(3).Length == 0,
                "Disabled channels retained stale counts.");
            var original = renderer.ReadSlotForValidation(0);
            renderer.Upload(1, candidates, Matrix4x4.Translate(Vector3.right), meshes[0], meshes[1]);
            Check(renderer.ReadSlotForValidation(0)[100].ObjectToWorld == original[100].ObjectToWorld, "Partial upload changed neighbor data.");
            renderer.Upload(0, new List<FoliageInstanceData>(), Matrix4x4.identity, meshes[0], meshes[1]);
            renderer.SetBlend(1, 0);
            renderer.CullAllForValidation(settings, meshes, materials, null, Vector3.zero, 3, 100, 10, Vector4.one, 1);
            Check(renderer.ReadVisibleForValidation(0).Length + renderer.ReadVisibleForValidation(2).Length == candidates.Count,
                "Slot replacement retained stale or missing candidates.");
        }
        finally { foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        Debug.Log("SHARED GRASS CULL PASS: one dispatch, four channels, >1023 instances, exact CPU selection/bounds parity, missing assets, stale counts and partial slots.");
    }
}
