using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Offscreen authoring preview only. Uses the actual terrain shader, floor policy and foliage assets.
public static class ForestMossPreview
{
    [MenuItem("Tools/Foliage/Render Forest Moss Preview")]
    public static void Run()
    {
        Render(false);
    }
    public static void RunRockEdge() => Render(false,true);
    private static void Render(bool colorComparison,bool rockEdge=false)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Preview requires Edit mode.");
        if(QualitySettings.activeColorSpace != ColorSpace.Linear)
            throw new InvalidOperationException("FirstSettlers moss previews require Linear color space, matching the game project.");
        const int size=128; const float scale=.3f;
        var pipeline=GraphicsSettings.defaultRenderPipeline; var quality=QualitySettings.renderPipeline;
        bool async=ShaderUtil.allowAsyncCompilation,fog=RenderSettings.fog;
        var ambientMode=RenderSettings.ambientMode; var ambient=RenderSettings.ambientLight; var oldSun=RenderSettings.sun;
        var scene=EditorSceneManager.NewPreviewScene(); var cleanup=new List<Object>();
        using var record=ForestFloorPreview.Fixture(false);
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;ShaderUtil.allowAsyncCompilation=false;
            RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.33f,.38f,.43f);
            var fields=record.WorldFeaturePlan.ForestStructure;
            for(int x=0;x<size+3;x++) for(int z=0;z<size+3;z++)
            {
                fields.FloorEcologyMap[x,z]=ForestFloorPolicy.Evaluate(new Unity.Mathematics.float2(x-1,z-1),1937,.85f,0,0,.85f,0);
                record.MoistureMap[x,z]=.85f;fields.CanopyIntentMap[x,z]=.85f;
                if(rockEdge)
                {
                    var ecology=fields.FloorEcologyMap[x,z];ecology.z=0;ecology.x=0;fields.FloorEcologyMap[x,z]=ecology;
                    if(x>=60 && x<=70) record.SurfaceTypeMap[x,z]=SurfaceType.Rock;
                }
            }
            record.NativeData.Dispose();
            typeof(ChunkRecord).GetField("nativeTerrainData",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .SetValue(record,new ChunkRecord.NativeTerrainData(record.HeightMap,record.SlopeMap,record.BiomeMap,
                    record.SurfaceTypeMap,null,record.GroundCoverMap,null,fields.FloorEcologyMap));
            var maps=TerrainControlMapBuilder.BuildRaw(record.SurfaceTypeMap,record.GroundCoverMap,forestStructure:fields,biomeMap:record.BiomeMap);
            var material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat"));cleanup.Add(material);
            for(int map=0;map<3;map++)
            {
                var texture=new Texture2D(maps.Width,maps.Height,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                texture.SetPixels32(maps.Maps[map]);texture.Apply();cleanup.Add(texture);material.SetTexture("_ControlMap"+map,texture);
            }
            float half=size*scale*.5f;
            var mesh=new Mesh {vertices=new[]{new Vector3(-half,0,-half),new Vector3(half,0,-half),new Vector3(half,0,half),new Vector3(-half,0,half)},
                uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[]{0,2,1,0,3,2}};
            mesh.RecalculateNormals();mesh.RecalculateBounds();cleanup.Add(mesh);
            var floor=Make("Moss terrain",scene);floor.AddComponent<MeshFilter>().sharedMesh=mesh;floor.AddComponent<MeshRenderer>().sharedMaterial=material;
            var grass=new GrassSettings {cellsPerAxis=144,subChunksPerChunk=10};
            FoliageGenerator.GenerateGrassForChunk(record,grass,null,null,1937,size,scale,10);
            var tuft=AssetDatabase.LoadAssetAtPath<GameObject>(ForestGrassPrefabBuilder.NearPath);
            for(int x=0;x<10;x++) for(int z=0;z<10;z++) foreach(var i in record.FoliageData.nearGrassInstancesBySubChunk[x,z])
                Instance(tuft,i.localPosition,i.localRotation,i.localScale,scene);
            var leaves=new LeafClusterGeneration(record,new LeafClusterSettings(),1937,size,scale,10,.65f);
            while(!leaves.Complete) leaves.Step(256);
            var scatter=AssetDatabase.LoadAssetAtPath<GameObject>(LeafClusterPrefabBuilder.PrefabPath);
            foreach(var i in leaves.Instances) Instance(scatter,i.position-new Vector3(half,0,half),i.rotation,Vector3.one*i.scale,scene,LeafClusterSystem.ScatterParams(i.rank),i.tint);
            var fernGeneration=new LeafClusterGeneration(record,new FernSettings(),1937,size,scale,10,.7f);
            while(!fernGeneration.Complete) fernGeneration.Step(256);
            var fernPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(ForestFernPrefabBuilder.NearPath);
            foreach(var i in fernGeneration.Instances) Instance(fernPrefab,i.position-new Vector3(half,0,half),i.rotation,Vector3.one*i.scale,scene,null,i.tint);
            if(!rockEdge)
            {
                var treePrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Spruce_LOD0_v01.prefab");
                foreach(var p in new[]{new Vector3(-5,0,6),new Vector3(5,0,9),new Vector3(-9,0,12),new Vector3(10,0,13),new Vector3(0,0,15)})
                {
                    var tree=(GameObject)PrefabUtility.InstantiatePrefab(treePrefab,scene);
                    tree.transform.position=p;tree.transform.localScale=Vector3.one*.75f;
                }
            }
            var sun=Make("Moss preview daylight",scene).AddComponent<Light>();sun.type=LightType.Directional;
            sun.intensity=.95f;sun.color=new Color(1,.97f,.9f);sun.shadows=LightShadows.Soft;
            sun.transform.rotation=Quaternion.Euler(52,-32,0);RenderSettings.sun=sun;
            var camera=Make("Moss preview camera",scene).AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cameraType=CameraType.Preview;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.22f,.24f);camera.fieldOfView=46;
            camera.nearClipPlane=.02f;camera.farClipPlane=100;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            camera.transform.position=new Vector3(1,3,-7);camera.transform.LookAt(new Vector3(0,0,3));
            Directory.CreateDirectory("ArtReferences/ForestFloor");
            var warmup=RenderTexture.GetTemporary(2800,1800,24,RenderTextureFormat.ARGB32);
            try {for(int i=0;i<2;i++) RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=warmup});}
            finally {RenderTexture.ReleaseTemporary(warmup);}
            if(colorComparison)
            {
                // Reproduce the former RGB literal in the same Linear lighting setup.
                material.SetColor("_MossFillColor",new Color(.12f,.18f,.035f).gamma);
                GrassTuftBroadPreview.Capture(camera,"ArtReferences/ForestFloor/Moss_Linear_Before.png");
                material.SetColor("_MossFillColor",new Color(.12f,.18f,.035f));
            }
            GrassTuftBroadPreview.Capture(camera,"ArtReferences/ForestFloor/"+(rockEdge?"Forest_Rock_Edge.png":"Moss_Floor.png"));
            if(!rockEdge) File.Copy("ArtReferences/ForestFloor/Moss_Floor.png","ArtReferences/ForestFloor/Forest_Understory.png",true);
            if(!rockEdge && fernGeneration.Instances.Count>0)
            {
                var closest=fernGeneration.Instances[0];float closestDistance=float.MaxValue;
                foreach(var i in fernGeneration.Instances)
                {
                    float d=(i.position-new Vector3(half,0,half)).sqrMagnitude;
                    if(d<closestDistance) {closest=i;closestDistance=d;}
                }
                Vector3 focus=closest.position-new Vector3(half,0,half);
                camera.transform.position=focus+new Vector3(1.4f,1.5f,-2.8f);
                camera.transform.LookAt(focus+Vector3.up*.34f);
                GrassTuftBroadPreview.Capture(camera,"ArtReferences/ForestFloor/Fern_Floor_Detail.png");
            }
            Debug.Log($"UNDERSTORY PREVIEW PASS: rock-edge fixture={rockEdge}; actual terrain material/control maps; {record.FoliageData.GetTotalNearGrassInstanceCount()} grass, {leaves.Instances.Count} leaf scatters, {fernGeneration.Instances.Count} ferns after moss exclusions. No live scene or Play mode.");
        }
        finally
        {
            foreach(var o in cleanup) Object.DestroyImmediate(o);EditorSceneManager.ClosePreviewScene(scene);
            ShaderUtil.allowAsyncCompilation=async;RenderSettings.fog=fog;RenderSettings.ambientMode=ambientMode;
            RenderSettings.ambientLight=ambient;RenderSettings.sun=oldSun;QualitySettings.renderPipeline=quality;GraphicsSettings.defaultRenderPipeline=pipeline;
        }
    }
    private static GameObject Make(string name,Scene scene)
    {var obj=new GameObject(name){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(obj,scene);return obj;}
    private static void Instance(GameObject prefab,Vector3 p,Quaternion q,Vector3 scale,Scene scene,Vector4? scatter=null,Vector4? tint=null)
    {
        var obj=Make(prefab.name,scene);obj.transform.SetPositionAndRotation(p,q);obj.transform.localScale=scale;
        obj.AddComponent<MeshFilter>().sharedMesh=prefab.GetComponent<MeshFilter>().sharedMesh;
        var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=prefab.GetComponent<MeshRenderer>().sharedMaterial;
        renderer.shadowCastingMode=ShadowCastingMode.Off;
        var properties=new MaterialPropertyBlock();properties.SetFloat("_FadeStart",55);properties.SetFloat("_FadeEnd",65);
        if(scatter.HasValue) properties.SetVector("_LeafScatterParams",scatter.Value);
        if(tint.HasValue) properties.SetVector("_LeafInstanceTint",tint.Value);
        renderer.SetPropertyBlock(properties);
    }
    public static void RunBatch()
    {
        try {ForestMossTreeValidation.Run();Run();EditorApplication.Exit(0);}
        catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void RunColorBatch()
    {
        try {Render(true);EditorApplication.Exit(0);}
        catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
