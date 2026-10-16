using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Detached, fully prepared replacement. Owns only its generated meshes and objects.</summary>
public sealed class TerrainSculptChunkSurface : IDisposable
{
    private GameObject root;
    private Mesh renderMesh, collisionMesh;
    private MeshRenderer renderer;
    private MeshCollider collider;
    internal ChunkRuntime BoundRuntime { get; private set; }
    public bool HasRender => renderMesh != null;
    public bool HasCollision => collisionMesh != null;
    public long PayloadBytes { get; private set; }
    public int RenderTriangles { get; private set; }
    public int CollisionTriangles { get; private set; }
    internal bool CollisionPrepared => collisionMesh == null || collider != null;
    internal Bounds WorldBounds => renderer!=null?renderer.bounds:collider!=null?collider.bounds:default;
    internal Mesh RenderMesh => renderMesh;

    internal TerrainSculptChunkSurface(ChunkCoord coord,TerrainSculptChunkBuild build,TerrainDensityProfile profile,int chunkSize,TerrainSculptSurfaceSettings appearance=null)
    {
        root=new GameObject($"Sculpt_{coord.x}_{coord.z}");root.SetActive(false);
        root.layer=GameplayLayers.WorldSolid;root.AddComponent<WorldGroundSurface>();
        try
        {
            if(build.Render!=null)
            {
                renderMesh=build.Render.CreateMesh(new double3(0),root.name+" render");
                var uv=new Vector2[build.Render.Positions.Length];
                double width=chunkSize*profile.Spacing;
                for(int i=0;i<uv.Length;i++)uv[i]=new Vector2((float)(build.Render.Positions[i].x/width-coord.x),(float)(build.Render.Positions[i].z/width-coord.z));
                renderMesh.uv=uv;
                root.AddComponent<MeshFilter>().sharedMesh=renderMesh;
                renderer=root.AddComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
                if(appearance!=null){var properties=new MaterialPropertyBlock();appearance.Apply(properties);renderer.SetPropertyBlock(properties);}
                PayloadBytes+=build.Render.PayloadBytes;
                RenderTriangles=build.Render.TriangleCount;
            }
            if(build.Collision!=null)
            {
                collisionMesh=build.Collision.CreateMesh(new double3(0),root.name+" collision");
                PayloadBytes+=build.Collision.PayloadBytes;
                CollisionTriangles=build.Collision.TriangleCount;
            }
        }
        catch { Dispose();throw; }
    }

    // Main-thread cooking is individually staged under the shared collider admission.
    internal void PrepareCollision()
    {
        if(collisionMesh==null||collider!=null)return;
        collider=root.AddComponent<MeshCollider>();collider.sharedMesh=collisionMesh;
    }
    internal void Bind(ChunkRuntime runtime,Material material,bool receiveShadows)
    {
        if(!CollisionPrepared)throw new InvalidOperationException("Collision must be prepared before attachment.");
        BoundRuntime=runtime;
        root.transform.SetParent(runtime.RootTransform,true);
        root.transform.position=Vector3.zero;root.transform.rotation=Quaternion.identity;root.transform.localScale=Vector3.one;
        if(renderer!=null){renderer.sharedMaterial=material;renderer.receiveShadows=receiveShadows;}
        root.SetActive(true);
    }
    internal void SetRenderVisible(bool visible,bool allowShadows=true)
    {
        if(renderer==null)return;
        renderer.enabled=visible||allowShadows;
        renderer.shadowCastingMode=visible?ShadowCastingMode.TwoSided:allowShadows?ShadowCastingMode.ShadowsOnly:ShadowCastingMode.Off;
    }
    internal void SetCollisionEnabled(bool enabled) { if(collider!=null)collider.enabled=enabled; }
    internal void Detach()
    {
        BoundRuntime=null;
        if(root==null)return;
        root.SetActive(false);root.transform.SetParent(null,true);
    }
    public void Dispose()
    {
        if(BoundRuntime!=null)BoundRuntime.SetSculptSurface(null);
        if(collider!=null)collider.sharedMesh=null;
        if(root!=null)root.SetActive(false);
        TerrainAssetOwner.Destroy(root);root=null;renderer=null;collider=null;
        TerrainAssetOwner.Destroy(renderMesh);TerrainAssetOwner.Destroy(collisionMesh);renderMesh=null;collisionMesh=null;
    }
}
