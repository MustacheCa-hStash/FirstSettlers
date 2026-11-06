using System.Collections.Generic;
using UnityEngine;

/// <summary>One streamed chunk of immutable instance matrices. No per-rock visual GameObjects.</summary>
public sealed class RockRenderSet
{
    public readonly struct Visual
    {
        public readonly Mesh Mesh, DistantMesh;
        public readonly Material Material;
        public readonly int Submesh;
        public readonly Matrix4x4 Matrix;
        public readonly Bounds Bounds;
        public readonly float LodDistance, ShadowDistance;
        public Visual(Mesh mesh, Mesh distant, Material material, int submesh, Matrix4x4 matrix, float lod, float shadowDistance)
        {
            Mesh=mesh;DistantMesh=distant;Material=material;Submesh=submesh;Matrix=matrix;LodDistance=lod;ShadowDistance=shadowDistance;
            var b=mesh.bounds;var ext=b.extents;
            Bounds=new Bounds(matrix.MultiplyPoint3x4(b.center),new Vector3(
                Mathf.Abs(matrix.m00)*ext.x+Mathf.Abs(matrix.m01)*ext.y+Mathf.Abs(matrix.m02)*ext.z,
                Mathf.Abs(matrix.m10)*ext.x+Mathf.Abs(matrix.m11)*ext.y+Mathf.Abs(matrix.m12)*ext.z,
                Mathf.Abs(matrix.m20)*ext.x+Mathf.Abs(matrix.m21)*ext.y+Mathf.Abs(matrix.m22)*ext.z)*2);
        }
    }
    public readonly List<Visual> Visuals = new();
    public bool Enabled = true;
    public Bounds CollisionBounds { get; private set; }
    public sealed class Entry
    {
        public GameObject Prefab;
        public RockInstanceData Placement;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
    }
    public readonly List<Entry> Entries = new();
    public int InstanceCount => Entries.Count;

    public void Add(GameObject prefab, RockInstanceData placement, Transform chunk)
    {
        if (prefab == null || chunk == null) return;
        var preset = prefab.GetComponent<RockPreset>();
        // A shallow embed seats the smooth, irregular base and modest pitch below the terrain.
        var position = placement.localPosition;
        if (preset != null) position.y -= preset.groundEmbed * Mathf.Abs(placement.localScale.y);
        var matrix = chunk.localToWorldMatrix * Matrix4x4.TRS(position, placement.localRotation, placement.localScale);
        Entries.Add(new Entry { Prefab = prefab, Placement = placement, Position = matrix.GetColumn(3),
            Rotation = matrix.rotation, Scale = matrix.lossyScale });
        var bound=Entries.Count==1?new Bounds(matrix.GetColumn(3),Vector3.zero):CollisionBounds;
        bound.Encapsulate((Vector3)matrix.GetColumn(3));CollisionBounds=bound;
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;
            var local = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var materials = renderer.sharedMaterials;
            for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
            {
                if (materials.Length == 0) continue;
                var material = materials[Mathf.Min(sub, materials.Length - 1)];
                if (material == null) continue;
                material.enableInstancing = true;
                Visuals.Add(new Visual(filter.sharedMesh, filter.gameObject == prefab && preset != null ? preset.distantMesh : null,
                    material, sub, matrix * local, preset != null ? preset.lodDistance : float.PositiveInfinity,
                    preset != null ? preset.shadowDistance : float.PositiveInfinity));
            }
        }
    }
    public void Accumulate(ref RenderGeometryStats stats)
    {
        foreach (var visual in Visuals)
        {
            stats.instances++;
            stats.vertices += visual.Mesh.vertexCount;
            stats.triangles += (long)visual.Mesh.GetIndexCount(visual.Submesh) / 3;
        }
    }
}
