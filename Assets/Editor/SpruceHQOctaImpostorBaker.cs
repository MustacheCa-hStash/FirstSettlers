using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SpruceHQOctaImpostorBaker
{
    public const string SourcePrefab = "Assets/Prefabs/Spruce_LOD0_v06.prefab";
    public const string AtlasFolder = "Assets/Textures/Trees/Impostors/SpruceHQ";
    public const string MaterialPath = "Assets/Materials/M_Trees/SpruceHQ/SpruceHQ_OctaImpostor_M.mat";
    public const string MeshPath = "Assets/Models/Trees/SpruceHQ_OctaImpostor_Runtime.mesh";
    public const string PrefabPath = "Assets/Prefabs/SpruceHQ_OctaImpostor_Runtime.prefab";
    public const string LeafPath = "Assets/Materials/M_Trees/Spruce/SpruceTreeLeaf_M.mat";
    public const string BarkPath = "Assets/Materials/M_Trees/Spruce/SpruceTreeBark_M.mat";
    public const float CoverageCutoff = .012f;

    [Serializable] public sealed class Metadata
    {
        public int framesPerAxis, tileResolution, paddingPixels;
        public float[] captureCenterLocal;
        public float captureRadius;
    }

    [MenuItem("Tools/Impostors/SpruceHQ/Bake and Assign SpruceHQ Billboards")]
    public static void BakeAndAssign()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
        foreach (var manager in Object.FindObjectsByType<WorldManager>(FindObjectsSortMode.None))
        {
            var reference = new SerializedObject(manager).FindProperty("treeSettings.spruceTreePrefab");
            if (reference?.objectReferenceValue is GameObject assigned) { source = assigned; break; }
        }
        Bake(source);
        ConfigureRuntimeImports();
        var proxy = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        foreach (var manager in Object.FindObjectsByType<WorldManager>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(manager, "Assign baked spruce HQ billboards");
            var settings = new SerializedObject(manager);
            settings.FindProperty("treeSettings.spruceTreeBillboardPrefab").objectReferenceValue = proxy;
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
            ConfigureRuntimeImports();
            SpruceHQImpostorValidation.Run();
            Debug.Log("SPRUCE HQ BAKE AND VALIDATION PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    public static void RebuildAndValidateBatch()
    {
        try
        {
            PrepareBatchHost(); CreateOrUpdate(); ConfigureRuntimeImports(); SpruceHQImpostorValidation.Run();
            Debug.Log("SPRUCE HQ BAKE AND VALIDATION PASSED"); EditorApplication.Exit(0);
        }
        catch(Exception ex) {Debug.LogException(ex); EditorApplication.Exit(1);}
    }

    public static void Bake(GameObject source)
    {
        if (!source) throw new InvalidOperationException("Missing assigned spruce HQ source prefab.");
        var leafInput=AssetDatabase.LoadAssetAtPath<Material>(LeafPath);
        var needleTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Trees/leafCard_Spruce_03.png");
        Require(needleTexture&&leafInput.GetTexture("_BaseMap")==needleTexture,"Assigned spruce needle card is missing from the bake project.");
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
            var camera = new GameObject("SpruceHQ semantic capture camera").AddComponent<Camera>();
            camera.enabled = false; camera.scene = scene; camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.allowHDR = false; camera.allowMSAA = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var setup = new GameObject("SpruceHQ capture setup").AddComponent<TreeImpostorCaptureSetup>();
            var serialized = new SerializedObject(setup);
            serialized.FindProperty("treeRoot").objectReferenceValue = tree.transform;
            serialized.FindProperty("captureCamera").objectReferenceValue = camera;
            serialized.FindProperty("leafMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(LeafPath);
            serialized.FindProperty("barkMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(BarkPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Require(setup.RecalculateBounds(out var bounds), bounds); Debug.Log("SPRUCE HQ CAPTURE: " + bounds);
            Require(setup.ConfigureCameraFromCurrentDirection(out var cameraMessage), cameraMessage);
            EnsureFolders();
            if (!AssetDatabase.LoadAssetAtPath<Material>(MaterialPath))
                AssetDatabase.CreateAsset(new Material(Shader.Find("Custom/SpruceOctaImpostor")) { name="SpruceHQ_OctaImpostor_M", enableInstancing=true }, MaterialPath);
            Require(TreeImpostorAtlasCaptureWindow.CaptureSpruceHQ(setup), "SpruceHQ semantic capture failed.");
            Debug.Log("SPRUCE HQ BAKE: semantic capture complete; starting ambient visibility.");
            Require(TreeImpostorAmbientAtlasBakerWindow.BakeSpruceHQ(setup), "SpruceHQ ambient bake failed.");
            CreateOrUpdate();
            // Retain bake inputs and settings as provenance alongside the generated atlases.
            File.WriteAllText(AtlasFolder + "/SpruceHQ_Octa_Source.json", JsonUtility.ToJson(new BakeSource {
                prefab=AssetDatabase.GetAssetPath(source), prefabGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)),
                dependencyHash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)).ToString(),
                supersample=2, voxelResolution=128, ambientTileResolution=128, aoRays=8, skyRays=12, raySteps=16,
                coverageExpansion=2, colorBleedPixels=2,
                albedoEncoding="sRGB RGB; linear coverage alpha; full authored evergreen RGB" }, true));
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
        public float coverageExpansionStrength = .75f;
    }

    public static void PrepareBatchHost()
    {
        // Unity refuses additive editor scenes beside an untitled unsaved scene.
        // Batch execution uses this saved, empty host inside the isolated project.
        var host=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        Directory.CreateDirectory("Assets/SpruceHQBakeValidation");
        EditorSceneManager.SaveScene(host,"Assets/SpruceHQBakeValidation/Host.unity");
    }

    [MenuItem("Tools/Impostors/SpruceHQ/Create or Update SpruceHQ Runtime Proxy")]
    public static void CreateOrUpdate()
    {
        EnsureFolders();
        var meta = JsonUtility.FromJson<Metadata>(File.ReadAllText(AtlasFolder + "/SpruceHQ_Octa_Metadata.json"));
        Require(meta.captureRadius > 0 && meta.captureCenterLocal?.Length == 3, "Invalid spruce HQ capture bounds metadata.");
        var center = new Vector3(meta.captureCenterLocal[0],meta.captureCenterLocal[1],meta.captureCenterLocal[2]);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material) { material = new Material(Shader.Find("Custom/SpruceOctaImpostor")); AssetDatabase.CreateAsset(material,MaterialPath); }
        material.name="SpruceHQ_OctaImpostor_M"; material.enableInstancing=true;
        var maps = new[] { "AlbedoCoverage", "Surface", "Depth", "MaterialId", "Ambient" };
        var properties = new[] { "_AlbedoCoverage", "_SurfaceAtlas", "_DepthAtlas", "_MaterialIdAtlas", "_AmbientAtlas" };
        for (int i=0;i<maps.Length;i++)
        {
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasFolder+"/SpruceHQ_Octa_"+maps[i]+".png");
            Require(texture, "Missing spruce HQ atlas: "+maps[i]); material.SetTexture(properties[i],texture);
        }
        material.SetVector("_CaptureCenterLS",center); material.SetFloat("_CaptureRadius",meta.captureRadius);
        material.SetFloat("_FramesPerAxis",meta.framesPerAxis); material.SetFloat("_AtlasTileResolution",meta.tileResolution);
        material.SetFloat("_AtlasPadding",meta.paddingPixels); material.SetFloat("_AmbientTileResolution",128);
        material.SetFloat("_DepthParallax",.45f); material.SetFloat("_AOStrength",.4f); material.SetFloat("_SkyStrength",1);
        material.SetFloat("_FoliageBrightness",.65f); material.SetFloat("_BarkBrightness",1); material.SetFloat("_BarkColorRemap",0);
        material.SetFloat("_Cutoff",CoverageCutoff); material.SetFloat("_TransmissionStrength",.4f);
        material.SetFloat("_WindStrength",.035f); material.SetFloat("_WindSpeed",1.15f);
        var leaves=AssetDatabase.LoadAssetAtPath<Material>(LeafPath);
        // Approved evergreen RGB is captured directly, independent of autumn.
        material.SetFloat("_UseSeasonPalette",0); material.SetFloat("_SeasonPaletteMode",0);
        material.SetFloat("_SeasonStrength",1); material.SetFloat("_SeasonAutumnAmount",0);
        material.SetColor("_TreeLeafTint",Color.white); material.SetColor("_LeafSeasonTint",Color.white);
        material.SetFloat("_StableLeafTransmission",0);
        material.SetColor("_LeafTransmissionColor",leaves.GetColor("_BacklightColor"));
        material.SetFloat("_AmbientFloor",leaves.GetFloat("_AmbientStrength")); material.SetFloat("_LeafLightWrap",leaves.GetFloat("_LightWrap"));
        EditorUtility.SetDirty(material);
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if(!mesh) {mesh=new Mesh {name="SpruceHQ_OctaImpostor_Runtime"}; AssetDatabase.CreateAsset(mesh,MeshPath);}
        mesh.Clear(); mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up}; mesh.triangles=new[]{0,1,2,0,2,3};
        mesh.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
        // Keep source bounds for ecological crown area. The shared rendering
        // path expands these for the root-anchored, camera-facing quad.
        mesh.bounds=new Bounds(center,Vector3.one*meta.captureRadius*2); EditorUtility.SetDirty(mesh);
        var root=new GameObject("SpruceHQ_OctaImpostor_Runtime");
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

    // Preserve 8K detail without quadrupling the old four-map RGBA32 GPU cost.
    public static void ConfigureRuntimeImports()
    {
        foreach(string map in new[]{"AlbedoCoverage","Surface","Depth","MaterialId","Ambient"})
        {
            string path=AtlasFolder+"/SpruceHQ_Octa_"+map+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize=map=="Ambient"?2048:8192;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=4;
            importer.wrapMode=TextureWrapMode.Clamp; importer.isReadable=false;
            importer.sRGBTexture=map=="AlbedoCoverage";
            importer.mipMapsPreserveCoverage=map=="AlbedoCoverage";
            importer.alphaTestReferenceValue=CoverageCutoff;
            var platform=importer.GetPlatformTextureSettings("Standalone");
            platform.overridden=true; platform.maxTextureSize=importer.maxTextureSize;
            platform.format=map=="Depth"?TextureImporterFormat.R8:TextureImporterFormat.BC7;
            platform.compressionQuality=100;
            importer.SetPlatformTextureSettings(platform); importer.SaveAndReimport();
        }
    }

    static void EnsureFolders()
    {
        Directory.CreateDirectory(AtlasFolder); Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
        AssetDatabase.Refresh();
    }
    static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
}
