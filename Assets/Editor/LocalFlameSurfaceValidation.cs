using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Pixel evidence for actual cache hits, direct/cache agreement, containment and edit recovery.</summary>
public static class LocalFlameSurfaceValidation
{
    static int checks;
    static LocalFlameLighting manager;
    static Camera camera;
    static readonly string Output=".utmp/local-flame";
    static void Check(bool condition,string message){checks++;if(!condition)throw new InvalidOperationException(message);}
    static Texture2D Capture(string name)
    {
        manager.RenderForCamera(camera,1);
        var rt=RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);camera.targetTexture=rt;
        for(int i=0;i<4;i++)camera.Render();
        RenderTexture.active=rt;var image=new Texture2D(256,256,TextureFormat.RGBAFloat,false,true);image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
        var preview=new Texture2D(256,256,TextureFormat.RGBA32,false,true);preview.SetPixels(image.GetPixels());preview.Apply();
        Directory.CreateDirectory(Output);File.WriteAllBytes(Output+"/surface-"+name+".png",preview.EncodeToPNG());Object.DestroyImmediate(preview);
        camera.targetTexture=null;RenderTexture.active=null;RenderTexture.ReleaseTemporary(rt);return image;
    }
    static double Mean(Texture2D image){double sum=0;var pixels=image.GetPixels();for(int y=80;y<176;y++)for(int x=80;x<176;x++){var c=pixels[y*256+x];sum+=(c.r+c.g+c.b)/3;}return sum/(96*96);}
    static double Difference(Texture2D a,Texture2D b){double sum=0;var aa=a.GetPixels();var bb=b.GetPixels();for(int y=80;y<176;y++)for(int x=80;x<176;x++){int i=y*256+x;sum+=Math.Abs(aa[i].r-bb[i].r)+Math.Abs(aa[i].g-bb[i].g)+Math.Abs(aa[i].b-bb[i].b);}return sum/(96*96*3);}
    static void Settle(int frames=100){for(int i=0;i<frames;i++)manager.RenderForCamera(camera,1);}
    static void Remove(GameObject box){LocalFlameGeometry.Remove(box.GetComponent<LocalFlameOccluder>());Object.DestroyImmediate(box);}
    public static void RunBatch()
    {try{Run();Debug.Log($"LOCAL FLAME SURFACE PASS: {checks} checks");EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);LocalFlameGeometry.Reset();LocalFlameValidation.Configure();
        camera=new GameObject("Surface validation camera").AddComponent<Camera>();camera.tag="MainCamera";camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.7f;camera.farClipPlane=150;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        // Off cell/texel boundaries; all normal axes are exercised below.
        Vector3 origin=new Vector3(1,1,1);camera.transform.SetPositionAndRotation(origin+Vector3.up*.8f,Quaternion.Euler(90,0,0));
        var material=LocalFlameValidation.WhiteWood();var floor=LocalFlameValidation.Box("Surface receiver",origin-Vector3.up*.125f,new Vector3(6,.25f,6),material);
        var source=new GameObject("Stationary cached source").AddComponent<LocalFlameSource>();source.localEmitter=Vector3.zero;source.transform.position=origin+Vector3.up*2;source.flicker=0;
        manager=LocalFlameLighting.Ensure();manager.Register(source);manager.targetCamera=camera;manager.automaticRendering=false;manager.useSurfaceCache=true;manager.adaptiveSurfaceCache=false;
        Settle();Check(manager.SurfaceCache.ReadyPages>0,"No surface pages became ready.");
        Shader.SetGlobalInt("_FSSurfaceDebug",1);var hits=Capture("hits");var center=hits.GetPixel(128,128);Check(center.g>.8f&&center.r<.1f,"Receiver did not actually sample cached surface illumination.");Shader.SetGlobalInt("_FSSurfaceDebug",0);
        manager.adaptiveSurfaceCache=true;Shader.SetGlobalInt("_FSSurfaceDebug",1);center=Capture("adaptive-single-source").GetPixel(128,128);Check(center.r>.8f&&center.g<.1f,"Adaptive lighting did not bypass cache overhead for one source.");Shader.SetGlobalInt("_FSSurfaceDebug",0);manager.adaptiveSurfaceCache=false;
        var cached=Capture("cached");manager.useSurfaceCache=false;var direct=Capture("direct");double error=Difference(cached,direct);Debug.Log($"SURFACE CACHE ERROR {error:F6}; cached {Mean(cached):F6}; direct {Mean(direct):F6}");Check(error<.025,"Surface cache differs from direct illumination.");manager.useSurfaceCache=true;
        Check(manager.SurfaceCache.Irradiance.mipmapCount==4,"Surface illumination did not allocate its distance levels.");
        var mipRead=UnityEngine.Rendering.AsyncGPUReadback.Request(manager.SurfaceCache.NormalDepth,1,TextureFormat.RGBAFloat);mipRead.WaitForCompletion();
        Check(mipRead.GetData<Color>(2)[2*16+2].a>.5f,"Coarse surface normal/depth data was not generated.");
        camera.orthographicSize=64;Shader.SetGlobalInt("_FSSurfaceDebug",2);center=Capture("coarse-mip").GetPixel(128,128);Debug.Log($"SURFACE COARSE PROBE {center}");Check(center.g>.8f&&center.r>.3f,"Distant surface footprint did not select coarser cached lighting.");Shader.SetGlobalInt("_FSSurfaceDebug",0);camera.orthographicSize=.7f;
        var dynamicReceiver=new MaterialPropertyBlock();dynamicReceiver.SetFloat("_FSFlameDynamicReceiver",1);floor.GetComponent<Renderer>().SetPropertyBlock(dynamicReceiver);
        Shader.SetGlobalInt("_FSSurfaceDebug",1);center=Capture("dynamic-receiver").GetPixel(128,128);Check(center.r>.8f&&center.g<.1f,"Dynamic receiver borrowed static surface illumination.");Shader.SetGlobalInt("_FSSurfaceDebug",0);floor.GetComponent<Renderer>().SetPropertyBlock(null);
        int updates=manager.SurfaceCache.TotalUpdates,refreshes=manager.TotalStaticRefreshes;Settle(8);
        Check(manager.SurfaceCache.TotalUpdates==updates&&manager.TotalStaticRefreshes==refreshes,"Unchanged lighting rebuilt surface or shadow data.");
        camera.transform.position+=Vector3.up*60;var distant=Capture("distant");Check(Difference(cached,distant)<.01,"Distant observation extinguished cached illumination.");Check(manager.SurfaceCache.TotalUpdates==updates,"Camera distance rebuilt static illumination.");camera.transform.position-=Vector3.up*60;
        var blocker=LocalFlameValidation.Box("New thin roof",origin+Vector3.up,new Vector3(7,.03f,7),material);
        var blocked=Capture("new-wall-immediate");Check(Mean(blocked)<Mean(cached)*.05,"A new wall left stale cached light behind it.");Settle();Check(Mean(Capture("new-wall-settled"))<Mean(cached)*.05,"Cached light leaked through a 3 cm wall.");Remove(blocker);Settle();Check(Mean(Capture("wall-removed"))>Mean(cached)*.95,"Removing a wall did not restore cached illumination.");
        var door=LocalFlameValidation.Box("Dynamic door",origin+Vector3.up,new Vector3(7,.1f,7),material,true);updates=manager.SurfaceCache.TotalUpdates;refreshes=manager.TotalStaticRefreshes;
        Check(Mean(Capture("moving-door-blocked"))<Mean(cached)*.05,"Dynamic door failed to shadow combined cached illumination.");
        door.transform.position+=Vector3.right*20;door.GetComponent<LocalFlameOccluder>().GeometryChanged();Check(Mean(Capture("moving-door-open"))>Mean(cached)*.95,"Opening a dynamic door left stale cached shadows.");Check(manager.SurfaceCache.TotalUpdates==updates&&manager.TotalStaticRefreshes==refreshes,"Moving door rebuilt static surface/shadow data.");Remove(door);
        source.intensity*=.5f;Settle();Check(Math.Abs(Mean(Capture("dimmed"))/Mean(cached)-.5)<.03,"Intensity change did not update cached illumination.");source.intensity*=2;
        source.burning=false;Settle();Check(Mean(Capture("extinguished"))<.001,"Extinguished source retained cached light.");source.burning=true;Settle();
        var coverageWorld=new GameObject("Render-only terrain coverage").AddComponent<BuildWorld>();var coverage=new RenderGuard();coverageWorld.TerrainGuard=coverage;source.coverageWorld=coverageWorld;
        Settle();Check(manager.ActiveLights==1,"Collision readiness incorrectly suppressed published lighting geometry.");
        coverage.Ready=false;Settle();Check(manager.CoverageSuppressed==1&&Mean(Capture("coverage-unready"))<.001,"Unavailable render geometry retained cached illumination.");
        coverage.Ready=true;Settle();Check(manager.ActiveLights==1,"Published render coverage did not restore illumination.");source.coverageWorld=null;Object.DestroyImmediate(coverageWorld.gameObject);
        var thin=LocalFlameValidation.Box("Parallel surface 3 cm away",origin+Vector3.up*.04f,new Vector3(7,.02f,7),material);
        camera.nearClipPlane=.001f;camera.transform.position=origin+Vector3.up*.02f;Settle();
        Shader.SetGlobalInt("_FSSurfaceDebug",1);center=Capture("parallel-surface-rejected").GetPixel(128,128);Check(center.r>.8f&&center.g<.1f,"Cache borrowed illumination from a nearby parallel surface.");Shader.SetGlobalInt("_FSSurfaceDebug",0);
        Check(Mean(Capture("parallel-surface-dark"))<.001,"Parallel-surface fallback leaked through a thin ceiling.");Remove(thin);camera.nearClipPlane=.02f;camera.transform.position=origin+Vector3.up*.8f;
        foreach(var normal in new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back})
        {
            floor.transform.SetPositionAndRotation(origin-normal*.125f,Quaternion.FromToRotation(Vector3.up,normal));floor.GetComponent<LocalFlameOccluder>().GeometryChanged();
            source.transform.position=origin+normal*2;camera.transform.SetPositionAndRotation(origin+normal*.8f,Quaternion.LookRotation(-normal,Mathf.Abs(normal.y)>.9f?Vector3.forward:Vector3.up));Settle();
            Shader.SetGlobalInt("_FSSurfaceDebug",1);center=Capture("axis-hit-"+checks).GetPixel(128,128);Check(center.g>.8f&&center.r<.1f,"Cache lookup failed on normal "+normal);Shader.SetGlobalInt("_FSSurfaceDebug",0);
            cached=Capture("axis-cache-"+checks);manager.useSurfaceCache=false;direct=Capture("axis-direct-"+checks);Check(Difference(cached,direct)<.025,"Cache projection mismatch on normal "+normal);manager.useSurfaceCache=true;
        }
        floor.transform.SetPositionAndRotation(origin-Vector3.up*.125f,Quaternion.identity);floor.GetComponent<LocalFlameOccluder>().GeometryChanged();source.transform.position=origin+Vector3.up*2;camera.transform.SetPositionAndRotation(origin+Vector3.up*.8f,Quaternion.Euler(90,0,0));
        floor.transform.localScale=new Vector3(20,.25f,20);floor.GetComponent<LocalFlameOccluder>().GeometryChanged();source.transform.position=origin+Vector3.up*.3f;camera.transform.position=origin+new Vector3(5,.8f,5);manager.useSurfaceCache=false;Settle();
        var grazing=Capture("grazing-plane");double grazingError=0,grazingExpected=0;
        for(int y=80;y<176;y++)for(int x=80;x<176;x++)
        {
            Vector3 p=camera.transform.position+camera.transform.right*((x+.5f)/256-.5f)*1.4f+camera.transform.up*((y+.5f)/256-.5f)*1.4f;
            p.y=origin.y;Vector3 delta=source.Position-p;float d2=delta.sqrMagnitude,ratio=d2/(source.range*source.range),edge=Mathf.Clamp01(1-ratio*ratio);
            double expected=source.color.linear.r*source.intensity*Mathf.Max(0,delta.normalized.y)*edge*edge/(1+d2*.2f);
            grazingExpected+=expected;grazingError+=Math.Abs(grazing.GetPixel(x,y).r-expected);
        }
        Debug.Log($"SURFACE GRAZING RELATIVE ERROR {grazingError/grazingExpected:F6}");Check(grazingError/grazingExpected<.03,"Grazing receiver has self-shadow bands instead of smooth analytic illumination.");
        floor.transform.localScale=new Vector3(6,.25f,6);floor.GetComponent<LocalFlameOccluder>().GeometryChanged();source.transform.position=origin+Vector3.up*2;camera.transform.position=origin+Vector3.up*.8f;manager.useSurfaceCache=true;
        for(int i=1;i<64;i++){var s=new GameObject("Hall source "+i).AddComponent<LocalFlameSource>();s.localEmitter=Vector3.zero;s.transform.position=origin+new Vector3((i%8-3.5f)*.15f,2,(i/8-3.5f)*.15f);s.intensity=.025f;s.flicker=0;manager.Register(s);}
        Settle(150);Check(manager.RegisteredSources==64&&manager.ActiveLights==64,"Hall sources were extinguished by the previous eight-light budget.");
        manager.adaptiveSurfaceCache=true;Shader.SetGlobalInt("_FSSurfaceDebug",1);center=Capture("adaptive-many-sources").GetPixel(128,128);Check(center.g>.8f&&center.r<.1f,"Adaptive lighting failed to reuse many stationary contributions.");Shader.SetGlobalInt("_FSSurfaceDebug",0);manager.adaptiveSurfaceCache=false;
        cached=Capture("64-lights");manager.useSurfaceCache=false;direct=Capture("64-direct");Check(Difference(cached,direct)<.035,"Combined 64-source cache disagrees with spatial direct lighting.");manager.useSurfaceCache=true;
        var manyDoor=LocalFlameValidation.Box("Door across 64 contributions",origin+Vector3.up,new Vector3(7,.1f,7),material,true);
        Settle();Check(Mean(Capture("64-door-blocked"))<Mean(cached)*.05,"Dynamic blocker failed to subtract multiple cached light contributions.");Remove(manyDoor);Settle();
        for(int i=0;i<10;i++)manager.RenderForCamera(camera,1);Check(manager.LastAllocatedBytes==0,"Warm surface lighting allocated managed memory.");
        foreach(var shader in new[]{Resources.Load<Shader>("Lighting/FlameSurfaceCache"),Shader.Find("Custom/BakedStoneWallMatte")})
        {Check(shader!=null&&shader.isSupported,"Production surface shader unsupported.");foreach(var message in ShaderUtil.GetShaderMessages(shader))Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);}
        File.WriteAllText(Output+"/surface-validation.json",JsonUtility.ToJson(new Result{checks=checks,directCacheError=error,grazingRelativeError=grazingError/grazingExpected,shadowBytes=LocalFlameLighting.AtlasBytes,surfaceBytes=LocalFlameSurfaceCache.Bytes,readyPages=manager.SurfaceCache.ReadyPages},true));
    }
    [Serializable]class Result {public int checks,readyPages;public double directCacheError,grazingRelativeError;public long shadowBytes,surfaceBytes;}
    sealed class RenderGuard : ITerrainMovementGuard,ITerrainLightGuard
    {public bool Ready=true;public bool CanMove(Unity.Mathematics.double3 from,Unity.Mathematics.double3 to,double radius,double height)=>false;public bool CanIlluminate(Unity.Mathematics.double3 center,double radius)=>Ready;}
}
