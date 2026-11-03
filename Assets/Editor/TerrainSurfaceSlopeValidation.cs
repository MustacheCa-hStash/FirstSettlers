using System;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class TerrainSurfaceSlopeValidation
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }

    public static void RunBatch()
    {
        bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        try
        {
            TerrainSlopeValidation.Validate();
            ValidateBoundaries();
            ForestFloorValidation.Run();
            SurfaceBlendValidation.Run();
            MountainSnowValidation.Run();
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-snowOutput");
            string output = index < 0 ? "Logs/TerrainSurfaces" : args[index + 1];
            Directory.CreateDirectory(output); Render(output);
            Debug.Log($"TERRAIN SURFACE SLOPE PASS: {checks} surface/cover boundary checks; forest ecology/placement, snow, surface seams and URP comparisons passed.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally { ShaderUtil.allowAsyncCompilation = async; }
    }

    private static SurfaceType Surface(float slope, BiomeType biome, float river = 0f, float height = 1f) =>
        SurfaceTypeClassifier.Classify(height, slope, river, biome, .24f);

    private static void ValidateBoundaries()
    {
        foreach (BiomeType biome in new[] { BiomeType.Forest, BiomeType.Grassland, BiomeType.Taiga, BiomeType.Tundra })
        {
            Check(Surface(45, biome) == SurfaceType.Grass, "Rooted cover must reach the 45-degree rock boundary.");
            Check(Surface(45.01f, biome) == SurfaceType.Rock, "Rooted cover survives above its limit.");
            Check(Surface(59.99f, biome) == SurfaceType.Rock && Surface(60, biome) == SurfaceType.Cliff,
                "Rock and cliff boundaries disagree.");
        }
        Check(Surface(34, BiomeType.Desert) == SurfaceType.Sand && Surface(34.01f, BiomeType.Desert) == SurfaceType.Rock,
            "Dry loose sand boundary failed.");
        Check(Surface(34, BiomeType.Beach, height: .26f) == SurfaceType.Sand &&
            Surface(34.01f, BiomeType.Beach, height: .26f) == SurfaceType.Rock, "Shore sand boundary failed.");
        Check(Surface(25, BiomeType.Grassland, .8f) == SurfaceType.Mud &&
            Surface(25.01f, BiomeType.Grassland, .8f) == SurfaceType.Rock, "Wet bank mud boundary failed.");
        Check(Surface(50, BiomeType.Snow) == SurfaceType.Snow && Surface(60, BiomeType.Snow) == SurfaceType.Cliff,
            "Snow label or underlying headwall classification changed unexpectedly.");
        foreach (BiomeType biome in Enum.GetValues(typeof(BiomeType)))
            for (int angle = 0; angle <= 90; angle++)
                Check(Surface(angle, biome, 1, .2f) == SurfaceType.Riverbed, "Slope exposed rock below water.");
        Check(BiomeClassifier.Classify(1, .8f, .25f, 42, 0, 0, .24f) == BiomeType.Taiga,
            "Taiga became a meadow at the tree cutoff.");
        Check(BiomeClassifier.Classify(1, .8f, .5f, 42, 0, 0, .24f) == BiomeType.Forest,
            "Deciduous forest became a meadow at the tree cutoff.");
        float previous = 0;
        for (int angle = 0; angle <= 90; angle++)
        {
            float exposure = TerrainSlopePolicy.LooseLitterExposure(angle);
            Check(exposure >= previous && exposure >= 0 && exposure <= 1, "Litter thinning is not bounded/monotonic.");
            previous = exposure;
            float4 floor = ForestFloorPolicy.Evaluate(new float2(12, 37), 1937, .8f, angle, 0, .8f, 0);
            Check(math.all(math.isfinite(floor)) && math.all(floor >= 0) && math.all(floor <= 1), "Invalid slope ecology.");
        }
        Check(TerrainSlopePolicy.LooseLitterExposure(28) == 0 && TerrainSlopePolicy.LooseLitterExposure(42) == 1,
            "Loose litter thinning endpoints failed.");
        float4 gentle = ForestFloorPolicy.Evaluate(new float2(12, 37), 1937, .8f, 20, 0, .8f, 0);
        float4 steep = ForestFloorPolicy.Evaluate(new float2(12, 37), 1937, .8f, 42, 0, .8f, 0);
        Check(steep.y > gentle.y + .6f && steep.x < gentle.x,
            "Steep forest ground did not thin its litter into soil and reduce understory.");
        ValidateTaigaFloor();
    }

    private static void ValidateTaigaFloor()
    {
        const int n = 11;
        var biome = new BiomeType[n,n]; var surface = new SurfaceType[n,n];
        var moisture = new float[n,n]; var slopes = new float[n,n]; var rivers = new float[n,n];
        for (int x=0;x<n;x++) for(int z=0;z<n;z++)
        { biome[x,z]=BiomeType.Taiga; surface[x,z]=SurfaceType.Grass; moisture[x,z]=.8f; slopes[x,z]=42; }
        var cover = GroundCoverMapGenerator.GenerateGroundCoverMap(biome,surface,moisture,slopes,rivers,
            new WorldFeaturePlan(n,n),8,46,new ChunkCoord(-3,2));
        for(int x=1;x<n-1;x++) for(int z=1;z<n-1;z++)
            Check(cover[x,z]==GroundCoverType.BareDirt,"Steep taiga retains a loose needle/grass carpet.");
    }

    private static void Render(string output)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
        GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        QualitySettings.renderPipeline = GraphicsSettings.defaultRenderPipeline;
        RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.32f,.36f,.42f);
        var sun = new GameObject("Slope comparison sun").AddComponent<Light>(); sun.type = LightType.Directional;
        sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50,-80,0); RenderSettings.sun = sun;
        var camera = new GameObject("Slope comparison camera").AddComponent<Camera>(); camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.35f,.46f,.57f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 1000; camera.orthographic = true; camera.orthographicSize = 53;
        camera.transform.position = new Vector3(40,160,-130); camera.transform.LookAt(new Vector3(40,48,10));
        var mesh = Ramp();
        camera.aspect = 1200f / 700f;
        Vector2 minimum = new Vector2(float.MaxValue,float.MaxValue), maximum = -minimum;
        foreach(var vertex in mesh.vertices)
        {
            Vector3 local=camera.transform.InverseTransformPoint(vertex);
            minimum=Vector2.Min(minimum,new Vector2(local.x,local.y));
            maximum=Vector2.Max(maximum,new Vector2(local.x,local.y));
        }
        Vector2 midpoint=(minimum+maximum)*.5f, extent=(maximum-minimum)*.5f;
        camera.transform.position+=camera.transform.right*midpoint.x+camera.transform.up*midpoint.y;
        camera.orthographicSize=Mathf.Max(extent.y,extent.x/camera.aspect)*1.12f;
        var ground = new GameObject("0 to 80 degree slope ramp");
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat"));
        ground.AddComponent<MeshRenderer>().sharedMaterial = material;
        material.SetFloat("_ReceiveShadows",0); material.SetVector("_TerrainHorizonParams",Vector4.zero);
        try
        {
            foreach (BiomeType biome in new[] { BiomeType.Grassland, BiomeType.Forest, BiomeType.Taiga, BiomeType.Desert, BiomeType.Beach, BiomeType.Snow })
                for(int mode=0;mode<2;mode++)
                {
                    bool legacy=mode==0; float river=biome==BiomeType.Beach?.8f:0;
                    const int width=163, depth=7;
                    var surfaces=new SurfaceType[width,depth]; var covers=new GroundCoverType[width,depth];
                    var biomes=new BiomeType[width,depth]; var fields=new ForestStructureFields(width,depth);
                    fields.EnsureFloorEcologyMap(width,depth);
                    for(int x=0;x<width;x++) for(int z=0;z<depth;z++)
                    {
                        float slope=math.clamp((x-1)*.5f,0,80); biomes[x,z]=biome;
                        surfaces[x,z]=legacy?LegacySurface(slope,biome,river):Surface(slope,biome,river);
                        if(surfaces[x,z]!=SurfaceType.Grass)continue;
                        if(biome==BiomeType.Forest)
                        {
                            var floor=ForestFloorPolicy.Evaluate(new float2((x-1)*.5f,(z-1)*5),1937,.8f,slope,0,.8f,0);
                            if(legacy)floor.y=math.saturate(floor.y-TerrainSlopePolicy.LooseLitterExposure(slope)*.65f+
                                math.smoothstep(24,45,slope)*.32f);
                            fields.FloorEcologyMap[x,z]=floor;
                        }
                    }
                    if(biome==BiomeType.Taiga)
                    {
                        var slopeMap=new float[width,depth];var moisture=new float[width,depth];
                        for(int x=0;x<width;x++)for(int z=0;z<depth;z++)
                        {slopeMap[x,z]=legacy?0:math.clamp((x-1)*.5f,0,80);moisture[x,z]=.8f;}
                        // A zero thinning slope reproduces the original taiga patch selection.
                        covers=GroundCoverMapGenerator.GenerateGroundCoverMap(biomes,surfaces,moisture,slopeMap,
                            new float[width,depth],new WorldFeaturePlan(width,depth),160,1937,default);
                    }
                    var controls=TerrainControlMapBuilder.BuildRaw(surfaces,covers,forestStructure:fields,biomeMap:biomes);
                    var maps=new Texture2D[3];
                    try
                    {
                        for(int m=0;m<3;m++)
                        {
                            maps[m]=new Texture2D(controls.Width,controls.Height,TextureFormat.RGBA32,false,true)
                                {wrapMode=TextureWrapMode.Clamp};
                            maps[m].SetPixels32(controls.Maps[m]);maps[m].Apply();material.SetTexture("_ControlMap"+m,maps[m]);
                        }
                        string label=biome==BiomeType.Beach?"wet-bank":biome.ToString().ToLowerInvariant();
                        Capture(camera,Path.Combine(output,label+(legacy?"-before.png":"-after.png")));
                    }
                    finally{foreach(var map in maps)if(map!=null)Object.DestroyImmediate(map);}
                }
            foreach(var message in ShaderUtil.GetShaderMessages(material.shader))
                Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
        }
        finally
        {
            Object.DestroyImmediate(ground);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);
            Object.DestroyImmediate(sun.gameObject);Object.DestroyImmediate(camera.gameObject);
            GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;
        }
    }

    // Frozen pre-change surface thresholds; only used for the controlled comparison.
    private static SurfaceType LegacySurface(float slope,BiomeType biome,float river)
    {
        if(slope>=70)return SurfaceType.Cliff;
        if(biome==BiomeType.Snow)return SurfaceType.Snow;
        if(slope>50)return SurfaceType.Rock;
        if(slope>40&&(river>=TerrainWaterSettings.RiverBankThreshold||biome==BiomeType.Desert))return SurfaceType.Rock;
        if(river>=TerrainWaterSettings.RiverBankThreshold)return slope>=40?SurfaceType.Rock:SurfaceType.Mud;
        return biome==BiomeType.Desert?SurfaceType.Sand:SurfaceType.Grass;
    }

    private static Mesh Ramp()
    {
        const int n=161; var vertices=new Vector3[n*2]; var normals=new Vector3[n*2];
        var uv=new Vector2[n*2];var colors=new Color[n*2];var triangles=new int[(n-1)*6]; float height=0;
        for(int x=0;x<n;x++)
        {
            float angle=x*.5f; if(x>0)height+=math.tan(math.radians(angle-.25f))*.5f;
            for(int z=0;z<2;z++)
            {
                int i=z*n+x;vertices[i]=new Vector3(x*.5f,height,z*20);
                normals[i]=new Vector3(-math.sin(math.radians(angle)),math.cos(math.radians(angle)),0);
                uv[i]=new Vector2(x/(float)(n-1),z);colors[i]=Color.white;
            }
            if(x==n-1)continue;int t=x*6;
            triangles[t]=x;triangles[t+1]=x+n;triangles[t+2]=x+1;
            triangles[t+3]=x+1;triangles[t+4]=x+n;triangles[t+5]=x+n+1;
        }
        var mesh=new Mesh{vertices=vertices,normals=normals,uv=uv,colors=colors,triangles=triangles};mesh.RecalculateBounds();return mesh;
    }

    private static void Capture(Camera camera,string path)
    {
        var target=new RenderTexture(1200,700,24);var pixels=new Texture2D(1200,700,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,1200,700),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);}
    }
}
