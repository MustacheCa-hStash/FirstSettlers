using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class CloverRenderRangeValidation
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    [MenuItem("Tools/Foliage/Validate Clover Render Range")]
    public static void Run()
    {
        var previousPipeline=GraphicsSettings.defaultRenderPipeline;var previousQuality=QualitySettings.renderPipeline;
        bool async=ShaderUtil.allowAsyncCompilation;
        var obj=new GameObject("Clover offscreen correctness camera");var camera=obj.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=0;
        var warmup=RenderTexture.GetTemporary(16,16,24);
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;ShaderUtil.allowAsyncCompilation=false;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=warmup});
            ValidateRouting();
            foreach(string path in new[]{"Assets/Prefabs/CloverClump_v02_A.prefab","Assets/Prefabs/CloverClump_v02_B_Flowering.prefab",
                "Assets/Prefabs/CloverClump_v02_Edge.prefab","Assets/Prefabs/CloverClump.prefab"})
                ValidatePixels(AssetDatabase.LoadAssetAtPath<GameObject>(path),camera);
            GroundFoliageStreamingValidation.Run();ForestFloorRedesignValidation.Run();
            Debug.Log("CLOVER RANGE PASS: independent three-chunk range, player-distance/negative/diagonal continuity, prefetched batches and live range tuning; actual leaf/flowering/edge/legacy instanced pixels beyond two chunks, outer fade/zero, player height independence, property-block overrides; streaming and forest habitat regressions. No FPS benchmark.");
        }
        finally
        {
            RenderTexture.ReleaseTemporary(warmup);Object.DestroyImmediate(obj);
            GraphicsSettings.defaultRenderPipeline=previousPipeline;QualitySettings.renderPipeline=previousQuality;ShaderUtil.allowAsyncCompilation=async;
        }
    }
    static void ValidateRouting()
    {
        var clover=new CloverSettings();var grass=new GrassSettings{activeRingRadius=1};
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CloverClump_v02_A.prefab");clover.cloverClumpPrefab=prefab;
        var manager=new FoliageManager(new WorldGenerationConfiguration { Seed = 1937, ChunkSize = 128, WorldScale = .3f, MeshHeightMultiplier = 10, Water = new TerrainWaterSettings(2.4f,10,1) },
            new WorldFoliageConfiguration { Grass = grass, Flowers = new FlowerSettings{enableFlowers=false}, LilyPads = null, Cattails = null, Clover = clover, Dandelions = null, Trees = new TreeSettings() });
        try
        {
            float radius=CloverStreamingPolicy.RenderDistance(clover,128,.3f);
            Require(Mathf.Abs(radius-115.2f)<.001f && Mathf.Abs(CloverStreamingPolicy.FadeWidth(clover,128,.3f)-19.2f)<.001f,
                "Clover defaults have incorrect world distance/fade.");
            var render=typeof(FoliageManager).GetMethod("IsWithinCloverRenderRange",Private);
            var generate=typeof(FoliageManager).GetMethod("IsWithinCloverGenerationRange",Private);
            var batch=typeof(FoliageManager).GetMethod("IsFoliageBatchWorkStillWanted",Private);
            var position=typeof(FoliageManager).GetField("cloverViewerPosition",Private);
            typeof(FoliageManager).GetField("hasCloverViewerPosition",Private).SetValue(manager,true);
            bool Inside(MethodInfo method,ChunkCoord viewer,ChunkCoord chunk) => (bool)method.Invoke(manager,new object[]{viewer,chunk});
            position.SetValue(manager,new Vector3(19.2f,0,19.2f));
            Require(Inside(render,new ChunkCoord(0,0),new ChunkCoord(2,0)),"Clover remains capped to detailed grass.");
            grass.activeRingRadius=0;
            Require(Inside(render,new ChunkCoord(0,0),new ChunkCoord(2,0)),"Grass tuning still clamps clover.");
            var prefetched=new ChunkCoord(4,0);
            Require(!Inside(render,new ChunkCoord(0,0),prefetched) && Inside(generate,new ChunkCoord(0,0),prefetched),
                "Clover prewarm ring is missing or draws before the fade range.");
            using(var record=new ChunkRecord(prefetched))
            {
                var type=typeof(FoliagePublicationKind);
                Require((bool)batch.Invoke(manager,new[]{(object)record,new ChunkCoord(0,0),Enum.Parse(type,"Clover")}),
                    "Prefetched clover placements cannot build batches before becoming visible.");
            }
            var seamTarget=new ChunkCoord(2,1);
            position.SetValue(manager,new Vector3(38.399f,0,19.2f));
            bool before=Inside(render,new ChunkCoord(0,0),seamTarget);
            position.SetValue(manager,new Vector3(38.401f,0,19.2f));
            Require(before && Inside(render,new ChunkCoord(1,0),seamTarget),"Crossing a chunk seam removed a nearby diagonal clover patch.");
            Require(CloverStreamingPolicy.WithinRange(new Vector3(-.001f,500,-.001f),new ChunkCoord(1,1),128,.3f,radius),
                "Negative coordinates/player altitude changed horizontal range.");
            var refresh=typeof(FoliageManager).GetMethod("RefreshCloverRangeWork",Private);
            var work=(IList)typeof(FoliageManager).GetField("pendingFoliageManagementWork",Private).GetValue(manager);
            var coords=new List<ChunkCoord>{new ChunkCoord(0,0),new ChunkCoord(5,0)};
            clover.activeRingRadius=4;refresh.Invoke(manager,new object[]{coords});
            Require(work.Count==2,"Inspector range change did not schedule stationary-viewer refresh.");
            refresh.Invoke(manager,new object[]{coords});Require(work.Count==2,"Stable range requeues management every frame.");
            clover.enableClover=false;
            Require(!Inside(render,new ChunkCoord(0,0),new ChunkCoord(0,0)),"Disabled clover still renders.");
        }
        finally{manager.Dispose();}
    }
    static void ValidatePixels(GameObject prefab,Camera camera)
    {
        Require(prefab!=null,"Missing clover validation prefab.");
        var filter=prefab.GetComponentInChildren<MeshFilter>();var material=filter.GetComponent<MeshRenderer>().sharedMaterial;
        var runtime=new ChunkFoliageRuntime{cloverRenderData=new[]{new CloverRenderData(filter.sharedMesh,material)},
            cloverInstanceDataPropertyId=Shader.PropertyToID("_CloverInstanceData"),isVisible=true};
        runtime.CacheCloverBatches(new[]{new List<Matrix4x4>{Matrix4x4.identity}},new[]{new List<Vector4>{new Vector4(.37f,.5f,0,0)}});
        var properties=(MaterialPropertyBlock)typeof(ChunkFoliageRuntime).GetField("cloverPropertyBlock",Private).GetValue(runtime);
        var command=new CommandBuffer();var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(256,256,TextureFormat.RGBA32,false);RenderTexture previous=RenderTexture.active;
        camera.transform.position=new Vector3(0,1.2f,-2.4f);camera.transform.LookAt(new Vector3(0,.12f,0));camera.aspect=1;camera.fieldOfView=45;
        float materialFade=material.GetFloat("_FadeEndDistance");
        try
        {
            target.Create();for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);
            Color32[] Render(float distance,float height=0)
            {
                runtime.DrawClover(new Vector3(0,height,-distance),115.2f,19.2f);
                Require(Mathf.Abs(properties.GetFloat("_FadeEndDistance")-115.2f)<.001f &&
                    Mathf.Abs(properties.GetFloat("_FadeStartDistance")-96)<.001f,"Runtime clover draw retained the short material fade.");
                command.Clear();command.SetRenderTarget(target);command.ClearRenderTarget(true,true,Color.black);
                var vp=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;
                command.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
                command.SetGlobalMatrix("unity_MatrixVP",vp);command.SetGlobalVector("_WorldSpaceCameraPos",camera.transform.position);
                command.SetGlobalVector("_ScreenParams",new Vector4(256,256,1f+1f/256,1f+1f/256));
                command.SetGlobalVector("_MainLightPosition",new Vector4(0,1,0,0));command.SetGlobalVector("_MainLightColor",Vector4.one);
                command.DrawMeshInstanced(filter.sharedMesh,0,material,0,new[]{Matrix4x4.identity},1,properties);
                Graphics.ExecuteCommandBuffer(command);RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();return pixels.GetPixels32();
            }
            int Coverage(Color32[] values)=>values.Count(p=>p.r+p.g+p.b>15);
            var full=Render(85);var high=Render(85,500);int count=Coverage(full),fading=Coverage(Render(105));
            Require(count>50,"Clover cannot render beyond two chunk widths: "+prefab.name);
            Require(full.SequenceEqual(high),"Clover render range incorrectly depends on player altitude.");
            int beyond=Coverage(Render(130));
            Require(fading>0 && fading<count*.85f && beyond==0,"Clover outer fade/zero coverage failed: "+prefab.name+
                " shader "+material.shader.name+" full "+count+" fading "+fading+" beyond "+beyond);
            Require(material.GetFloat("_FadeEndDistance")==materialFade,"Runtime range mutated the shared clover material.");
            foreach(var message in ShaderUtil.GetShaderMessages(material.shader))
                Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
        }
        finally
        {
            RenderTexture.active=previous;command.Dispose();target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);
        }
    }
    public static void RunBatch()
    {
        try{Run();EditorApplication.Exit(0);}catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
}
