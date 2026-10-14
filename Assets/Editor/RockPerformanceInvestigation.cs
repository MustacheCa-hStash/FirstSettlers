using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug=UnityEngine.Debug;

/// <summary>Explicit offline investigation, using the saved world configuration and production generators.</summary>
public static class RockPerformanceInvestigation
{
    private static bool benchmark;
    private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static GameObject Prefab(TreeSettings settings,WorldFeaturePlacement p)
    {
        GameObject[] list=p.variant switch
        {
            WorldFeatureVariant.SnowBoulder=>settings.snowRockPrefabs,
            WorldFeatureVariant.SnowLargeBoulder=>settings.snowLargeRockPrefabs,
            WorldFeatureVariant.GrasslandBoulder=>settings.grasslandRockPrefabs,
            WorldFeatureVariant.GrasslandLargeBoulder=>settings.grasslandLargeRockPrefabs,
            _=>settings.forestRockPrefabs
        };
        return list!=null && list.Length>0?list[Mathf.Clamp(p.prefabIndex,0,list.Length-1)]:null;
    }
    public static void Run()
    {
        Directory.CreateDirectory(".utmp/rocks");
        EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity");
        var manager=UnityEngine.Object.FindAnyObjectByType<WorldManager>();
        var config=(WorldConfiguration)typeof(WorldManager).GetMethod("BuildConfiguration",Private).Invoke(manager,null);
        var g=config.Generation;var settings=config.Foliage.Trees;var features=TreeGenerationSnapshot.Create(settings);
        var arguments=Environment.GetCommandLineArgs();
        int seedArgument=Array.IndexOf(arguments,"-rockBenchmarkSeed");
        if(seedArgument>=0 && seedArgument+1<arguments.Length)g.Seed=int.Parse(arguments[seedArgument+1]);
        int ring=settings.gameObjectRockChunkRingRadius,size=g.ChunkSize,n=size+3;
        var coords=new List<ChunkCoord>();
        var center=new ChunkCoord(Mathf.FloorToInt(config.Scene.Viewer.position.x/(size*g.WorldScale)),Mathf.FloorToInt(config.Scene.Viewer.position.z/(size*g.WorldScale)));
        for(int z=-ring;z<=ring;z++)for(int x=-ring;x<=ring;x++)if(x*x+z*z<=ring*ring)coords.Add(new ChunkCoord(center.x+x,center.z+z));
        var report=new StringBuilder($"Rock performance investigation: seed {g.Seed}, saved viewer {config.Scene.Viewer.position}, chunk {size*g.WorldScale:F2} m, ring {ring}, {coords.Count} chunks, centre ({center.x},{center.z}).\n");
        foreach(var name in RockPresetSetup.Names)
        {var mesh=Resources.Load<Mesh>("Rocks/"+name);report.AppendLine($"{name}: {mesh.vertexCount} imported vertices, {mesh.GetIndexCount(0)/3} triangles");}
        var biomes=new BiomeType[n,n];var surfaces=new SurfaceType[n,n];var m=new float[n,n];var t=new float[n,n];var slope=new float[n,n];var river=new float[n,n];
        foreach(var biome in new[]{BiomeType.Forest,BiomeType.Grassland,BiomeType.Snow})
        {
            for(int x=0;x<n;x++)for(int z=0;z<n;z++){biomes[x,z]=biome;surfaces[x,z]=biome==BiomeType.Snow?SurfaceType.Snow:SurfaceType.Grass;m[x,z]=.5f;t[x,z]=biome==BiomeType.Snow?.05f:.5f;}
            long vertices=0,triangles=0,rocks=0,calls=0,extraSamples=0;
            var timer=Stopwatch.StartNew();
            foreach(var coord in coords)
            {
                var samples=new HashSet<int>();
                var plan=WorldFeaturePlanGenerator.GenerateTreePlacements(coord,size,g.Seed,biomes,surfaces,m,t,slope,river,features,(x,z)=>samples.Add(x*n+z),sampleHeight:(x,z)=>1);
                int withSnow=samples.Count;var disabled=features;disabled.maxSnowRocksPerChunk=0;samples.Clear();
                WorldFeaturePlanGenerator.GenerateTreePlacements(coord,size,g.Seed,biomes,surfaces,m,t,slope,river,disabled,(x,z)=>samples.Add(x*n+z),sampleHeight:(x,z)=>1);
                extraSamples+=withSnow-samples.Count;
                var groups=new HashSet<(Mesh,Material)>();
                foreach(var p in plan.Placements.Where(p=>p.featureType==WorldFeatureType.Boulder))
                {
                    var prefab=Prefab(settings,p);if(prefab==null)continue;
                    var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;
                    rocks++;vertices+=mesh.vertexCount;triangles+=mesh.GetIndexCount(0)/3;
                    groups.Add((mesh,prefab.GetComponent<MeshRenderer>().sharedMaterial));
                }
                calls+=groups.Count;
            }
            report.AppendLine($"Synthetic all-{biome} at saved seed/coordinates: {rocks} rocks, {vertices} imported vertex instances, {triangles} triangles; {calls} per-chunk draws. {extraSamples} extra distinct sparse terrain/climate positions from snow-rock generation. Plan comparison elapsed {timer.Elapsed.TotalMilliseconds:F2} ms.");
            Debug.Log(report.ToString().Split('\n').LastOrDefault(s=>s.Length>0));
        }
        // Actual saved-world terrain, not a uniform-biome estimate. Includes every chunk in the configured range.
        long actualRocks=0,actualVertices=0,actualTriangles=0,actualCalls=0;
        var chunkCounts=new List<int>();var chunkVertices=new List<long>();var conversionTimes=new List<double>();var planTimes=new List<double>();
        var renderSets=new List<RockRenderSet>();var chunkBounds=new List<Bounds>();
        var worldRenderer=new RockWorldRenderer(settings);using var collisionPool=new RockColliderPool(settings);
        var chunkRoot=new GameObject("Rock measurement chunk");
        int index=0;
        foreach(var coord in coords)
        {
            var terrain=HeightMapGenerator.GenerateTerrainHeightField(size,g.Seed,g.SampleScale,coord,g.Water.WaterLevel,g.MountainHorizontalScale,g.MeshHeightMultiplier,g.Erosion);
            var moisture=ClimateGenerator.GenerateTerrainMoistureMap(size,g.Seed,g.SampleScale,g.Octaves,g.Persistence,g.Lacunarity,coord);
            var temperature=ClimateGenerator.GenerateTerrainTemperatureMap(size,g.Seed,g.SampleScale,g.Octaves,g.Persistence,g.Lacunarity,coord);
            var b=BiomeMapGenerator.GenerateBiomeMap(terrain.HeightMap,moisture,temperature,terrain.SlopeMap,terrain.MountainMaskMap,terrain.RiverMaskMap,g.Water.WaterLevel);
            var s=SurfaceMapGenerator.GenerateSurfaceTypeMap(terrain.HeightMap,terrain.SlopeMap,terrain.RiverMaskMap,b,g.Water.WaterLevel);
            var timer=Stopwatch.StartNew();
            var plan=WorldFeaturePlanGenerator.Generate(coord,size,g.Seed,b,s,moisture,temperature,terrain.SlopeMap,terrain.RiverMaskMap,features,terrain.HeightMap,g.Water.WaterLevel,terrain.MountainMaskMap);
            planTimes.Add(timer.Elapsed.TotalMilliseconds);
            using var record=new ChunkRecord(coord);int version=record.BeginTerrainDataRequest();
            record.TryCompleteTerrainDataRequest(version,terrain.HeightMap,terrain.SlopeMap,moisture,temperature,b,s,new WaterState[n,n],new GroundCoverType[n,n],plan,terrain.RiverMaskMap,null);
            timer.Restart();FoliageGenerator.GenerateRocksForChunk(record,settings,g.Seed,size,g.WorldScale,g.MeshHeightMultiplier);conversionTimes.Add(timer.Elapsed.TotalMilliseconds);
            var groups=new HashSet<(Mesh,Material)>();long cv=0;int count=0;
            var set=new RockRenderSet();chunkRoot.transform.position=new Vector3((coord.x+.5f)*size*g.WorldScale,0,(coord.z+.5f)*size*g.WorldScale);
            foreach(var rock in record.FoliageData.rockInstances)
            {
                var prefab=Prefab(settings,new WorldFeaturePlacement(WorldFeatureType.Boulder,rock.variant,0,0,default,default,0,0,rock.prefabIndex));
                if(prefab==null)continue;
                var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;actualRocks++;actualVertices+=mesh.vertexCount;cv+=mesh.vertexCount;actualTriangles+=mesh.GetIndexCount(0)/3;count++;
                groups.Add((mesh,prefab.GetComponent<MeshRenderer>().sharedMaterial));
                set.Add(prefab,rock,chunkRoot.transform);
            }
            set.Finish();renderSets.Add(set);worldRenderer.Register(set);collisionPool.Register(set);
            float bh=Mathf.Max(200,g.MeshHeightMultiplier*2+100);chunkBounds.Add(new Bounds(new Vector3(chunkRoot.transform.position.x,bh*.5f,chunkRoot.transform.position.z),new Vector3(size*g.WorldScale,bh,size*g.WorldScale)));
            chunkCounts.Add(count);chunkVertices.Add(cv);actualCalls+=groups.Count;
            if(++index%16==0)Debug.Log($"ROCK_INVESTIGATION_PROGRESS: {index}/{coords.Count} actual world chunks");
        }
        report.AppendLine($"Actual saved viewer range: {actualRocks} rocks, {actualVertices} imported vertex instances, {actualTriangles} triangles, {actualCalls} per-chunk draws before camera/shadow rejection. Mean {chunkCounts.Average():F3} rocks / {chunkVertices.Average():F1} vertices per chunk; maximum chunk {chunkCounts.Max()} rocks / {chunkVertices.Max()} vertices.");
        report.AppendLine($"Main-thread rock conversion per actual chunk: average {conversionTimes.Average():F4} ms, maximum {conversionTimes.Max():F4} ms. Whole worker feature plan (all vegetation): average {planTimes.Average():F3} ms, maximum {planTimes.Max():F3} ms. Isolated editor timings; not live-world FPS.");
        var context=HeightMapGenerator.CreateSamplingContext(g.Seed,g.Water.WaterLevel,g.MountainHorizontalScale,g.Erosion);
        var focus=config.Scene.Viewer.position;
        focus.y=HeightMapGenerator.SampleTerrainHeightNative(focus.x/g.WorldScale,focus.z/g.WorldScale,g.SampleScale,context.RiverSeed,g.Water.WaterLevel,g.MountainHorizontalScale,context.Erosion).Height*g.MeshHeightMultiplier*g.WorldScale;
        var cameraObject=new GameObject("Rock measurement camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=65;camera.aspect=16f/9;camera.farClipPlane=400;camera.transform.position=focus+Vector3.up*1.6f;
        var oldDraws=new List<int>();var oldVertices=new List<long>();var newDraws=new List<int>();var newVertices=new List<long>();var newTriangles=new List<long>();
        var frustum=new Plane[6];
        for(int yaw=0;yaw<360;yaw+=45)
        {
            camera.transform.rotation=Quaternion.Euler(0,yaw,0);GeometryUtility.CalculateFrustumPlanes(camera,frustum);
            int oldCalls=0;long oldV=0;
            for(int c=0;c<renderSets.Count;c++)
            {
                bool visible=GeometryUtility.TestPlanesAABB(frustum,chunkBounds[c]);var bound=chunkBounds[c];bound.Expand(new Vector3(size*g.WorldScale*config.Content.FoliageFrustumPaddingChunks*2,0,size*g.WorldScale*config.Content.FoliageFrustumPaddingChunks*2));
                bool shadow=GeometryUtility.TestPlanesAABB(frustum,bound);
                if(!visible && !shadow)continue;
                oldCalls+=renderSets[c].Visuals.Select(v=>(v.Mesh,v.Material,v.Submesh)).Distinct().Count();
                if(visible)oldV+=renderSets[c].Visuals.Sum(v=>(long)v.Mesh.vertexCount);
            }
            worldRenderer.Draw(focus,camera,false);
            oldDraws.Add(oldCalls);oldVertices.Add(oldV);newDraws.Add(worldRenderer.DrawCalls);newVertices.Add(worldRenderer.VisibleGeometry.vertices);newTriangles.Add(worldRenderer.VisibleGeometry.triangles);
        }
        report.AppendLine($"Eight ground-level headings (65 degree FOV): legacy chunk-culling submission model mean {oldDraws.Average():F1} draws, {oldVertices.Average():F0} visible full-detail vertex instances. Optimized renderer mean {newDraws.Average():F1} draws including offscreen shadows, {newVertices.Average():F0} visible vertices / {newTriangles.Average():F0} triangles. Visible vertex range {newVertices.Min()}..{newVertices.Max()}.");
        for(int i=0;i<100;i++)worldRenderer.Draw(focus,camera,false);
        long allocated=GC.GetAllocatedBytesForCurrentThread();var cullTime=Stopwatch.StartNew();for(int i=0;i<1000;i++)worldRenderer.Draw(focus,camera,false);
        report.AppendLine($"Optimized warmed CPU cull/group (no graphics submit): {cullTime.Elapsed.TotalMilliseconds/1000:F4} ms/frame; {(GC.GetAllocatedBytesForCurrentThread()-allocated)/1000} bytes/frame.");
        collisionPool.Update(focus,0);for(int i=1;i<=8;i++)collisionPool.Update(focus,i*.02);int scans=collisionPool.ScanCount;
        var poolTime=Stopwatch.StartNew();for(int i=0;i<1000;i++)collisionPool.Update(focus,1+i*.2);
        report.AppendLine($"Stationary collision update: {poolTime.Elapsed.TotalMilliseconds/1000:F4} ms/frame; {collisionPool.ScanCount-scans} repeat scans, last scan inspected {collisionPool.LastScanEntries}/{worldRenderer.ResidentInstances} rock records, {collisionPool.ActiveCount} active bodies.");
        UnityEngine.Object.DestroyImmediate(chunkRoot);
        if(benchmark)
        {
            Benchmark(report,worldRenderer,renderSets,chunkBounds,camera,focus,size*g.WorldScale*config.Content.FoliageFrustumPaddingChunks);
            return;
        }
        worldRenderer.Dispose();UnityEngine.Object.DestroyImmediate(cameraObject);
        File.WriteAllText(".utmp/rocks/performance-investigation.txt",report.ToString());Debug.Log("ROCK_PERFORMANCE_REPORT\n"+report);
    }
    private static void Benchmark(StringBuilder report,RockWorldRenderer renderer,List<RockRenderSet> sets,List<Bounds> bounds,Camera camera,Vector3 focus,float padding)
    {
        var target=new RenderTexture(1280,720,24);target.Create();
        var previousPipeline=GraphicsSettings.defaultRenderPipeline;var previousQuality=QualitySettings.renderPipeline;
        bool previousAsync=ShaderUtil.allowAsyncCompilation;
        int mode=0;long submitTicks=0;int stage=0,frame=0;
        int[] stages={0,1,2,2,1,0};var times=new List<double>();
        var planes=new Plane[6];camera.cullingMask=1<<GameplayLayers.WorldSolid;camera.transform.rotation=Quaternion.Euler(0,0,0);
        Action<ScriptableRenderContext,Camera> submit=(_,c)=>
        {
            if(c!=camera)return;long start=Stopwatch.GetTimestamp();
            if(mode==1)
            {
                GeometryUtility.CalculateFrustumPlanes(camera,planes);
                for(int i=0;i<sets.Count;i++)
                {
                    bool visible=GeometryUtility.TestPlanesAABB(planes,bounds[i]);var bound=bounds[i];bound.Expand(new Vector3(padding*2,0,padding*2));
                    bool shadows=GeometryUtility.TestPlanesAABB(planes,bound);sets[i].Draw(visible,shadows);
                }
            }
            else if(mode==2)renderer.Draw(focus,camera);
            submitTicks+=Stopwatch.GetTimestamp()-start;
        };
        EditorApplication.CallbackFunction update=null;
        void Cleanup()
        {
            EditorApplication.update-=update;RenderPipelineManager.beginCameraRendering-=submit;
            GraphicsSettings.defaultRenderPipeline=previousPipeline;QualitySettings.renderPipeline=previousQuality;ShaderUtil.allowAsyncCompilation=previousAsync;
            target.Release();UnityEngine.Object.DestroyImmediate(target);renderer.Dispose();UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
        update=()=>
        {
            try
            {
                // Exactly one request per actual editor frame: Graphics.DrawMeshInstanced submissions
                // persist until the frame ends, so a tight render loop produces invalid accumulating draws.
                var timer=Stopwatch.StartNew();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                if(frame++>=24)times.Add(timer.Elapsed.TotalMilliseconds);
                else submitTicks=0;
                if(frame<104)return;
                times.Sort();string line=$"1280x720 warmed rock-only camera render, {(mode==0?"no rocks":mode==1?"legacy per-chunk submission":"optimized global submission")}: median {times[40]:F3} ms, p95 {times[76]:F3} ms; managed/native rock submission mean {(submitTicks*1000.0/Stopwatch.Frequency/80):F3} ms. CPU wall timings, not GPU frame timestamps or live-game FPS.";
                report.AppendLine(line);Debug.Log("ROCK_BENCHMARK_STAGE: "+line);
                if(mode==2)
                {
                    var previousTarget=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
                    image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(".utmp/rocks/performance-frame.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=previousTarget;
                }
                if(++stage<stages.Length){mode=stages[stage];frame=0;times.Clear();submitTicks=0;return;}
                if(ShaderUtil.ShaderHasError(Resources.Load<Material>("Rocks/Stone_Snow").shader))throw new Exception("Rock GPU benchmark shader failed");
                File.WriteAllText(".utmp/rocks/performance-investigation.txt",report.ToString());Debug.Log("ROCK_PERFORMANCE_REPORT\n"+report);
                Cleanup();EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);Cleanup();EditorApplication.Exit(1);}
        };
        var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;ShaderUtil.allowAsyncCompilation=false;
        RenderPipelineManager.beginCameraRendering+=submit;EditorApplication.update+=update;
    }
    public static void RunBatch(){try{Run();if(!benchmark)EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    public static void BenchmarkBatch(){benchmark=true;RunBatch();}
}
