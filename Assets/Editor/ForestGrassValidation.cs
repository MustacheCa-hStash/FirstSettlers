using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Asset and compute correctness only. No game scene, camera, or Play mode.
public static class ForestGrassValidation
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void ValidateAssets()
    {
        foreach (bool far in new[] { false, true })
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(far ? ForestGrassPrefabBuilder.FarPath : ForestGrassPrefabBuilder.NearPath);
            Check(prefab != null, "Forest tuft prefab missing.");
            Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            Material material = prefab.GetComponent<MeshRenderer>().sharedMaterial;
            Check(prefab.transform.localToWorldMatrix == Matrix4x4.identity, "Forest tuft has an unbaked prefab transform.");
            Check(material.enableInstancing && material.GetTag("GrassIndirect", false, "").Equals("true", StringComparison.OrdinalIgnoreCase), "Forest grass lacks indirect instancing support.");
            Check(mesh.vertexCount == (far ? 21 : 70) && mesh.triangles.Length / 3 == (far ? 7 : 42), "Tuft geometry budget changed.");
            Check(mesh.bounds.max.y > .18f && mesh.bounds.max.y < .25f && mesh.bounds.min.y < 0, "Tuft height/root seating changed.");
            Check(prefab.GetComponentsInChildren<Collider>().Length == 0, "Ground tuft has colliders.");
            int verticesPerBlade = far ? 3 : 5, dry = 0;
            Color[] colors = mesh.colors;
            for (int blade = 0; blade < mesh.vertexCount / verticesPerBlade; blade++)
            {
                int start = blade * verticesPerBlade;
                if (colors[start].a > .5f) dry++;
                for (int v = start; v < start + verticesPerBlade; v++)
                    Check(colors[v] == colors[start], "Fine color detail was added within a blade.");
            }
            Check(dry == (far ? 1 : 2), "Dry blade accent lost across LODs.");
            foreach (int index in mesh.triangles) Check(index >= 0 && index < mesh.vertexCount, "Invalid blade triangle.");
            for (int t = 0; t < mesh.triangles.Length; t += 3)
                Check(mesh.triangles[t] / verticesPerBlade == mesh.triangles[t + 1] / verticesPerBlade &&
                    mesh.triangles[t] / verticesPerBlade == mesh.triangles[t + 2] / verticesPerBlade, "Filled geometry bridges separate blade roots.");
            foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                Check(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
        }
        // Resolve the same Resources defaults used by the actual manager, without constructing a world.
        var resolve = typeof(FoliageManager).GetMethod("ResolveForestGrassAsset", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (string lod in new[] { "LOD0", "LOD1" })
        {
            object[] args = { null, "Foliage/ForestGrassTuft_" + lod, null, null };
            resolve.Invoke(null, args);
            Check(args[2] is Mesh && args[3] is Material, "Automatic forest asset resolution failed.");
        }
    }
    private static void ValidateStreaming()
    {
        // Reuse existing synthetic state/cache regressions, excluding its rendering test.
        foreach (string method in new[] { "ValidatePolicy", "ValidateStreaming" })
            typeof(GrassStreamingValidation).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }
    private static void ValidateGpu()
    {
        ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/GrassCompact.compute");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ForestGrassPrefabBuilder.NearPath);
        Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        Material material = prefab.GetComponent<MeshRenderer>().sharedMaterial;
        Check(GrassIndirectRenderer.IsSupported(shader, material), "Forest material would force CPU fallback on this GPU.");
        var settings = new GrassSettings { grassCompactShader = shader };
        var candidates = new List<FoliageInstanceData>();
        for (uint i = 0; i < 2048; i++)
            candidates.Add(new FoliageInstanceData(new Vector3(i * .002f, 0, 10), Quaternion.identity, Vector3.one,
                i * 2097152u + 137u, i % 2 == 0 ? 1 : 0));
        using (var renderer = new ResidentGrassRenderer(2, 2, 1))
        {
            // Forces arena growth beyond initial capacity; both biomes share the same source payload.
            renderer.Upload(0, candidates, Matrix4x4.identity, mesh, mesh, mesh, mesh);
            renderer.Upload(1, new List<FoliageInstanceData>(), Matrix4x4.identity, mesh, mesh);
            foreach (float blend in new[] { 0f, .25f, .5f, 1f })
            foreach (float coverage in new[] { 1f, .37f, 0f })
            {
                renderer.SetBlend(0, blend);
                var all = new HashSet<uint>();
                for (int biome = 0; biome < 2; biome++) for (int lod = 0; lod < 2; lod++)
                {
                    var expected = new HashSet<uint>();
                    for (uint i = 0; i < candidates.Count; i++)
                    {
                        var c = candidates[(int)i];
                        int selected = GrassStreamingPolicy.Select(GrassStreamingPolicy.UnitRank(c.selectionRank),
                            GrassStreamingPolicy.RepresentationRank(c.selectionRank), new Vector2(c.localPosition.x,c.localPosition.z).magnitude,
                            4, Vector4.one * coverage, coverage, blend, 100, 10);
                        if ((c.forestBlend >= .5f ? 1 : 0) == biome && selected == lod) expected.Add(i);
                    }
                    uint[] result = renderer.CullForValidation(biome, lod, settings, mesh, material, Vector3.zero, 4, 100, 10,
                        Vector4.one * coverage, coverage);
                    Check(expected.SetEquals(result) && result.Length == expected.Count, "GPU biome/LOD selection disagrees with CPU reference.");
                    foreach (uint id in result) Check(all.Add(id), "Candidate duplicated across biome or LOD draws.");
                }
            }
            var original = renderer.ReadSlotForValidation(0);
            renderer.Upload(1, candidates, Matrix4x4.Translate(Vector3.right), mesh, mesh, mesh, mesh);
            Check(renderer.ReadSlotForValidation(0)[50].ObjectToWorld == original[50].ObjectToWorld, "Biome split repacked neighboring source slot.");
            renderer.SetBlend(0, 1); renderer.SetBlend(1, 1);
            Check(renderer.CullForValidation(-1, 1, settings, mesh, material, Vector3.zero, 4, 100, 10, Vector4.one, 1).Length == 4096,
                "Legacy unsplit asset fallback lost forest candidates.");
            renderer.Upload(0, new List<FoliageInstanceData>(), Matrix4x4.identity, mesh, mesh);
            renderer.Upload(1, new List<FoliageInstanceData>(), Matrix4x4.identity, mesh, mesh);
            for (int biome = 0; biome < 2; biome++)
                Check(renderer.CullForValidation(biome, 1, settings, mesh, material, Vector3.zero, 4, 100, 10, Vector4.one, 1).Length == 0,
                    "Empty replacement retained stale biome instances.");
        }
        Debug.Log("FOREST GRASS GPU PASS: 2048 mixed candidates, four complementary biome/LOD lists, density/empty transitions, shared source slots, growth and partial replacement. Compute only; no cameras or scene rendering.");
    }
    public static void BuildAndRunBatch()
    {
        try
        {
            ForestGrassPrefabBuilder.Build(); ValidateAssets(); ValidateStreaming();
            Debug.Log("FOREST GRASS ASSETS/STREAMING PASS: simple separate blades, LOD budgets, Resources defaults, synthetic streaming/cache regression.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void RunGpuBatch()
    {
        try { ValidateAssets(); ValidateGpu(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    [MenuItem("Tools/Foliage/Validate Forest Grass (assets and compute only)")]
    public static void Run() { ValidateAssets(); ValidateGpu(); }
}
