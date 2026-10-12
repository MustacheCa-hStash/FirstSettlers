using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class DistantFogSetup
{
    public const string NoisePath = "Assets/Settings/DistantFogNoise.asset";
    public const string ScenePath = "Assets/Scenes/SmearScene.unity";
    public const string RendererPath = "Assets/Settings/PC_Renderer.asset";

    [MenuItem("Tools/Atmosphere/Install Distant Fog in SmearScene")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install outside Play mode.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var noise = AssetDatabase.LoadAssetAtPath<Texture3D>(NoisePath);
        if (noise == null)
        {
            noise = DistantFogLayer.CreateNoiseTexture(64);
            AssetDatabase.CreateAsset(noise, NoisePath);
        }
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var feature = renderer.rendererFeatures.OfType<DistantFogRendererFeature>().FirstOrDefault();
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<DistantFogRendererFeature>();
            feature.name = "Distant Noisy Fog";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
        }
        var properties = new SerializedObject(feature);
        properties.FindProperty("fogShader").objectReferenceValue = Shader.Find("Hidden/FirstSettlers/DistantFog");
        properties.FindProperty("depthCopyShader").objectReferenceValue = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
        properties.ApplyModifiedPropertiesWithoutUndo();
        feature.SetActive(true);
        feature.Create();
        // Preserve URP's local-file-ID map when adding a subasset outside its inspector.
        var rendererProperties = new SerializedObject(renderer);
        var map = rendererProperties.FindProperty("m_RendererFeatureMap");
        map.arraySize = renderer.rendererFeatures.Count;
        for (int i = 0; i < map.arraySize; i++)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string guid, out long id);
            map.GetArrayElementAtIndex(i).longValue = id;
        }
        rendererProperties.ApplyModifiedPropertiesWithoutUndo();
        renderer.SetDirty();
        EditorUtility.SetDirty(renderer);

        var scene = EditorSceneManager.OpenScene(ScenePath);
        var layer = UnityEngine.Object.FindAnyObjectByType<DistantFogLayer>();
        if (layer == null) layer = new GameObject("Distant Fog Layer").AddComponent<DistantFogLayer>();
        layer.world = UnityEngine.Object.FindAnyObjectByType<WorldManager>();
        layer.noiseTexture = noise;
        // Existing surface fog would erase distant peaks independently of this effect.
        // SunCycleController continues publishing its fog color while standard fog is off.
        RenderSettings.fog = false;
        EditorUtility.SetDirty(layer);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("DISTANT FOG INSTALLED: SmearScene; PC renderer; 600-unit start; world-Y -45..85, billows +/-20; clear surfaces by Y150; half resolution, 32 steps. No culling changes.");
    }
}
