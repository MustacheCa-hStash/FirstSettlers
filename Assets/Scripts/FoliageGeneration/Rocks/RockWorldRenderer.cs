using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Groups all resident rock shapes across chunks, with tight per-instance culling and reusable submission arrays.</summary>
public sealed class RockWorldRenderer : IDisposable
{
    private readonly struct Source
    {
        public readonly RockRenderSet Set;
        public readonly RockRenderSet.Visual Visual;
        public Source(RockRenderSet set, RockRenderSet.Visual visual){Set=set;Visual=visual;}
    }
    private sealed class Submission
    {
        public Matrix4x4[] Matrices=new Matrix4x4[16];
        public int Count;
    }
    private sealed class Group
    {
        public Mesh Mesh;
        public Material Material;
        public int Submesh;
        public readonly List<Source> Sources=new();
        public readonly Submission Near=new(),Far=new(),Shadow=new();
    }
    private readonly Dictionary<(Mesh,Material,int),Group> groups=new();
    private readonly HashSet<RockRenderSet> sets=new();
    private readonly Plane[] planes=new Plane[6];
    private readonly TreeSettings settings;
    private bool submitGraphics;
    private static readonly ProfilerMarker CullMarker=new("FS.Rocks.CullAndGroup"),SubmitMarker=new("FS.Rocks.Submit");
    public int ResidentInstances { get; private set; }
    public int DrawCalls { get; private set; }
    public RenderGeometryStats VisibleGeometry { get; private set; }
    public RenderGeometryStats ShadowGeometry { get; private set; }
    public RockWorldRenderer(TreeSettings settings){this.settings=settings;}
    public void Register(RockRenderSet set)
    {
        if(!sets.Add(set))return;
        ResidentInstances+=set.InstanceCount;
        var flameParts=new List<LocalFlameGeometry.Part>(set.Visuals.Count);
        foreach(var visual in set.Visuals)flameParts.Add(new(visual.Mesh,visual.Matrix,visual.Material,visual.Submesh));
        LocalFlameGeometry.Set(set,flameParts);
        foreach(var visual in set.Visuals)
        {
            Add(set,visual,visual.Mesh);
            if(visual.DistantMesh!=null && visual.DistantMesh!=visual.Mesh && visual.Submesh<visual.DistantMesh.subMeshCount)Add(set,visual,visual.DistantMesh);
        }
    }
    private void Add(RockRenderSet set,RockRenderSet.Visual visual,Mesh mesh)
    {
        var key=(mesh,visual.Material,visual.Submesh);
        if(!groups.TryGetValue(key,out var group))groups.Add(key,group=new Group{Mesh=mesh,Material=visual.Material,Submesh=visual.Submesh});
        group.Sources.Add(new Source(set,visual));
        int capacity=Mathf.Min(1023,Mathf.NextPowerOfTwo(group.Sources.Count));
        if(capacity>group.Near.Matrices.Length)
        {
            Array.Resize(ref group.Near.Matrices,capacity);Array.Resize(ref group.Far.Matrices,capacity);Array.Resize(ref group.Shadow.Matrices,capacity);
        }
    }
    public void Unregister(RockRenderSet set)
    {
        if(set==null || !sets.Remove(set))return;
        LocalFlameGeometry.Remove(set);
        ResidentInstances-=set.InstanceCount;
        foreach(var group in groups.Values)
            for(int i=group.Sources.Count-1;i>=0;i--)if(group.Sources[i].Set==set)group.Sources.RemoveAt(i);
    }
    public void Draw(Vector3 focus,Camera camera,bool submitGraphics = true)
    {
        this.submitGraphics=submitGraphics;
        DrawCalls=0;VisibleGeometry=default;ShadowGeometry=default;
        if(settings!=null && !settings.enableRockRendering)return;
        bool cull=camera!=null;
        if(cull)GeometryUtility.CalculateFrustumPlanes(camera,planes);
        float shadowRange=settings!=null?Mathf.Max(0,settings.rockShadowDistance):140;
        if(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)shadowRange=Mathf.Min(shadowRange,pipeline.shadowDistance);
        Vector3 lodFocus=cull?camera.transform.position:focus;
        using var marker=CullMarker.Auto();
        foreach(var group in groups.Values)
        {
            group.Near.Count=group.Far.Count=group.Shadow.Count=0;
            foreach(var source in group.Sources)
            {
                if(!source.Set.Enabled)continue;
                var visual=source.Visual;
                float distance=(visual.Bounds.center-lodFocus).sqrMagnitude;
                bool low=visual.DistantMesh!=null && distance>visual.LodDistance*visual.LodDistance;
                if(group.Mesh!=(low?visual.DistantMesh:visual.Mesh))continue;
                bool visible=!cull || GeometryUtility.TestPlanesAABB(planes,visual.Bounds);
                float instanceShadowRange=Mathf.Min(shadowRange,visual.ShadowDistance);
                float reach=instanceShadowRange+visual.Bounds.extents.magnitude;
                bool shadow=instanceShadowRange>0 && (visual.Bounds.center-focus).sqrMagnitude<=reach*reach;
                if(!visible && !shadow)continue;
                var submission=visible?(shadow?group.Near:group.Far):group.Shadow;
                submission.Matrices[submission.Count++]=visual.Matrix;
                if(submission.Count==1023)Flush(group,submission,visible,shadow,camera);
            }
            Flush(group,group.Near,true,true,camera);Flush(group,group.Far,true,false,camera);Flush(group,group.Shadow,false,true,camera);
        }
    }
    private void Flush(Group group,Submission submission,bool visible,bool shadows,Camera camera)
    {
        int count=submission.Count;if(count==0)return;
        using var marker=SubmitMarker.Auto();
        if(submitGraphics)Graphics.DrawMeshInstanced(group.Mesh,group.Submesh,group.Material,submission.Matrices,count,null,
                visible?(shadows?ShadowCastingMode.On:ShadowCastingMode.Off):ShadowCastingMode.ShadowsOnly,
                true,GameplayLayers.WorldSolid,camera,LightProbeUsage.Off);
        DrawCalls++;
        var stats=visible?VisibleGeometry:ShadowGeometry;stats.instances+=count;stats.vertices+=(long)group.Mesh.vertexCount*count;
        stats.triangles+=(long)group.Mesh.GetIndexCount(group.Submesh)/3*count;
        if(visible)VisibleGeometry=stats;else ShadowGeometry=stats;
        submission.Count=0;
    }
    public void Dispose(){foreach(var set in sets)LocalFlameGeometry.Remove(set);groups.Clear();sets.Clear();ResidentInstances=0;}
}
