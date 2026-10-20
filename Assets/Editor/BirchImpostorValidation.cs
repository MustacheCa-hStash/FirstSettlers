using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class BirchImpostorValidation
{
    [MenuItem("Tools/Impostors/Birch/Validate Baked Birch")]
    public static void Run()
    {
        bool previousAsync=ShaderUtil.allowAsyncCompilation;
        try { RunChecks(); }
        finally { ShaderUtil.allowAsyncCompilation=previousAsync; }
    }

    static void RunChecks()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(BirchOctaImpostorBaker.PrefabPath);
        Require(prefab,"Missing baked birch proxy.");
        var material=prefab.GetComponent<MeshRenderer>().sharedMaterial;
        Require(material.enableInstancing && material.GetFloat("_SeasonPaletteMode")==3,"Birch instancing/palette settings.");
        ValidateSource();
        foreach(var name in new[]{"AlbedoCoverage","Surface","Depth","MaterialId","Ambient"})
        {
            string path=BirchOctaImpostorBaker.AtlasFolder+"/Birch_Octa_"+name+".png";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int size=name=="Ambient"?1056:4064;
            Require(texture && texture.width==size && texture.height==size,"Atlas resolution: "+name);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            Require(importer.npotScale==TextureImporterNPOTScale.None && importer.wrapMode==TextureWrapMode.Clamp &&
                importer.textureCompression==TextureImporterCompression.Uncompressed && importer.sRGBTexture==(name=="AlbedoCoverage"),"Atlas import: "+name);
        }
        var albedo=AssetDatabase.LoadAssetAtPath<Texture2D>(BirchOctaImpostorBaker.AtlasFolder+"/Birch_Octa_AlbedoCoverage.png");
        var ids=AssetDatabase.LoadAssetAtPath<Texture2D>(BirchOctaImpostorBaker.AtlasFolder+"/Birch_Octa_MaterialId.png");
        var a=albedo.GetPixels32(); var id=ids.GetPixels32();
        int leaves=0,bark=0;
        for(int fy=0;fy<8;fy++) for(int fx=0;fx<8;fx++)
        {
            int covered=0;
            for(int y=2;y<506;y+=2) for(int x=2;x<506;x+=2)
            {
                int i=(fy*508+y)*4064+fx*508+x;
                if(a[i].a<10) continue;
                covered++;
                Require(id[i].r>100 || id[i].g>100,"Visible coverage has no leaf/bark semantics.");
                if(id[i].r>id[i].g)
                {
                    leaves++;
                    // Supersampled boundaries legitimately mix neutral leaves
                    // and brown bark; inspect only pure leaf samples for hue.
                    if(id[i].r>250 && id[i].g<3 && a[i].a>240)
                        Require(Math.Abs(a[i].r-a[i].g)<3 && Math.Abs(a[i].g-a[i].b)<3,"Season hue was baked into foliage.");
                }
                else bark++;
            }
            Require(covered>100,"Empty or nearly empty birch frame: "+fx+","+fy);
        }
        Require(leaves>10000 && bark>1000,"Birch capture is missing foliage or bark.");
        Debug.Log($"BIRCH ATLAS PASS: 64 valid frames, sampled foliage={leaves}, bark={bark}; unlit neutral foliage and painted bark.");
        BirchImpostorColorValidation.Run();
        OakImpostorColorValidation.Run();
        MapleImpostorColorValidation.Run();
        SpruceImpostorGpuValidation.ValidateAdditionalSpecies(BirchOctaImpostorBaker.PrefabPath);
        TreeImpostorGroundingValidation.RunSpecies("Birch");
        RenderPreviews(prefab);
        WriteValidationRecord();
        Debug.Log("BIRCH IMPOSTOR PASS: actual atlases, source palette, CPU/indirect routing and pixels, bounds, fades, root anchoring and source/proxy previews.");
    }

    static void ValidateSource()
    {
        ShaderUtil.allowAsyncCompilation=false;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(BirchOctaImpostorBaker.SourcePrefab);
        foreach(string path in new[]{BirchOctaImpostorBaker.LeafPath,BirchOctaImpostorBaker.BarkPath})
        {
            var authored=AssetDatabase.LoadAssetAtPath<Material>(path);
            Require(authored && authored.enableInstancing && source.GetComponentsInChildren<Renderer>(true)
                .Any(renderer=>renderer.sharedMaterials.Contains(authored)),"Source prefab material/instancing: "+path);
        }
        var leaf=AssetDatabase.LoadAssetAtPath<Material>(BirchOctaImpostorBaker.LeafPath);
        var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(leaf.GetTexture("_BaseMap")));
        Require(importer.alphaIsTransparency && importer.mipMapsPreserveCoverage && importer.wrapMode==TextureWrapMode.Clamp &&
            importer.alphaTestReferenceValue==leaf.GetFloat("_Cutoff"),"Birch alpha import/cutoff mismatch.");
        foreach(var variant in new[]{WorldFeatureVariant.BirchAspenTree,WorldFeatureVariant.GrasslandBirchAspenTree})
        {
            var colors=new System.Collections.Generic.HashSet<Color32>();
            for(int i=0;i<24;i++)
            {
                FoliageGenerator.GetDeterministicTreeColors(variant,1937,7,new ChunkCoord(2,-3),i,i*.4f,out var a,out var barkA);
                FoliageGenerator.GetDeterministicTreeColors(variant,1937,7,new ChunkCoord(2,-3),i,i*.4f,out var b,out var barkB);
                Require(a.Equals(b)&&barkA.Equals(barkB)&&a.r>a.g&&a.g>a.b&&barkA.r>230,"Birch placement color determinism/palette.");
                colors.Add(a);
            }
            Require(colors.Count>8,"Birch colors do not vary with placement location.");
        }
    }

    [Serializable] sealed class Signature { public string path, sha256; }
    [Serializable] sealed class ValidationRecord { public Signature[] inputs, outputs; }
    static Signature Sign(string path)
    {
        using(var hash=SHA256.Create())
            return new Signature {path=path,sha256=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","")};
    }
    static void WriteValidationRecord()
    {
        var outputs=new[]{"AlbedoCoverage","Surface","Depth","MaterialId","Ambient"}
            .Select(n=>BirchOctaImpostorBaker.AtlasFolder+"/Birch_Octa_"+n+".png")
            .Concat(new[]{BirchOctaImpostorBaker.MaterialPath,BirchOctaImpostorBaker.MeshPath,BirchOctaImpostorBaker.PrefabPath});
        var record=new ValidationRecord {
            inputs=AssetDatabase.GetDependencies(BirchOctaImpostorBaker.SourcePrefab,true).Where(p=>p.StartsWith("Assets/")&&File.Exists(p)).Select(Sign).ToArray(),
            outputs=outputs.Select(Sign).ToArray()};
        string path=BirchOctaImpostorBaker.AtlasFolder+"/Birch_Octa_Validation.json";
        File.WriteAllText(path,JsonUtility.ToJson(record,true));AssetDatabase.ImportAsset(path);
    }

    public static void ValidateBatch()
    {
        try {BirchOctaImpostorBaker.PrepareBatchHost(); Run(); Debug.Log("BIRCH BAKE AND VALIDATION PASSED"); EditorApplication.Exit(0);}
        catch(Exception ex) {Debug.LogException(ex); EditorApplication.Exit(1);}
    }

    static void RenderPreviews(GameObject prefab)
    {
        var previousScene=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var oldPipeline=GraphicsSettings.defaultRenderPipeline; var oldQuality=QualitySettings.renderPipeline;
        float oldEnabled=Shader.GetGlobalFloat("_TreeSeasonSimulationEnabled"), oldSeason=Shader.GetGlobalFloat("_TreeSeasonSimulationAutumnAmount");
        var data=ScriptableObject.CreateInstance<UniversalRendererData>(); data.renderingMode=RenderingMode.ForwardPlus;
        var pipeline=UniversalRenderPipelineAsset.Create(data);
        var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var pixels=new Texture2D(512,512,TextureFormat.RGBAFloat,false,true);
        var copies=new System.Collections.Generic.List<Material>();
        var oldTarget=RenderTexture.active;
        try
        {
            SceneManager.SetActiveScene(scene); GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=pipeline;
            RenderSettings.fog=false; RenderSettings.skybox=null; RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.35f,.35f,.35f);
            var source=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BirchOctaImpostorBaker.SourcePrefab),scene);
            var proxy=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); source.transform.localScale=Vector3.one;
            foreach(var root in new[]{source,proxy}) foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>{
                    var copy=new Material(m); copies.Add(copy);
                    if(copy.HasProperty("_WindStrength"))copy.SetFloat("_WindStrength",0);
                    if(copy.HasProperty("_WindFlutterStrength"))copy.SetFloat("_WindFlutterStrength",0);
                    copy.SetColor("_TreeLeafTint",new Color(.94f,.75f,.24f,1));
                    return copy;
                }).ToArray();
                // Match the world renderer's raw per-tree instance vectors;
                // relying on an auto-instanced Color property can decode twice.
                var properties=new MaterialPropertyBlock();
                properties.SetFloat("_StandingTreeEnabled",1);
                properties.SetVector("_StandingTreeLeaves",new Vector4(.94f,.75f,.24f,1));
                properties.SetVector("_TreeLeafTint",new Vector4(.94f,.75f,.24f,1));
                properties.SetVector("_StandingTreeBark",Vector4.one);
                properties.SetVector("_StandingTreeAppearance",new Vector4(0,1,0,0));
                properties.SetVector("_StandingTreeFade",new Vector4(0,1,1,0));
                renderer.SetPropertyBlock(properties);
            }
            var sun=new GameObject("Preview sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1;
            sun.transform.rotation=Quaternion.Euler(40,-35,0); RenderSettings.sun=sun;
            var camera=new GameObject("Birch comparison camera").AddComponent<Camera>(); camera.enabled=false;camera.scene=scene;
            camera.orthographic=true;camera.aspect=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.21f,1);
            camera.nearClipPlane=.1f;camera.farClipPlane=500;
            var mat=prefab.GetComponent<MeshRenderer>().sharedMaterial;
            Vector3 center=mat.GetVector("_CaptureCenterLS");float radius=mat.GetFloat("_CaptureRadius");camera.orthographicSize=radius*.9f;
            target.Create();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            foreach(var copy in copies.Where(m=>m.shader.name!="Custom/SpruceOctaImpostor"))
            {
                Require(copy.passCount==4,"Birch must provide forward, shadow, depth and normals: "+copy.name);
                foreach(var keywords in new[]{Array.Empty<string>(),new[]{"INSTANCING_ON","LOD_FADE_CROSSFADE",
                    "_CLUSTER_LIGHT_LOOP","_MAIN_LIGHT_SHADOWS_CASCADE","_SHADOWS_SOFT_MEDIUM","_GBUFFER_NORMALS_OCT","_CASTING_PUNCTUAL_LIGHT_SHADOW"}})
                {
                    copy.shaderKeywords=keywords;
                    for(int pass=0;pass<copy.passCount;pass++) ShaderUtil.CompilePass(copy,pass,true);
                    Require(!ShaderUtil.ShaderHasError(copy.shader),"Birch pass compilation failed: "+copy.shader.name);
                }
                copy.shaderKeywords=Array.Empty<string>();
            }
            Debug.Log("BIRCH SOURCE PASS: four passes on bark/leaves, plain and instanced Forward+/shadow/depth/normal variants compiled.");
            string output=Path.GetFullPath("../render-results");Directory.CreateDirectory(output);
            foreach(int yaw in new[]{0,90}) foreach(float season in new[]{0f,1f}) foreach(bool far in new[]{false,true})
            {
                Shader.SetGlobalFloat("_TreeSeasonSimulationEnabled",1);Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount",season);
                source.SetActive(!far);proxy.SetActive(far);
                camera.transform.position=center+Quaternion.Euler(-8,yaw,0)*Vector3.back*radius*3;camera.transform.LookAt(center);
                for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();
                var colors=pixels.GetPixels();Require(colors.Any(c=>c.g>.2f),"Birch preview did not render.");
                var display=new Texture2D(512,512,TextureFormat.RGBA32,false);display.SetPixels(colors.Select(c=>c.gamma).ToArray());display.Apply();
                File.WriteAllBytes(output+"/birch-"+(far?"impostor":"source")+"-"+(season==0?"summer":"autumn")+"-"+yaw+".png",display.EncodeToPNG());Object.DestroyImmediate(display);
            }
        }
        finally
        {
            Shader.SetGlobalFloat("_TreeSeasonSimulationEnabled",oldEnabled);Shader.SetGlobalFloat("_TreeSeasonSimulationAutumnAmount",oldSeason);
            RenderTexture.active=oldTarget;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);
            foreach(var material in copies)Object.DestroyImmediate(material);
            QualitySettings.renderPipeline=oldQuality;GraphicsSettings.defaultRenderPipeline=oldPipeline;
            if(previousScene.IsValid())SceneManager.SetActiveScene(previousScene);EditorSceneManager.CloseScene(scene,true);
            Object.DestroyImmediate(pipeline);Object.DestroyImmediate(data);
        }
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
