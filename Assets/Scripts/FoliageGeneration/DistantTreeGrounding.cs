using System;
using Unity.Profiling;
using UnityEngine;

// A manifest's tree positions and published terrain grids are immutable.
// Resample only when its support or conformity changes; continue blending existing heights.
public sealed class DistantTreeGrounding
{
    private static readonly ProfilerMarker RefreshMarker = new("FS.DistantTrees.RefreshGroundTargets");
    private static readonly ProfilerMarker BlendMarker = new("FS.DistantTrees.BlendGroundHeights");
    private readonly TreeInstanceData[] trees;
    private readonly Matrix4x4[] matrices;
    private readonly float[] targets;
    private float[,] lastGrid;
    private Vector2 lastOrigin;
    private float lastSize, lastScale, lastConform;
    private bool initialized, blending;
    public float[] Heights { get; }
    public float LargestShift { get; private set; }

    public DistantTreeGrounding(TreeInstanceData[] trees, Matrix4x4[] matrices, float[] heights)
    {
        if (trees == null || matrices == null || heights == null || trees.Length != matrices.Length || trees.Length != heights.Length)
            throw new ArgumentException("Tree grounding arrays must have matching lengths.");
        this.trees = trees; this.matrices = matrices; Heights = heights;
        targets = new float[trees.Length];
    }

    public void Update(float[,] grid, Vector2 origin, float size,
        float verticalScale, float conform, bool snap, float maximumDelta)
    {
        conform = Mathf.Clamp01(conform);
        if (!initialized || !ReferenceEquals(lastGrid, grid) ||
            lastOrigin.x != origin.x || lastOrigin.y != origin.y || lastSize != size || lastScale != verticalScale || lastConform != conform)
        {
            using (RefreshMarker.Auto())
            {
                lastGrid = grid; lastOrigin = origin; lastSize = size;
                lastScale = verticalScale; lastConform = conform; initialized = true; blending = true;
                for (int i = 0; i < trees.Length; i++)
                {
                    float trueY = trees[i].localPosition.y;
                    if (grid == null || conform == 0f) { targets[i] = trueY; continue; }
                    var matrix = matrices[i];
                    float ground = DistantTreeManager.SampleSurface(grid, origin, size, matrix.m03, matrix.m23);
                    targets[i] = Mathf.Lerp(trueY, ground * verticalScale, conform);
                }
            }
        }
        if (!blending) return;
        using (BlendMarker.Auto())
        {
            blending = false; LargestShift = 0f;
            maximumDelta = Mathf.Max(0f, maximumDelta);
            for (int i = 0; i < trees.Length; i++)
            {
                float target = targets[i];
                Heights[i] = snap ? target : Mathf.MoveTowards(Heights[i], target, maximumDelta);
                blending |= Heights[i] != target;
                LargestShift = Mathf.Max(LargestShift, Mathf.Abs(Heights[i] - trees[i].localPosition.y));
            }
        }
    }
}
