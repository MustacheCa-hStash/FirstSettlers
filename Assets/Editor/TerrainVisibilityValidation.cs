using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>Actual runtime visibility at mountain/negative heights, including culled and reused renderers.</summary>
public static class TerrainVisibilityValidation
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool yes,string message){checks++;if(!yes)throw new InvalidOperationException(message);}
    private static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,Private).GetValue(value);
    private static void Invoke(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Private).Invoke(value,args);
    private static Mesh Plane(float width,float height)
    {
        float h=width*.5f;var mesh=new Mesh{name="Visibility fixture at "+height};
        mesh.vertices=new[]{new Vector3(-h,height,-h),new Vector3(-h,height,h),new Vector3(h,height,h),new Vector3(h,height,-h)};
        mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    private sealed class Fixture:IDisposable
    {
        internal readonly GameObject Host=new GameObject("Terrain visibility fixture");
        internal readonly Camera Camera;
        internal readonly Material Material;
        internal readonly ChunkManager Manager;
        internal readonly ChunkRuntime Runtime;
        internal readonly List<Mesh> Meshes=new List<Mesh>();
        internal readonly float Width;
        internal Fixture(float scale)
        {
            Camera=Host.AddComponent<Camera>();Camera.enabled=false;Camera.nearClipPlane=.3f;Camera.farClipPlane=6500;Camera.fieldOfView=60;Camera.aspect=1;
            Camera.cullingMask=1<<GameplayLayers.WorldSolid;Camera.clearFlags=CameraClearFlags.SolidColor;Camera.backgroundColor=Color.black;
            Material=new Material(Shader.Find("Unlit/Color"));Material.color=Color.magenta;
            var config=new WorldConfiguration();config.Generation.WorldScale=scale;config.Generation.MeshHeightMultiplier=200;config.Sculpting=default;
            config.Scene=new WorldSceneReferences{Viewer=Host.transform,ViewerCamera=Camera};
            config.Rendering=new WorldRenderingConfiguration{TerrainMaterial=Material,WaterMaterial=Material};
            Manager=new ChunkManager(config);Width=config.Generation.ChunkSize*scale;
            var record=new ChunkRecord(default);Runtime=new ChunkRuntime(record,config.Generation.ChunkSize,scale,null,Material,Material,false);
            Field<Dictionary<ChunkCoord,ChunkRuntime>>(Manager,"loadedChunks").Add(default,Runtime);
            Field<Dictionary<ChunkCoord,ChunkRecord>>(Manager,"chunkRecords").Add(default,record);
        }
        internal Mesh Mesh(float height){var mesh=Plane(Width,height);Meshes.Add(mesh);return mesh;}
        internal void Aim(float height,float pitch,float yaw=0)
        {
            Camera.transform.SetPositionAndRotation(Runtime.RootTransform.position+new Vector3(0,height+2,0),Quaternion.Euler(pitch,yaw,0));
            GeometryUtility.CalculateFrustumPlanes(Camera,Field<Plane[]>(Manager,"frustumPlanes"));
            Invoke(Manager,"UpdateChunkRenderVisibility",default(ChunkCoord),Runtime);
        }
        public void Dispose(){Manager.Dispose();foreach(var mesh in Meshes)Object.DestroyImmediate(mesh);Object.DestroyImmediate(Material);Object.DestroyImmediate(Host);}
    }
    [MenuItem("Tools/Terrain/Validate Terrain Visibility Bounds")]
    public static void Run()
    {
        checks=0;
        foreach(float scale in new[]{.3f,1f,2f})using(var f=new Fixture(scale))
        {
            Check(!f.Runtime.TryGetRenderBounds(out _),"Empty runtime retained render bounds");
            foreach(float height in new[]{-80f,10f,240f,700f,3000f})
            {
                var mesh=f.Mesh(height);f.Runtime.SetMeshes(mesh,null,0);f.Runtime.SetVisible(true);
                Check(f.Runtime.TryGetRenderBounds(out var actual)&&Mathf.Abs(actual.max.y-height)<.001f,"Terrain bounds ignored mesh height or scale");
                for(int yaw=0;yaw<8;yaw++)foreach(float pitch in new[]{-10f,0f,25f,60f,90f})
                {
                    f.Runtime.SetRenderVisible(false);f.Aim(height,pitch,yaw*45);
                    Check(f.Runtime.IsRenderVisible,"Visible terrain culled by camera pitch at y="+height+", scale="+scale);
                    Check(f.Runtime.Root.GetComponent<MeshRenderer>().enabled,"Visibility state failed to restore renderer");
                }
            }
            // A hidden renderer still supplies bounds; water and LOD replacements update those bounds immediately.
            var high=f.Mesh(950);var low=f.Mesh(-125);f.Runtime.SetMeshes(high,low,5);f.Runtime.SetRenderVisible(false);
            Check(f.Runtime.TryGetRenderBounds(out var both)&&both.min.y<=-125&&both.max.y>=950,"Water or far-LOD bounds omitted while culled");
            f.Runtime.SetMeshes(low,null,0);Check(f.Runtime.TryGetRenderBounds(out var replaced)&&Mathf.Abs(replaced.max.y+125)<.001f,"LOD swap retained stale high bounds");
            using(var sculpt=SculptSurface())
            {
                f.Runtime.SetSculptSurface(sculpt);f.Runtime.SetRenderVisible(false);
                Check(f.Runtime.TryGetRenderBounds(out var carved)&&carved.max.y>=1400,"Sculpted surface omitted from hidden chunk bounds");
            }
            Check(f.Runtime.TryGetRenderBounds(out var detached)&&detached.max.y<0,"Detached sculpt retained stale bounds");
            var pool=new GameObject("Visibility pool");
            try
            {
                f.Runtime.ReleaseToPool(pool.transform);Check(!f.Runtime.TryGetRenderBounds(out _),"Pooled runtime retained old bounds");
                f.Runtime.Reinitialize(f.Manager.GetChunkRecord(default),128,scale,null,false);
                f.Runtime.SetMeshes(high,null,0);f.Aim(950,0);Check(f.Runtime.IsRenderVisible,"Reused runtime remained culled at new height");
            }finally{Object.DestroyImmediate(pool);}
            // Reproduce the old y=0..500 box rejecting a surface directly below the camera at y=700.
            f.Runtime.SetMeshes(f.Mesh(700),null,0);f.Aim(700,0);
            var old=new Bounds(f.Runtime.RootTransform.position+Vector3.up*250,new Vector3(f.Width,500,f.Width));
            Check(!GeometryUtility.TestPlanesAABB(Field<Plane[]>(f.Manager,"frustumPlanes"),old),"Fixture did not reproduce old forward-view culling");
            Check(f.Runtime.IsRenderVisible,"Fixed mountain still culled in forward view");
        }
        Debug.Log("TERRAIN VISIBILITY PASS: "+checks+" checks; camera pitch/yaw, y=-80..3000, world scales, hidden renderers, water, LOD swaps, sculpting and pool reuse.");
    }
    public static void RunBatch()
    {try{Run();EditorApplication.Exit(0);}catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}}
    private static TerrainSculptChunkSurface SculptSurface()
    {
        var data=(TerrainSculptMeshData)Activator.CreateInstance(typeof(TerrainSculptMeshData),Private,null,new object[]{
            new[]{new double3(0,1400,0),new double3(0,1400,4),new double3(4,1400,0)},
            new[]{new float3(0,1,0),new float3(0,1,0),new float3(0,1,0)},new[]{0,1,2},0,0,null},null);
        var buildType=typeof(TerrainSculptChunkSurface).Assembly.GetType("TerrainSculptChunkBuild");
        var build=Activator.CreateInstance(buildType,true);buildType.GetField("Render",Private).SetValue(build,data);
        return (TerrainSculptChunkSurface)Activator.CreateInstance(typeof(TerrainSculptChunkSurface),Private,null,
            new object[]{default(ChunkCoord),build,TerrainDensityProfile.Default,128,null},null);
    }
    public static void RunGraphicsBatch()
    {
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        try
        {
            Run();GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            using var f=new Fixture(.3f);f.Runtime.SetMeshes(f.Mesh(700),null,0);f.Runtime.SetVisible(true);
            Directory.CreateDirectory(".utmp/terrain-visibility");
            f.Aim(700,0);f.Runtime.SetRenderVisible(false);Check(Capture(f.Camera,"forward-before")==0,"Old disabled renderer unexpectedly drew terrain");
            f.Aim(700,0);Check(Capture(f.Camera,"forward-after")>100,"Forward camera still shows a terrain hole");
            f.Aim(700,90);Check(Capture(f.Camera,"down-after")>100,"Downward camera lost terrain");
            File.WriteAllText(".utmp/terrain-visibility/validation.json","{\"passed\":true,\"checks\":"+checks+",\"renderedViews\":3,\"mountainHeight\":700}");
            Debug.Log("TERRAIN VISIBILITY GRAPHICS PASS: actual before/after forward and downward renders at y=700.");
            EditorApplication.Exit(0);
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
        finally{GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;}
    }
    private static int Capture(Camera camera,string name)
    {
        var target=new RenderTexture(128,128,24);target.Create();var previous=RenderTexture.active;var texture=new Texture2D(128,128,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,128,128),0,0);texture.Apply();
            File.WriteAllBytes(".utmp/terrain-visibility/"+name+".png",texture.EncodeToPNG());int visible=0;
            foreach(var p in texture.GetPixels32())if(p.r>100&&p.b>100&&p.g<50)visible++;
            return visible;
        }finally{camera.targetTexture=null;RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);}
    }
}
