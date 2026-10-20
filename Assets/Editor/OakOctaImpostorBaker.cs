using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class OakOctaImpostorBaker
{
    public const string SourcePrefab = "Assets/Prefabs/Oak_LOD0_v03.prefab";
    public const string AtlasFolder = "Assets/Textures/Trees/Impostors/Oak";
    public const string MaterialPath = "Assets/Materials/M_Trees/Oak/Oak_OctaImpostor_M.mat";
    public const string MeshPath = "Assets/Models/Trees/Oak_OctaImpostor_Runtime.mesh";
    public const string PrefabPath = "Assets/Prefabs/Oak_OctaImpostor_Runtime.prefab";
    public const string LeafPath = "Assets/Materials/M_Trees/Oak/M_Oak_Leaf.mat";
    public const string BarkPath = "Assets/Materials/M_Trees/Oak/M_Oak_Bark.mat";

    [Serializable] public sealed class Metadata
    {
        public int framesPerAxis, tileResolution, paddingPixels;
        public float[] captureCenterLocal;
        public float captureRadius;
    }

    [MenuItem("Tools/Impostors/Oak/Bake and Assign Oak Billboards")]
    public static void BakeAndAssign()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
        foreach (var manager in Object.FindObjectsByType<WorldManager>(FindObjectsSortMode.None))
        {
            var reference = new SerializedObject(manager).FindProperty("treeSettings.oakTreePrefab");
            if (reference?.objectReferenceValue is GameObject assigned) { source = assigned; break; }
        }
        Bake(source);
        var proxy = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        foreach (var manager in Object.FindObjectsByType<WorldManager>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(manager, "Assign baked oak billboards");
            var settings = new SerializedObject(manager);
            settings.FindProperty("treeSettings.oakTreeBillboardPrefab").objectReferenceValue = proxy;
            settings.FindProperty("treeSettings.grasslandOakTreeBillboardPrefab").objectReferenceValue = proxy;
            settings.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }

    public static void BakeBatch()
    {
        try
        {
            PrepareBatchHost();
            Bake(AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab));
            OakImpostorValidation.Run();
            Debug.Log("OAK BAKE AND VALIDATION PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    public static void RebuildAndValidateBatch()
    {
        try
        {
            PrepareBatchHost(); CreateOrUpdate(); OakImpostorValidation.Run();
            Debug.Log("OAK BAKE AND VALIDATION PASSED"); EditorApplication.Exit(0);
        }
        catch(Exception ex) {Debug.LogException(ex); EditorApplication.Exit(1);}
    }

    public static void Bake(GameObject source)
    {
        if (!source) throw new InvalidOperationException("Missing assigned oak source prefab.");
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        rendererData.renderingMode = RenderingMode.ForwardPlus;
        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        try
        {
            SceneManager.SetActiveScene(scene);
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.fog = false; RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            tree.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); tree.transform.localScale = Vector3.one;
            foreach (var group in tree.GetComponentsInChildren<LODGroup>()) group.ForceLOD(0);
            foreach (var renderer in tree.GetComponentsInChildren<MeshRenderer>())
            {
                var data = new SerializedObject(renderer);
                var lod = data.FindProperty("m_ForceMeshLod");
                if (lod != null) { lod.intValue = 0; data.ApplyModifiedPropertiesWithoutUndo(); }
            }
            var camera = new GameObject("Oak semantic capture camera").AddComponent<Camera>();
            camera.enabled = false; camera.scene = scene; camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.allowHDR = false; camera.allowMSAA = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var setup = new GameObject("Oak capture setup").AddComponent<TreeImpostorCaptureSetup>();
            var serialized = new SerializedObject(setup);
            serialized.FindProperty("treeRoot").objectReferenceValue = tree.transform;
            serialized.FindProperty("captureCamera").objectReferenceValue = camera;
            serialized.FindProperty("leafMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(LeafPath);
            serialized.FindProperty("barkMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(BarkPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Require(setup.RecalculateBounds(out var bounds), bounds); Debug.Log("OAK CAPTURE: " + bounds);
            Require(setup.ConfigureCameraFromCurrentDirection(out var cameraMessage), cameraMessage);
            EnsureFolders();
            if (!AssetDatabase.LoadAssetAtPath<Material>(MaterialPath))
                AssetDatabase.CreateAsset(new Material(Shader.Find("Custom/SpruceOctaImpostor")) { name="Oak_OctaImpostor_M", enableInstancing=true }, MaterialPath);
            Require(TreeImpostorAtlasCaptureWindow.CaptureOak(setup), "Oak semantic capture failed.");
            Debug.Log("OAK BAKE: semantic capture complete; starting ambient visibility.");
            Require(TreeImpostorAmbientAtlasBakerWindow.BakeOak(setup), "Oak ambient bake failed.");
            CreateOrUpdate();
            // Retain bake inputs and settings as provenance alongside the generated atlases.
            File.WriteAllText(AtlasFolder + "/Oak_Octa_Source.json", JsonUtility.ToJson(new BakeSource {
                prefab=AssetDatabase.GetAssetPath(source), prefabGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)),
                dependencyHash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)).ToString(),
                supersample=2, voxelResolution=128, ambientTileResolution=128, aoRays=8, skyRays=12, raySteps=16,
                coverageExpansion=0, colorBleedPixels=1,
                albedoEncoding="sRGB RGB; linear coverage alpha; leaf value stored at half range" }, true));
            AssetDatabase.Refresh(); AssetDatabase.SaveAssets();
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = previousAsync;
            QualitySettings.renderPipeline = previousQuality; GraphicsSettings.defaultRenderPipeline = previousPipeline;
            if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene);
            EditorSceneManager.CloseScene(scene, true);
            Object.DestroyImmediate(pipeline); Object.DestroyImmediate(rendererData);
            EditorUtility.ClearProgressBar();
        }
    }

    [Serializable] sealed class BakeSource
    {
        public string prefab, prefabGuid, dependencyHash, albedoEncoding;
        public int supersample, voxelResolution, ambientTileResolution, aoRays, skyRays, raySteps;
        public int coverageExpansion, colorBleedPixels;
    }

    public static void PrepareBatchHost()
    {
        // Unity refuses additive editor scenes beside an untitled unsaved scene.
        // Batch execution uses this saved, empty host inside the isolated project.
        var host=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        Directory.CreateDirectory("Assets/OakBakeValidation");
        EditorSceneManager.SaveScene(host,"Assets/OakBakeValidation/Host.unity");
    }

    [MenuItem("Tools/Impostors/Oak/Create or Update Oak Runtime Proxy")]
    public static void CreateOrUpdate()
    {
        EnsureFolders();
        var meta = JsonUtility.FromJson<Metadata>(File.ReadAllText(AtlasFolder + "/Oak_Octa_Metadata.json"));
        Require(meta.captureRadius > 0 && meta.captureCenterLocal?.Length == 3, "Invalid oak capture bounds metadata.");
        var center = new Vector3(meta.captureCenterLocal[0],meta.captureCenterLocal[1],meta.captureCenterLocal[2]);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material) { material = new Material(Shader.Find("Custom/SpruceOctaImpostor")); AssetDatabase.CreateAsset(material,MaterialPath); }
        material.name="Oak_OctaImpostor_M"; material.enableInstancing=true;
        var maps = new[] { "AlbedoCoverage", "Surface", "Depth", "MaterialId", "Ambient" };
        var properties = new[] { "_AlbedoCoverage", "_SurfaceAtlas", "_DepthAtlas", "_MaterialIdAtlas", "_AmbientAtlas" };
        for (int i=0;i<maps.Length;i++)
        {
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasFolder+"/Oak_Octa_"+maps[i]+".png");
            Require(texture, "Missing oak atlas: "+maps[i]); material.SetTexture(properties[i],texture);
        }
        material.SetVector("_CaptureCenterLS",center); material.SetFloat("_CaptureRadius",meta.captureRadius);
        material.SetFloat("_FramesPerAxis",meta.framesPerAxis); material.SetFloat("_AtlasTileResolution",meta.tileResolution);
        material.SetFloat("_AtlasPadding",meta.paddingPixels); material.SetFloat("_AmbientTileResolution",128);
        material.SetFloat("_DepthParallax",.45f); material.SetFloat("_AOStrength",.55f); material.SetFloat("_SkyStrength",1);
        material.SetFloat("_FoliageBrightness",2); material.SetFloat("_BarkBrightness",1); material.SetFloat("_BarkColorRemap",0);
        material.SetFloat("_Cutoff",.04f); material.SetFloat("_TransmissionStrength",1);
        material.SetFloat("_WindStrength",.035f); material.SetFloat("_WindSpeed",1.15f);
        var leaves=AssetDatabase.LoadAssetAtPath<Material>(LeafPath);
        material.SetColor("_SummerLeafColor",leaves.GetColor("_SummerLeafColor"));
        material.SetColor("_AutumnYellowColor",leaves.GetColor("_AutumnOchreColor"));
        material.SetColor("_AutumnOrangeColor",leaves.GetColor("_AutumnRussetColor"));
        material.SetColor("_AutumnRedColor",leaves.GetColor("_AutumnWineColor"));
        material.SetColor("_LeafTransmissionColor",leaves.GetColor("_BacklightingColor"));
        foreach(var property in new[]{"_SeasonAutumnAmount","_AutumnVariationStrength","_TreeTintStrength"}) material.SetFloat(property,leaves.GetFloat(property));
        material.SetColor("_TreeLeafTint",leaves.GetColor("_TreeLeafTint")); material.SetColor("_LeafSeasonTint",Color.white);
        material.SetFloat("_UseSeasonPalette",1); material.SetFloat("_SeasonPaletteMode",2);
        material.SetFloat("_AmbientFloor",leaves.GetFloat("_AmbientStrength")); material.SetFloat("_LeafLightWrap",leaves.GetFloat("_LightWrap"));
        EditorUtility.SetDirty(material);
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if(!mesh) {mesh=new Mesh {name="Oak_OctaImpostor_Runtime"}; AssetDatabase.CreateAsset(mesh,MeshPath);}
        mesh.Clear(); mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up}; mesh.triangles=new[]{0,1,2,0,2,3};
        mesh.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
        // Keep source bounds for ecological crown area. The shared rendering
        // path expands these for the root-anchored, camera-facing quad.
        mesh.bounds=new Bounds(center,Vector3.one*meta.captureRadius*2); EditorUtility.SetDirty(mesh);
        var root=new GameObject("Oak_OctaImpostor_Runtime");
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=root.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=true;
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
    }

    static void EnsureFolders()
    {
        Directory.CreateDirectory(AtlasFolder); Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
        AssetDatabase.Refresh();
    }
    static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
}
