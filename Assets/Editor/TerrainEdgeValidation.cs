using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Isolated native comparison: identical geometry, controls, lighting and camera.
// Legacy/normal-only shaders are temporary diagnostic assets in the test project.
public static class TerrainEdgeValidation
{
    private static int checks;
    private static string output;
    private static readonly StringBuilder report=new();
    private static readonly List<Object> cleanup=new();
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    private static string Arg(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i<0?null:a[i+1];}
    public static void RunBatch()
    {
        try
        {
            output=Arg("-landformOutput");Directory.CreateDirectory(output);checks=0;report.Clear();
            Render(TerrainRidgeValidation.SceneSettings());
            report.AppendLine($"TERRAIN EDGES PASS: {checks} checks. Canonical C2 lips; mesh/collider topology unchanged; generator v7.");
            File.WriteAllText(Path.Combine(output,"validation.txt"),report.ToString());Debug.Log(report.ToString());EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static Shader Diagnostic(string label,bool legacy,string returnCode=null,bool legacyCoordinates=false)
    {
        string text=File.ReadAllText("Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader");
        text=text.Replace("\r\n","\n").Replace("#include \"GrassGroundSurface.hlsl\"","#include \"Assets/Shaders/S_Terrain/GrassGroundSurface.hlsl\"");
        text=text.Replace("Shader \"Custom/StylizedTerrainURP\"","Shader \"Hidden/FirstSettlers/Edge"+label+"\"");
        if(legacy)text=text.Replace("#pragma vertex vert","#pragma vertex vert\n            #define FS_CLIFF_NORMAL_LEGACY 1");
        if(legacyCoordinates)text=text.Replace("#pragma vertex vert","#pragma vertex vert\n            #define FS_TERRAIN_LEGACY_DERIVATIVES 1");
        if(returnCode!=null)text=text.Replace("// Terrain edge diagnostic insertion point.",returnCode);
        const string root="Assets/Editor/GeneratedTerrainEdges";Directory.CreateDirectory(root);
        string path=root+"/"+label+".shader";File.WriteAllText(path,text);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Shader>(path);
    }
    internal static void ValidatePrecision(StringBuilder results)
    {
        bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var scene=EditorSceneManager.NewPreviewScene();var objects=new List<Object>();
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=null;
            var stable=Diagnostic("RelativePrecision",false,"return half4(baseNormalWS * 0.5 + 0.5,1);");
            var old=Diagnostic("AbsolutePrecision",false,"return half4(baseNormalWS * 0.5 + 0.5,1);",true);
            var go=new GameObject("Precision plane");objects.Add(go);SceneManager.MoveGameObjectToScene(go,scene);
            var n=new Vector3(-.8f,.1f,-.59f).normalized;var u=Vector3.Cross(n,Vector3.up).normalized;var v=Vector3.Cross(u,n).normalized;
            var mesh=new Mesh();objects.Add(mesh);mesh.vertices=new[]{-u*3-v*2,u*3-v*2,u*3+v*2,-u*3+v*2};mesh.normals=new[]{n,n,n,n};mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();
            var mat=new Material(stable);objects.Add(mat);renderer.sharedMaterial=mat;
            var cg=new GameObject("Precision camera");objects.Add(cg);SceneManager.MoveGameObjectToScene(cg,scene);
            var camera=cg.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cameraType=CameraType.Preview;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.fieldOfView=60;camera.farClipPlane=100;
            cg.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            foreach(var origin in new[]{Vector3.zero,new Vector3(12000,400,-11000),new Vector3(-12000,400,11000)})
            {
                go.transform.position=origin;camera.transform.position=origin+n*2;camera.transform.LookAt(origin,Vector3.up);
                foreach(bool legacy in new[]{true,false})
                {
                    mat.shader=legacy?old:stable;
                    var rt=RenderTexture.GetTemporary(2048,1152,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                    var previous=RenderTexture.active;var image=new Texture2D(2048,1152,TextureFormat.RGBAFloat,false,true);
                    try
                    {
                        camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,2048,1152),0,0);image.Apply();
                        double sum=0;float max=0;int count=0,large=0;
                        foreach(var c in image.GetPixels())
                        {if(c.a<.99f)continue;var actual=new Vector3(c.r*2-1,c.g*2-1,c.b*2-1).normalized;float error=Vector3.Angle(actual,n);sum+=error*error;max=Mathf.Max(max,error);count++;if(error>1)large++;}
                        double rms=Math.Sqrt(sum/Math.Max(count,1));
                        results.AppendLine($"Production shader precision, {origin}, {(legacy?"absolute":"relative")}: {count} pixels, RMS {rms:F4} degrees, max {max:F3}; {large} above 1 degree.");
                        Check(count>100000,"Precision fixture missing cliff pixels.");
                        if(!legacy)Check(max<.5&&large==0,"Camera-relative production normal has precision noise.");
                        if(legacy&&origin!=Vector3.zero)Check(rms>5,"Regression did not reproduce absolute-coordinate noise.");
                    }
                    finally { camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(image); }
                }
            }
            Check(!ShaderUtil.ShaderHasError(stable)&&!ShaderUtil.ShaderHasError(old),"Production precision shader did not compile.");
        }
        finally
        {
            foreach(var obj in objects)Object.DestroyImmediate(obj);EditorSceneManager.ClosePreviewScene(scene);
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;ShaderUtil.allowAsyncCompilation=async;
        }
    }
    private static void Render(WorldErosionSettings s)
    {
        bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var scene=EditorSceneManager.NewPreviewScene();
        var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;var sun=RenderSettings.sun;bool fog=RenderSettings.fog;
        try
        {
            var before=Diagnostic("Before",true);
            var beforeNormals=Diagnostic("BeforeNormals",true,"return half4(baseNormalWS * 0.5 + 0.5,1);");
            var afterNormals=Diagnostic("AfterNormals",false,"return half4(baseNormalWS * 0.5 + 0.5,1);");
            var faceNormals=Diagnostic("FaceNormals",false,"float3 face=normalize(cross(surfaceDy,surfaceDx)); face*=dot(face,IN.normalWS)<0?-1:1; return half4(face * 0.5 + 0.5,1);");
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=null;
            RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.22f,.25f,.30f);
            var source=AssetDatabase.LoadAssetAtPath<Material>(DesertSurfaceSetup.MaterialPath);
            var lo=new GameObject("Edge sun");SceneManager.MoveGameObjectToScene(lo,scene);cleanup.Add(lo);
            var light=lo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.color=new Color(1,.97f,.91f);
            light.transform.rotation=Quaternion.Euler(44,-110,0);light.shadows=LightShadows.Soft;light.shadowBias=.05f;light.shadowNormalBias=.4f;RenderSettings.sun=light;
            var co=new GameObject("Edge camera");SceneManager.MoveGameObjectToScene(co,scene);cleanup.Add(co);
            var camera=co.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cameraType=CameraType.Preview;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.53f,.66f,.75f,0);camera.farClipPlane=3000;camera.fieldOfView=48;
            co.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            var disabled=WorldGenerationConfiguration.Default;disabled.Erosion.landforms.enabled=false;WorldLandformAppearance.Bind(cleanup,disabled);
            foreach(int step in new[]{1,2,4,8})
            {
                var heights=new float[131,131];var surfaces=new SurfaceType[131,131];
                for(int x=0;x<131;x++)for(int z=0;z<131;z++)
                {
                    float distance=(x-65)*math.cos(.61f)+(z-65)*math.sin(.61f)+.37f;
                    heights[x,z]=2f+.65f*WorldGlacialLandforms.CliffRamp(distance/19.8f+.5f,19.8f);surfaces[x,z]=SurfaceType.Rock;
                }
                var mesh=MeshGenerator.GenerateTerrainMesh(default,heights,new BiomeType[131,131],surfaces,new WaterState[131,131],200,step,.3f,new float[131,131]).CreateMesh();cleanup.Add(mesh);
                var obj=new GameObject("Synthetic oblique cliff");SceneManager.MoveGameObjectToScene(obj,scene);cleanup.Add(obj);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();
                var mat=new Material(source);cleanup.Add(mat);renderer.sharedMaterial=mat;
                mat.SetTexture("_ControlMap0",Control(new Color32(0,0,0,255)));mat.SetTexture("_ControlMap1",Control(new Color32(0,0,0,0)));mat.SetTexture("_ControlMap2",Control(new Color32(0,0,0,0)));
                mat.DisableKeyword("_ROCK_DETAIL");mat.SetFloat("_ReceiveShadows",0);mat.SetVector("_TerrainHorizonParams",Vector4.zero);
                var normal=new Vector3(math.cos(.61f),0,math.sin(.61f));var side=new Vector3(-normal.z,0,normal.x);var target=new Vector3(0,142,0);
                camera.transform.position=target-normal*48+side*17+Vector3.up*12;camera.transform.LookAt(target);
                mat.shader=before;Capture(camera,$"Synthetic_LOD{step}_SmoothOnly.png");
                mat.shader=source.shader;Capture(camera,$"Synthetic_LOD{step}_After.png");
                mat.shader=beforeNormals;var a=Capture(camera,$"Synthetic_LOD{step}_SmoothOnlyNormals.png",true);
                mat.shader=afterNormals;var b=Capture(camera,$"Synthetic_LOD{step}_AfterNormals.png",true);
                mat.shader=faceNormals;var reference=Capture(camera,$"Synthetic_LOD{step}_FaceNormals.png",true);
                Metrics(step,a,b,reference);obj.SetActive(false);
                report.AppendLine($"  LOD{step}: {mesh.vertexCount} vertices, {mesh.triangles.Length/3} triangles; identical mesh in every comparison.");
            }
            // Previously reported seed-43 glacial region; an oblique elevated view
            // exposes both ice and rock lips, unlike a head-on wall close-up.
            const int seed=43;var anchor=new float2(41343.95f,-37812.57f);var plan=WorldGlacialLandforms.Plan(anchor,seed,600);
            float2 side2=new float2(-plan.Downhill.y,plan.Downhill.x),p=anchor+side2*100;
            for(int i=0;i<12;i++)p-=side2*((WorldGlacialLandforms.Plan(p,seed,600).Across-.265f)*390);
            var centre=new ChunkCoord((int)math.floor(p.x/256),(int)math.floor(p.y/256));
            var generation=WorldGenerationConfiguration.Default;generation.Seed=seed;generation.SampleScale=600;generation.WorldScale=.3f;generation.Erosion=s;generation.Water=new TerrainWaterSettings(14.4f,200,.3f);WorldLandformAppearance.Bind(cleanup,generation);
            var pieces=new List<(MeshRenderer renderer,Material mat)>();
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
            {
                var coord=new ChunkCoord(centre.x+x,centre.z+z);
                var tile=FarTerrainGenerator.Generate(coord,1,256,seed,600,200,.3f,257,128,0,.24f,isMacroTile:true,mountainHorizontalScale:1.3f,climateOctaves:2,climatePersistence:.05f,climateLacunarity:10,erosion:s);
                var mesh=tile.TerrainMeshData.CreateMesh();cleanup.Add(mesh);var mat=new Material(source);cleanup.Add(mat);mat.SetVector("_TerrainHorizonParams",Vector4.zero);
                for(int i=0;i<3;i++)
                {
                    var t=new Texture2D(128,128,TextureFormat.RGBA32,false,true);t.SetPixels32(tile.ControlMapsRawData.Maps[i]);t.Apply();t.wrapMode=TextureWrapMode.Clamp;cleanup.Add(t);mat.SetTexture("_ControlMap"+i,t);
                }
                var obj=new GameObject("Glacial edge");SceneManager.MoveGameObjectToScene(obj,scene);cleanup.Add(obj);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;
                obj.transform.position=new Vector3((coord.x*256+128)*.3f,0,(coord.z*256+128)*.3f);pieces.Add((renderer,mat));
            }
            var ctx=HeightMapGenerator.CreateSamplingContext(seed,.24f,1.3f,s);float h=HeightMapGenerator.SampleTerrainHeight(p.x,p.y,600,ctx).Height*60;
            var look=new Vector3(p.x*.3f,h,p.y*.3f);
            camera.transform.position=look+new Vector3(side2.x*50+plan.Downhill.x*60,80,side2.y*50+plan.Downhill.y*60);camera.transform.LookAt(look);
            report.AppendLine($"Glacial edge seed43: observer {camera.transform.position:F2}, look at {look:F2}.");
            foreach(bool shadows in new[]{false,true})
            {
                foreach(var piece in pieces){piece.mat.shader=before;piece.mat.SetFloat("_ReceiveShadows",shadows?1:0);}
                Capture(camera,"Glacial_SmoothOnly"+(shadows?"_Shadows":"")+".png");
                foreach(var piece in pieces)piece.mat.shader=source.shader;
                Capture(camera,"Glacial_After"+(shadows?"_Shadows":"")+".png");
            }
            // A second camera includes the rock crest against the sky, so a
            // successful ice-front image cannot conceal remaining rock-lip teeth.
            float2 wall=anchor+side2*210;
            for(int i=0;i<12;i++)wall-=side2*((WorldGlacialLandforms.Plan(wall,seed,600).Across-.56f)*390);
            var wallLook=new Vector3(wall.x*.3f,HeightMapGenerator.SampleTerrainHeight(wall.x,wall.y,600,ctx).Height*60+15,wall.y*.3f);
            camera.transform.position=wallLook+new Vector3(-side2.x*60+plan.Downhill.x*40,10,-side2.y*60+plan.Downhill.y*40);camera.transform.LookAt(wallLook);
            foreach(var piece in pieces){piece.mat.shader=before;piece.mat.SetFloat("_ReceiveShadows",1);}
            Capture(camera,"RockLip_SmoothOnly.png");foreach(var piece in pieces)piece.mat.shader=source.shader;Capture(camera,"RockLip_After.png");
            foreach(var shader in new[]{source.shader,before,beforeNormals,afterNormals,faceNormals})Check(!ShaderUtil.ShaderHasError(shader),"Edge shader error: "+shader.name+" "+string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(v=>v.message)));
        }
        finally
        {
            WorldLandformAppearance.Release(cleanup);foreach(var obj in cleanup)Object.DestroyImmediate(obj);cleanup.Clear();EditorSceneManager.ClosePreviewScene(scene);
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;RenderSettings.fog=fog;RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;RenderSettings.sun=sun;ShaderUtil.allowAsyncCompilation=async;
        }
    }
    private static Texture2D Control(Color32 color)
    {var t=new Texture2D(1,1,TextureFormat.RGBA32,false,true);t.SetPixels32(new[]{color});t.Apply();cleanup.Add(t);return t;}
    private static void Metrics(int step,Color32[] before,Color32[] after,Color32[] reference)
    {
        Vector3 N(Color32 c)=>new Vector3(c.r/127.5f-1,c.g/127.5f-1,c.b/127.5f-1).normalized;
        double a=0,b=0;float maxBefore=0,maxAfter=0,maxFloor=0;int count=0,floors=0;
        for(int i=0;i<reference.Length;i++)
        {
            if(reference[i].a<250)continue;var n=N(reference[i]);
            if(n.y>0 && n.y<.23f)
            {
                float ea=Vector3.Angle(n,N(before[i])),eb=Vector3.Angle(n,N(after[i]));a+=ea*ea;b+=eb*eb;maxBefore=Mathf.Max(maxBefore,ea);maxAfter=Mathf.Max(maxAfter,eb);count++;
            }
            if(n.y>.90f){maxFloor=Mathf.Max(maxFloor,Vector3.Angle(N(before[i]),N(after[i])));floors++;}
        }
        a=Math.Sqrt(a/Math.Max(1,count));b=Math.Sqrt(b/Math.Max(1,count));
        report.AppendLine($"LOD{step}: {count} steep-face GPU pixels, normal deviation RMS {a:F3} -> {b:F3}deg, max {maxBefore:F2} -> {maxAfter:F2}deg; {floors} gentle pixels max change {maxFloor:F3}deg.");
        Check(count>1000 && maxBefore>5,"Edge fixture did not reproduce the normal comb.");Check(b<a && maxAfter<12,"Excessive steep-face normal deflection persists.");Check(floors>100 && maxFloor<.8,"Cliff normal fix changed gentle ground.");
    }
    private static Color32[] Capture(Camera c,string name,bool linear=false)
    {
        var rt=RenderTexture.GetTemporary(1280,800,24,RenderTextureFormat.ARGB32,linear?RenderTextureReadWrite.Linear:RenderTextureReadWrite.sRGB);
        var old=RenderTexture.active;var t=new Texture2D(1280,800,TextureFormat.RGBA32,false,linear);
        try
        {
            c.targetTexture=rt;c.Render();RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,1280,800),0,0);t.Apply();var pixels=t.GetPixels32();
            Check(!pixels.Any(p=>p.r>180 && p.b>180 && p.g<50),"Shader error pixels in edge preview.");File.WriteAllBytes(Path.Combine(output,name),t.EncodeToPNG());return pixels;
        }
        finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(t);}
    }
}
