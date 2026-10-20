using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Run in the isolated material-validation project; does not bake billboards.
public static class WhitePineMaterialValidation
{
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    public static void RunBatch()
    {
        try { Run(); Debug.Log("WHITE PINE MATERIAL VALIDATION PASSED"); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ShaderUtil.allowAsyncCompilation = false;
        var bark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Trees/WhitePine/M_WhitePineBark.mat");
        var leaf = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Trees/WhitePine/M_WhitePineLeaves.mat");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WhitePine_LOD0_v01.prefab");
        Require(prefab && bark && leaf, "Missing white pine source assets.");
        var sourceRenderers = prefab.GetComponentsInChildren<Renderer>(true);
        Require(sourceRenderers.Any(r => r.sharedMaterials.Contains(bark)) && sourceRenderers.Any(r => r.sharedMaterials.Contains(leaf)), "Assigned source prefab materials.");
        string texturePath = "Assets/Textures/Trees/Bark/T_WhitePineBark_Stylized.png";
        Require(bark.GetTexture("_BaseMap") == AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath), "Painted bark assignment.");
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        Require(importer.sRGBTexture && importer.wrapMode == TextureWrapMode.Repeat && importer.alphaSource == TextureImporterAlphaSource.None, "Opaque repeating sRGB bark import.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var data = ScriptableObject.CreateInstance<UniversalRendererData>(); data.renderingMode = RenderingMode.ForwardPlus;
        var pipeline = UniversalRenderPipelineAsset.Create(data);
        GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
        RenderSettings.fog = false; RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.3f,.3f,.3f);
        var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        tree.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); tree.transform.localScale = Vector3.one;
        var renderers = tree.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
            renderer.sharedMaterials = renderer.sharedMaterials.Select(m => {
                var copy = new Material(m);
                if (copy.HasProperty("_WindStrength")) copy.SetFloat("_WindStrength",0);
                if (copy.HasProperty("_WindFlutterStrength")) copy.SetFloat("_WindFlutterStrength",0);
                return copy;
            }).ToArray();
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional;
        sun.intensity = 1; sun.transform.rotation = Quaternion.Euler(40,-35,0); RenderSettings.sun = sun;
        var camera = new GameObject("White pine preview").AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.aspect = 1; camera.nearClipPlane = .1f; camera.farClipPlane = 500;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f,.16f,.21f);
        var target = new RenderTexture(768,768,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); target.Create();
        var pixels = new Texture2D(768,768,TextureFormat.RGBAFloat,false,true);
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        foreach (var material in new[]{bark,leaf})
        {
            Require(material.enableInstancing && material.shader.isSupported, "Unsupported shader or disabled instancing: " + material.name);
            foreach (var keywords in new[]{Array.Empty<string>(),new[]{"INSTANCING_ON","LOD_FADE_CROSSFADE","_CLUSTER_LIGHT_LOOP","_MAIN_LIGHT_SHADOWS_CASCADE","_SHADOWS_SOFT_MEDIUM"}})
            {
                var copy = new Material(material); copy.shaderKeywords = keywords;
                for(int pass=0;pass<copy.passCount;pass++) ShaderUtil.CompilePass(copy,pass,true);
                Require(!ShaderUtil.ShaderHasError(copy.shader), "Shader compile failure: " + copy.shader.name);
                Object.DestroyImmediate(copy);
            }
        }
        string output = Path.GetFullPath("../render-results"); Directory.CreateDirectory(output);
        Color[] Capture(Vector3 center, float size, int yaw)
        {
            camera.orthographicSize = size;
            camera.transform.position = center + Quaternion.Euler(-5,yaw,0) * Vector3.back * bounds.size.magnitude * 2;
            camera.transform.LookAt(center);
            for(int i=0;i<2;i++) RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,768,768),0,0); pixels.Apply();
            return pixels.GetPixels();
        }
        void Save(string name, Color[] colors)
        {
            var display = new Texture2D(768,768,TextureFormat.RGBA32,false);
            display.SetPixels(colors.Select(c=>c.gamma).ToArray()); display.Apply();
            File.WriteAllBytes(output+"/"+name+".png",display.EncodeToPNG()); Object.DestroyImmediate(display);
        }
        float size = Mathf.Max(bounds.extents.y,bounds.extents.x,bounds.extents.z)*1.12f;
        Shader.SetGlobalFloat("_TreeSeasonSimulationEnabled",1); Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount",0);
        var summer = Capture(bounds.center,size,0); Save("white-pine-full-0",summer);
        Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount",1);
        var autumn = Capture(bounds.center,size,0);
        Require(summer.Zip(autumn,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)).Max()<.0001f,"Evergreen changes with autumn preview.");
        Save("white-pine-full-90",Capture(bounds.center,size,90));
        Save("white-pine-trunk",Capture(new Vector3(0,bounds.min.y+bounds.size.y*.18f,0),bounds.size.y*.2f,0));
        // Exercise the same location-aware appearance block used by standing trees.
        var properties = new MaterialPropertyBlock(); properties.SetFloat("_StandingTreeEnabled",1);
        properties.SetVector("_StandingTreeLeaves",new Vector4(.92f,.98f,.88f,1)); properties.SetVector("_StandingTreeBark",new Vector4(.90f,.88f,.82f,1));
        properties.SetVector("_StandingTreeAppearance",new Vector4(0,1,0,0)); properties.SetVector("_StandingTreeFade",new Vector4(0,1,1,0));
        foreach(var renderer in renderers) renderer.SetPropertyBlock(properties);
        var tinted = Capture(bounds.center,size,0); Save("white-pine-world-tint",tinted);
        Require(summer.Zip(tinted,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)).Max()>.001f,"Standing tree tint has no effect.");
        Require(!ShaderUtil.ShaderHasError(leaf.shader) && !ShaderUtil.ShaderHasError(bark.shader), "Rendered shader errors.");
        Debug.Log("WHITE PINE PASS: source assignments, bark import, Forward+ shader variants, evergreen season stability, world tint, four rendered previews; bounds="+bounds);
        RenderTexture.active = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
    }
}
