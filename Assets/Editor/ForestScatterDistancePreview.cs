using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Actual mesh/material comparison, isolated from the gameplay scene.
public static class ForestScatterDistancePreview
{
    public const string Output="ArtReferences/ForestFloor/Fern_LOD_Comparison.png";
    [MenuItem("Tools/Foliage/Render Fern LOD Comparison")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Preview requires Edit mode.");
        var scene=EditorSceneManager.NewPreviewScene();
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var ambientMode=RenderSettings.ambientMode;var ambient=RenderSettings.ambientLight;
        bool fog=RenderSettings.fog,async=ShaderUtil.allowAsyncCompilation;
        Light previousSun=RenderSettings.sun;Material ground=null;Texture2D pixels=null;RenderTexture target=null;
        RenderTexture previous=RenderTexture.active;
        GameObject Make(string name){var obj=new GameObject(name);SceneManager.MoveGameObjectToScene(obj,scene);return obj;}
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;ShaderUtil.allowAsyncCompilation=false;
            RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.33f,.38f,.43f);
            var paths=new[]{ForestFernPrefabBuilder.NearPath,ForestFernPrefabBuilder.FarPath,ForestFernPrefabBuilder.CoarsePath};
            for(int lod=0;lod<3;lod++)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(paths[lod]);
                if(prefab==null)throw new InvalidOperationException("Missing fern LOD: "+paths[lod]);
                var plant=Make("Fern LOD "+lod);plant.AddComponent<MeshFilter>().sharedMesh=prefab.GetComponent<MeshFilter>().sharedMesh;
                var renderer=plant.AddComponent<MeshRenderer>();renderer.sharedMaterial=prefab.GetComponent<MeshRenderer>().sharedMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                var properties=new MaterialPropertyBlock();properties.SetFloat("_FadeStart",100);properties.SetFloat("_FadeEnd",110);
                properties.SetVector("_LeafInstanceTint",Vector4.one);renderer.SetPropertyBlock(properties);
                plant.transform.position=new Vector3((lod-1)*2.4f,0,0);plant.transform.localScale=Vector3.one*2;
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.position=Vector3.down*.005f;floor.transform.localScale=new Vector3(1.3f,1,1);
            ground=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            ground.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Ground/Forest/leaf-fall1-albedo.png"));
            ground.SetTextureScale("_BaseMap",new Vector2(5,4));ground.SetFloat("_Smoothness",0);
            floor.GetComponent<Renderer>().sharedMaterial=ground;
            var sun=Make("Fern preview daylight").AddComponent<Light>();sun.type=LightType.Directional;
            sun.intensity=.95f;sun.color=new Color(1,.97f,.9f);sun.transform.rotation=Quaternion.Euler(52,-32,0);RenderSettings.sun=sun;
            var camera=Make("Fern LOD camera").AddComponent<Camera>();camera.enabled=false;camera.cameraType=CameraType.Preview;camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.05f);
            camera.transform.position=new Vector3(0,3.1f,-7.3f);camera.transform.LookAt(new Vector3(0,.35f,0));
            camera.fieldOfView=40;camera.aspect=1400f/650;camera.allowHDR=false;
            target=new RenderTexture(1400,650,24,RenderTextureFormat.ARGB32);target.Create();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;pixels=new Texture2D(1400,650,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,1400,650),0,0);pixels.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllBytes(Output,pixels.EncodeToPNG());
            Debug.Log("FERN LOD PREVIEW: "+Output+"; left to right LOD0/1/2, same 2x scale, actual materials and Linear URP lighting.");
        }
        finally
        {
            RenderTexture.active=previous;if(target!=null){target.Release();Object.DestroyImmediate(target);}
            if(pixels!=null)Object.DestroyImmediate(pixels);if(ground!=null)Object.DestroyImmediate(ground);
            EditorSceneManager.ClosePreviewScene(scene);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;
            RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambient;RenderSettings.fog=fog;RenderSettings.sun=previousSun;
            ShaderUtil.allowAsyncCompilation=async;
        }
    }
    public static void RunBatch()
    {
        try
        {
            ForestFernPrefabBuilder.BuildCoarse();ForestScatterGpuValidation.Run();ForestFloorRedesignValidation.Run();ForestUnderstoryValidation.Run();
            Run();EditorApplication.Exit(0);
        }
        catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
}
