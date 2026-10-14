using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

public static class DaisyWeedPerformance
{
    public static void RunBatch()
    {
        try{Start();}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static void Start()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Directory.CreateDirectory(".utmp/daisy");
        var root=new GameObject("Daisy performance fixture");var camera=root.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=50;camera.nearClipPlane=.03f;camera.farClipPlane=60;
        camera.transform.position=new Vector3(6,2.5f,-7);camera.transform.LookAt(new Vector3(6,.45f,5));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.2f,.29f,.33f);
        var sun=root.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.shadows=LightShadows.Soft;
        // Camera and sun need independent transforms.
        Object.DestroyImmediate(sun);var sunObject=new GameObject("Daisy benchmark sun");sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.shadows=LightShadows.Soft;sunObject.transform.rotation=Quaternion.Euler(48,-32,0);
        var legacy=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Grass+Flowers/daisyWeeds.fbx");var oldMesh=legacy.GetComponentInChildren<MeshFilter>().sharedMesh;
        var oldMat=new Material(Shader.Find("Custom/DaisyWeedsColor"));oldMat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Flowers/daisyWeeds.png"));
        oldMat.SetColor("_FlowerColor",new Color(.52f,.24f,.78f));oldMat.SetColor("_StemColor",new Color(.18f,.48f,.15f));oldMat.SetFloat("_AmbientStrength",.679f);oldMat.SetFloat("_Cutoff",.25f);oldMat.SetFloat("_RedThreshold",.115f);oldMat.SetFloat("_MaskSoftness",.186f);oldMat.enableInstancing=true;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(DaisyWeedAuthoring.PrefabPath);var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;var mat=prefab.GetComponent<MeshRenderer>().sharedMaterial;
        var runtime=new ChunkFoliageRuntime{root=root.transform,daisyWeedMesh=mesh,daisyWeedMaterial=mat,flowerPetalColorPropertyId=Shader.PropertyToID("_FlowerPetalColor")};runtime.SetVisible(true);
        var matrices=new Matrix4x4[1024];var colors=new Vector4[1024];
        for(int i=0;i<1024;i++){matrices[i]=Matrix4x4.TRS(new Vector3(i%32*.4f,0,i/32*.4f),Quaternion.Euler(0,i*137.5f,0),Vector3.one*.575f);colors[i]=new Vector4(.94f,.91f,1,1);}
        runtime.CacheFlowerBatches(matrices,colors,false,true);
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;bool oldAsync=ShaderUtil.allowAsyncCompilation;
        var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;ShaderUtil.allowAsyncCompilation=false;
        var target=new RenderTexture(1280,720,24);target.Create();int[] modes={0,1,2,2,1,0};int stage=0,frame=0;var times=new List<double>();long submitTicks=0;
        var report=new System.Text.StringBuilder("1024 daisy clumps; same positions, scale .575, camera, shadow lighting and 1280x720 target. One request per actual editor frame; 24 warm-up and 80 measured frames per stage. CPU wall timings, not live-game FPS.\n");
        Action<ScriptableRenderContext,Camera> submit=(_,c)=>
        {
            if(c!=camera || modes[stage]==0)return;long start=Stopwatch.GetTimestamp();
            runtime.daisyWeedMesh=modes[stage]==1?oldMesh:mesh;runtime.daisyWeedMaterial=modes[stage]==1?oldMat:mat;runtime.DrawFlowers();submitTicks+=Stopwatch.GetTimestamp()-start;
        };
        EditorApplication.CallbackFunction tick=null;
        void Cleanup(){EditorApplication.update-=tick;RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(root);Object.DestroyImmediate(sunObject);Object.DestroyImmediate(oldMat);}
        tick=()=>
        {
            try
            {
                var timer=Stopwatch.StartNew();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                if(frame++>=24)times.Add(timer.Elapsed.TotalMilliseconds);else submitTicks=0;
                if(frame<104)return;
                times.Sort();string mode=modes[stage]==0?"none":modes[stage]==1?"legacy cards":"painted 3D clumps";
                string line=$"{mode}: median {times[40]:F3} ms, p95 {times[76]:F3} ms, flower submission {submitTicks*1000.0/Stopwatch.Frequency/80:F3} ms; per-clump triangles {(modes[stage]==1?oldMesh.GetIndexCount(0):mesh.GetIndexCount(0))/3}.";
                report.AppendLine(line);Debug.Log("DAISY_BENCHMARK_STAGE: "+line);
                if(++stage<modes.Length){frame=0;times.Clear();submitTicks=0;return;}
                if(ShaderUtil.ShaderHasError(mat.shader))throw new Exception("Daisy benchmark shader failed");
                File.WriteAllText(".utmp/daisy/performance.txt",report.ToString());Debug.Log("DAISY_BENCHMARK_PASS\n"+report);Cleanup();EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);Cleanup();EditorApplication.Exit(1);}
        };
        RenderPipelineManager.beginCameraRendering+=submit;EditorApplication.update+=tick;
    }
}
