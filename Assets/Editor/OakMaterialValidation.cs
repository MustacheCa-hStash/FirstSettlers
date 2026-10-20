using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Run in an isolated project via Tools/ValidateOakMaterials.ps1.</summary>
public static class OakMaterialValidation
{
    public static void RunBatch()
    {
        try { Run(); Debug.Log("OAK MATERIAL VALIDATION PASSED"); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ShaderUtil.allowAsyncCompilation = false;
        var sourceLeaf = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Trees/Oak/M_Oak_Leaf.mat");
        var sourceBark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Trees/Oak/M_Oak_Bark.mat");
        Check(sourceLeaf && sourceBark, "Missing oak materials.");
        Check(sourceLeaf.shader.name == "Custom/OakLeafSimpleLitCutout" && sourceBark.shader.name == "Custom/OakBarkSimpleLit", "Oak shader assignments.");
        var leafTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Trees/Oak/T_OakLeafCard.png");
        var barkTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Trees/Bark/T_WhiteOakBark_Stylized.png");
        Check(sourceLeaf.GetTexture("_BaseMap") == leafTexture && sourceBark.GetTexture("_BaseMap") == barkTexture, "Texture assignments.");
        var leafImport = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(leafTexture));
        var barkImport = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(barkTexture));
        Check(leafImport.alphaIsTransparency && leafImport.mipMapsPreserveCoverage && leafImport.wrapMode == TextureWrapMode.Clamp &&
            leafImport.alphaTestReferenceValue == sourceLeaf.GetFloat("_Cutoff"), "Leaf alpha/coverage import settings.");
        Check(barkImport.alphaSource == TextureImporterAlphaSource.None && barkImport.wrapMode == TextureWrapMode.Repeat, "Bark must be opaque and repeating.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tree_Blender_012_Oak.prefab");
        Check(prefab && prefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Contains(sourceLeaf)) &&
            prefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Contains(sourceBark)), "Existing oak prefab material references.");
        var currentModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Trees/Oak_LOD0_P2557_B1800_L600_v02.fbx");
        Check(currentModel && currentModel.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Contains(sourceLeaf)) &&
            currentModel.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Contains(sourceBark)), "Current oak FBX material remaps.");
        foreach (var variant in new[] { WorldFeatureVariant.OakTree, WorldFeatureVariant.GrasslandOakTree })
        {
            var colors = new System.Collections.Generic.HashSet<Color32>();
            for (int i = 0; i < 24; i++)
            {
                FoliageGenerator.GetDeterministicTreeColors(variant, 12345, 7, new ChunkCoord(2, -3), i, i * .4f, out var leafA, out var barkA);
                FoliageGenerator.GetDeterministicTreeColors(variant, 12345, 7, new ChunkCoord(2, -3), i, i * .4f, out var leafB, out var barkB);
                Check(leafA.Equals(leafB) && barkA.Equals(barkB), "Placement colors changed for the same seed/location.");
                Check(leafA.r > leafA.g && leafA.a == 255 && barkA.r > 220, "Oak autumn/bark palette bounds.");
                colors.Add(leafA);
            }
            Check(colors.Count > 8, "Oak placement colors do not vary with location.");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        rendererData.renderingMode = RenderingMode.ForwardPlus;
        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = RenderSettings.ambientEquatorColor = RenderSettings.ambientGroundColor = Color.black;
        RenderSettings.fog = false;
        var camera = new GameObject("Oak validation camera").AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 1; camera.aspect = 1;
        camera.transform.position = new Vector3(0, 0, -3); camera.transform.LookAt(Vector3.zero);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        var target = new RenderTexture(384, 384, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create(); camera.targetTexture = target;
        var pixels = new Texture2D(384, 384, TextureFormat.RGBAFloat, false, true);
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = .8f; sun.shadows = LightShadows.None; RenderSettings.sun = sun;
        sun.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        var card = new GameObject("Oak card");
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-.9f,-.9f,0), new Vector3(-.9f,.9f,0), new Vector3(.9f,.9f,0), new Vector3(.9f,-.9f,0) };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        card.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = card.AddComponent<MeshRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off;
        var leaf = new Material(sourceLeaf); var bark = new Material(sourceBark);
        // Initialize URP before querying the active subshader's pass count.
        renderer.enabled = false;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        foreach (var material in new[] { leaf, bark })
        {
            Check(material.enableInstancing && material.passCount == 4, $"Expected instancing and forward/shadow/depth/normals passes: {material.name}, instancing={material.enableInstancing}, passes={material.passCount}.");
            foreach (var keywords in new[] { Array.Empty<string>(), new[] { "INSTANCING_ON", "LOD_FADE_CROSSFADE" },
                new[] { "_CLUSTER_LIGHT_LOOP", "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT_MEDIUM" },
                new[] { "INSTANCING_ON", "_GBUFFER_NORMALS_OCT", "_CASTING_PUNCTUAL_LIGHT_SHADOW" } })
            {
                material.shaderKeywords = keywords;
                for (int pass = 0; pass < 4; pass++) ShaderUtil.CompilePass(material, pass, true);
                Check(!ShaderUtil.ShaderHasError(material.shader), "Shader compile errors: " + material.shader.name);
            }
            material.shaderKeywords = Array.Empty<string>();
        }
        leaf.SetFloat("_WindStrength", 0); leaf.SetFloat("_WindFlutterStrength", 0);
        var output = Path.GetFullPath("../render-results"); Directory.CreateDirectory(output);
        Color[] Render(Material material, string name = null, Color? tint = null, bool backface = false)
        {
            material.SetFloat("_StandingTreeEnabled", tint.HasValue ? 1 : 0);
            renderer.sharedMaterial = material; renderer.enabled = !tint.HasValue;
            card.transform.rotation = Quaternion.Euler(0, backface ? 180 : 0, 0);
            var block = new MaterialPropertyBlock();
            block.SetVectorArray("_StandingTreeLeaves", new[] { (Vector4)(tint ?? Color.white) });
            block.SetVectorArray("_StandingTreeBark", new[] { (Vector4)(tint ?? Color.white) });
            block.SetVectorArray("_StandingTreeAppearance", new[] { new Vector4(0,1,0,0) });
            block.SetVectorArray("_StandingTreeFade", new[] { new Vector4(0,1,1,0) });
            for (int frame = 0; frame < 2; frame++)
            {
                if (tint.HasValue) Graphics.DrawMeshInstanced(mesh, 0, material, new[] { card.transform.localToWorldMatrix }, 1,
                    block, ShadowCastingMode.Off, true, 0, camera, LightProbeUsage.Off);
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            }
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,384,384),0,0); pixels.Apply();
            var result = pixels.GetPixels();
            if (name != null)
            {
                var display = new Texture2D(384,384,TextureFormat.RGBA32,false);
                display.SetPixels(result.Select(c => c.gamma).ToArray()); display.Apply();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), display.EncodeToPNG()); Object.DestroyImmediate(display);
            }
            return result;
        }
        float Channel(Color[] image, int c) => image.Where(p => p.a > .9f).Average(p => p[c]);
        float Difference(Color[] a, Color[] b) => a.Zip(b, (x,y) => Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)).Average();
        void SameAlpha(Color[] a, Color[] b) => Check(a.Zip(b,(x,y) => Mathf.Abs(x.a-y.a)).Max() < .002f, "Season/tint changed leaf coverage.");
        Shader.SetGlobalFloat("_TreeSeasonSimulationEnabled", 1);
        Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount", 0);
        var summer = Render(leaf, "oak-summer");
        Check(summer.Count(p => p.a > .9f) > 15000 && summer.Count(p => p.a < .01f) > 30000, "Leaf silhouettes/transparent background failed.");
        Check(Channel(summer,1) > Channel(summer,0) * 1.3f, "Summer leaves are not green.");
        Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount", .5f);
        var mid = Render(leaf, "oak-midseason");
        Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount", 1);
        var autumn = Render(leaf, "oak-autumn");
        Check(Channel(autumn,0) > Channel(autumn,1) * 1.3f && Difference(summer, mid) > .01f && Difference(mid, autumn) > .01f, "Season blending failed.");
        SameAlpha(summer, mid); SameAlpha(summer, autumn);
        var tintA = new Color(.68f,.43f,.16f,1); var tintB = new Color(.40f,.16f,.18f,1);
        leaf.SetColor("_TreeLeafTint", tintA);
        var ordinary = Render(leaf, "oak-ordinary-ochre");
        var instanced = Render(leaf, "oak-instanced-ochre", tintA);
        var other = Render(leaf, "oak-instanced-wine", tintB);
        Debug.Log($"OAK TINT DIAGNOSTIC: ordinary={Channel(ordinary,0)}/{Channel(ordinary,1)}/{Channel(ordinary,2)}, instanced={Channel(instanced,0)}/{Channel(instanced,1)}/{Channel(instanced,2)}, difference={Difference(ordinary,instanced)}");
        Check(Difference(ordinary, instanced) < .005f, "Instanced and standalone oak tints disagree.");
        Check(Difference(instanced, other) > .01f, "Instanced oak ignores location tint."); SameAlpha(instanced, other);
        Shader.SetGlobalFloat("_TreeSeasonSimulationEnabled", 0);
        leaf.SetFloat("_SeasonAutumnAmount", 0);
        var authoredSummer = Render(leaf);
        Check(Channel(authoredSummer,1) > Channel(authoredSummer,0), "Season override cleanup did not restore authored summer.");
        leaf.SetFloat("_BacklightingEnabled", 0);
        var unlit = Render(leaf);
        leaf.SetFloat("_BacklightingEnabled", 1);
        var backlit = Render(leaf, "oak-backlit");
        Check(Difference(unlit, backlit) > .01f, "Forward+ stable backlighting had no effect.");
        var backface = Render(leaf, null, null, true);
        Check(Mathf.Abs(Channel(backlit,1) - Channel(backface,1)) < .005f, "Stable two-sided backlighting differs between card faces.");
        bark.SetFloat("_AmbientStrength", 1);
        var dryBark = Render(bark, "oak-bark");
        var tintedBark = Render(bark, null, new Color(.6f,.8f,1,1));
        Check(Difference(dryBark, tintedBark) > .01f, "Instanced bark ignores per-tree tint.");
        Check(Channel(dryBark,0) > .05f && Channel(dryBark,0) < .9f, "Painted bark was crushed or blown out.");
        Debug.Log($"OAK GPU: summer RGB={Channel(summer,0):F3}/{Channel(summer,1):F3}/{Channel(summer,2):F3}; autumn RGB={Channel(autumn,0):F3}/{Channel(autumn,1):F3}/{Channel(autumn,2):F3}; visible pixels={summer.Count(p=>p.a>.9f)}; location difference={Difference(instanced,other):F3}; backlight difference={Difference(unlit,backlit):F3}");
        Shader.SetGlobalFloat("_TreeSeasonSimulationEnabled", 0); Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount", 0);
        RenderTexture.active = null; target.Release();
        Object.DestroyImmediate(leaf); Object.DestroyImmediate(bark); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
    }
}
