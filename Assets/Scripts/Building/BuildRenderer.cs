using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

/// <summary>Spatial instanced groups updated only for changed pieces. Preview overlays have separate ownership.</summary>
public sealed class BuildRenderer : IDisposable
{
    private static readonly ProfilerMarker DrawMarker = new("FS.Building.Rendering");
    private static readonly ProfilerMarker RebuildMarker = new("FS.Building.RenderBatchRebuild");
    private const int Capacity = 500;
    private readonly struct Part
    {
        public readonly ulong Id;
        public readonly Matrix4x4 Matrix;
        public readonly Bounds Bounds;
        public Part(ulong id,Matrix4x4 matrix,Bounds bounds) { Id=id;Matrix=matrix;Bounds=bounds; }
    }
    private sealed class Batch
    {
        public readonly Matrix4x4[] Matrices = new Matrix4x4[Capacity];
        public readonly ulong[] Ids = new ulong[Capacity];
        public Matrix4x4[] Filtered;
        public int FilteredCount, Count;
        public bool IsFiltered;
        public Bounds Bounds;
    }
    private sealed class Group
    {
        public (Material,Mesh,Vector3Int) Key;
        public Material Material;
        public Mesh Mesh;
        public readonly Dictionary<ulong,List<Part>> Members = new();
        public readonly List<Batch> Batches = new();
    }
    private readonly BuildSession session;
    private readonly Dictionary<(Material,Mesh,Vector3Int),Group> groups = new();
    private readonly Dictionary<(Material,Mesh,Vector3Int),Group> previewGroups = new();
    private readonly Dictionary<ulong,HashSet<Group>> pieceGroups = new();
    private readonly HashSet<ulong> pending = new(), hidden = new();
    private readonly HashSet<Group> dirty = new(), filterGroups = new();
    private readonly Plane[] planes = new Plane[6];
    private IReadOnlyDictionary<ulong,BuildResolvedState> preview, lastPreview;
    private bool disposed;
    public int DrawCalls { get; private set; }
    // Counts synchronization passes, rather than rebuilding every committed group.
    public int BatchRebuilds { get; private set; }
    public int LastUpdatedPieces { get; private set; }
    public int LastRebuiltGroups { get; private set; }
    public int CommittedInstanceCount { get; private set; }
    public int PreviewInstanceCount { get; private set; }
    public BuildRenderer(BuildSession session)
    {
        this.session=session;
        foreach(ulong id in session.Pieces.Keys)pending.Add(id);
        session.VisualChanged+=OnVisualChanged;
    }
    private void OnVisualChanged(ulong id)=>pending.Add(id);
    public void SetPreview(IReadOnlyDictionary<ulong,BuildResolvedState> value)=>preview=value;
    public void Draw(Camera camera,float range,float shadowRange)
    {
        using var scope=DrawMarker.Auto();
        DrawCalls=0;LastUpdatedPieces=0;LastRebuiltGroups=0;
        if(disposed || camera==null)return;
        bool samePreview=ReferenceEquals(preview,lastPreview);
        if(!samePreview && preview!=null && lastPreview!=null && preview.Count==lastPreview.Count)
        {
            samePreview=true;
            foreach(var p in preview)
                if(!lastPreview.TryGetValue(p.Key,out var old) || !p.Value.SameVisuals(old)){samePreview=false;break;}
        }
        bool changed=ApplyPending();
        if(changed || !samePreview)RebuildPreview();
        lastPreview=preview;
        GeometryUtility.CalculateFrustumPlanes(camera,planes);
        foreach(var group in groups.Values)DrawGroup(group,camera,range,shadowRange);
        foreach(var group in previewGroups.Values)DrawGroup(group,camera,range,shadowRange);
    }
    private void DrawGroup(Group group,Camera camera,float range,float shadowRange)
    {
        foreach(var batch in group.Batches)
        {
            int count=batch.IsFiltered?batch.FilteredCount:batch.Count;
            if(count==0)continue;
            float distanceSquared=batch.Bounds.SqrDistance(camera.transform.position);
            bool visible=distanceSquared<=range*range && GeometryUtility.TestPlanesAABB(planes,batch.Bounds);
            bool castsShadows=shadowRange>0 && distanceSquared<=shadowRange*shadowRange;
            if(!visible && !castsShadows)continue;
            bool twoSided=group.Material.HasProperty("_Cull") && group.Material.GetFloat("_Cull")<.5f;
            var parameters=new RenderParams(group.Material) { camera=camera,worldBounds=batch.Bounds,
                receiveShadows=true,shadowCastingMode=!visible?ShadowCastingMode.ShadowsOnly:
                    castsShadows?(twoSided?ShadowCastingMode.TwoSided:ShadowCastingMode.On):ShadowCastingMode.Off };
            Graphics.RenderMeshInstanced(parameters,group.Mesh,0,batch.IsFiltered?batch.Filtered:batch.Matrices,count);
            DrawCalls++;
        }
    }
    private bool ApplyPending()
    {
        if(pending.Count==0)return false;
        using var scope=RebuildMarker.Auto();
        BatchRebuilds++;LastUpdatedPieces=pending.Count;dirty.Clear();
        foreach(ulong id in pending)
        {
            if(pieceGroups.TryGetValue(id,out var previous))
            {
                foreach(var group in previous)
                {
                    CommittedInstanceCount-=group.Members[id].Count;
                    group.Members.Remove(id);dirty.Add(group);
                }
                pieceGroups.Remove(id);
            }
            if(!session.TryGet(id,out var piece))continue;
            var state=piece.Resolved;
            AddCommitted(piece,state!=null?state.mesh:BuildRoof.VisualMesh(piece,session),piece.Definition.material,Matrix4x4.identity);
            if(state==null)continue;
            if(state.auxiliaryMesh!=null)AddCommitted(piece,state.auxiliaryMesh,piece.Definition.material,Matrix4x4.identity);
            foreach(var a in state.attachments)AddCommitted(piece,a.mesh,a.material,a.localMatrix);
        }
        pending.Clear();
        foreach(var group in dirty)
        {
            LastRebuiltGroups++;
            if(group.Members.Count==0){groups.Remove(group.Key);continue;}
            RebuildGroup(group);Filter(group);
        }
        dirty.Clear();
        return true;
    }
    private static Group AddPart(Dictionary<(Material,Mesh,Vector3Int),Group> target,BuildPieceRecord piece,Mesh mesh,Material material,Matrix4x4 local)
    {
        if(mesh==null || material==null)return null;
        var matrix=Matrix4x4.TRS(piece.Origin,BuildGeometry.Rotation(piece.WorldYawStep),Vector3.one)*local;
        var bounds=TransformBounds(mesh.bounds,matrix);
        var key=(material,mesh,BuildGeometry.Cell(bounds.center));
        if(!target.TryGetValue(key,out var group))target.Add(key,group=new Group { Key=key,Material=material,Mesh=mesh });
        if(!group.Members.TryGetValue(piece.Id,out var parts))group.Members.Add(piece.Id,parts=new List<Part>());
        parts.Add(new Part(piece.Id,matrix,bounds));
        return group;
    }
    private void AddCommitted(BuildPieceRecord piece,Mesh mesh,Material material,Matrix4x4 local)
    {
        var group=AddPart(groups,piece,mesh,material,local);if(group==null)return;
        if(!pieceGroups.TryGetValue(piece.Id,out var owned))pieceGroups.Add(piece.Id,owned=new HashSet<Group>());
        owned.Add(group);dirty.Add(group);CommittedInstanceCount++;
    }
    private static void RebuildGroup(Group group)
    {
        foreach(var old in group.Batches){old.Count=0;old.IsFiltered=false;}
        int index=0;Batch batch=null;
        foreach(var parts in group.Members.Values)foreach(var part in parts)
        {
            if(batch==null || batch.Count==Capacity)
            {
                if(index==group.Batches.Count)group.Batches.Add(new Batch());
                batch=group.Batches[index++];
            }
            batch.Ids[batch.Count]=part.Id;batch.Matrices[batch.Count++]=part.Matrix;
            if(batch.Count==1)batch.Bounds=part.Bounds;else batch.Bounds.Encapsulate(part.Bounds);
        }
    }
    private void Filter(Group group)
    {
        foreach(var batch in group.Batches)
        {
            batch.IsFiltered=false;
            for(int i=0;i<batch.Count;i++)if(hidden.Contains(batch.Ids[i])){batch.IsFiltered=true;break;}
            if(!batch.IsFiltered)continue;
            batch.Filtered??=new Matrix4x4[Capacity];batch.FilteredCount=0;
            for(int i=0;i<batch.Count;i++)if(!hidden.Contains(batch.Ids[i]))batch.Filtered[batch.FilteredCount++]=batch.Matrices[i];
        }
    }
    private void RebuildPreview()
    {
        filterGroups.Clear();
        foreach(ulong id in hidden)if(pieceGroups.TryGetValue(id,out var previous))filterGroups.UnionWith(previous);
        hidden.Clear();
        if(preview!=null)foreach(var pair in preview)if(session.TryGet(pair.Key,out _))
        {
            hidden.Add(pair.Key);
            if(pieceGroups.TryGetValue(pair.Key,out var affected))filterGroups.UnionWith(affected);
        }
        foreach(var group in filterGroups)Filter(group);
        filterGroups.Clear();previewGroups.Clear();PreviewInstanceCount=0;
        if(preview==null)return;
        foreach(var pair in preview)if(session.TryGet(pair.Key,out var piece))
        {
            var state=pair.Value;
            AddPreview(piece,state.mesh,piece.Definition.material,Matrix4x4.identity);
            if(state.auxiliaryMesh!=null)AddPreview(piece,state.auxiliaryMesh,piece.Definition.material,Matrix4x4.identity);
            foreach(var a in state.attachments)AddPreview(piece,a.mesh,a.material,a.localMatrix);
        }
        foreach(var group in previewGroups.Values)RebuildGroup(group);
    }
    private void AddPreview(BuildPieceRecord piece,Mesh mesh,Material material,Matrix4x4 local)
    { if(AddPart(previewGroups,piece,mesh,material,local)!=null)PreviewInstanceCount++; }
    public void Dispose()
    {
        if(disposed)return;disposed=true;session.VisualChanged-=OnVisualChanged;
        groups.Clear();previewGroups.Clear();pieceGroups.Clear();pending.Clear();hidden.Clear();
        dirty.Clear();filterGroups.Clear();preview=null;lastPreview=null;
        CommittedInstanceCount=PreviewInstanceCount=0;
    }
    public static Bounds TransformBounds(Bounds bounds,Matrix4x4 matrix)
    {
        Vector3 x=matrix.MultiplyVector(Vector3.right*bounds.extents.x),y=matrix.MultiplyVector(Vector3.up*bounds.extents.y),z=matrix.MultiplyVector(Vector3.forward*bounds.extents.z);
        return new Bounds(matrix.MultiplyPoint3x4(bounds.center),new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z))*2);
    }
}
