using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class SpruceHQImpostorValidation
{
    const string OldPrefab="Assets/Prefabs/Spruce_OctaImpostor_Runtime.prefab";
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    public static void ValidateBatch()
    {
        try{SpruceHQOctaImpostorBaker.PrepareBatchHost();Run();Debug.Log("SPRUCE HQ BAKE AND VALIDATION PASSED");EditorApplication.Exit(0);}
        catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation=false;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(SpruceHQOctaImpostorBaker.PrefabPath);
        Require(prefab,"Missing new spruce HQ prefab.");
        var leafInput=AssetDatabase.LoadAssetAtPath<Material>(SpruceHQOctaImpostorBaker.LeafPath);
        var needleTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Trees/leafCard_Spruce_03.png");
        Require(needleTexture&&leafInput.GetTexture("_BaseMap")==needleTexture,"Missing assigned source needle card.");
        var material=prefab.GetComponent<MeshRenderer>().sharedMaterial;
        Require(material.enableInstancing&&material.GetFloat("_UseSeasonPalette")==0,"Evergreen/instancing settings.");
        long bytes=0;
        foreach(string map in new[]{"AlbedoCoverage","Surface","Depth","MaterialId","Ambient"})
        {
            string path=SpruceHQOctaImpostorBaker.AtlasFolder+"/SpruceHQ_Octa_"+map+".png";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int expected=map=="Ambient"?1088:8192;
            Require(texture&&texture.width==expected&&texture.height==expected,"Atlas was resized: "+map);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            Require(importer.npotScale==TextureImporterNPOTScale.None&&importer.filterMode==FilterMode.Trilinear&&
                importer.sRGBTexture==(map=="AlbedoCoverage"),"Atlas filtering/encoding: "+map);
            var platform=importer.GetPlatformTextureSettings("Standalone");
            Require(platform.overridden&&platform.format==(map=="Depth"?TextureImporterFormat.R8:TextureImporterFormat.BC7),"HQ storage: "+map);
            if(map=="AlbedoCoverage")Require(importer.mipMapsPreserveCoverage&&Mathf.Abs(importer.alphaTestReferenceValue-material.GetFloat("_Cutoff"))<.0001f,"Coverage mip cutoff mismatch.");
            bytes+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
        }
        Debug.Log("SPRUCE HQ STORAGE: "+(bytes/(1024*1024))+" MiB for all five runtime textures; 1016px per view, 64 views.");
        ValidateAtlas();
        SpruceImpostorGpuValidation.ValidateAdditionalSpecies(SpruceHQOctaImpostorBaker.PrefabPath);
        TreeImpostorGroundingValidation.RunSpecies("SpruceHQ");
        RenderComparisons(prefab);
        WriteRecord();
        Debug.Log("SPRUCE HQ IMPOSTOR PASS: 8K atlases, coverage mipmaps, CPU/GPU routing and pixels, bounds, fades, root anchoring and near/old/new distance comparisons.");
    }
    static Color32[] Readback(Texture2D source)
    {
        var previous=RenderTexture.active;
        var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        var pixels=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
        try{Graphics.Blit(source,rt);RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,source.width,source.height),0,0);pixels.Apply();return pixels.GetPixels32();}
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(pixels);}
    }
    static void ValidateAtlas()
    {
        var albedo=Readback(AssetDatabase.LoadAssetAtPath<Texture2D>(SpruceHQOctaImpostorBaker.AtlasFolder+"/SpruceHQ_Octa_AlbedoCoverage.png"));
        var ids=Readback(AssetDatabase.LoadAssetAtPath<Texture2D>(SpruceHQOctaImpostorBaker.AtlasFolder+"/SpruceHQ_Octa_MaterialId.png"));
        int leaves=0,bark=0;
        for(int fy=0;fy<8;fy++)for(int fx=0;fx<8;fx++)
        {
            int count=0;
            for(int y=4;y<1020;y+=4)for(int x=4;x<1020;x+=4)
            {
                int i=(fy*1024+y)*8192+fx*1024+x;
                if(albedo[i].a<20)continue;
                count++;
                Require(ids[i].r>80||ids[i].g>80,"Coverage without matching material identity.");
                if(ids[i].r>ids[i].g)leaves++;else bark++;
            }
            Require(count>100,"Empty spruce frame: "+fx+","+fy);
        }
        Require(leaves>10000&&bark>1000,"Missing needles or bark.");
        Debug.Log($"SPRUCE HQ ATLAS PASS: 64 views; sampled needles={leaves}, bark={bark}.");
    }
    [Serializable] sealed class Metric{public string kind;public int size,yaw,covered,crownHoles;}
    [Serializable] sealed class Comparison{public Metric[] views;}
    static void RenderComparisons(GameObject prefab)
    {
        var previousScene=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
        var oldTarget=RenderTexture.active;
        var data=ScriptableObject.CreateInstance<UniversalRendererData>();data.renderingMode=RenderingMode.ForwardPlus;
        var pipeline=UniversalRenderPipelineAsset.Create(data);var copies=new List<Material>();
        try
        {
            SceneManager.SetActiveScene(scene);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            RenderSettings.fog=false;RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.35f,.35f);
            var source=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SpruceHQOctaImpostorBaker.SourcePrefab),scene);
            var old=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefab),scene);
            var proxy=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var roots=new[]{source,old,proxy};var names=new[]{"source","old","hq"};
            foreach(var root in roots)
            {
                root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=Vector3.one;
                foreach(var group in root.GetComponentsInChildren<LODGroup>())group.ForceLOD(0);
                foreach(var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    renderer.shadowCastingMode=ShadowCastingMode.Off;
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>{var copy=new Material(m);copies.Add(copy);
                        if(copy.HasProperty("_WindStrength"))copy.SetFloat("_WindStrength",0);
                        if(copy.HasProperty("_WindFlutterStrength"))copy.SetFloat("_WindFlutterStrength",0);return copy;}).ToArray();
                }
            }
            var sun=new GameObject("Comparison sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1;
            sun.transform.rotation=Quaternion.Euler(40,-35,0);RenderSettings.sun=sun;
            var camera=new GameObject("Spruce distance comparison").AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
            camera.orthographic=true;camera.aspect=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.nearClipPlane=.1f;camera.farClipPlane=500;
            var material=prefab.GetComponent<MeshRenderer>().sharedMaterial;
            Vector3 center=material.GetVector("_CaptureCenterLS");float radius=material.GetFloat("_CaptureRadius");camera.orthographicSize=radius*.9f;
            string output=Path.GetFullPath("../render-results");Directory.CreateDirectory(output);var metrics=new List<Metric>();
            foreach(int size in new[]{512,256,128,64})
            {
                var target=new RenderTexture(size,size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
                var pixels=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
                try
                {
                    foreach(int yaw in new[]{0,45,90})
                    {
                        camera.transform.position=center+Quaternion.Euler(-8,yaw,0)*Vector3.back*radius*3;camera.transform.LookAt(center);
                        for(int kind=0;kind<roots.Length;kind++)
                        {
                            for(int i=0;i<roots.Length;i++)roots[i].SetActive(i==kind);
                            for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,size,size),0,0);pixels.Apply();var colors=pixels.GetPixels();
                            int covered=colors.Count(c=>c.a>.001f),holes=0;
                            for(int y=size/4;y<size*9/10;y++)
                            {
                                int left=size,right=-1;
                                for(int x=0;x<size;x++)if(colors[y*size+x].a>.001f){left=Math.Min(left,x);right=Math.Max(right,x);}
                                for(int x=left;x<=right;x++)if(colors[y*size+x].a<=.001f)holes++;
                            }
                            Require(covered>15,"Empty distance rendering: "+names[kind]+size);
                            metrics.Add(new Metric{kind=names[kind],size=size,yaw=yaw,covered=covered,crownHoles=holes});
                            var display=new Texture2D(size,size,TextureFormat.RGBA32,false);
                            display.SetPixels(colors.Select(c=>c.a>.001f?new Color(c.gamma.r,c.gamma.g,c.gamma.b,1):new Color(.125f,.16f,.21f,1)).ToArray());display.Apply();
                            File.WriteAllBytes(output+"/spruce-"+names[kind]+"-"+size+"-"+yaw+".png",display.EncodeToPNG());Object.DestroyImmediate(display);
                        }
                    }
                }
                finally{RenderTexture.active=oldTarget;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);}
            }
            File.WriteAllText(output+"/spruce-hq-distance-metrics.json",JsonUtility.ToJson(new Comparison{views=metrics.ToArray()},true));
            foreach(int size in new[]{256,128,64})
            {
                int oldPixels=metrics.Where(m=>m.kind=="old"&&m.size==size).Sum(m=>m.covered);
                int newPixels=metrics.Where(m=>m.kind=="hq"&&m.size==size).Sum(m=>m.covered);
                int oldHoles=metrics.Where(m=>m.kind=="old"&&m.size==size).Sum(m=>m.crownHoles);
                int newHoles=metrics.Where(m=>m.kind=="hq"&&m.size==size).Sum(m=>m.crownHoles);
                Debug.Log($"SPRUCE HQ DISTANCE {size}px: covered old={oldPixels}, new={newPixels}; interior gaps old={oldHoles}, new={newHoles}.");
            }
            foreach(var copy in copies)Require(!ShaderUtil.ShaderHasError(copy.shader),"Rendered shader error: "+copy.shader.name);
        }
        finally
        {
            RenderTexture.active=oldTarget;foreach(var copy in copies)Object.DestroyImmediate(copy);
            QualitySettings.renderPipeline=oldQuality;GraphicsSettings.defaultRenderPipeline=oldPipeline;
            if(previousScene.IsValid())SceneManager.SetActiveScene(previousScene);EditorSceneManager.CloseScene(scene,true);
            Object.DestroyImmediate(pipeline);Object.DestroyImmediate(data);
        }
    }
    [Serializable] sealed class Signature{public string path,sha256;}
    [Serializable] sealed class Record{public Signature[] inputs,outputs;}
    static Signature Sign(string path)
    {using(var hash=SHA256.Create())return new Signature{path=path,sha256=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","")};}
    static void WriteRecord()
    {
        var record=new Record{inputs=AssetDatabase.GetDependencies(SpruceHQOctaImpostorBaker.SourcePrefab,true).Where(p=>p.StartsWith("Assets/")&&File.Exists(p)).Select(Sign).ToArray(),
            outputs=new[]{"AlbedoCoverage","Surface","Depth","MaterialId","Ambient"}.Select(n=>SpruceHQOctaImpostorBaker.AtlasFolder+"/SpruceHQ_Octa_"+n+".png")
                .Concat(new[]{SpruceHQOctaImpostorBaker.MaterialPath,SpruceHQOctaImpostorBaker.MeshPath,SpruceHQOctaImpostorBaker.PrefabPath}).Select(Sign).ToArray()};
        string path=SpruceHQOctaImpostorBaker.AtlasFolder+"/SpruceHQ_Octa_Validation.json";File.WriteAllText(path,JsonUtility.ToJson(record,true));AssetDatabase.ImportAsset(path);
    }
}
