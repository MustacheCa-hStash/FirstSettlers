using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Cached, spatially bounded instanced submissions. No placed-piece renderer GameObjects.</summary>
public sealed class BuildRenderer
{
    private const int Capacity = 500;
    private sealed class Batch
    {
        public BuildDefinition Definition;
        public readonly Matrix4x4[] Matrices = new Matrix4x4[Capacity];
        public int Count;
        public Bounds Bounds;
    }
    private readonly BuildSession session;
    private readonly Dictionary<(BuildDefinition, Vector3Int), Batch> current = new();
    private readonly List<Batch> batches = new();
    private readonly Plane[] planes = new Plane[6];
    private int revision = -1;
    public int DrawCalls { get; private set; }
    public BuildRenderer(BuildSession session) { this.session = session; }
    public void Draw(Camera camera, float range, float shadowRange)
    {
        DrawCalls = 0;
        if (camera == null) return;
        if (revision != session.Revision) Rebuild();
        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        foreach (var batch in batches)
        {
            if (batch.Bounds.SqrDistance(camera.transform.position) > range * range || !GeometryUtility.TestPlanesAABB(planes, batch.Bounds)) continue;
            var parameters = new RenderParams(batch.Definition.material) { camera = camera, worldBounds = batch.Bounds,
                receiveShadows = true, shadowCastingMode = batch.Bounds.SqrDistance(camera.transform.position) <= shadowRange * shadowRange ? ShadowCastingMode.On : ShadowCastingMode.Off };
            Graphics.RenderMeshInstanced(parameters, batch.Definition.mesh, 0, batch.Matrices, batch.Count);
            DrawCalls++;
        }
    }
    private void Rebuild()
    {
        batches.Clear(); current.Clear();
        foreach (var piece in session.Pieces.Values)
        {
            var key = (piece.Definition, BuildGeometry.Cell(piece.WorldBounds.center));
            if (!current.TryGetValue(key, out var batch) || batch.Count == Capacity)
            { batch = new Batch { Definition = piece.Definition }; current[key] = batch; batches.Add(batch); }
            batch.Matrices[batch.Count++] = Matrix4x4.TRS(piece.Origin, BuildGeometry.Rotation(piece.WorldYawStep), Vector3.one);
            Bounds visualBounds = BuildGeometry.WorldBounds(piece.Definition.mesh.bounds, piece.Origin, piece.WorldYawStep);
            if (batch.Count == 1) batch.Bounds = visualBounds; else batch.Bounds.Encapsulate(visualBounds);
        }
        revision = session.Revision;
    }
}
