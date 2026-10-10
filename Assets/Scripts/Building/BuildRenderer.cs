using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

/// <summary>Cached, spatially bounded instanced submissions. No placed-piece renderer GameObjects.</summary>
public sealed class BuildRenderer
{
    private static readonly ProfilerMarker DrawMarker = new("FS.Building.Rendering");
    private static readonly ProfilerMarker RebuildMarker = new("FS.Building.RenderBatchRebuild");
    private const int Capacity = 500;
    private sealed class Batch
    {
        public Material Material;
        public Mesh Mesh;
        public readonly Matrix4x4[] Matrices = new Matrix4x4[Capacity];
        public int Count;
        public Bounds Bounds;
    }
    private readonly BuildSession session;
    private readonly Dictionary<(Material, Mesh, Vector3Int), Batch> current = new();
    private readonly List<Batch> batches = new();
    private readonly Plane[] planes = new Plane[6];
    private int revision = -1;
    private IReadOnlyDictionary<ulong,BuildResolvedState> preview;
    private IReadOnlyDictionary<ulong,BuildResolvedState> lastPreview;
    public void SetPreview(IReadOnlyDictionary<ulong,BuildResolvedState> value)
    {
        preview=value;
    }
    public int DrawCalls { get; private set; }
    public BuildRenderer(BuildSession session) { this.session = session; }
    public void Draw(Camera camera, float range, float shadowRange)
    {
        using var scope = DrawMarker.Auto();
        DrawCalls = 0;
        if (camera == null) return;
        bool samePreview=ReferenceEquals(preview,lastPreview);
        if(!samePreview && preview!=null && lastPreview!=null && preview.Count==lastPreview.Count)
        {samePreview=true;foreach(var p in preview)if(!lastPreview.TryGetValue(p.Key,out var old) || !p.Value.SameVisuals(old)){samePreview=false;break;}}
        if (revision != session.Revision || !samePreview) Rebuild();
        lastPreview=preview;
        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        foreach (var batch in batches)
        {
            float distanceSquared = batch.Bounds.SqrDistance(camera.transform.position);
            bool visible = distanceSquared <= range * range && GeometryUtility.TestPlanesAABB(planes, batch.Bounds);
            bool castsShadows = shadowRange > 0 && distanceSquared <= shadowRange * shadowRange;
            // A roof/wall outside the camera view can still shade visible surfaces.
            // Keep nearby casters in Unity's light culling without drawing their color/depth passes.
            if (!visible && !castsShadows) continue;
            bool twoSided = batch.Material.HasProperty("_Cull") && batch.Material.GetFloat("_Cull") < .5f;
            var parameters = new RenderParams(batch.Material) { camera = camera, worldBounds = batch.Bounds,
                receiveShadows = true, shadowCastingMode = !visible ? ShadowCastingMode.ShadowsOnly :
                    castsShadows ? (twoSided ? ShadowCastingMode.TwoSided : ShadowCastingMode.On) : ShadowCastingMode.Off };
            Graphics.RenderMeshInstanced(parameters, batch.Mesh, 0, batch.Matrices, batch.Count);
            DrawCalls++;
        }
    }
    private void Rebuild()
    {
        using var scope = RebuildMarker.Auto();
        batches.Clear(); current.Clear();
        foreach (var piece in session.Pieces.Values)
        {
            var state=preview!=null && preview.TryGetValue(piece.Id,out var proposed)?proposed:piece.Resolved;
            Add(piece,state!=null?state.mesh:BuildRoof.VisualMesh(piece,session));
            if(state?.auxiliaryMesh!=null)Add(piece,state.auxiliaryMesh);
            if(state!=null)foreach(var attachment in state.attachments)Add(piece,attachment.mesh,attachment.material,attachment.localMatrix);
        }
        revision = session.Revision;
    }
    private void Add(BuildPieceRecord piece,Mesh mesh)
        =>Add(piece,mesh,piece.Definition.material,Matrix4x4.identity);
    private void Add(BuildPieceRecord piece,Mesh mesh,Material material,Matrix4x4 localMatrix)
    {
        if(mesh==null || material==null)return;
        var matrix=Matrix4x4.TRS(piece.Origin,BuildGeometry.Rotation(piece.WorldYawStep),Vector3.one)*localMatrix;
        var visualBounds=TransformBounds(mesh.bounds,matrix);
        var key=(material,mesh,BuildGeometry.Cell(visualBounds.center));
        if(!current.TryGetValue(key,out var batch) || batch.Count==Capacity)
        {batch=new Batch{Material=material,Mesh=mesh};current[key]=batch;batches.Add(batch);}
        batch.Matrices[batch.Count++]=matrix;
        if(batch.Count==1)batch.Bounds=visualBounds;else batch.Bounds.Encapsulate(visualBounds);
    }
    public static Bounds TransformBounds(Bounds bounds,Matrix4x4 matrix)
    {
        Vector3 x=matrix.MultiplyVector(Vector3.right*bounds.extents.x),y=matrix.MultiplyVector(Vector3.up*bounds.extents.y),z=matrix.MultiplyVector(Vector3.forward*bounds.extents.z);
        return new Bounds(matrix.MultiplyPoint3x4(bounds.center),new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z))*2);
    }
}
