using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Standalone, uncapped 1080p fixture. Same receivers, shadows and source positions for cached/baseline runs.</summary>
[DefaultExecutionOrder(1000)]
public sealed class LocalFlameBenchmark : MonoBehaviour
{
    [Serializable]public class Measurement
    {
        public string mode;public int sources,active,samples,gpuSamples,shadowDraws,staticRefreshes,dynamicRefreshes;
        public double cpuP50,cpuP95,frameP50,frameP95,gpuP50,gpuP95;public long maxManagedBytes;
    }
    [Serializable]public class Report
    {
        public string unity,gpu,graphicsApi,cpu,os;public int width,height;public long atlasBytes;
        public string fixture="256 opaque cubes; 256px radial depth faces; >=1s warmup / >=3s and 360 sample frames; fixed 1920x1080 offscreen render each frame; baseline redraws the same depth maps every frame";
        public string gpuTimingNote="GPU times are -1 when Unity supplies no timing samples. Frame times measure the player loop, not GPU-only time.";
        public List<Measurement> measurements=new();
    }
    readonly List<LocalFlameSource> sources=new();
    readonly FrameTiming[] timings=new FrameTiming[1];
    LocalFlameLighting manager;
    GameObject door;
    Camera renderCamera;
    RenderTexture renderTarget;
    CommandBuffer gpuScope;
    UnityEngine.Profiling.Recorder gpuRecorder;
    bool renderReady;
    void LateUpdate()
    {
        if(!renderReady)return;
        gpuScope.Clear();gpuScope.BeginSample("FS.Flame.BenchmarkGPU");Graphics.ExecuteCommandBuffer(gpuScope);
        manager.RenderForCamera(renderCamera);
        renderCamera.Render();
        gpuScope.Clear();gpuScope.EndSample("FS.Flame.BenchmarkGPU");Graphics.ExecuteCommandBuffer(gpuScope);
    }
    IEnumerator Start()
    {
        QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Application.runInBackground=true;
        Screen.SetResolution(1920,1080,false);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.003f,.003f,.003f);RenderSettings.fog=false;
        var camera=new GameObject("Benchmark camera").AddComponent<Camera>();camera.tag="MainCamera";
        camera.transform.SetPositionAndRotation(new Vector3(0,5,-9),Quaternion.Euler(22,0,0));camera.farClipPlane=80;
        var material=new Material(Shader.Find("Custom/BuildingWoodMatte"));material.SetFloat("_AmbientFloor",0);
        for(int i=0;i<256;i++)
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name="Fixture block";
            box.transform.position=new Vector3((i%16-7.5f)*.8f,-.25f+(i%13==0?.75f:0),(i/16-7.5f)*.8f);
            box.transform.localScale=new Vector3(.78f,i%13==0?1.5f:.5f,.78f);box.GetComponent<Renderer>().sharedMaterial=material;box.AddComponent<LocalFlameOccluder>();
        }
        door=GameObject.CreatePrimitive(PrimitiveType.Cube);door.name="Moving occluder";door.transform.localScale=new Vector3(.15f,2,2);door.transform.position=new Vector3(0,1,0);
        door.GetComponent<Renderer>().sharedMaterial=material;var occluder=door.AddComponent<LocalFlameOccluder>();occluder.dynamicOccluder=true;occluder.GeometryChanged();door.SetActive(false);
        for(int i=0;i<32;i++)
        {
            var source=new GameObject("Benchmark emitter "+i).AddComponent<LocalFlameSource>();source.localEmitter=Vector3.zero;
            source.transform.position=new Vector3((i%4-1.5f)*2,1.8f,(i/4%4-1.5f)*2);source.flicker=.08f;sources.Add(source);source.gameObject.SetActive(false);
        }
        manager=LocalFlameLighting.Instance;manager.targetCamera=camera;
        manager.automaticRendering=false;renderCamera=camera;camera.enabled=false;
        renderTarget=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGBHalf);renderTarget.Create();camera.targetTexture=renderTarget;
        gpuScope=new CommandBuffer{name="Flame benchmark GPU scope"};
        renderReady=true;yield return null;
        gpuRecorder=UnityEngine.Profiling.Recorder.Get("FS.Flame.BenchmarkGPU");gpuRecorder.enabled=true;
        var report=new Report{unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),cpu=SystemInfo.processorType,os=SystemInfo.operatingSystem,atlasBytes=LocalFlameLighting.AtlasBytes};
        foreach(int count in new[]{1,2,8,32})foreach(string mode in new[]{"off","dynamic-baseline","cached","cached-moving-occluder"})
        {
            for(int i=0;i<sources.Count;i++)sources[i].gameObject.SetActive(i<count && mode!="off");
            door.SetActive(mode=="cached-moving-occluder");manager.cacheStationary=mode!="dynamic-baseline";manager.InvalidateAll();
            var cpu=new List<double>(8192);var frame=new List<double>(8192);var gpu=new List<double>(8192);
            int draws=0,statics=0,dynamics=0;long allocation=0;ulong lastGpu=0;
            double warmEnd=Time.realtimeSinceStartupAsDouble+1,sampleEnd=double.PositiveInfinity;int sample=-1;
            while(sample<360 || Time.realtimeSinceStartupAsDouble<sampleEnd)
            {
                if(door.activeSelf)door.transform.position=new Vector3(Mathf.Sin(Time.time*1.2f)*2,1,0);
                FrameTimingManager.CaptureFrameTimings();yield return null;
                if(Time.realtimeSinceStartupAsDouble<warmEnd)continue;
                if(sample<0){sample=0;sampleEnd=Time.realtimeSinceStartupAsDouble+3;}
                sample++;
                int expected=mode=="off"?0:Math.Min(count,LocalFlameLighting.Capacity);
                if(manager.ActiveLights!=expected)
                {Debug.LogError($"Benchmark invalid: expected {expected} lights, got {manager.ActiveLights}.");Application.Quit(2);yield break;}
                cpu.Add(manager.LastCpuMs);frame.Add(Time.unscaledDeltaTime*1000);draws+=manager.ShadowDraws;statics+=manager.StaticRefreshes;dynamics+=manager.DynamicRefreshes;allocation=Math.Max(allocation,manager.LastAllocatedBytes);
                if(FrameTimingManager.GetLatestTimings(1,timings)>0 && timings[0].gpuFrameTime>0 && timings[0].frameStartTimestamp!=lastGpu)
                {gpu.Add(timings[0].gpuFrameTime);lastGpu=timings[0].frameStartTimestamp;}
                else if(gpuRecorder.gpuSampleBlockCount>0 && gpuRecorder.gpuElapsedNanoseconds>0)
                    gpu.Add(gpuRecorder.gpuElapsedNanoseconds/1000000.0);
            }
            var measurement=new Measurement{mode=mode,sources=count,active=manager.ActiveLights,samples=cpu.Count,gpuSamples=gpu.Count,
                shadowDraws=draws,staticRefreshes=statics,dynamicRefreshes=dynamics,maxManagedBytes=allocation,
                cpuP50=Percentile(cpu,.5),cpuP95=Percentile(cpu,.95),frameP50=Percentile(frame,.5),frameP95=Percentile(frame,.95),gpuP50=Percentile(gpu,.5),gpuP95=Percentile(gpu,.95)};
            report.measurements.Add(measurement);Debug.Log("FLAME BENCHMARK "+JsonUtility.ToJson(measurement));
        }
        report.width=Screen.width;report.height=Screen.height;
        string output=Path.Combine(Application.dataPath,"../../benchmark.json");
        var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]=="-flameReport")output=args[i+1];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));File.WriteAllText(output,JsonUtility.ToJson(report,true));
        Debug.Log("LOCAL FLAME BENCHMARK COMPLETE: "+Path.GetFullPath(output));Application.Quit(0);
    }
    void OnDestroy(){gpuScope?.Release();if(renderTarget!=null){renderTarget.Release();Destroy(renderTarget);}if(gpuRecorder!=null)gpuRecorder.enabled=false;}
    static double Percentile(List<double> values,double p){if(values.Count==0)return -1;values.Sort();return values[Math.Min(values.Count-1,(int)Math.Ceiling(values.Count*p)-1)];}
}
