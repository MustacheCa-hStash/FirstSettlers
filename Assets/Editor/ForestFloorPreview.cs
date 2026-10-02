using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// User-requested actual URP asset renders. Isolated authoring scene; no world scene or Play mode.
public static class ForestFloorPreview
{
    public const string Output = "ArtReferences/ForestFloor";
    private const int Seed = 145678, Size = 128;
    private const float Scale = .3f;
    private static void Set(ChunkRecord record,string name,object value) =>
        typeof(ChunkRecord).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(record,value);
    public static ChunkRecord Fixture(bool opening = true, GroundCoverType? coverOverride = null)
    {
        var record = new ChunkRecord(new ChunkCoord(0,0));
        int n = Size+3;
        var heights = new float[n,n]; var slopes = new float[n,n]; var biomes = new BiomeType[n,n];
        var moisture = new float[n,n];
        var surfaces = new SurfaceType[n,n]; var ground = new GroundCoverType[n,n];
        var plan = new WorldFeaturePlan(n,n); plan.ForestStructure.EnsureFloorEcologyMap(n,n);
        for(int x=0;x<n;x++) for(int z=0;z<n;z++)
        {
            float clearing = opening ? Mathf.SmoothStep(0,1,Mathf.InverseLerp(.35f,.8f,Mathf.PerlinNoise(x*.022f,z*.022f))) * .75f : 0;
            biomes[x,z] = BiomeType.Forest; surfaces[x,z] = SurfaceType.Grass;
            moisture[x,z]=.62f;
            plan.ForestStructure.CanopyIntentMap[x,z]=.65f;plan.ForestStructure.ClearingMap[x,z]=clearing;
            ground[x,z] = coverOverride ?? (clearing > .35f ? GroundCoverType.DarkGrass : GroundCoverType.LeafLitter);
            plan.ForestStructure.FloorEcologyMap[x,z] = ForestFloorPolicy.Evaluate(new float2(x-1,z-1),Seed,.62f,0,0,.65f,clearing);
        }
        Set(record,"heightMap",heights); Set(record,"slopeMap",slopes); Set(record,"biomeMap",biomes);
        Set(record,"moistureMap",moisture);
        Set(record,"surfaceTypeMap",surfaces); Set(record,"groundCoverMap",ground); Set(record,"worldFeaturePlan",plan);
        Set(record,"nativeTerrainData",new ChunkRecord.NativeTerrainData(heights,slopes,biomes,surfaces,null,ground,null,plan.ForestStructure.FloorEcologyMap));
        return record;
    }
    public static CloverSettings CloverOptions() => new CloverSettings { patchCellSize=16,patchSpawnChance=.3f,
        minClumpsPerPatch=2,maxClumpsPerPatch=5,patchRadiusRange=new Vector2(.8f,2.1f),
        uniformScaleRange=new Vector2(1.2f,1.4f),grassDensityInsidePatch=0,grassInfluenceRadius=.7f,grassFadePadding=.7f };
    [MenuItem("Tools/Foliage/Render Forest Floor Previews")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Previews require Edit mode.");
        Directory.CreateDirectory(Output);
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
        bool oldAsync = ShaderUtil.allowAsyncCompilation;
        AmbientMode oldAmbientMode=RenderSettings.ambientMode; Color oldAmbient=RenderSettings.ambientLight;
        bool oldFog=RenderSettings.fog; Light oldSun=RenderSettings.sun;
        Scene scene = EditorSceneManager.NewPreviewScene();
        var cleanup = new List<Object>();
        using var record = Fixture();
        try
        {
            GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            QualitySettings.renderPipeline = GraphicsSettings.defaultRenderPipeline;
            ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.33f,.38f,.43f);
            RenderSettings.fog=false;
            Material ground = new Material(Shader.Find("Universal Render Pipeline/Lit")); cleanup.Add(ground);
            ground.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Ground/Forest/leaf-fall1-albedo.png"));
            ground.SetTextureScale("_BaseMap",Vector2.one * (Size*Scale*.35f)); ground.SetColor("_BaseColor",Color.white); ground.SetFloat("_Smoothness",0);
            GameObject plane=GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager.MoveGameObjectToScene(plane,scene);
            plane.transform.localScale=Vector3.one * (Size*Scale/10); plane.transform.position=Vector3.down*.004f;
            plane.GetComponent<Renderer>().sharedMaterial=ground;
            Light sun=Make("Forest preview sun",scene).AddComponent<Light>(); sun.type=LightType.Directional;
            sun.intensity=.95f; sun.color=new Color(1,.97f,.90f); sun.shadows=LightShadows.Soft;
            sun.transform.rotation=Quaternion.Euler(52,-32,0);
            RenderSettings.sun=sun;
            Camera camera=Make("Forest asset camera",scene).AddComponent<Camera>(); camera.enabled=false;
            camera.scene=scene; camera.cameraType=CameraType.Preview; camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.18f,.22f,.24f); camera.nearClipPlane=.015f; camera.farClipPlane=160; camera.fieldOfView=46;
            var cameraData=camera.GetUniversalAdditionalCameraData(); cameraData.renderShadows=true; cameraData.renderPostProcessing=false;
            GameObject trees=Make("Forest backdrop",scene);
            foreach(Vector3 p in new[]{new Vector3(-5,0,6),new Vector3(5,0,9),new Vector3(-9,0,12),new Vector3(10,0,13),new Vector3(0,0,15)})
            {
                GameObject tree=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Spruce_LOD0_v01.prefab"),scene);
                tree.transform.position=p; tree.transform.localScale=Vector3.one*.75f;
                tree.transform.SetParent(trees.transform,true);
            }
            CloverSettings clover=CloverOptions();
            FoliageGenerator.GenerateCloverForChunk(record,clover,3,Seed,Size,Scale,10);
            var grass=new GrassSettings { cellsPerAxis=144,subChunksPerChunk=10,billboardRingRadius=3,uniformScaleRange=new Vector2(.9f,1.3f) };
            FoliageGenerator.GenerateGrassForChunk(record,grass,clover,null,Seed,Size,Scale,10);
            var leaves=new LeafClusterGeneration(record,new LeafClusterSettings(),Seed,Size,Scale,10,.65f);
            while(!leaves.Complete) leaves.Step(256);
            Vector3 focus=Vector3.zero;
            float closest=float.MaxValue;
            foreach(var c in record.FoliageData.cloverInstances)
                if(c.localPosition.sqrMagnitude<closest) {closest=c.localPosition.sqrMagnitude; focus=c.localPosition;}
            Debug.Log($"FOREST PREVIEW GENERATION: {record.FoliageData.GetTotalNearGrassInstanceCount()} grass, {leaves.Instances.Count} leaf scatters, {record.FoliageData.cloverInstances.Count} opening clover clumps. Range {GrassStreamingPolicy.RenderDistance(grass,Size,Scale)}.");
            for(int pass=0;pass<2;pass++)
            {
                if(pass==1) { ForestGrassPrefabBuilder.Build(); LeafClusterPrefabBuilder.Build(); }
                GameObject group=Make("Forest floor asset pass",scene);
                GameObject grassPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(ForestGrassPrefabBuilder.NearPath);
                GameObject leafPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(LeafClusterPrefabBuilder.PrefabPath);
                Material leafMat=new Material(leafPrefab.GetComponent<MeshRenderer>().sharedMaterial); cleanup.Add(leafMat);
                leafMat.SetFloat("_UseAtlasColor",pass==0 ? 1 : 0); leafMat.SetFloat("_FadeStart",102); leafMat.SetFloat("_FadeEnd",115.2f);
                for(int x=0;x<10;x++) for(int z=0;z<10;z++) foreach(var tuft in record.FoliageData.nearGrassInstancesBySubChunk[x,z])
                    Instance(grassPrefab,tuft.localPosition,tuft.localRotation,tuft.localScale,scene,group,null);
                foreach(var leaf in leaves.Instances)
                {
                    var obj=Instance(leafPrefab,leaf.position-new Vector3(Size*Scale*.5f,0,Size*Scale*.5f),leaf.rotation,Vector3.one*leaf.scale,scene,group,leafMat);
                    var block=new MaterialPropertyBlock(); block.SetVector("_LeafInstanceTint",leaf.tint);
                    block.SetVector("_LeafScatterParams",LeafClusterSystem.ScatterParams(leaf.rank));
                    obj.GetComponent<MeshRenderer>().SetPropertyBlock(block);
                }
                if(pass==1) foreach(var c in record.FoliageData.cloverInstances)
                {
                    string[] names={"CloverClump_v02_A","CloverClump_v02_B_Flowering","CloverClump_v02_Edge"};
                    Instance(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+names[c.prefabIndex]+".prefab"),c.localPosition,c.localRotation,c.localScale,scene,group,null);
                }
                trees.SetActive(false);
                Pose(camera,focus+new Vector3(1.1f,1.0f,-2.5f),focus+new Vector3(0,.1f,.4f));
                Capture(camera,Output+(pass==0 ? "/Before_Close.png" : "/After_Close.png"));
                trees.SetActive(true);
                Pose(camera,new Vector3(1,1.2f,-8),new Vector3(0,.2f,5));
                Capture(camera,Output+(pass==0 ? "/Before_Forest.png" : "/After_Forest.png"));
                Object.DestroyImmediate(group);
            }
            GameObject scatter=AssetDatabase.LoadAssetAtPath<GameObject>(LeafClusterPrefabBuilder.PrefabPath);
            trees.SetActive(false);
            GameObject single=Instance(scatter,Vector3.zero,Quaternion.identity,Vector3.one,scene,null,null);
            Pose(camera,new Vector3(.7f,.8f,-.9f),Vector3.zero);
            Capture(camera,Output+"/LeafScatter_Detail.png"); Object.DestroyImmediate(single);
            var composition=Make("Combined forest assets",scene);
            Instance(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CloverClump_v02_A.prefab"),
                new Vector3(.5f,0,.35f),Quaternion.Euler(0,20,0),Vector3.one*1.2f,scene,composition,null);
            Instance(scatter,new Vector3(-.25f,.006f,-.45f),Quaternion.Euler(0,25,0),Vector3.one,scene,composition,null);
            GameObject tuftPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(ForestGrassPrefabBuilder.NearPath);
            foreach(var p in new[]{new Vector3(-.8f,0,.05f),new Vector3(.75f,0,-.65f),new Vector3(-.55f,0,-.8f)})
                Instance(tuftPrefab,p,Quaternion.Euler(0,p.x*120,0),Vector3.one*1.1f,scene,composition,null);
            Pose(camera,new Vector3(1.15f,1.2f,-2.4f),new Vector3(.2f,.08f,.05f));
            Capture(camera,Output+"/Forest_Foliage_Close.png"); Object.DestroyImmediate(composition);
            Debug.Log("FOREST PREVIEWS PASS: actual URP prefab/shader renders on the forest ground texture, before/after close and forest context, leaf detail; isolated scene, no Play mode.");
        }
        finally
        {
            foreach(var obj in cleanup) Object.DestroyImmediate(obj);
            EditorSceneManager.ClosePreviewScene(scene); ShaderUtil.allowAsyncCompilation=oldAsync;
            RenderSettings.ambientMode=oldAmbientMode; RenderSettings.ambientLight=oldAmbient;
            RenderSettings.fog=oldFog; RenderSettings.sun=oldSun;
            QualitySettings.renderPipeline=oldQuality; GraphicsSettings.defaultRenderPipeline=oldPipeline;
        }
    }
    private static GameObject Make(string name,Scene scene)
    {
        var obj=new GameObject(name){hideFlags=HideFlags.HideAndDontSave}; SceneManager.MoveGameObjectToScene(obj,scene); return obj;
    }
    private static GameObject Instance(GameObject prefab,Vector3 p,Quaternion q,Vector3 scale,Scene scene,GameObject parent,Material material)
    {
        GameObject obj=Make(prefab.name,scene); obj.transform.SetPositionAndRotation(p,q); obj.transform.localScale=scale;
        if(parent!=null) obj.transform.SetParent(parent.transform,true);
        obj.AddComponent<MeshFilter>().sharedMesh=prefab.GetComponent<MeshFilter>().sharedMesh;
        var renderer=obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material!=null ? material : prefab.GetComponent<MeshRenderer>().sharedMaterial;
        renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=true; return obj;
    }
    private static void Pose(Camera camera,Vector3 p,Vector3 target) {camera.transform.position=p; camera.transform.LookAt(target);}
    private static void Capture(Camera camera,string path)
    {
        // Match the clover reference's warm-up before reading an offscreen frame.
        RenderTexture warmup=RenderTexture.GetTemporary(2800,1800,24,RenderTextureFormat.ARGB32);
        try
        {
            for(int frame=0;frame<2;frame++) RenderPipeline.SubmitRenderRequest(camera,
                new UniversalRenderPipeline.SingleCameraRequest {destination=warmup});
            GrassTuftBroadPreview.Capture(camera,path);
        }
        finally {RenderTexture.ReleaseTemporary(warmup);}
    }
    public static void RunBatch()
    {
        try { Run(); ForestFloorRedesignValidation.Run(); EditorApplication.Exit(0); }
        catch(Exception e) {Debug.LogException(e); EditorApplication.Exit(1);}
    }
}
