using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

/// <summary>Actual GPU depth captures and receiver pixels, in an isolated project.</summary>
[InitializeOnLoad]
public static class LocalFlameValidation
{
    const string PlayPending="FS.LocalFlame.PlayValidation";
    static IEnumerator playRoutine;
    static LocalFlameValidation()=>EditorApplication.playModeStateChanged+=state=>
    {
        if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PlayPending,false))return;
        SessionState.SetBool(PlayPending,false);playRoutine=PlayChecks();EditorApplication.update+=PlayTick;
    };
    static int lastPlayFrame=-1;
    static void PlayTick()
    {
        if(lastPlayFrame==Time.frameCount)return;lastPlayFrame=Time.frameCount;
        try{if(playRoutine.MoveNext())return;EditorApplication.update-=PlayTick;Debug.Log($"LOCAL FLAME PLAY PASS: {checks} checks");EditorApplication.Exit(0);}
        catch(Exception error){EditorApplication.update-=PlayTick;Debug.LogException(error);EditorApplication.Exit(1);}
    }
    public static void RunPlayBatch()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Configure();
        SessionState.SetBool(PlayPending,true);EditorApplication.EnterPlaymode();
    }
    static IEnumerator PlayChecks()
    {
        checks=0;
        var camera=new GameObject("Play validation camera").AddComponent<Camera>();camera.tag="MainCamera";camera.enabled=false;
        camera.transform.SetPositionAndRotation(new Vector3(2,1.5f,2.5f),Quaternion.LookRotation(Vector3.back));
        var target=new RenderTexture(256,256,24);target.Create();camera.targetTexture=target;
        var world=new GameObject("Play building world").AddComponent<BuildWorld>();world.Camera=camera;world.Focus=camera.transform;
        // Production BuildWorld lives on the player, with the view camera beneath it.
        camera.transform.SetParent(world.transform,true);
        LocalFlameSource[] MountedSources()=>Array.FindAll(Object.FindObjectsByType<LocalFlameSource>(),s=>s.stationary && s.coverageWorld==world);
        var catalog=world.Catalog;Check(catalog.Find("build.lighting.wall-torch")!=null,"Installed torch missing in Play mode.");
        var frame=world.Session.CreateFrame(Vector3.zero,0);var wall=world.Session.Add(catalog.Find("build.prototype.wall"),frame,Vector3Int.zero,0,true);
        BuildPieceRecord AddTorch()
        {
            var p=BuildFurnishing.Solve(catalog.Find("build.lighting.wall-torch"),new Vector3(2,1,.125f),Vector3.forward,wall,world.Session.Frame(wall.OwnFrameId),0,0,Vector3Int.zero);
            return world.Session.Add(p.Definition,world.Session.CreateFrame(p.Frame.Origin,p.Frame.YawStep),p.Anchor,p.YawStep,false);
        }
        var torch=AddTorch();
        for(int i=0;i<40;i++){camera.Render();yield return null;}
        var manager=LocalFlameLighting.Instance;Check(manager!=null && manager.RegisteredSources==1 && manager.ActiveLights==1,"Placed torch did not create live lighting.");
        Check(manager.StaticRefreshes==0,"Mounted torch did not settle into cached mode.");
        var effects=MountedSources();Check(effects.Length==1,"Unexpected mounted effect ownership.");var pooled=effects[0];
        Vector3 emitter=pooled.Position,flameBase=pooled.GetComponentInChildren<LocalFlameEffect>().transform.position;
        Quaternion effectRotation=pooled.transform.rotation;Vector3 effectScale=pooled.transform.lossyScale;
        var mountedRoot=pooled.transform.parent;
        int anchoredRefreshes=manager.TotalStaticRefreshes;
        world.transform.SetPositionAndRotation(new Vector3(.4f,.1f,.2f),Quaternion.Euler(0,25,0));
        world.transform.localScale=new Vector3(1.1f,.9f,1.2f);
        for(int i=0;i<15;i++){camera.Render();yield return null;}
        Check((pooled.Position-emitter).sqrMagnitude<1e-10f,"Mounted light followed the moving/turning player without equipping a held torch.");
        Check((pooled.GetComponentInChildren<LocalFlameEffect>().transform.position-flameBase).sqrMagnitude<1e-10f,"Mounted flame detached from its world-space base.");
        Check(Quaternion.Angle(pooled.transform.rotation,effectRotation)<.001f && (pooled.transform.lossyScale-effectScale).sqrMagnitude<1e-10f,"Player rotation/scale changed the mounted effect pose.");
        Check(manager.TotalStaticRefreshes==anchoredRefreshes,"Moving the player rebuilt the stationary torch cache.");
        Check(manager.RegisteredSources==1 && Object.FindAnyObjectByType<LocalFlameHeldTorch>()==null,"Player movement created a held torch without T.");
        int refreshes=manager.TotalStaticRefreshes;world.Session.Damage(torch.Id,1);
        for(int i=0;i<5;i++){camera.Render();yield return null;}
        Check(manager.TotalStaticRefreshes==refreshes,"Health-only item update rebuilt shadows.");
        world.Remove(torch.Id);
        for(int i=0;i<8;i++){camera.Render();yield return null;}
        Check(manager.RegisteredSources==0 && manager.ActiveLights==0,"Removing a torch leaked its light registration.");
        torch=AddTorch();for(int i=0;i<30;i++){camera.Render();yield return null;}
        effects=MountedSources();Check(effects.Length==1 && effects[0]==pooled,"Mounted effect did not reuse its pool entry.");
        Check((pooled.Position-emitter).sqrMagnitude<1e-10f,"Reused mounted light lost its world-space placement.");
        Check(manager.ActiveLights==1,"Reused mounted source failed to light.");
        world.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);world.transform.localScale=Vector3.one;
        var held=new GameObject("Play offhand").AddComponent<LocalFlameHeldTorch>();held.Toggle(camera,world);
        for(int i=0;i<30;i++){camera.Render();yield return null;}
        var hand=held.GetComponentInChildren<LocalFlameSource>();Check(held.Equipped && hand!=null && !hand.stationary,"Held torch did not equip as a moving source.");
        Check(manager.RegisteredSources==2 && manager.StaticRefreshes>=1,"Moving source did not redraw its environment shadows.");
        camera.transform.position=new Vector3(2,1.5f,.21f);Physics.SyncTransforms();
        for(int i=0;i<5;i++){camera.Render();yield return null;}
        Check(!hand.burning,"Held emitter was not suppressed against a nearby solid wall.");
        camera.transform.position=new Vector3(2,1.5f,2.5f);held.Toggle(camera,world);
        for(int i=0;i<5;i++){camera.Render();yield return null;}Check(manager.RegisteredSources==1,"Unequipping leaked a moving source.");
        held.Toggle(camera,world);for(int i=0;i<20;i++){camera.Render();yield return null;}
        Check(hand.burning && manager.RegisteredSources==2,"Re-equipping did not restore the held source.");
        camera.transform.SetParent(null,true);Object.Destroy(held.gameObject);Object.Destroy(world.gameObject);
        for(int i=0;i<5;i++){camera.Render();yield return null;}
        Check(manager.RegisteredSources==0,"World/offhand teardown retained lighting sources.");
        Check(pooled==null && mountedRoot==null,"World teardown leaked the detached mounted-effect pool.");
        camera.targetTexture=null;target.Release();Object.Destroy(target);
    }
    static int checks;
    static readonly string Output=".utmp/local-flame";
    static void Check(bool condition,string message){checks++;if(!condition)throw new InvalidOperationException(message);}
    public static void RunBatch()
    {
        try{Run();Debug.Log($"LOCAL FLAME VALIDATION PASS: {checks} checks");EditorApplication.Exit(0);}
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    public static GameObject Box(string name,Vector3 center,Vector3 size,Material material,bool dynamic=false)
    {
        var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name=name;box.transform.position=center;box.transform.localScale=size;
        if(material!=null)box.GetComponent<Renderer>().sharedMaterial=material;
        var occluder=box.AddComponent<LocalFlameOccluder>();occluder.dynamicOccluder=dynamic;occluder.GeometryChanged();return box;
    }
    public static Material WhiteWood()
    {
        var material=new Material(Shader.Find("Custom/BuildingWoodMatte"));
        material.SetFloat("_AmbientFloor",0);material.SetFloat("_ShadowTintStrength",0);material.SetFloat("_Saturation",1);material.SetFloat("_Contrast",1);
        material.SetTexture("_BaseMap",Texture2D.whiteTexture);material.SetColor("_BaseColor",Color.white);material.SetFloat("_Brightness",1);
        return material;
    }
    public static void Configure()
    {
        ShaderUtil.allowAsyncCompilation=false;
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;RenderSettings.fog=false;
        Shader.SetGlobalInt("_TerrainSkyEnabled",0);
    }
    static Texture2D Capture(Camera camera,LocalFlameLighting manager,string name)
    {
        manager.RenderForCamera(camera,1);
        var target=RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        camera.targetTexture=target;
        // The first manual render initializes URP resources and material variants in batch mode.
        for(int warm=0;warm<4;warm++)camera.Render();
        RenderTexture.active=target;
        var image=new Texture2D(256,256,TextureFormat.RGBA32,false,true);image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
        Directory.CreateDirectory(Output);File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=null;RenderTexture.ReleaseTemporary(target);return image;
    }
    static double Mean(Texture2D image)
    {double sum=0;var pixels=image.GetPixels32();for(int y=64;y<192;y++)for(int x=64;x<192;x++){var p=pixels[y*256+x];sum+=(p.r+p.g+p.b)/(3.0*255);}return sum/(128*128);}
    static double Difference(Texture2D a,Texture2D b)
    {var aa=a.GetPixels32();var bb=b.GetPixels32();double sum=0;for(int i=0;i<aa.Length;i++)sum+=Math.Abs(aa[i].r-bb[i].r)+Math.Abs(aa[i].g-bb[i].g)+Math.Abs(aa[i].b-bb[i].b);return sum/(aa.Length*3*255.0);}
    public static void Run()
    {
        checks=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);LocalFlameGeometry.Reset();Configure();
        var camera=new GameObject("Validation camera").AddComponent<Camera>();camera.tag="MainCamera";camera.orthographic=true;camera.orthographicSize=1.2f;
        camera.nearClipPlane=.02f;camera.farClipPlane=20;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        camera.transform.SetPositionAndRotation(new Vector3(0,.8f,0),Quaternion.Euler(90,0,0));
        camera.GetUniversalAdditionalCameraData().renderShadows=true;
        var material=WhiteWood();var floor=Box("Floor",new Vector3(0,-.125f,0),new Vector3(8,.25f,8),material);
        var source=new GameObject("Torch proof emitter").AddComponent<LocalFlameSource>();source.localEmitter=Vector3.zero;source.transform.position=new Vector3(0,2,0);source.flicker=0;
        source.range=6;source.intensity=4;source.color=new Color(1,.52f,.19f);
        var manager=LocalFlameLighting.Ensure();manager.Register(source);manager.targetCamera=camera;manager.automaticRendering=false;
        manager.useSurfaceCache=false;manager.maxActiveLights=8;
        for(int i=0;i<20;i++)manager.RenderForCamera(camera);
        var lit=Capture(camera,manager,"floor-lit");double litMean=Mean(lit);
        Debug.Log($"FLAME FLOOR LIT {litMean:F6}; active {manager.ActiveLights}; draws {manager.ShadowDraws}");
        Check(lit.GetPixel(128,128).g>.02f,"Warm source lost its green channel or material resources were not ready.");
        Check(manager.StaticAtlas!=null,"R32 float shadow arrays unavailable.");Check(litMean>.015,"Local light did not illuminate floor.");
        Check(manager.SurfaceCache==null&&manager.AllocatedGpuBytes==LocalFlameLighting.AtlasBytes,"Disabled surface caching allocated unused GPU atlases.");
        Check(manager.StaticRefreshes==0,"Stationary cache redrew without a geometry change.");
        int original=manager.TotalStaticRefreshes;
        source.intensity*=.8f;source.color=new Color(1,.6f,.3f);manager.RenderForCamera(camera);
        Check(manager.TotalStaticRefreshes==original,"Color/intensity changes rebuilt visibility.");
        source.intensity/=.8f;source.color=new Color(1,.52f,.19f);
        camera.transform.position+=Vector3.right*.05f;manager.RenderForCamera(camera);
        Check(manager.TotalStaticRefreshes==original,"Camera movement rebuilt a stationary cache.");camera.transform.position-=Vector3.right*.05f;
        var roof=Box("Thin offscreen roof",new Vector3(0,1,0),new Vector3(7,.03f,7),material);
        Check(!manager.IsCached(source),"New wall did not synchronously invalidate visibility.");
        var blocked=Capture(camera,manager,"floor-blocked");double blockedMean=Mean(blocked);
        Debug.Log($"FLAME FLOOR BLOCKED {blockedMean:F6}");Check(blockedMean<litMean*.1,"Light leaked through 3 cm offscreen roof.");
        DestroyBox(roof);var reopened=Capture(camera,manager,"floor-reopened");
        Check(Mean(reopened)>litMean*.9,"Removing roof retained stale shadow.");
        var far=Box("Unrelated distant rebuild",new Vector3(30,1,0),Vector3.one,material);original=manager.TotalStaticRefreshes;
        manager.RenderForCamera(camera);Check(manager.TotalStaticRefreshes==original,"Distant geometry invalidated this light.");DestroyBox(far);
        var moving=Box("Dynamic door",new Vector3(0,1,0),new Vector3(7,.1f,7),material,true);
        var dynamicBlocked=Capture(camera,manager,"dynamic-blocked");Check(Mean(dynamicBlocked)<litMean*.1,"Dynamic occluder did not block cached light.");
        original=manager.TotalStaticRefreshes;moving.transform.position=Vector3.right*20;moving.GetComponent<LocalFlameOccluder>().GeometryChanged();
        var dynamicOpen=Capture(camera,manager,"dynamic-open");Check(Mean(dynamicOpen)>litMean*.9,"Moving door left a stale dynamic shadow.");
        Check(manager.TotalStaticRefreshes==original,"Moving geometry rebuilt stationary geometry.");DestroyBox(moving);
        var cached=Capture(camera,manager,"cached-reference");manager.cacheStationary=false;
        var baseline=Capture(camera,manager,"dynamic-baseline");
        Check(manager.StaticRefreshes==1,"Baseline did not redraw stationary shadow.");Check(Difference(cached,baseline)<.002,"Cached and baseline receiver images differ.");
        manager.cacheStationary=true;source.transform.position+=Vector3.right*.2f;manager.RenderForCamera(camera);
        Check(manager.StaticRefreshes==1,"Moving light did not invalidate its visibility.");
        source.burning=false;var dark=Capture(camera,manager,"extinguished");Check(Mean(dark)<.002,"Extinguished light still illuminates surfaces.");source.burning=true;
        // Terrain uses its actual production shader, independently of cave ambient.
        var terrainMaterial=new Material(Shader.Find("Custom/StylizedTerrainURP"));
        var control=new Texture2D(1,1);control.SetPixel(0,0,new Color(0,0,1));control.Apply();
        terrainMaterial.SetTexture("_ControlMap0",control);terrainMaterial.SetFloat("_AmbientStrength",0);
        terrainMaterial.SetFloat("_GrassNormalStrength",0);floor.GetComponent<Renderer>().sharedMaterial=terrainMaterial;
        var terrain=Capture(camera,manager,"terrain-lit");Check(Mean(terrain)>.005,"Terrain shader did not receive local illumination.");
        source.burning=false;var terrainDark=Capture(camera,manager,"terrain-dark");Check(Mean(terrainDark)<Mean(terrain)*.1,"Terrain did not return to darkness.");
        source.burning=true;source.transform.position=new Vector3(0,2,0);
        // All six cubemap directions, including negative coordinates, must contain light behind a thin blocker.
        floor.GetComponent<Renderer>().sharedMaterial=material;
        foreach(var normal in new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back})
        {
            var rotation=Quaternion.FromToRotation(Vector3.up,normal);
            floor.transform.SetPositionAndRotation(-normal*.125f,rotation);floor.GetComponent<LocalFlameOccluder>().GeometryChanged();
            source.transform.position=normal*2;camera.transform.SetPositionAndRotation(normal*.8f,Quaternion.LookRotation(-normal,Mathf.Abs(normal.y)>.9f?Vector3.forward:Vector3.up));
            var openAxis=Capture(camera,manager,"axis-open-"+checks);Check(Mean(openAxis)>.015,"A cubemap face failed to illuminate its receiver: "+normal);
            var blocker=Box("Axis blocker",normal,new Vector3(7,.03f,7),material);blocker.transform.rotation=rotation;blocker.GetComponent<LocalFlameOccluder>().GeometryChanged();
            var blockedAxis=Capture(camera,manager,"axis-blocked-"+checks);Check(Mean(blockedAxis)<Mean(openAxis)*.1,"Thin blocker leaked on cubemap direction "+normal);
            DestroyBox(blocker);
        }
        floor.transform.SetPositionAndRotation(new Vector3(0,-.125f,0),Quaternion.identity);floor.GetComponent<LocalFlameOccluder>().GeometryChanged();
        camera.transform.SetPositionAndRotation(new Vector3(0,.8f,0),Quaternion.Euler(90,0,0));source.transform.position=new Vector3(0,2,0);
        source.range=.5f;var outOfRange=Capture(camera,manager,"range-cutoff");Check(Mean(outOfRange)<.002,"Light escaped its finite range.");source.range=6;
        var unchanged=LocalFlameGeometry.Count;floor.GetComponent<LocalFlameOccluder>().GeometryChanged();Check(LocalFlameGeometry.Count==unchanged,"Geometry notification duplicated a registry entry.");
        // Budget overflow must queue complete caches and never publish unshadowed substitutes.
        for(int i=0;i<31;i++)
        {var s=new GameObject("Budget source "+i).AddComponent<LocalFlameSource>();s.localEmitter=Vector3.zero;s.transform.position=new Vector3((i%8)*.2f,2,(i/8)*.2f);manager.Register(s);}
        for(int i=0;i<12;i++){manager.RenderForCamera(camera);Check(manager.ActiveLights<=LocalFlameLighting.Capacity,"Active-light cap exceeded.");Check(manager.StaticRefreshes<=1,"Static rebuild admission exceeded.");}
        Check(manager.RegisteredSources==32,"Overflow sources were lost.");Check(manager.ActiveLights==8,"Nearest eight lights did not obtain complete caches.");
        Check(LocalFlameLighting.AtlasBytes==102L*1024*1024,"Unexpected cache memory budget.");
        for(int i=0;i<10;i++)manager.RenderForCamera(camera);
        Check(manager.LastAllocatedBytes==0,$"Steady render update allocated {manager.LastAllocatedBytes} managed bytes.");
        foreach(var shader in new[]{Shader.Find("Custom/BuildingWoodMatte"),Shader.Find("Custom/StylizedTerrainURP"),Shader.Find("Custom/WeatheredRock"),Resources.Load<Shader>("Lighting/FlameShadowDepth")})
        {Check(shader!=null && shader.isSupported,"Shader unsupported.");foreach(var message in ShaderUtil.GetShaderMessages(shader))Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);}
        File.WriteAllText(Output+"/validation.json",JsonUtility.ToJson(new Result{checks=checks,lit=litMean,blocked=blockedMean,cacheBaselineDifference=Difference(cached,baseline),atlasBytes=LocalFlameLighting.AtlasBytes},true));
    }
    [Serializable]class Result{public int checks;public double lit,blocked,cacheBaselineDifference;public long atlasBytes;}
    static void DestroyBox(GameObject box){LocalFlameGeometry.Remove(box.GetComponent<LocalFlameOccluder>());Object.DestroyImmediate(box);}
    public static void BuildBenchmark()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Configure();
            new GameObject("Local flame benchmark").AddComponent<LocalFlameBenchmark>();
            const string scene="Assets/LocalFlameBenchmark.unity";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);
            PlayerSettings.enableFrameTimingStats=true;PlayerSettings.runInBackground=true;
            string output=Path.GetFullPath(Output+"/player/FlameBenchmark.exe");Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            Check(report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded,"Benchmark player build failed.");
            Debug.Log("LOCAL FLAME BENCHMARK BUILD PASS: "+output);EditorApplication.Exit(0);
        }catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    public static void BuildComparison()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Configure();
            // Only the isolated benchmark project's copied pipeline asset is changed.
            var pipeline=new SerializedObject(GraphicsSettings.defaultRenderPipeline);
            pipeline.FindProperty("m_AdditionalLightsShadowmapResolution").intValue=4096;pipeline.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
            foreach(var tier in new[]{"m_AdditionalLightsShadowResolutionTierLow","m_AdditionalLightsShadowResolutionTierMedium","m_AdditionalLightsShadowResolutionTierHigh"})pipeline.FindProperty(tier).intValue=256;
            pipeline.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
            new GameObject("Local flame comparison").AddComponent<LocalFlameComparisonBenchmark>();
            const string scene="Assets/LocalFlameComparison.unity";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);
            PlayerSettings.enableFrameTimingStats=true;PlayerSettings.runInBackground=true;
            string output=Path.GetFullPath(Output+"/player/FlameBenchmark.exe");Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            Check(report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded,"Comparison player build failed.");
            Debug.Log("LOCAL FLAME COMPARISON BUILD PASS: "+output);EditorApplication.Exit(0);
        }catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
