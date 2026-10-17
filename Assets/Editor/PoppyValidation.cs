using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class PoppyValidation
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Set(ChunkRecord record, string name, object value) =>
        typeof(ChunkRecord).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(record, value);
    private static ChunkRecord Fixture(int size, ChunkCoord coord, BiomeType biome = BiomeType.Grassland)
    {
        int n = size + 3;
        var record = new ChunkRecord(coord) { FoliageData = new ChunkFoliageData() };
        var height = new float[n,n]; var slope = new float[n,n]; var biomes = new BiomeType[n,n];
        var surface = new SurfaceType[n,n]; var ground = new GroundCoverType[n,n];
        var plan = new WorldFeaturePlan(n,n); plan.ForestStructure.EnsureFloorEcologyMap(n,n);
        for (int x = 0; x < n; x++) for (int z = 0; z < n; z++)
        {
            height[x,z] = .2f + (coord.x * size + x - 1) * .001f + (coord.z * size + z - 1) * .002f;
            biomes[x,z] = biome; surface[x,z] = SurfaceType.Grass;
        }
        Set(record, "heightMap", height); Set(record, "slopeMap", slope); Set(record, "biomeMap", biomes);
        Set(record, "surfaceTypeMap", surface); Set(record, "groundCoverMap", ground); Set(record, "worldFeaturePlan", plan);
        return record;
    }
    private static FlowerSettings Settings(GameObject prefab) => new FlowerSettings
    {
        patchSpawnChance = 0, maxFlowersPerPatch = 1, minFlowersPerPatch = 1,
        patchCellSize = 128, patchRadiusRange = Vector2.one,
        poppies = new PoppySettings { prefab = prefab, patchNoiseThreshold = 0, patchSpawnChance = 1,
            patchCellSize = 20, minPlantsPerPatch = 24, maxPlantsPerPatch = 32, patchRadiusRange = new Vector2(5,9) }
    };
    private static FlowerInstanceData[] Generate(ChunkRecord record, FlowerSettings settings, int size = 128, int seed = 1937, float scale = .3f)
    {
        FoliageGenerator.GenerateFlowersForChunk(record, settings, seed, size, scale, 10);
        return record.FoliageData.flowerInstances.Where(f => f.isPoppy).ToArray();
    }
    private static string Key(FlowerInstanceData f, int size, ChunkCoord coord)
    {
        float x = f.localPosition.x / .3f + size * .5f + coord.x * size;
        float z = f.localPosition.z / .3f + size * .5f + coord.z * size;
        return $"{Mathf.RoundToInt(x*1000)}:{Mathf.RoundToInt(z*1000)}:{Mathf.RoundToInt(f.localScale.x*10000)}:{Mathf.RoundToInt(f.localRotation.eulerAngles.y*100)}";
    }

    [MenuItem("Tools/Foliage/Poppies/Validate")]
    public static void Run()
    {
        checks = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PoppyAuthoring.PrefabPath);
        Check(prefab != null && prefab.transform.childCount == 0, "Poppy needs one root mesh.");
        var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        var material = prefab.GetComponent<MeshRenderer>().sharedMaterial;
        Check(mesh.vertexCount == 59 && mesh.GetIndexCount(0)/3 == 61 && mesh.subMeshCount == 1, "Poppy geometry budget changed.");
        Check(prefab.GetComponentsInChildren<Collider>(true).Length == 0 && material.enableInstancing, "Poppy must be instanced without per-plant colliders.");
        Check(prefab.transform.localScale == Vector3.one && prefab.transform.localRotation == Quaternion.identity, "Raw mesh transform is not baked.");
        Check(material.shader.name == "Custom/PoppyPainted" && AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")) == PoppyAuthoring.TexturePath, "Poppy texture/shader binding.");
        Check(mesh.bounds.size.y > 1.4f && mesh.bounds.size.y < 1.65f, "Plant/wind bounds.");
        var vertices = mesh.vertices; var normals = mesh.normals; var uv = mesh.uv; var indices = mesh.triangles;
        for (int i = 0; i < vertices.Length; i++)
        {
            Check(float.IsFinite(vertices[i].sqrMagnitude) && normals[i].sqrMagnitude > .8f, "Invalid vertex/normal.");
            Check(uv[i].x >= 0 && uv[i].x <= 1 && uv[i].y >= 0 && uv[i].y <= 1, "UV outside atlas.");
        }
        for (int i = 0; i < indices.Length; i += 3)
            Check(Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]], vertices[indices[i+2]]-vertices[indices[i]]).sqrMagnitude > 1e-12f, "Degenerate triangle.");
        Check(mesh.colors.Any(c => c.b == 1 && c.a == 0) && mesh.colors.Any(c => c.r == 1 && c.a == 1), "Dark center/petal metadata.");

        var settings = Settings(prefab);
        using var record = Fixture(128, new ChunkCoord(0,0));
        var first = Generate(record, settings); var again = Generate(record, settings);
        Check(first.Length > 100 && first.SequenceEqual(again), "Poppy seed determinism/fixture coverage.");
        Check(!first.SequenceEqual(Generate(record, settings, seed: 1938)), "World seed did not change poppies.");
        foreach (var f in first)
        {
            float x = f.localPosition.x / .3f + 64, z = f.localPosition.z / .3f + 64;
            Check(x >= 0 && x < 128 && z >= 0 && z < 128, "Poppy chunk ownership.");
            Check(Mathf.Abs(f.localPosition.y - (.2f+x*.001f+z*.002f)*3f) < .0001f, "Bilinear terrain grounding.");
            Check(f.localScale.x >= .46f && f.localScale.x <= .62f && f.petalColor.r >= 218, "Poppy size or tint inherited pastel flower values.");
        }
        Check(Generate(record, settings, scale: .7f).Length == first.Length, "World scale changed unblocked candidates.");
        foreach (var biome in new[] { BiomeType.Forest, BiomeType.Taiga, BiomeType.Tundra })
        {
            using var other = Fixture(128, new ChunkCoord(0,0), biome);
            Check(Generate(other, settings).Length == 0, "Poppies escaped meadow into " + biome);
        }
        for (int x=0;x<131;x++) for (int z=0;z<131;z++) record.SlopeMap[x,z] = 60;
        Check(Generate(record, settings).Length == 0, "Poppies on steep slope.");
        for (int x=0;x<131;x++) for (int z=0;z<131;z++) { record.SlopeMap[x,z]=0; record.SurfaceTypeMap[x,z]=SurfaceType.Rock; }
        Check(Generate(record, settings).Length == 0, "Poppies on nongrass surface.");
        for (int x=0;x<131;x++) for (int z=0;z<131;z++) { record.SurfaceTypeMap[x,z]=SurfaceType.Grass; record.WorldFeaturePlan.ForestStructure.FloorEcologyMap[x,z]=new float4(0,0,1,0); }
        Check(Generate(record, settings).Length == 0, "Moss failed to exclude poppies.");
        for (int x=0;x<131;x++) for (int z=0;z<131;z++) record.WorldFeaturePlan.ForestStructure.FloorEcologyMap[x,z]=0;
        record.WorldFeaturePlan.Placements.Add(new WorldFeaturePlacement(WorldFeatureType.Boulder, default, 64,64, Quaternion.identity, Vector3.one, 30,30));
        var blocked = Generate(record, settings);
        Check(blocked.Length < first.Length && blocked.All(f => new Vector2(f.localPosition.x,f.localPosition.z).magnitude >= 30*.7f*.7f*.3f), "Planned rock clearance.");
        record.WorldFeaturePlan.Placements.Clear();
        settings.poppies.enabled=false; Check(Generate(record,settings).Length==0,"Poppy toggle ignored.");
        settings.poppies.enabled=true; settings.poppies.prefab=null; Check(Generate(record,settings).Length==0,"Missing poppy prefab still generated.");
        settings.poppies.prefab=prefab; settings.poppies.patchSpawnChance=0; Check(Generate(record,settings).Length==0,"Zero poppy frequency still generated.");
        settings.poppies.patchSpawnChance=1; settings.enableFlowers=false; Check(Generate(record,settings).Length==0,"Master flower toggle ignored."); settings.enableFlowers=true;

        foreach (int origin in new[] { 0, -1 })
        {
            var coord = new ChunkCoord(origin,origin);
            using var whole = Fixture(128,coord);
            var expected = Generate(whole,settings).Select(f => (position: Global(f,128,coord), flower: f)).OrderBy(f => f.position.x).ToArray();
            var split = new HashSet<string>();
            var actual = new List<(Vector3 position, FlowerInstanceData flower)>();
            for(int x=0;x<2;x++) for(int z=0;z<2;z++)
            {
                var c = new ChunkCoord(origin*2+x,origin*2+z); using var part=Fixture(64,c);
                foreach(var f in Generate(part,settings,64))
                { actual.Add((Global(f,64,c), f)); Check(split.Add(Key(f,64,c)),"Duplicate across chunk seam."); }
            }
            var sorted = actual.OrderBy(f => f.position.x).ToArray();
            Check(sorted.Length > 0 && expected.Length == sorted.Length, "Chunk subdivision changed candidate count.");
            for(int i=0;i<expected.Length;i++)
                Check((expected[i].position-sorted[i].position).sqrMagnitude < 1e-8f &&
                    expected[i].flower.localScale==sorted[i].flower.localScale && expected[i].flower.localRotation==sorted[i].flower.localRotation,
                    "Chunk subdivision changed world placement/identity.");
        }
        // Check revision cancellation while the new managed patch iterator is suspended.
        using (var work = FoliageGenerator.GenerateFlowersIncrementally(record,settings,1937,128,.3f,10))
        {
            int guard=0; while(work.MoveNext() && record.FoliageData.flowerInstances.Count==0 && ++guard<100000) { }
            Check(record.FoliageData.flowerInstances.Count>0,"Cancellation fixture did not reach poppy stage.");
            record.FoliageData.ClearFlowers(); while(work.MoveNext()){Check(++guard<100100,"Canceled discovery did not exit.");}
            Check(!record.FoliageData.flowersGenerated && record.FoliageData.flowerInstances.Count==0,"Stale poppy iterator published after invalidation.");
        }
        ValidateBatches(prefab, mesh, material);
        // Adding scarlet accents must not reshuffle or remove the three existing flower families.
        settings.flowerPrefab = settings.tallFlowerPrefab = settings.daisyWeedPrefab = prefab;
        settings.patchSpawnChance=1;settings.patchNoiseThreshold=0;settings.patchCellSize=24;
        settings.tallFlowerPatchSpawnChance=1;settings.tallFlowerPatchNoiseThreshold=0;
        settings.tallFlowerMeadowSpawnChance=1;settings.tallFlowerMeadowNoiseThreshold=0;
        settings.daisyWeedPatchSpawnChance=1;settings.daisyWeedPatchNoiseThreshold=0;
        settings.poppies.enabled=false;Generate(record,settings);
        var legacy=record.FoliageData.flowerInstances.ToArray();
        Check(legacy.Any(f=>f.isTallFlower) && legacy.Any(f=>f.isDaisyWeed),"Existing flora regression fixture incomplete.");
        settings.poppies.enabled=true;Generate(record,settings);
        Check(legacy.SequenceEqual(record.FoliageData.flowerInstances.Where(f=>!f.isPoppy)),"Poppies changed existing flower placement.");
        Debug.Log($"POPPY_VALIDATION_PASS: {checks} checks; geometry, deterministic generation, positive/negative seams, grounding, slope/surface/biome/moss/blockers, toggles, stale discovery, mixed publication and instancing.");
    }

    private static Vector3 Global(FlowerInstanceData flower, int size, ChunkCoord coord) =>
        flower.localPosition + new Vector3((coord.x*size+size*.5f)*.3f,0,(coord.z*size+size*.5f)*.3f);

    private static void ValidateBatches(GameObject prefab, Mesh mesh, Material material)
    {
        var options = new WorldFoliageConfiguration { Grass=new GrassSettings(), Flowers=new FlowerSettings { poppies=new PoppySettings { prefab=prefab } },
            Clover=new CloverSettings { enableClover=false }, Dandelions=new DandelionSettings { enableDandelions=false },
            LilyPads=new LilyPadSettings { enableLilyPads=false }, Cattails=new CattailSettings { enableCattails=false } };
        var assets = new FoliageRenderAssets(options);
        Check(assets.PoppyMesh==mesh && assets.HasFlowerRenderAssets(),"Poppy-only render binding depends on clover/legacy flowers.");
        using var record=Fixture(128,new ChunkCoord(0,0));
        var simple=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var runtime=new ChunkRuntime(record,128,.3f,null,simple,simple,true);
        try
        {
            var target=new ChunkFoliageRuntime { root=runtime.RootTransform }; runtime.FoliageRuntime=target; assets.ApplyTo(target);
            target.flowerMesh=target.tallFlowerMesh=target.daisyWeedMesh=mesh;
            target.flowerMaterial=target.tallFlowerMaterial=target.daisyWeedMaterial=material;
            for(int i=0;i<1040;i++) record.FoliageData.flowerInstances.Add(new FlowerInstanceData(new Vector3(i,0,0),Quaternion.identity,Vector3.one,Color.white,isPoppy:true));
            for(int group=0;group<3;group++)record.FoliageData.flowerInstances.Add(new FlowerInstanceData(Vector3.zero,Quaternion.identity,Vector3.one,Color.white,group==1,group==2));
            record.FoliageData.flowersGenerated=true;
            int objects=Object.FindObjectsByType<Transform>().Length;
            using(var work=new FoliagePublication(record,runtime,FoliagePublicationKind.Flower,0,Matrix4x4.identity))
            { int steps=0;while(!work.Advance()){Check(work.LastSliceInstances<=128 && ++steps<1000,"Unbounded poppy publication.");} Check(work.Published,"Poppy publication failed."); }
            var batches=(List<FlowerRenderBatch>)typeof(ChunkFoliageRuntime).GetField("flowerRenderBatches",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
            Check(batches.Count(b=>b.isPoppy)==2 && batches.Where(b=>b.isPoppy).Sum(b=>b.matrices.Length)==1040,"Poppy batch split/group identity.");
            var stats=new WorldRenderStatsDebugInfo();target.AccumulateFlowerRenderStats(ref stats);
            Check(stats.Flowers.instances==1043 && stats.Flowers.triangles==1043*61,"Mixed flower render stats.");
            Check(Object.FindObjectsByType<Transform>().Length==objects,"Publication created per-poppy objects.");
            // The actual sparse excavation mask must filter the fourth group too, including later publications.
            var bits=Enumerable.Repeat(ulong.MaxValue,4).ToArray();
            var mask=(TerrainSculptSuppression)Activator.CreateInstance(typeof(TerrainSculptSuppression),
                BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{new Dictionary<int2,ulong[]>{{new int2(1,1),bits}},1d,1L},null);
            target.ApplySuppression(mask,128*.3f);
            Check(target.GpuFlowerInstanceCount<1043 && target.GpuFlowerInstanceCount>0,"Excavation mask did not remove poppies.");
            Check(((List<FlowerRenderBatch>)typeof(ChunkFoliageRuntime).GetField("flowerRenderBatches",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target))
                .Where(b=>b.isPoppy).Sum(b=>b.matrices.Length)<1040,"Excavation removed other flowers but retained poppies.");
        }
        finally { runtime.DestroyRuntime();Object.DestroyImmediate(simple); }
    }

    [MenuItem("Tools/Foliage/Poppies/Render Preview")]
    public static void Render()
    {
        Directory.CreateDirectory(".utmp/poppy");EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var material=AssetDatabase.LoadAssetAtPath<Material>(PoppyAuthoring.MaterialPath);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PoppyAuthoring.PrefabPath);var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;
        var root=new GameObject("Poppy preview data");var cameraObject=new GameObject("Poppy preview camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        camera.backgroundColor=new Color(.20f,.29f,.33f);camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.02f;camera.farClipPlane=100;camera.fieldOfView=45;camera.aspect=1.5f;
        var lightObject=new GameObject("Poppy daylight");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.shadows=LightShadows.Soft;lightObject.transform.rotation=Quaternion.Euler(48,-32,0);
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.transform.position=Vector3.down*.004f;ground.transform.localScale=Vector3.one*.8f;
        var groundMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMaterial.SetColor("_BaseColor",new Color(.19f,.29f,.075f));groundMaterial.SetFloat("_Smoothness",0);ground.GetComponent<Renderer>().sharedMaterial=groundMaterial;
        var runtime=new ChunkFoliageRuntime{root=root.transform,poppyMesh=mesh,poppyMaterial=material,flowerPetalColorPropertyId=Shader.PropertyToID("_FlowerPetalColor")};runtime.SetVisible(true);
        var positions=new List<Matrix4x4>();var tints=new List<Vector4>();
        for(int i=0;i<24;i++)
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
            runtime.CacheFlowerBatches(new[]{Matrix4x4.Scale(Vector3.one*.575f)},new[]{Vector4.one},false,false,true);
            Capture("poppy-clump",new Vector3(.80f,1.10f,-1.35f),new Vector3(0,.34f,0));
            Capture("poppy-back",new Vector3(-.85f,.75f,1.3f),new Vector3(0,.34f,0));
            Capture("poppy-overhead",new Vector3(.05f,1.7f,-.18f),new Vector3(0,.34f,0));
            Capture("poppy-side-front",new Vector3(0,.76f,-1.15f),new Vector3(0,.76f,0));
            Capture("poppy-side-back",new Vector3(0,.76f,1.15f),new Vector3(0,.76f,0));
            Capture("poppy-side-left",new Vector3(-1.15f,.76f,0),new Vector3(0,.76f,0));
            Capture("poppy-side-right",new Vector3(1.15f,.76f,0),new Vector3(0,.76f,0));
            runtime.ClearFlowerBatches();runtime.CacheFlowerBatches(positions.ToArray(),tints.ToArray(),false,false,true);
            Capture("poppy-patch",new Vector3(1.5f,1.15f,-2.0f),new Vector3(0,.32f,0));
            for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);
            Check(!ShaderUtil.ShaderHasError(material.shader),"Poppy shader pass compilation failed");
            Run();Debug.Log("POPPY_RENDER_PASS: production instanced flower renderer, front/back/overhead/patch views, all shader passes, actual pixel coverage. No live-world FPS claim.");
            void Capture(string name,Vector3 eye,Vector3 look)
            {
                camera.transform.position=eye;camera.transform.LookAt(look);
                for(int warm=0;warm<2;warm++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                var rgba=pixels.GetPixels32();Check(!rgba.Any(p=>p.r>230 && p.b>230 && p.g<50),"Magenta error pixels");
                File.WriteAllBytes(".utmp/poppy/"+name+".png",pixels.EncodeToPNG());
                Debug.Log($"POPPY_PIXELS {name}: bright={rgba.Count(p=>p.r>110 && p.r>p.g*1.7f && p.r>p.b*1.5f)}, maxRGB={rgba.Max(p=>p.r)}/{rgba.Max(p=>p.g)}/{rgba.Max(p=>p.b)}, sun={Shader.GetGlobalVector("_MainLightColor")}, sunDirection={Shader.GetGlobalVector("_MainLightPosition")}");
                Check(rgba.Count(p=>p.r>110 && p.r>p.g*1.7f && p.r>p.b*1.5f)>100,"Poppy flower heads missing from "+name);
                Object.DestroyImmediate(pixels);
            }
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering-=submit;GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=previousTarget;
            target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);Object.DestroyImmediate(ground);Object.DestroyImmediate(groundMaterial);
        }
    }
    public static void RenderBatch(){try{Render();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}

    public static void RunBatch()
    { try { Run(); EditorApplication.Exit(0); } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }
}
