using System;
using UnityEditor;
using UnityEngine;

public static class DistantTreeGroundingValidation
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Validation/Validate Cached Tree Grounding")]
    public static void Run()
    {
        checks = 0;
        const int count = 64;
        var grid = new float[65, 65];
        for (int x = 0; x <= 64; x++) for (int z = 0; z <= 64; z++)
            grid[x, z] = 0.3f + Mathf.Sin(x * 0.18f) * 0.1f + Mathf.Cos(z * 0.21f) * 0.1f;
        var trees = new TreeInstanceData[count]; var matrices = new Matrix4x4[count];
        var heights = new float[count]; var expected = new float[count];
        var random = new System.Random(145678);
        Vector2 origin = new(-90f, -140f); const float size = 153.6f;
        for (int i = 0; i < count; i++)
        {
            trees[i] = new TreeInstanceData(new Vector3(0f, 10f + (float)random.NextDouble() * 30f, 0f),
                Quaternion.identity, Vector3.one, WorldFeatureVariant.MapleTree);
            matrices[i] = Matrix4x4.Translate(new Vector3(origin.x + ((float)random.NextDouble() * 1.2f - 0.1f) * size,
                trees[i].localPosition.y, origin.y + ((float)random.NextDouble() * 1.2f - 0.1f) * size));
            heights[i] = expected[i] = trees[i].localPosition.y;
        }
        var grounding = new DistantTreeGrounding(trees, matrices, heights);
        void Apply(float[,] support, Vector2 supportOrigin, float supportSize,
            float scale = 60f, float conform = 1f, bool snap = false, float delta = 0.75f)
        {
            grounding.Update(support, supportOrigin, supportSize, scale, conform, snap, delta);
            float largestShift = 0f;
            for (int i = 0; i < count; i++)
            {
                float trueY = trees[i].localPosition.y;
                float target = support == null ? trueY : Mathf.Lerp(trueY,
                    DistantTreeManager.SampleSurface(support, supportOrigin, supportSize, matrices[i].m03, matrices[i].m23) * scale,
                    Mathf.Clamp01(conform));
                expected[i] = snap ? target : Mathf.MoveTowards(expected[i], target, Mathf.Max(0f, delta));
                Check(Mathf.Abs(heights[i] - expected[i]) < 0.00001f, "Cached grounding preserves triangle seating and height blending");
                largestShift = Mathf.Max(largestShift, Mathf.Abs(expected[i] - trueY));
            }
            Check(Mathf.Abs(grounding.LargestShift - largestShift) < 0.00001f, "Cached bounds contain the current tree height shifts");
        }
        Apply(grid, origin, size, snap: true);
        Apply(grid, origin + Vector2.one, size, delta: 0f);
        for (int frame = 0; frame < 100; frame++) Apply(grid, origin + Vector2.one, size);
        Apply(grid, origin, size * 2f);
        Apply(grid, origin, size * 2f, scale: 30f);
        Apply(grid, origin, size * 2f, scale: 30f, conform: 0.4f);
        var replacement = (float[,])grid.Clone();
        for (int x = 0; x <= 64; x++) for (int z = 0; z <= 64; z++) replacement[x, z] += 0.1f;
        Apply(replacement, origin, size);
        for (int frame = 0; frame < 100; frame++) Apply(replacement, origin, size);
        Apply(replacement, origin, size, conform: 0f, snap: true);
        Apply(null, origin, size, snap: true);
        Apply(replacement, origin, size, snap: true);
        for (int i = 0; i < 100; i++) grounding.Update(replacement, origin, size, 60f, 1f, false, 0.5f);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) grounding.Update(replacement, origin, size, 60f, 1f, false, 0.5f);
        long steadyAllocations = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(steadyAllocations == 0, "Ten thousand steady grounding updates allocate nothing");
        var empty = new DistantTreeGrounding(Array.Empty<TreeInstanceData>(), Array.Empty<Matrix4x4>(), Array.Empty<float>());
        empty.Update(replacement, origin, size, 60f, 1f, true, 1f);
        Check(empty.LargestShift == 0f, "Empty manifests require no surface work");
        Debug.Log($"TREE GROUNDING CACHE PASS: {checks} checks; legacy heights/blending/bounds, support/settings changes, near terrain, empty manifests and {steadyAllocations} allocated bytes in 10,000 settled updates.");
    }
}
