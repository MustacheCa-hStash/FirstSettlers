using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug=UnityEngine.Debug;

/// <summary>Identical 1080p hall/tunnel fixtures for native URP, spatial direct and cached surface lighting.</summary>
[DefaultExecutionOrder(1100)]
public sealed class LocalFlameComparisonBenchmark : MonoBehaviour
{
    [Serializable] public class Measurement
    {
        public string scene,mode;public int sources,active,samples,gpuSamples,readyPages,residentPages;
        public double lightingCpuP95,renderSubmitCpuP95,frameP95,gpuP95,cacheHitFraction;
        public int shadowRefreshes,surfaceUpdates,dynamicRefreshes;public long managedBytes;
    }
    [Serializable] public class Report
    {
        public string unity,cpu,gpu,api;public int width=1920,height=1080;public long shadowBytes=LocalFlameLighting.AtlasBytes,surfaceBytes=LocalFlameSurfaceCache.Bytes;
        public string method="24x48m hall and 48m carved tunnel; 8/32/64 flames and models; >=1.5s warmup until cache ready; >=3s and 180 samples; manual offscreen 1080p render. Native URP uses 256px requested shadows, 4096 atlas, intensity 12 vs custom 5 (different attenuation). Eight-light reference uses the new attenuation, not the historical implementation.";
        public string gpuTiming="gpuP95=-1 means Unity supplied no valid GPU timing samples. Frame and CPU submission times are not GPU-only times.";
        public List<Measurement> measurements=new();
        public List<Recovery> recoveries=new();
    }
    [Serializable] public class Recovery {public string scene,edit;public int sources,frames;public double milliseconds,maxLightingCpuMs;}
    readonly List<LocalFlameSource> sources=new();
    readonly List<Light> native=new();
    readonly FrameTiming[] timings=new FrameTiming[1];
    GameObject fixture,door;
    Camera view;
    RenderTexture target;
    LocalFlameLighting manager;
    Material receiver;
    bool rendering;
    double submitMs;
    string mode;
    string output;
    void LateUpdate()
    {
        if(!rendering)return;
        if(mode=="surface-moving-door")door.transform.position=new Vector3((fixture.name.Contains("hall")?12:24)+Mathf.Sin(Time.time*1.3f)*3,2,12);
        for(int i=0;i<native.Count;i++)if(native[i].enabled)native[i].intensity=12*sources[i].FlameStrength(Time.time);
        long start=Stopwatch.GetTimestamp();manager.RenderForCamera(view);view.Render();submitMs=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
    }
    IEnumerator Start()
    {
        QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Application.runInBackground=true;
        Screen.SetResolution(1920,1080,false);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;RenderSettings.fog=false;
        view=new GameObject("Comparison view").AddComponent<Camera>();view.tag="MainCamera";view.enabled=false;view.nearClipPlane=.05f;view.farClipPlane=180;view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=Color.black;
        target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGBHalf);target.Create();view.targetTexture=target;
        receiver=new Material(Shader.Find("Custom/BuildingWoodMatte"));receiver.SetFloat("_AmbientFloor",0);receiver.SetFloat("_ShadowTintStrength",0);receiver.SetFloat("_DirectLightStrength",1);receiver.SetFloat("_LightWrap",0);receiver.SetColor("_BaseColor",new Color(.35f,.32f,.27f));
        manager=LocalFlameLighting.Ensure();manager.targetCamera=view;manager.automaticRendering=false;
        output=Path.GetFullPath(".utmp/local-flame/comparison.json");var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]=="-flameReport")output=Path.GetFullPath(args[i+1]);Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report=new Report{unity=Application.unityVersion,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};
        foreach(string scene in new[]{"hall","cave"})
        {
            Build(scene);rendering=true;yield return null;
            foreach(int count in new[]{8,32,64})foreach(string currentMode in new[]{"off","eight-light-reference","spatial-direct","surface-forced","surface-cache","surface-moving-door","native-urp"})
            {
                mode=currentMode;bool urp=mode=="native-urp",off=mode=="off";
                manager.maxActiveLights=mode=="eight-light-reference"?8:64;manager.useSurfaceCache=mode.StartsWith("surface");manager.cacheStationary=true;
                manager.adaptiveSurfaceCache=mode!="surface-forced";
                door.SetActive(mode=="surface-moving-door");
                for(int i=0;i<sources.Count;i++)
                {
                    sources[i].gameObject.SetActive(i<count&&!off);sources[i].enabled=!urp;
                    sources[i].FlameVisible=urp;native[i].enabled=urp&&i<count&&!off;
                }
                manager.InvalidateAll();double warmEnd=Time.realtimeSinceStartupAsDouble+1.5;
                while(Time.realtimeSinceStartupAsDouble<warmEnd || manager.PendingRefreshes>0 || manager.useSurfaceCache&&manager.SurfaceCache!=null&&manager.SurfaceCache.ReadyPages<manager.SurfaceCache.ResidentPages)yield return null;
                if(mode=="surface-cache")
                {
                    Shader.SetGlobalInt("_FSSurfaceDebug",1);view.Render();report.measurements.Add(new Measurement{scene=scene,mode="coverage-probe",sources=count,cacheHitFraction=CacheHitFraction()});Shader.SetGlobalInt("_FSSurfaceDebug",0);
                }
                SaveImage(scene+"-"+count+"-"+mode);
                var cpu=new List<double>(8192);var submits=new List<double>(8192);var frames=new List<double>(8192);var gpu=new List<double>(8192);
                int shadows=0,surfaceUpdates=0,dynamics=0;long allocation=0;ulong gpuStamp=0;double end=Time.realtimeSinceStartupAsDouble+3;
                while(cpu.Count<180 || Time.realtimeSinceStartupAsDouble<end)
                {
                    FrameTimingManager.CaptureFrameTimings();yield return null;
                    int expected=urp||off?0:Math.Min(count,manager.maxActiveLights);
                    if(manager.ActiveLights!=expected){Debug.LogError($"COMPARISON INVALID {scene}/{mode}: expected {expected}, got {manager.ActiveLights}");Application.Quit(2);yield break;}
                    cpu.Add(manager.LastCpuMs);submits.Add(submitMs);frames.Add(Time.unscaledDeltaTime*1000);
                    shadows+=manager.StaticRefreshes;surfaceUpdates+=manager.useSurfaceCache?manager.SurfaceCache.UpdatedPages:0;dynamics+=manager.DynamicRefreshes;allocation=Math.Max(allocation,manager.LastAllocatedBytes);
                    if(FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].gpuFrameTime>0&&timings[0].frameStartTimestamp!=gpuStamp){gpu.Add(timings[0].gpuFrameTime);gpuStamp=timings[0].frameStartTimestamp;}
                }
                var m=new Measurement{scene=scene,mode=mode,sources=count,active=manager.ActiveLights,samples=cpu.Count,gpuSamples=gpu.Count,lightingCpuP95=P95(cpu),renderSubmitCpuP95=P95(submits),frameP95=P95(frames),gpuP95=P95(gpu),shadowRefreshes=shadows,surfaceUpdates=surfaceUpdates,dynamicRefreshes=dynamics,managedBytes=allocation,readyPages=manager.SurfaceCache?.ReadyPages??0,residentPages=manager.SurfaceCache?.ResidentPages??0};
                report.measurements.Add(m);Debug.Log("FLAME COMPARISON "+JsonUtility.ToJson(m));
                if(mode=="surface-cache")
                {
                    var blocker=Box("Construction change",new Vector3(scene=="hall"?2:21,2,12),new Vector3(.15f,4,8));
                    yield return Recover(report,scene,count,"wall-added");Destroy(blocker);yield return null;yield return Recover(report,scene,count,"wall-removed");
                    sources[0].gameObject.SetActive(false);yield return Recover(report,scene,count,"torch-removed");sources[0].gameObject.SetActive(true);yield return Recover(report,scene,count,"torch-restored");
                }
                File.WriteAllText(output,JsonUtility.ToJson(report,true));
            }
            rendering=false;Destroy(fixture);sources.Clear();native.Clear();yield return null;
        }
        Debug.Log("LOCAL FLAME COMPARISON COMPLETE: "+output);Application.Quit(0);
    }
    IEnumerator Recover(Report report,string scene,int count,string edit)
    {
        int frames=0;double start=Time.realtimeSinceStartupAsDouble,maxCpu=0;
        do {yield return null;frames++;maxCpu=Math.Max(maxCpu,manager.LastCpuMs);}
        while(frames<2 || manager.PendingRefreshes>0 || manager.SurfaceCache.ReadyPages<manager.SurfaceCache.ResidentPages);
        report.recoveries.Add(new Recovery{scene=scene,sources=count,edit=edit,frames=frames,milliseconds=(Time.realtimeSinceStartupAsDouble-start)*1000,maxLightingCpuMs=maxCpu});
    }
    GameObject Box(string name,Vector3 pos,Vector3 size)
    {var b=GameObject.CreatePrimitive(PrimitiveType.Cube);b.name=name;b.transform.SetParent(fixture.transform,false);b.transform.position=pos;b.transform.localScale=size;b.GetComponent<Renderer>().sharedMaterial=receiver;b.AddComponent<LocalFlameOccluder>();return b;}
    void Build(string scene)
    {
        fixture=new GameObject("Comparison "+scene);
        Box("Floor",new Vector3(scene=="hall"?12:24,-.2f,24),new Vector3(scene=="hall"?24:8,.4f,48));
        if(scene=="hall")
        {
            Box("Left",new Vector3(-.2f,3,24),new Vector3(.4f,6,48));Box("Right",new Vector3(24.2f,3,24),new Vector3(.4f,6,48));Box("Back",new Vector3(12,3,48.2f),new Vector3(24,6,.4f));Box("Roof",new Vector3(12,6.2f,24),new Vector3(24,.4f,48));
            for(int row=0;row<16;row++){float z=2+row*2.9f;Box("Post",new Vector3(8,3,z),new Vector3(.4f,6,.4f));Box("Post",new Vector3(16,3,z),new Vector3(.4f,6,.4f));}
            view.transform.SetPositionAndRotation(new Vector3(12,2.7f,2),Quaternion.LookRotation(new Vector3(0,-.03f,1)));
        }
        else
        {
            var tunnel=new GameObject("Excavated cave receiver");tunnel.transform.SetParent(fixture.transform,false);tunnel.AddComponent<MeshFilter>().sharedMesh=TunnelMesh();tunnel.AddComponent<MeshRenderer>().sharedMaterial=receiver;tunnel.AddComponent<LocalFlameOccluder>();
            view.transform.SetPositionAndRotation(new Vector3(24,2.2f,2),Quaternion.LookRotation(new Vector3(0,0,1)));
        }
        door=Box("Moving door",new Vector3(24,2,12),new Vector3(3,4,.15f));var occluder=door.GetComponent<LocalFlameOccluder>();occluder.dynamicOccluder=true;occluder.GeometryChanged();door.SetActive(false);
        var definition=Resources.Load<BuildCatalog>("Building/PrototypeCatalog").Find("build.lighting.wall-torch");
        for(int i=0;i<64;i++)
        {
            int column=i%4;int side=scene=="hall"?(column<2?0:1):i%2;float z=scene=="hall"?2+(i/4)*2.9f:2+(i/2)*1.42f;float x=column==0?.65f:column==1?8.65f:column==2?15.35f:23.35f;Vector3 emitter=new Vector3(scene=="hall"?x:(side==0?20.7f:27.3f),2.3f,z);
            var effect=Instantiate(Resources.Load<GameObject>("Lighting/MountedTorchFlame"),fixture.transform);effect.name="Comparison torch "+i;
            var source=effect.GetComponent<LocalFlameSource>();effect.transform.rotation=Quaternion.Euler(0,side==0?90:-90,0);effect.transform.position=emitter-effect.transform.rotation*source.localEmitter;sources.Add(source);source.flicker=.08f;
            var body=new GameObject("Torch solid");body.transform.SetParent(effect.transform,false);body.AddComponent<MeshFilter>().sharedMesh=definition.mesh;body.AddComponent<MeshRenderer>().sharedMaterial=definition.material;body.AddComponent<LocalFlameOccluder>();
            var light=new GameObject("Native reference light").AddComponent<Light>();light.transform.SetParent(effect.transform,false);light.transform.localPosition=source.localEmitter;light.type=LightType.Point;light.range=12;light.intensity=12;light.color=source.color;light.shadows=LightShadows.Soft;light.shadowBias=.01f;light.shadowNormalBias=.1f;light.GetUniversalAdditionalLightData();light.enabled=false;native.Add(light);
            effect.SetActive(false);
        }
    }
    static Mesh TunnelMesh()
    {
        const int sides=32,rings=25;var vertices=new Vector3[sides*rings];var uv=new Vector2[vertices.Length];var triangles=new int[(rings-1)*sides*6];
        for(int r=0;r<rings;r++)for(int s=0;s<sides;s++){float a=s*Mathf.PI*2/sides,radius=4+.12f*Mathf.Sin(s*1.7f+r*.8f);vertices[r*sides+s]=new Vector3(24+Mathf.Cos(a)*radius,3+Mathf.Sin(a)*radius,r*2);uv[r*sides+s]=new Vector2((float)s/sides,(float)r/rings);}
        int t=0;for(int r=0;r<rings-1;r++)for(int s=0;s<sides;s++){int a=r*sides+s,b=r*sides+(s+1)%sides,c=a+sides,d=b+sides;triangles[t++]=a;triangles[t++]=c;triangles[t++]=b;triangles[t++]=b;triangles[t++]=c;triangles[t++]=d;}
        var mesh=new Mesh{name="Carved tunnel fixture"};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    double CacheHitFraction()
    {
        var image=ReadTarget();var pixels=image.GetPixels();int hits=0,misses=0;foreach(var p in pixels){if(p.g>p.r*2&&p.g>.01f)hits++;else if(p.r>p.g*2&&p.r>.01f)misses++;}Destroy(image);return hits/(double)Math.Max(1,hits+misses);
    }
    Texture2D ReadTarget(){RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=null;return image;}
    void SaveImage(string name)
    {
        view.Render();var image=ReadTarget();var pixels=image.GetPixels();
        for(int i=0;i<pixels.Length;i++)pixels[i]=pixels[i].gamma;
        image.SetPixels(pixels);image.Apply();File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),name+".png"),image.EncodeToPNG());Destroy(image);
    }
    static double P95(List<double> values){if(values.Count==0)return -1;values.Sort();return values[Math.Min(values.Count-1,(int)Math.Ceiling(values.Count*.95)-1)];}
    void OnDestroy(){if(target!=null){target.Release();Destroy(target);}if(receiver!=null)Destroy(receiver);}
}
