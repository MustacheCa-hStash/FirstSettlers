using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class DaisyWeedValidation
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    [MenuItem("Tools/Foliage/Daisy Weeds/Validate Replacement")]
    public static void Run()
    {
        checks=0;var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(DaisyWeedAuthoring.PrefabPath);
        Check(prefab!=null && prefab.transform.childCount==0,"Daisy needs a single root");
        Check(PrefabUtility.GetPrefabAssetType(prefab)==PrefabAssetType.Regular,"Legacy model variant still in use");
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab,out string guid,out long id);
        Check(guid=="ff0e8ff02b583064abb831387de61597" && id==8896480132052262351,"Existing scene/recovery reference changed");
        var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;var material=prefab.GetComponent<MeshRenderer>().sharedMaterial;
        Check(AssetDatabase.GetAssetPath(mesh)==DaisyWeedAuthoring.MeshPath,"Old whole-plant card still assigned");
        Check(mesh.vertexCount==132 && mesh.GetIndexCount(0)/3==119,"Unexpected geometry budget");
        Check(mesh.subMeshCount==1 && prefab.GetComponent<MeshRenderer>().sharedMaterials.Length==1 && material.enableInstancing,"Instanced single-material binding");
        Check(material.shader.name=="Custom/DaisyWeedsPainted" && AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"))==DaisyWeedAuthoring.TexturePath,"Replacement texture/shader unassigned");
        Check(prefab.transform.localPosition==Vector3.zero && prefab.transform.localRotation==Quaternion.identity && prefab.transform.localScale==Vector3.one,"Prefab transform not baked");
        Check(prefab.GetComponentsInChildren<Collider>(true).Length==0,"Decorative weeds created colliders");
        Check(mesh.bounds.size.y>1.1f && mesh.bounds.size.y<1.4f && mesh.bounds.size.x<.8f,"Metre-scale or wind bounds incorrect");
        var vertices=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;var triangles=mesh.triangles;var colors=mesh.colors;
        for(int i=0;i<vertices.Length;i++)
        {
            Check(float.IsFinite(vertices[i].x) && float.IsFinite(vertices[i].y) && float.IsFinite(vertices[i].z),"Nonfinite position");
            Check(normals[i].sqrMagnitude>.8f,"Missing smooth normal");
            Check(uv[i].x>=0 && uv[i].x<=1 && uv[i].y>=0 && uv[i].y<=1,"Atlas UV outside texture");
        }
        for(int i=0;i<triangles.Length;i+=3)Check(Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).sqrMagnitude>1e-12f,"Degenerate triangle");
        Check(colors.Any(c=>c.a==0) && colors.Any(c=>c.a==1 && c.r==1) && colors.Any(c=>c.a==1 && c.r==0),"Stem/petal/leaf metadata missing");
        var options=new WorldFoliageConfiguration
        {
            Grass=new GrassSettings{grassPrefab=prefab,billboardGrassPrefab=prefab,forestGrassPrefab=prefab,forestBillboardGrassPrefab=prefab},
            Flowers=new FlowerSettings{flowerPrefab=prefab,daisyWeedPrefab=prefab},
            Clover=new CloverSettings{enableClover=false},Dandelions=new DandelionSettings{enableDandelions=false},
            LilyPads=new LilyPadSettings{enableLilyPads=false},Cattails=new CattailSettings{enableCattails=false},Trees=null
        };
        var assets=new FoliageRenderAssets(options);
        Check(assets.DaisyWeedMesh==mesh && assets.DaisyWeedMaterial==material,"Daisies disappeared when clover was disabled");
        var root=new GameObject("Daisy batch fixture");
        try
        {
            var runtime=new ChunkFoliageRuntime{root=root.transform,daisyWeedMesh=mesh,daisyWeedMaterial=material};
            int count=Object.FindObjectsByType<Transform>().Length;
            var matrices=Enumerable.Repeat(Matrix4x4.identity,1040).ToArray();var tint=Enumerable.Repeat(Vector4.one,1040).ToArray();
            runtime.CacheFlowerBatches(matrices,tint,false,true);
            var stats=new WorldRenderStatsDebugInfo();runtime.AccumulateFlowerRenderStats(ref stats);
            Check(stats.Flowers.instances==1040 && stats.Flowers.triangles==1040*119,"Daisy batching or 1023 split incorrect");
            Check(Object.FindObjectsByType<Transform>().Length==count,"Batch construction created per-plant GameObjects");
        }
        finally{Object.DestroyImmediate(root);}
        Debug.Log($"DAISY_VALIDATION_PASS: {checks} checks, replacement identity, mesh/UV/normal/alpha metadata, 1040-instance batching, no plant objects, independent clover binding.");
    }
    [MenuItem("Tools/Foliage/Daisy Weeds/Render Preview")]
    public static void Render()
    {
        Directory.CreateDirectory(".utmp/daisy");EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var material=AssetDatabase.LoadAssetAtPath<Material>(DaisyWeedAuthoring.MaterialPath);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(DaisyWeedAuthoring.PrefabPath);var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;
        var root=new GameObject("Daisy preview data");var cameraObject=new GameObject("Daisy preview camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        camera.backgroundColor=new Color(.20f,.29f,.33f);camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.02f;camera.farClipPlane=100;camera.fieldOfView=45;camera.aspect=1.5f;
        var lightObject=new GameObject("Daisy daylight");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.shadows=LightShadows.Soft;lightObject.transform.rotation=Quaternion.Euler(48,-32,0);
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.transform.position=Vector3.down*.004f;ground.transform.localScale=Vector3.one*.8f;
        var groundMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMaterial.SetColor("_BaseColor",new Color(.19f,.29f,.075f));groundMaterial.SetFloat("_Smoothness",0);ground.GetComponent<Renderer>().sharedMaterial=groundMaterial;
        var runtime=new ChunkFoliageRuntime{root=root.transform,daisyWeedMesh=mesh,daisyWeedMaterial=material,flowerPetalColorPropertyId=Shader.PropertyToID("_FlowerPetalColor")};runtime.SetVisible(true);
        var positions=new List<Matrix4x4>();var tints=new List<Vector4>();
        for(int i=0;i<12;i++)
        {
            float angle=i*2.399963f;float radius=i==0?0:Mathf.Sqrt(i)*.21f;
            positions.Add(Matrix4x4.TRS(new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius),Quaternion.Euler(0,i*137.5f,0),Vector3.one*(.54f+i%3*.025f)));
            tints.Add(new Vector4(.92f,.90f,1,1));
        }
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;bool oldAsync=ShaderUtil.allowAsyncCompilation;var previousTarget=RenderTexture.active;
        var target=new RenderTexture(1500,1000,24);target.Create();
        Action<ScriptableRenderContext,Camera> submit=(_,c)=>{if(c==camera)runtime.DrawFlowers();};
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;ShaderUtil.allowAsyncCompilation=false;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.25f,.28f,.32f);
            RenderPipelineManager.beginCameraRendering+=submit;
            runtime.CacheFlowerBatches(new[]{Matrix4x4.Scale(Vector3.one*.575f)},new[]{Vector4.one},false,true);
            Capture("daisy-clump",new Vector3(.80f,.78f,-1.35f),new Vector3(0,.34f,0));
            Capture("daisy-back",new Vector3(-.85f,.75f,1.3f),new Vector3(0,.34f,0));
            Capture("daisy-overhead",new Vector3(.05f,1.7f,-.18f),new Vector3(0,.34f,0));
            Capture("daisy-side-front",new Vector3(0,.67f,-1.15f),new Vector3(0,.67f,0));
            Capture("daisy-side-back",new Vector3(0,.67f,1.15f),new Vector3(0,.67f,0));
            Capture("daisy-side-left",new Vector3(-1.15f,.67f,0),new Vector3(0,.67f,0));
            Capture("daisy-side-right",new Vector3(1.15f,.67f,0),new Vector3(0,.67f,0));
            runtime.ClearFlowerBatches();runtime.CacheFlowerBatches(positions.ToArray(),tints.ToArray(),false,true);
            Capture("daisy-patch",new Vector3(1.5f,1.15f,-2.0f),new Vector3(0,.32f,0));
            for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);
            Check(!ShaderUtil.ShaderHasError(material.shader),"Daisy shader pass compilation failed");
            Run();Debug.Log("DAISY_RENDER_PASS: production instanced flower renderer, front/back/overhead/patch and four level side views, all shader passes, actual pixel coverage. No live-world FPS claim.");
            void Capture(string name,Vector3 eye,Vector3 look)
            {
                camera.transform.position=eye;camera.transform.LookAt(look);
                for(int warm=0;warm<2;warm++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                var rgba=pixels.GetPixels32();Check(!rgba.Any(p=>p.r>230 && p.b>230 && p.g<50),"Magenta error pixels");
                File.WriteAllBytes(".utmp/daisy/"+name+".png",pixels.EncodeToPNG());
                Debug.Log($"DAISY_PIXELS {name}: bright={rgba.Count(p=>p.r>160 && p.g>150 && p.b>115)}, maxRGB={rgba.Max(p=>p.r)}/{rgba.Max(p=>p.g)}/{rgba.Max(p=>p.b)}, sun={Shader.GetGlobalVector("_MainLightColor")}, sunDirection={Shader.GetGlobalVector("_MainLightPosition")}");
                Check(rgba.Count(p=>p.r>160 && p.g>150 && p.b>115)>100,"Daisy flower heads missing from "+name);
                Object.DestroyImmediate(pixels);
            }
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=previousTarget;
            target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);Object.DestroyImmediate(ground);Object.DestroyImmediate(groundMaterial);
        }
    }
    public static void RunBatch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    public static void RenderBatch(){try{Render();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
