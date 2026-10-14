using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class RockPresetValidation
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    [MenuItem("Tools/Terrain/Validate Rock Presets and Pooling")]
    public static void Run()
    {
        checks=0;Assets();Generation();Pooling();
        FoliagePublicationValidation.Run();QueryTargetValidation.Run();
        RockPerformanceValidation.Run();
        Debug.Log($"ROCK_VALIDATION_PASS: {checks} checks; presets, deterministic dense/sparse snow placement, terrain filters, instanced batches, collision reuse and visibility independence.");
    }
    private static void Assets()
    {
        foreach(var name in RockPresetSetup.Names)foreach(var finish in new[]{"Bare","Moss","Snow"})
        {
            var p=Resources.Load<GameObject>("Rocks/"+name+"_"+finish);
            Check(p!=null,name+" prefab missing");
            var m=p.GetComponent<MeshFilter>().sharedMesh;
            Check(m.GetIndexCount(0)/3<=800,"Visual budget");
            Check(m.bounds.min.y>=-.001f && m.bounds.min.y<=.001f,"Ground origin");
            Check(m.bounds.size.x<5 && m.bounds.size.y<3,"Metre import scale: "+name+" "+m.bounds);
            var hull=p.GetComponent<RockPreset>().collisionHull;
            Check(hull!=null && hull.GetIndexCount(0)/3<=100,"Hull budget");
            var mat=p.GetComponent<MeshRenderer>().sharedMaterial;
            Check(mat!=null && mat.enableInstancing && mat.GetTexture("_BaseMap")!=null,"Assigned material/texture");
            Check(AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap"))=="Assets/Textures/Rock/Presets/PaintedStone_Albedo.png","Painted albedo assignment");
            Check(AssetDatabase.GetAssetPath(mat.GetTexture("_CoatingMap"))=="Assets/Textures/Rock/Presets/WeatheredStone_ColorHeight.png","Preserved coating-mask assignment");
            Check(mat.GetFloat("_Relief")==0,"Photoreal surface relief remains enabled");
            if(finish=="Snow")Check(mat.GetFloat("_Coverage")==1,"Snow coverage changed");
            Check(mat.GetFloat("_SurfaceMode")== (finish=="Bare"?0:finish=="Moss"?1:2),"Coating assignment");
        }
    }
    private static void Generation()
    {
        const int size=128,n=size+3;
        var b=new BiomeType[n,n];var s=new SurfaceType[n,n];
        var moisture=new float[n,n];var temperature=new float[n,n];var slope=new float[n,n];var river=new float[n,n];var heights=new float[n,n];
        for(int x=0;x<n;x++)for(int z=0;z<n;z++){b[x,z]=BiomeType.Snow;s[x,z]=SurfaceType.Snow;moisture[x,z]=.35f;temperature[x,z]=.05f;heights[x,z]=1;}
        int small=0,large=0;
        var settings=WorldFeatureGenerationSettings.Default;
        for(int seed=0;seed<12;seed++)
        {
            var coord=new ChunkCoord(seed-6,2-seed);
            var dense=WorldFeaturePlanGenerator.Generate(coord,size,seed*193,b,s,moisture,temperature,slope,river,settings,heights);
            var sparse=WorldFeaturePlanGenerator.GenerateTreePlacements(coord,size,seed*193,b,s,moisture,temperature,slope,river,settings,(x,z)=>{},sampleHeight:(x,z)=>heights[x,z]);
            var rocks=dense.Placements.Where(p=>p.featureType==WorldFeatureType.Boulder).ToArray();
            var sparseRocks=sparse.Placements.Where(p=>p.featureType==WorldFeatureType.Boulder).ToArray();
            Check(rocks.Length>0 && rocks.Length<=6,"Snow placement budget or empty chunk");
            Check(rocks.Length==sparseRocks.Length,"Dense/sparse rock count");
            for(int i=0;i<rocks.Length;i++)
            {
                var a=rocks[i];var c=sparseRocks[i];
                Check(a.variant==c.variant && a.sampleX==c.sampleX && a.sampleZ==c.sampleZ && a.rotation==c.rotation && a.scale==c.scale && a.prefabIndex==c.prefabIndex,"Dense/sparse identity");
                Check(a.prefabIndex>=0 && a.prefabIndex<3,"Preset index");
                if(a.variant==WorldFeatureVariant.SnowBoulder)small++;else large++;
            }
            using var record=new ChunkRecord(coord);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(ChunkRecord).GetField("worldFeaturePlan",flags).SetValue(record,dense);
            typeof(ChunkRecord).GetField("surfaceTypeMap",flags).SetValue(record,s);
            typeof(ChunkRecord).GetField("heightMap",flags).SetValue(record,heights);
            FoliageGenerator.GenerateRocksForChunk(record,null,seed,size,2,10);
            Check(record.FoliageData.rockInstances.Count==rocks.Length,"Snow surface conversion rejected rocks");
            foreach(var rock in record.FoliageData.rockInstances)Check(Mathf.Abs(rock.localPosition.y-20)<.001f,"Terrain height/world scale");
            if(seed==0)
            {
                for(int x=0;x<n;x++)for(int z=0;z<n;z++)heights[x,z]=1+(x-1)*.03f;
                FoliageGenerator.GenerateRocksForChunk(record,null,seed,size,2,10);
                var slopeRotation=Quaternion.FromToRotation(Vector3.up,new Vector3(-.3f,1,0).normalized);
                for(int i=0;i<rocks.Length;i++)
                    Check(Quaternion.Angle(record.FoliageData.rockInstances[i].localRotation,slopeRotation*rocks[i].rotation)<.02f,"Analytic terrain seating normal");
                for(int x=0;x<n;x++)for(int z=0;z<n;z++)heights[x,z]=1;
            }
        }
        Check(small>0 && large>0,"Small and large snow variation");
        for(int mode=0;mode<4;mode++)
        {
            for(int x=0;x<n;x++)for(int z=0;z<n;z++){b[x,z]=mode==0?BiomeType.Water:BiomeType.Snow;s[x,z]=mode==1?SurfaceType.Cliff:SurfaceType.Snow;slope[x,z]=mode==2?60:0;river[x,z]=mode==3?1:0;}
            var p=WorldFeaturePlanGenerator.Generate(new ChunkCoord(0,0),size,123,b,s,moisture,temperature,slope,river,settings,heights);
            Check(!p.Placements.Any(a=>a.featureType==WorldFeatureType.Boulder),"Snow rocks on water/cliff/steep/river terrain");
        }
        settings.maxSnowRocksPerChunk=0;
        for(int x=0;x<n;x++)for(int z=0;z<n;z++){s[x,z]=SurfaceType.Snow;slope[x,z]=0;river[x,z]=0;}
        var disabled=WorldFeaturePlanGenerator.Generate(new ChunkCoord(0,0),size,123,b,s,moisture,temperature,slope,river,settings,heights);
        Check(!disabled.Placements.Any(a=>a.featureType==WorldFeatureType.Boulder),"Zero rock budget");
    }
    private static void Pooling()
    {
        var prefab=Resources.Load<GameObject>("Rocks/Fieldstone_Bare");
        var root=new GameObject("Rock fixture");
        var settings=new TreeSettings{rockColliderActivationRadius=28,rockColliderReleaseRadius=36,rockColliderActivationsPerFrame=8,maxActiveRockColliders=4,maxPooledRockColliders=2};
        using var pool=new RockColliderPool(settings);
        try
        {
            var set=new RockRenderSet();
            set.Add(prefab,new RockInstanceData(Vector3.zero,Quaternion.identity,Vector3.one,WorldFeatureVariant.GrasslandBoulder,0),root.transform);set.Finish();pool.Register(set);
            pool.Update(Vector3.zero,0);Check(pool.ActiveCount==1,"Near activation");
            var query=Object.FindObjectsByType<RockQueryTarget>().Single(q=>q.gameObject.scene.IsValid());
            query.TryGetInfo(out var oldInfo);
            Check(query.GetComponentsInChildren<Renderer>().Length==0,"Collision proxy owns renderer");
            Check(query.gameObject.layer==GameplayLayers.WorldSolid,"Collision layer");
            root.SetActive(false);pool.Update(Vector3.zero,.2);
            Check(pool.ActiveCount==1 && query.gameObject.activeInHierarchy,"Render hiding disabled collision");
            Physics.SyncTransforms();Check(Physics.Raycast(new Ray(new Vector3(0,4,0),Vector3.down),out _,8,1<<GameplayLayers.WorldSolid),"Hull collision ray miss");
            pool.Update(new Vector3(32,0,0),.4);Check(pool.ActiveCount==1,"Hysteresis release too early");
            pool.Update(new Vector3(50,0,0),.6);Check(pool.ActiveCount==0 && pool.PooledCount==1 && !oldInfo.IsValid,"Pool release/query identity");
            pool.Update(Vector3.zero,.8);Check(pool.ActiveCount==1 && pool.CreatedCount==1 && pool.PooledCount==0,"Pool did not reuse body");
            Check(!oldInfo.IsValid,"Old identity revived after reuse");
            pool.Unregister(set);Check(pool.ActiveCount==0,"Stream unload did not release bodies");
            var snowSet=new RockRenderSet();snowSet.Add(Resources.Load<GameObject>("Rocks/Fieldstone_Snow"),
                set.Entries[0].Placement,root.transform);snowSet.Finish();pool.Register(snowSet);
            pool.Update(Vector3.zero,.9);Check(pool.CreatedCount==1 && pool.ActiveCount==1,"Snow/bare coatings did not share the hull pool");
            pool.Unregister(snowSet);
            root.SetActive(true);
            int objectsBefore=Object.FindObjectsByType<Transform>().Length;
            var runtime=new ChunkFoliageRuntime{root=root.transform,RockPool=pool,grasslandRockPrefabs=new[]{prefab}};
            var placements=new List<RockInstanceData>();
            for(int i=0;i<1040;i++)placements.Add(new RockInstanceData(new Vector3(i*2,0,0),Quaternion.identity,Vector3.one,WorldFeatureVariant.GrasslandBoulder,0));
            runtime.RebuildRockGameObjects(placements,root.transform);
            Check(runtime.RockInstanceCount==1040,"1023 instance boundary");
            Check(Object.FindObjectsByType<Transform>().Length==objectsBefore,"Publication created per-rock GameObjects");
            var stats=new WorldRenderStatsDebugInfo();runtime.AccumulateRockGameObjectRenderStats(ref stats);
            Check(stats.RockGameObjects.instances==1040 && stats.RockGameObjects.triangles==1040*320,"Instanced geometry stats");
            pool.Update(Vector3.zero,1);pool.Update(Vector3.zero,1.2);Check(pool.ActiveCount<=4,"Active body cap");
            runtime.ClearRockGameObjects();Check(pool.ActiveCount==0 && pool.PooledCount<=2,"Unload and retained pool cap");
        }
        finally{Object.DestroyImmediate(root);}
    }
    [MenuItem("Tools/Terrain/Render Rock Finish Preview")]
    public static void Render()
    {
        Directory.CreateDirectory(".utmp/rocks");
        var root=new GameObject("Rock preview");var camera=root.AddComponent<Camera>();camera.enabled=false;
        camera.orthographic=true;camera.orthographicSize=7.6f;camera.aspect=1.5f;
        camera.transform.position=new Vector3(16,17,-22);camera.transform.LookAt(new Vector3(5,0,4));
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.2f,.21f);
        var lightObj=new GameObject("Rock preview light");var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.Soft;lightObj.transform.rotation=Quaternion.Euler(42,-32,0);
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.transform.position=new Vector3(5,-.1f,4);ground.transform.localScale=new Vector3(4,1,3);
        var groundMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMat.SetColor("_BaseColor",new Color(.19f,.24f,.16f));groundMat.SetFloat("_Smoothness",0);ground.GetComponent<Renderer>().sharedMaterial=groundMat;
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;var oldActive=RenderTexture.active;bool oldAsync=ShaderUtil.allowAsyncCompilation;
        var target=new RenderTexture(1500,1000,24);target.Create();
        var set=new RockRenderSet();string[] finishes={"Bare","Moss","Snow"};
        var chunk=new GameObject("Rock preview transforms");
        for(int f=0;f<3;f++)for(int i=0;i<6;i++)
        {
            var prefab=Resources.Load<GameObject>("Rocks/"+RockPresetSetup.Names[i]+"_"+finishes[f]);
            var placement=new RockInstanceData(new Vector3(i%3*4.6f,0,f*4.5f+i/3*1.9f),Quaternion.Euler(0,25+i*41,0),Vector3.one,WorldFeatureVariant.GrasslandBoulder,0);
            // Identity chunk transform, independent of the preview camera's transform.
            set.Add(prefab,placement,chunk.transform);
        }
        set.Finish();
        using var worldRenderer=new RockWorldRenderer(new TreeSettings());worldRenderer.Register(set);
        Action<ScriptableRenderContext,Camera> submit=(_,c)=>{if(c==camera)worldRenderer.Draw(new Vector3(5,0,4),camera);};
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;ShaderUtil.allowAsyncCompilation=false;
            RenderPipelineManager.beginCameraRendering+=submit;
            for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            var material=Resources.Load<Material>("Rocks/Stone_Bare");
            for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);
            Check(!ShaderUtil.ShaderHasError(material.shader),"Rock shader import/variant error");
            RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
            int magenta=pixels.GetPixels32().Count(c=>c.r>220 && c.b>220 && c.g<60);
            Check(magenta==0,"Error shader pixels");File.WriteAllBytes(".utmp/rocks/unity-finishes.png",pixels.EncodeToPNG());Object.DestroyImmediate(pixels);
            Debug.Log("ROCK_RENDER_PASS: actual instanced bare/moss/snow pixels, forward/shadow/depth/depth-normal shader compilation; no FPS claim.");
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=oldActive;
            target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(root);Object.DestroyImmediate(lightObj);Object.DestroyImmediate(ground);Object.DestroyImmediate(groundMat);
            Object.DestroyImmediate(chunk);
        }
    }
    public static void RunBatch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    public static void RenderBatch(){try{Render();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
