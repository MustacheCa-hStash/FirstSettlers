using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Regression sites supplied in the seed 67/68 screenshots, using the scene's actual settings.
public static class TerrainRidgeValidation
{
    private static int checks;
    private static bool baseline;
    private static string output;
    private static readonly StringBuilder report = new();
    private static readonly (string name, int seed, Vector2 world)[] Sites = {
        ("67_ForestRidge",67,new Vector2(-211.09f,704.97f)),
        ("67_LakesideCollider",67,new Vector2(-110.05f,780.91f)),
        ("67_HighUpland",67,new Vector2(286.72f,1369.66f)),
        ("68_GrasslandSplit",68,new Vector2(-261.91f,504.08f)),
        ("66_Control",66,new Vector2(286.72f,1369.66f))
    };
    private static void Check(bool ok, string message) { checks++; if (!baseline && !ok) throw new Exception(message); }
    private static string Arg(string name) { var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i<0?null:args[i+1]; }
    internal static WorldErosionSettings SceneSettings()
    {
        string path=Arg("-terrainIssueScene");
        var s=WorldErosionSettings.Default;
        string text=File.ReadAllText(path);text=text.Substring(text.IndexOf("  erosion:",StringComparison.Ordinal));
        foreach(var field in typeof(WorldErosionSettings).GetFields())
        {
            var match=Regex.Match(text,@"^    "+field.Name+@": ([^\r\n]+)",RegexOptions.Multiline);
            if(!match.Success)continue;
            object boxed=s;
            if(field.FieldType==typeof(float))field.SetValue(boxed,float.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture));
            else if(field.FieldType==typeof(int))field.SetValue(boxed,int.Parse(match.Groups[1].Value));
            else if(field.FieldType==typeof(bool))field.SetValue(boxed,match.Groups[1].Value=="1");
            s=(WorldErosionSettings)boxed;
        }
        object landforms=s.landforms;
        foreach(var field in typeof(WorldLandformSettings).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
        {
            var match=Regex.Match(text,@"^      "+field.Name+@": ([^\r\n]+)",RegexOptions.Multiline);
            if(!match.Success)continue;
            if(field.FieldType==typeof(float))field.SetValue(landforms,float.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture));
            else if(field.FieldType==typeof(int))field.SetValue(landforms,int.Parse(match.Groups[1].Value));
            else if(field.FieldType==typeof(bool))field.SetValue(landforms,match.Groups[1].Value=="1");
        }
        s.landforms=(WorldLandformSettings)landforms;
        return s.Sanitized();
    }
    public static void RunBatch()
    {
        try
        {
            baseline=Environment.GetCommandLineArgs().Contains("-terrainIssueBaseline");
            output=Arg("-landformOutput");Directory.CreateDirectory(output);report.Clear();checks=0;
            var settings=SceneSettings();
            ValidateUplandProfile(settings.landforms);
            foreach(var site in Sites) Survey(site.name,site.seed,site.world,settings);
            RenderSites(settings);
            report.AppendLine($"TERRAIN RIDGES {(baseline?"BASELINE":"PASS")}: {checks} checks.");
            File.WriteAllText(Path.Combine(output,"validation.txt"),report.ToString());Debug.Log(report.ToString());
            EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static void ValidateUplandProfile(WorldLandformSettings settings)
    {
        const float water=.24f;
        foreach(int seed in new[]{66,67,68,-733})
        {
            var p=new float2(-873.2f,4565.5f);
            float last=WorldLandforms.FinishUplands(p,water,water,1,seed,600,water,settings);
            for(int i=1;i<=20000;i++)
            {
                float h=water+i*.0005f;
                float shaped=WorldLandforms.FinishUplands(p,h,h,1,seed,600,water,settings);
                Check(math.isfinite(shaped) && shaped>=last-.00001f,"Soft upland profile folds over itself.");
                Check(shaped-last<.002f,"Soft upland profile jumps at a terrace repeat.");
                if(h<=water+1.4f)Check(shaped==h,"Soft upland shaping changed low elevations.");
                last=shaped;
            }
            Check(WorldLandforms.FinishUplands(p,water-.1f,4,1,seed,600,water,settings)==water-.1f,"Upland remap changed submerged terrain.");
            var off=settings;off.enabled=false;
            Check(WorldLandforms.FinishUplands(p,4,4,1,seed,600,water,off)==4,"Disabled uplands changed terrain.");
            Check(WorldLandforms.FinishUplands(p,4,4,0,seed,600,water,settings)==4,"Ordinary mountains received plateau terraces.");
            Check(WorldLandforms.Surface(p,seed,600,4,10,0,BiomeType.Desert,water,settings)!=SurfaceType.DryWash,
                "High rock shelf received a lowland dry-wash material.");
        }
        report.AppendLine("Soft upland profile: monotone, continuous terrace repeats, lowland/water bypass and high-bedrock policy pass.");
    }
    public static void ProbeBatch()
    {
        try
        {
            var settings=SceneSettings();var log=new StringBuilder();
            foreach(var site in Sites)
            {
                var center=new float2(site.world.x,site.world.y)/.3f;
                var context=HeightMapGenerator.CreateSamplingContext(site.seed,.24f,1.3f,settings);
                float H(float2 p)=>HeightMapGenerator.SampleTerrainHeight(p.x,p.y,600,context).Height;
                float max=0;float2 worst=center;
                for(int x=-128;x<=128;x+=4)for(int z=-128;z<=128;z+=4)
                {
                    var p=center+new float2(x,z);float d=math.abs(H(p+new float2(1,0))-H(p));
                    if(d>max){max=d;worst=p;}
                }
                log.AppendLine(site.name+" maximum 1-unit rise "+max*60+"m at "+worst);
                foreach(var p in new[]{center,worst,worst+new float2(1,0)})
                {
                    var b=WorldTerrainHeight.Base(p,600,site.seed,1.3f,settings);
                    float e=WorldTerrainHeight.Erode(p,b,600,site.seed,1.3f,settings);
                    float a=WorldTerrainHeight.ShapeForAccess(p,b,e,.24f,site.seed,settings);
                    var r=WorldLandforms.Region(p,site.seed,600,settings.landforms);
                    log.AppendLine($"  {p}: base {b.Height:F5} erosion {e:F5} access {a:F5} finish {H(p):F5}, gradient {b.Gradient}, mountainContribution {b.MountainContribution:F5} plateau {b.LandformMask:F5}, arid {r.Aridity.x:F5} basin {r.Basin.x:F5} wash {WorldLandforms.DryWash(p,site.seed,600):F5}");
                }
            }
            Debug.Log(log.ToString());EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static void Survey(string name,int seed,Vector2 world,WorldErosionSettings settings)
    {
        var context=HeightMapGenerator.CreateSamplingContext(seed,.24f,1.3f,settings);
        float H(float2 p)=>HeightMapGenerator.SampleTerrainHeight(p.x,p.y,600,context).Height;
        var center=new float2(world.x,world.y)/.3f;
        float maxRise=0,maxCurvature=0;int gentle=0,total=0;
        var centerBase=WorldTerrainHeight.Base(center,600,seed,1.3f,settings);
        float eroded=WorldTerrainHeight.Erode(center,centerBase,600,seed,1.3f,settings);
        const int width=259;
        using var sampled=new NativeArray<float>(width*width,Allocator.TempJob);
        new SurveyJob{center=center,seed=seed,settings=settings,heights=sampled}.Schedule(sampled.Length,64).Complete();
        for(int x=-128;x<=128;x++)for(int z=-128;z<=128;z++)
        {
            int i=(x+129)*width+z+129;
            float h=sampled[i],hx=sampled[i+width],hz=sampled[i+1];
            maxRise=math.max(maxRise,math.max(math.abs(hx-h),math.abs(hz-h))*60f);
            maxCurvature=math.max(maxCurvature,math.abs(sampled[i-width]+hx-2*h)*60f);
            float slope=math.degrees(math.atan(math.length(new float2(hx-h,hz-h))*200f));
            if(slope<15)gentle++;total++;Check(math.isfinite(h),"Nonfinite reported-site height.");
        }
        report.AppendLine($"{name}, seed {seed}, world XZ {world}: height {H(center)*60:F3}m, base {centerBase.Height*60:F3}m, erosion delta {(eroded-centerBase.Height)*60:F3}m; max 0.3m edge rise {maxRise:F3}m, second difference {maxCurvature:F3}m, gentle <15deg {gentle*100f/total:F1}%.");
        Debug.Log($"Ridge survey {name}: rise {maxRise:F4}m, curvature {maxCurvature:F4}m, gentle {gentle*100f/total:F1}%.");
        Check(maxRise<1.5f && maxCurvature<.5f,$"{name}: narrow terrain spike returned.");
        if(name=="67_HighUpland")
        {
            Check(gentle>total*.25f,"Reported high upland lacks broad gentle building ground.");
            Check(maxRise<.6f && maxCurvature<.03f,"High upland has abrupt elevation detail.");
        }
        // A synthetic curved boundary also catches vertical fan triangles independently of the terrain field.
        const int size=128;var coord=new ChunkCoord((int)math.floor(center.x/size),(int)math.floor(center.y/size));
        var field=HeightMapGenerator.GenerateTerrainHeightField(size,seed,600,coord,.24f,1.3f,200,settings);
        var biomes=new BiomeType[size+3,size+3];var surfaces=new SurfaceType[size+3,size+3];var water=new WaterState[size+3,size+3];
        using var native=new ChunkRecord.NativeTerrainData(field.HeightMap,field.SlopeMap,biomes,surfaces,water,
            new GroundCoverType[size+3,size+3],field.RiverMaskMap);
        foreach(int step in new[]{1,2,4,8,16})
        {
            var data=MeshGenerator.GenerateTerrainMesh(coord,field.HeightMap,biomes,surfaces,water,200,step,.3f,field.RiverMaskMap);
            var mesh=data.CreateMesh();int vertical=0;double area=0;var vertices=mesh.vertices;var indices=mesh.triangles;
            var production=MeshGenerator.GenerateTerrainMesh(coord,native,200,step,.3f).CreateMesh();
            try
            {
                Check(vertices.SequenceEqual(production.vertices) && indices.SequenceEqual(production.triangles),"Native/managed terrain topology differs.");
            }
            finally{Object.DestroyImmediate(production);}
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                float projected=Vector3.Cross(b-a,c-a).y;
                if(projected<1e-6f)vertical++;area+=projected*.5f;
            }
            report.AppendLine($"  LOD step {step}: {vertical} vertical/reversed triangles; projected area {area:F3}m2.");
            Check(vertical==0,$"{name} step {step}: vertical/reversed border triangles.");
            Check(Math.Abs(area-size*size*.09)<.02,"LOD stitching does not cover chunk exactly once.");
            if(step==1) ValidateCollision(name,field.HeightMap,mesh);
            Object.DestroyImmediate(mesh);
        }
    }
    [BurstCompile(FloatMode=FloatMode.Strict,FloatPrecision=FloatPrecision.Standard)]
    private struct SurveyJob : IJobParallelFor
    {
        public float2 center;
        public int seed;
        public WorldErosionSettings settings;
        [WriteOnly] public NativeArray<float> heights;
        public void Execute(int index)
        {
            int x=index/259,z=index%259;
            float2 p=center+new float2(x-129,z-129);
            heights[index]=HeightMapGenerator.SampleTerrainHeightNative(p.x,p.y,600,seed+60000,.24f,1.3f,settings).Height;
        }
    }
    private static void ValidateCollision(string name,float[,] heights,Mesh render)
    {
        int size=heights.GetLength(0)-3;
        var mesh=ColliderMeshGenerator.GenerateColliderMesh(heights,200,baseline?8:ColliderMeshGenerator.RuntimeStep,.3f).CreateMesh();
        var obj=new GameObject("Ridge collision fixture"){hideFlags=HideFlags.HideAndDontSave};
        var collider=obj.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();
        try
        {
            int misses=0;float maxError=0;
            for(int x=0;x<size;x+=3)for(int z=0;z<size;z+=3)
            {
                const float fx=.37f,fz=.61f;
                float a=heights[x+1,z+1],b=heights[x+1,z+2],c=heights[x+2,z+2],d=heights[x+2,z+1];
                float expected=(fx<fz?a+(b-a)*fz+(c-b)*fx:a+(d-a)*fx+(c-d)*fz)*60;
                var origin=new Vector3((x+fx-size*.5f)*.3f,expected+100,(z+fz-size*.5f)*.3f);
                bool hit=collider.Raycast(new Ray(origin,Vector3.down),out var result,300);
                if(!hit)misses++;else maxError=Mathf.Max(maxError,Mathf.Abs(result.point.y-expected));
                Check(hit && Mathf.Abs(result.point.y-expected)<.002f,$"{name}: physics ray misses rendered LOD0 surface at {x},{z}.");
            }
            report.AppendLine($"  PhysX collider: {mesh.vertexCount} vertices, {misses} missed rays, maximum surface error {maxError:F6}m.");
            if(!baseline)
            {
                var cv=mesh.vertices;var rv=render.vertices;var ct=mesh.triangles;var rt=render.triangles;
                Check(cv.Length==rv.Length && ct.Length==rt.Length,"Collider topology differs from LOD0.");
                for(int i=0;i<ct.Length;i++)
                    Check(Vector3.SqrMagnitude(cv[ct[i]]-rv[rt[i]])<1e-10f,"Collider triangle differs from LOD0.");
            }
        }
        finally{Object.DestroyImmediate(obj);Object.DestroyImmediate(mesh);}
    }
    private static void RenderSites(WorldErosionSettings settings)
    {
        bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var scene=EditorSceneManager.NewPreviewScene();var cleanup=new List<Object>();
        var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;var sun=RenderSettings.sun;bool fog=RenderSettings.fog;
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=null;
            RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.34f,.36f,.39f);
            var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat");
            var lightObject=new GameObject("Ridge sun");SceneManager.MoveGameObjectToScene(lightObject,scene);cleanup.Add(lightObject);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;
            light.color=new Color(1f,.94f,.82f);light.transform.rotation=Quaternion.Euler(38,-125,0);RenderSettings.sun=light;
            var cameraObject=new GameObject("Ridge camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);cleanup.Add(cameraObject);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cameraType=CameraType.Preview;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.50f,.65f,.73f);camera.farClipPlane=3000;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            foreach(var site in Sites)
            {
                var objects=new List<GameObject>();var p=site.world/.3f;
                var coord=new ChunkCoord((int)Mathf.Floor(p.x/128),(int)Mathf.Floor(p.y/128));
                for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)
                {
                    var c=new ChunkCoord(coord.x+x,coord.z+z);
                    var tile=FarTerrainGenerator.Generate(c,1,128,site.seed,600,200,.3f,129,32,0,.24f,mountainHorizontalScale:1.3f,
                        climateOctaves:2,climatePersistence:.05f,climateLacunarity:10,erosion:settings);
                    var field=HeightMapGenerator.GenerateTerrainHeightField(128,site.seed,600,c,.24f,1.3f,200,settings);
                    var mesh=MeshGenerator.GenerateTerrainMesh(c,field.HeightMap,new BiomeType[131,131],new SurfaceType[131,131],new WaterState[131,131],200,2,.3f,field.RiverMaskMap).CreateMesh();cleanup.Add(mesh);
                    var material=new Material(source);cleanup.Add(material);material.SetFloat("_ReceiveShadows",0);material.SetVector("_TerrainHorizonParams",Vector4.zero);
                    for(int i=0;i<3;i++)
                    {
                        var t=new Texture2D(tile.ControlMapsRawData.Width,tile.ControlMapsRawData.Height,TextureFormat.RGBA32,false,true);
                        t.SetPixels32(tile.ControlMapsRawData.Maps[i]);t.Apply();t.wrapMode=TextureWrapMode.Clamp;cleanup.Add(t);material.SetTexture("_ControlMap"+i,t);
                    }
                    var terrain=new GameObject(site.name);SceneManager.MoveGameObjectToScene(terrain,scene);cleanup.Add(terrain);objects.Add(terrain);
                    terrain.AddComponent<MeshFilter>().sharedMesh=mesh;terrain.AddComponent<MeshRenderer>().sharedMaterial=material;
                    terrain.transform.position=new Vector3((c.x*128+64)*.3f,0,(c.z*128+64)*.3f);
                }
                var generation=WorldGenerationConfiguration.Default;generation.Seed=site.seed;generation.SampleScale=600;generation.WorldScale=.3f;generation.Erosion=settings;
                WorldLandformAppearance.Bind(cleanup,generation);
                var context=HeightMapGenerator.CreateSamplingContext(site.seed,.24f,1.3f,settings);
                float h=HeightMapGenerator.SampleTerrainHeight(p.x,p.y,600,context).Height*60;
                var target=new Vector3(site.world.x,h,site.world.y);
                camera.transform.position=target+new Vector3(55,55,-65);camera.transform.LookAt(target);camera.fieldOfView=53;
                var rt=RenderTexture.GetTemporary(1280,800,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                var previous=RenderTexture.active;var texture=new Texture2D(1280,800,TextureFormat.RGB24,false);
                try
                {
                    camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1280,800),0,0);texture.Apply();
                    File.WriteAllBytes(Path.Combine(output,site.name+".png"),texture.EncodeToPNG());
                }
                finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(texture);}
                foreach(var obj in objects)obj.SetActive(false);
            }
        }
        finally
        {
            WorldLandformAppearance.Release(cleanup);foreach(var obj in cleanup)Object.DestroyImmediate(obj);
            EditorSceneManager.ClosePreviewScene(scene);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;
            RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;RenderSettings.sun=sun;RenderSettings.fog=fog;ShaderUtil.allowAsyncCompilation=async;
        }
    }
}
