using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class WorldLandformValidation
{
    private static int checks;
    private static string output;
    private static readonly List<(string label, int seed, float2 position)> sites = new();
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }

    [MenuItem("Tools/Terrain/Validate Regional Landforms")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before running the isolated landform fixture.");
        checks = 0; sites.Clear();
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-landformOutput");
        output = index < 0 ? "Logs/WorldLandforms" : args[index + 1]; Directory.CreateDirectory(output);
        DesertSurfaceSetup.Install();
        ValidateRegions(); ValidateDunesAndWashes(); ValidateSettings(); ValidateClimateExtent(); ValidateDesertRocks();
        ValidateDryDeserts();
        foreach (var site in sites.Take(4)) ValidateIntegration(site.seed, site.position);
        TerrainSlopeValidation.Validate();
        WorldErosionValidation.Run();
        TerrainHeightSamplingValidation.Run();
        MountainSnowValidation.Run();
        ValidateShaderFields();
        ValidateDesertTextureImports();
        RenderPreviews();
        File.WriteAllText(Path.Combine(output,"validation.txt"), $"WORLD LANDFORMS PASS: {checks} checks.\n" +
            string.Join("\n", sites.Select(s => $"{s.label}: seed {s.seed}, terrain XZ {s.position}")));
        Debug.Log($"WORLD LANDFORMS PASS: {checks} checks; analytic gradients, broad climate, dunes/washes, settings/identity, repeatability, negative-coordinate seams, near/far/macro/collision/density agreement and rendered previews.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static void ValidateRegions()
    {
        var s = WorldErosionSettings.Default;
        var old = s; old.landforms.enabled = false;
        int dry = 0, sandstone = 0, otherCliffs = 0, plateau = 0, sand = 0, basin = 0;
        float gradientError = 0f;
        var classes = new HashSet<WorldRockClass>();
        float bestMesa = -1f, bestSand = -1f, bestMountain = -1f, bestBasin = -1f;
        (int seed, float2 p) mesa = default, dunes = default, mountains = default, playa = default;
        foreach (int seed in new[] { 7, 42, 14567 })
        for (int x = -40; x <= 40; x++) for (int z = -40; z <= 40; z++)
        {
            float2 p = new float2(x * 411.37f, z * 397.19f);
            var r = WorldLandforms.Region(p, seed, 600f, s.landforms);
            classes.Add(WorldLandforms.RockClass(p,seed,600f,s.landforms));
            var b = WorldTerrainHeight.Base(p,600f,seed,1.3f,s);
            var original = WorldTerrainHeight.Base(p,600f,seed,1.3f,old);
            Check(math.isfinite(b.Height) && math.all(math.isfinite(b.Gradient)), "Nonfinite landform base.");
            var dx = new float2(0.5f,0f); var dz = new float2(0f,0.5f);
            var numeric = new float2(WorldTerrainHeight.Base(p+dx,600f,seed,1.3f,s).Height-WorldTerrainHeight.Base(p-dx,600f,seed,1.3f,s).Height,
                WorldTerrainHeight.Base(p+dz,600f,seed,1.3f,s).Height-WorldTerrainHeight.Base(p-dz,600f,seed,1.3f,s).Height);
            float error = math.distance(numeric,b.Gradient); gradientError=math.max(error,gradientError);
            Check(error < 0.002f, $"Landform gradient mismatch {error} at seed {seed}, {p}.");
            if (r.Aridity.x > 0.9f) dry++;
            if (r.Plateau.x > 0.45f && r.Aridity.x > 0.8f) sandstone++;
            if (b.Height-original.Height > 0.1f && r.Aridity.x < 0.15f) otherCliffs++;
            if (b.LandformMask > 0.1f) plateau++;
            if (r.Sand.x > 0.6f && original.Height > 0.4f && b.LandformMask < 0.05f) sand++;
            if (r.Basin.x > 0.7f) basin++;
            float mesaScore = r.Plateau.x * math.smoothstep(0.4f,0.8f,original.Height) * r.Aridity.x;
            if (mesaScore > bestMesa && original.MountainMask < 0.15f)
            { bestMesa=mesaScore; mesa=(seed,p); }
            float sandScore = r.Sand.x * r.Aridity.x * (1f-r.Basin.x) * (1f-math.saturate(b.LandformMask*4f));
            if(sandScore > bestSand && original.Height > 0.5f && original.MountainMask < 0.1f)
            { bestSand=sandScore; dunes=(seed,p); }
            float mountainScore = r.Aridity.x * original.MountainContribution;
            if (mountainScore > bestMountain) { bestMountain=mountainScore; mountains=(seed,p); }
            if(r.Basin.x > bestBasin && original.Height > 0.45f && original.MountainMask < 0.05f)
            { bestBasin=r.Basin.x; playa=(seed,p); }
        }
        Check(dry>500 && sandstone>100 && otherCliffs>50 && plateau>200 && sand>100 && basin>50 && classes.Count==4,
            $"Missing regional variety: dry {dry}, sandstone {sandstone}, temperate relief {otherCliffs}, plateau {plateau}, sand {sand}, basin {basin}, classes {classes.Count}.");
        // Choose a real accumulated dune at the centre of its preview tile, after rivers and erosion.
        bestSand=-1f;
        var noDunes=s;noDunes.landforms.duneHeight=0f;
        foreach(int seed in new[]{7,42,14567})
        {
            var context=HeightMapGenerator.CreateSamplingContext(seed,.24f,1.3f,s);
            var bareContext=HeightMapGenerator.CreateSamplingContext(seed,.24f,1.3f,noDunes);
            for(int x=-32;x<=32;x++)for(int z=-32;z<=32;z++)
            {
                var p=new float2((x+.5f)*512f,(z+.5f)*512f);
                var r=WorldLandforms.Region(p,seed,600,s.landforms);
                if(r.Aridity.x<.8f || r.Sand.x<.55f || r.Basin.x>.2f)continue;
                float height=HeightMapGenerator.SampleTerrainHeight(p.x,p.y,600,context).Height;
                float bare=HeightMapGenerator.SampleTerrainHeight(p.x,p.y,600,bareContext).Height;
                if(height-bare>bestSand){bestSand=height-bare;dunes=(seed,p);}
            }
        }
        Check(bestSand>.025f,"No actual dune accumulation survived the complete terrain pipeline.");
        sites.Add(("Plateaus",mesa.seed,mesa.p)); sites.Add(("Dunes",dunes.seed,dunes.p));
        sites.Add(("DryMountains",mountains.seed,mountains.p)); sites.Add(("DryBasin",playa.seed,playa.p));
        float bestWash=-1f; (int seed,float2 p) washSite=default;
        foreach(int seed in new[]{7,42,14567})
        {
            var context=HeightMapGenerator.CreateSamplingContext(seed,.24f,1.3f,s);
            float H(float2 p)=>HeightMapGenerator.SampleTerrainHeight(p.x,p.y,600,context).Height;
            for(int x=-24;x<=24;x++)for(int z=-24;z<=24;z++)
            {
                var p=new float2((x+.5f)*512f,(z+.5f)*512f);
                var r=WorldLandforms.Region(p,seed,600,s.landforms);
                float wash=WorldLandforms.DryWash(p,seed,600);
                if(r.Aridity.x<.85f || wash<.6f || r.Basin.x>.3f)continue;
                float h=H(p);
                var gradient=new float2(H(p+new float2(4,0))-H(p-new float2(4,0)),H(p+new float2(0,4))-H(p-new float2(0,4)))/8f;
                float slope=TerrainSlopePolicy.FromGradient(math.length(gradient),200);
                if(WorldLandforms.Surface(p,seed,600,h,slope,0,BiomeType.Desert,.24f,s.landforms)!=SurfaceType.DryWash)continue;
                float score=wash*(1f-math.smoothstep(12,30,slope));
                if(score>bestWash){bestWash=score;washSite=(seed,p);}
            }
        }
        Check(bestWash>.25f,"No actual dry wash survived terrain shaping and surface classification.");
        sites.Add(("DryWashes",washSite.seed,washSite.p));
        Debug.Log($"LANDFORM SURVEY: dry {dry}, sandstone {sandstone}, temperate relief {otherCliffs}, plateau {plateau}, sand {sand}, basin {basin}; max base-gradient error {gradientError:G5}.");
    }

    private static void ValidateDunesAndWashes()
    {
        var s = WorldLandformSettings.Default; var noDunes=s; noDunes.duneHeight=0f;
        var noWash=noDunes; noWash.dryWashDepth=0f;
        int duneSamples=0,washSamples=0,rock=0,sand=0;
        float largestDune=0f, duneSlope=0f;
        foreach(var site in sites.Where(t=>t.label=="Dunes" || t.label=="DryBasin"))
        for(int x=-128;x<=128;x++) for(int z=-16;z<=16;z++)
        {
            float2 p=site.position+new float2(x*4f,z*16f);
            var input=new WorldBaseSample(.9f,float2.zero,0f,0f,0f);
            float h=WorldLandforms.FinishSurface(p,input,.9f,0f,600f,site.seed,.24f,s);
            float without=WorldLandforms.FinishSurface(p,input,.9f,0f,600f,site.seed,.24f,noDunes);
            float delta=h-without; if(delta>.002f)duneSamples++;
            largestDune=math.max(largestDune,delta);
            Check(delta>=-1e-6f && delta<=s.duneHeight+1e-6f,"Dunes remove bedrock or exceed their amplitude.");
            var next=p+new float2(1f,0f);
            float nextDune=WorldLandforms.FinishSurface(next,input,.9f,0f,600f,site.seed,.24f,s)-
                WorldLandforms.FinishSurface(next,input,.9f,0f,600f,site.seed,.24f,noDunes);
            var nextZ=p+new float2(0f,1f);
            float zDune=WorldLandforms.FinishSurface(nextZ,input,.9f,0f,600f,site.seed,.24f,s)-
                WorldLandforms.FinishSurface(nextZ,input,.9f,0f,600f,site.seed,.24f,noDunes);
            duneSlope=math.max(duneSlope,TerrainSlopePolicy.FromGradient(math.length(new float2(nextDune-delta,zDune-delta)),200f));
            float dry=WorldLandforms.FinishSurface(p,input,.45f,0f,600f,site.seed,.24f,noDunes);
            float uncut=WorldLandforms.FinishSurface(p,input,.45f,0f,600f,site.seed,.24f,noWash);
            if(uncut-dry>.001f)washSamples++;
            Check(dry>.24f,"Dry wash/basin became submerged.");
            var wet=s;wet.dryDeserts=false;
            Check(WorldLandforms.FinishSurface(p,input,.20f,0f,600f,site.seed,.24f,wet)==.20f,"Non-desert water policy changed.");
            Check(WorldLandforms.FinishSurface(p,input,.9f,1f,600f,site.seed,.24f,wet)==.9f,"Surface pass changed a retained wet channel.");
            var surface=WorldLandforms.Surface(p,site.seed,600f,1f,10f,0f,BiomeType.Desert,.24f,s);
            if(surface==SurfaceType.Rock || surface==SurfaceType.DryWash)rock++;
            else if(surface==SurfaceType.Sand || surface==SurfaceType.CrackedClay)sand++;
        }
        Check(duneSamples>100 && largestDune>.025f && washSamples>50 && rock>100 && sand>100,
            $"Missing dunes, dry washes, or mixed desert surfaces: dunes {duneSamples}, peak {largestDune}, washes {washSamples}, rock {rock}, sand {sand}.");
        Check(duneSlope<34f,$"Dune slip face exceeds loose-sand slope: {duneSlope}.");
        Check(BiomeClassifier.Classify(4f,.16f,.82f,10f,.8f,0f,.24f)==BiomeType.Desert,"Hot dry mountain bench became meadow.");
        Debug.Log($"DUNES/WASHES: {duneSamples} dune samples; peak {largestDune*60f:F2} m; max sampled dune-only slope {duneSlope:F2} deg; {washSamples} dry cuts, {rock} rocky and {sand} sandy samples.");
    }

    private static void ValidateSettings()
    {
        var migrated=JsonUtility.FromJson<WorldErosionSettings>("{\"version\":4,\"mountainRelief\":20}").Sanitized();
        Check(migrated.landforms.enabled && migrated.mountainRelief==20f,"Existing scene settings did not migrate.");
        var s=WorldErosionSettings.Default;s.landforms.enabled=false;
        Check(!s.Sanitized().landforms.enabled,"Explicit landform disable was lost.");
        var generation=WorldGenerationConfiguration.Default;
        var a=TerrainEditIdentity.Create("landform-validation",generation,TerrainDensityProfile.Default);
        generation.Erosion.landforms.duneHeight+=.01f;
        var b=TerrainEditIdentity.Create("landform-validation",generation,TerrainDensityProfile.Default);
        Check(a.Fingerprint!=b.Fingerprint,"Landform settings missing from world fingerprint.");
        var bad=WorldLandformSettings.Default;bad.duneHeight=float.NaN;bad.desertRegionScale=float.PositiveInfinity;
        Check(math.isfinite(bad.Sanitized().duneHeight) && math.isfinite(bad.Sanitized().desertRegionScale),"Nonfinite settings survived.");
        var previous=JsonUtility.FromJson<WorldLandformSettings>("{\"version\":1,\"enabled\":true}").Sanitized();
        Check(previous.dryDeserts,"Previous regional settings did not acquire the dry-desert policy.");
        var off=WorldLandformSettings.Default;off.dryDeserts=false;
        Check(!off.Sanitized().dryDeserts,"Explicit wet-world policy was lost.");
    }

    private static void ValidateDryDeserts()
    {
        var s=WorldErosionSettings.Default;int drySamples=0,oldWater=0,clay=0,washes=0;
        foreach(int seed in new[]{7,42,14567})
        {
            var points=new List<float2>();
            for(int x=-28;x<=28;x++)for(int z=-28;z<=28;z++)
            {
                var p=new float2(x*613.7f,z*607.3f);
                if(WorldLandforms.Desert(p,seed,600,s.landforms.desertRegionScale).x>.65f)points.Add(p);
            }
            using var positions=new NativeArray<float2>(points.ToArray(),Allocator.Persistent);
            using var values=new NativeArray<float3>(points.Count,Allocator.Persistent);
            new RepeatJob{Seed=seed,Settings=s,Positions=positions,Values=values}.Schedule(points.Count,17).Complete();
            var old=s;old.landforms.dryDeserts=false;
            for(int i=0;i<points.Count;i++)
            {
                var p=points[i];var value=values[i];drySamples++;
                Check(value.x>.24f+TerrainWaterSettings.WetBand && value.z==0f,"Desert core has water or wet-channel ecology.");
                Check(WaterStateClassifier.Classify(value.x,value.z,.24f)==WaterState.Dry,"Desert core received a wet water state.");
                float baseline=HeightMapGenerator.SampleTerrainHeightNative(p.x,p.y,600,seed+60000,.24f,1.3f,old).Height;
                if(baseline<=.24f)oldWater++;
                var surface=WorldLandforms.Surface(p,seed,600,value.x,5,0,BiomeType.Desert,.24f,s.landforms);
                Check(surface!=SurfaceType.Riverbed && surface!=SurfaceType.Mud,"Dry ground kept the submerged/muddy surface classification.");
                if(surface==SurfaceType.CrackedClay)clay++;if(surface==SurfaceType.DryWash)washes++;
            }
        }
        Check(drySamples>1500 && oldWater>100 && clay>100 && washes>20,
            $"Dry-desert survey missed cases: {drySamples} samples, {oldWater} former submerged beds, {clay} clay, {washes} washes.");
        var site=sites.First(t=>t.label=="Dunes");const int size=32;
        var coord=new ChunkCoord((int)math.floor(site.position.x/size),(int)math.floor(site.position.y/size));
        var near=HeightMapGenerator.GenerateTerrainHeightField(size,site.seed,600,coord,.24f,1.3f,200,s);
        var water=WaterStateMapGenerator.GenerateWaterStateMap(near.HeightMap,near.RiverMaskMap,.24f);
        foreach(int step in new[]{1,4,16})Check(WaterMeshGenerator.GenerateWaterMesh(water,step,.3f,14.4f).VertexCount==0,"Detailed desert generated a water mesh.");
        var far=FarTerrainGenerator.Generate(coord,1,size,site.seed,600,200,.3f,9,9,0,.24f,mountainHorizontalScale:1.3f,erosion:s);
        Check(far.WaterMeshData.VertexCount==0,"Distant desert generated a water mesh.");
        Debug.Log($"DRY DESERTS: {drySamples} native samples; {oldWater} formerly submerged beds, {clay} clay pans, {washes} gravel washes; detailed/far water meshes empty.");
    }

    private static void ValidateClimateExtent()
    {
        int oldDesert=0,newDesert=0;
        var s=WorldLandformSettings.Default;var off=s;off.enabled=false;
        foreach(int seed in new[]{7,42,14567})
        {
            using var mo=ClimateGenerator.CreateOctaveOffsets(seed+1000,1,Allocator.Persistent);
            using var te=ClimateGenerator.CreateOctaveOffsets(seed+2000,1,Allocator.Persistent);
            for(int x=-30;x<=30;x++)for(int z=-30;z<=30;z++)
            {
                float wx=x*611.7f,wz=z*603.3f;
                float m=ClimateGenerator.SampleWorldClimate01(wx,wz,seed,600,false,.05f,10,1,mo,s);
                float t=ClimateGenerator.SampleWorldClimate01(wx,wz,seed,600,true,.05f,10,1,te,s);
                float om=ClimateGenerator.SampleWorldClimate01(wx,wz,seed,600,false,.05f,10,1,mo,off);
                float ot=ClimateGenerator.SampleWorldClimate01(wx,wz,seed,600,true,.05f,10,1,te,off);
                bool before=ot>.65f && om<.35f,after=t>.65f && m<.35f;
                if(before)oldDesert++;if(after)newDesert++;
                Check(!before || after,"An existing hot/dry climate patch was removed.");
            }
        }
        Check(newDesert>oldDesert*1.4f,$"Desert provinces failed to expand: {oldDesert} -> {newDesert}.");
        Debug.Log($"CLIMATE EXTENT: {oldDesert} -> {newDesert} hot/dry samples out of 11163 across three seeds.");
    }

    private static void ValidateDesertRocks()
    {
        const int size=128,n=size+3;
        var biomes=new BiomeType[n,n];var surfaces=new SurfaceType[n,n];
        var slope=new float[n,n];var moisture=new float[n,n];var temperature=new float[n,n];var rivers=new float[n,n];
        for(int x=0;x<n;x++)for(int z=0;z<n;z++)
        {biomes[x,z]=BiomeType.Desert;surfaces[x,z]=SurfaceType.Rock;slope[x,z]=18;moisture[x,z]=.16f;temperature[x,z]=.82f;}
        var settings=WorldFeatureGenerationSettings.Default;settings.grasslandRockPrefabCount=3;settings.grasslandLargeRockPrefabCount=3;
        WorldFeaturePlan Plan(ChunkCoord coord)=>WorldFeaturePlanGenerator.Generate(coord,size,7,biomes,surfaces,moisture,temperature,slope,rivers,settings);
        int count=0;
        for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
        {
            var coord=new ChunkCoord(x,z);var a=Plan(coord);Plan(new ChunkCoord(x+1,z));var b=Plan(coord);
            Check(a.Placements.Count==b.Placements.Count,"Desert placement count depends on neighbour generation.");
            Check(a.Placements.Count<=3,"Desert scatter exceeds its sparse budget.");
            for(int i=0;i<a.Placements.Count;i++)
            {
                var one=a.Placements[i];var two=b.Placements[i];count++;
                Check(one.sampleX==two.sampleX && one.sampleZ==two.sampleZ && one.scale==two.scale &&
                    one.rotation==two.rotation && one.prefabIndex==two.prefabIndex,"Desert rocks are not repeatable.");
                Check(one.sampleX>=12 && one.sampleX<=size-12 && one.sampleZ>=12 && one.sampleZ<=size-12,"Rock intrudes into neighbour exclusion margin.");
            }
        }
        Check(count>20,"Missing sparse desert boulders.");
        Debug.Log($"DESERT ROCKS: {count} deterministic placements over 81 fixture chunks; maximum three per chunk.");
    }

    private static void ValidateShaderFields()
    {
        const int n=17;
        var shader=Shader.Find("Hidden/FirstSettlers/LandformFieldValidation");Check(shader!=null,"Missing field validation shader.");
        var material=new Material(shader);var owner=new object();
        var target=RenderTexture.GetTemporary(n,n,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var texture=new Texture2D(n,n,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
        float maxError=0;
        try
        {
            foreach(int seed in new[]{7,-733,int.MinValue,int.MaxValue})
            {
                var generation=WorldGenerationConfiguration.Default;generation.Seed=seed;generation.SampleScale=600;generation.WorldScale=.3f;
                WorldLandformAppearance.Bind(owner,generation);
                var rect=new Vector4(-7311.3f,-5247.9f,16387.2f,15238.6f);material.SetVector("_DebugRect",rect);
                Graphics.Blit(null,target,material);RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,n,n),0,0);texture.Apply();var pixels=texture.GetPixels();
                for(int y=0;y<n;y++)for(int x=0;x<n;x++)
                {
                    var p=new float2(rect.x+(x+.5f)/n*rect.z,rect.y+(y+.5f)/n*rect.w)/.3f;
                    var r=WorldLandforms.Region(p,seed,600,WorldLandformSettings.Default);
                    var color=pixels[y*n+x];
                    float error=math.max(math.abs(color.r-r.Aridity.x),math.abs(color.g-r.Sediment.x));
                    maxError=math.max(maxError,error);
                    Check(error<.0003f,$"CPU/GPU geography mismatch for seed {seed}, pixel {x},{y}: {error}.");
                }
            }
            Check(!ShaderUtil.ShaderHasError(shader),"Geology shader error.");
            Debug.Log($"CPU/GPU GEOLOGY: full 32-bit signed seeds and negative coordinates pass, max error {maxError:G5}.");
        }
        finally{WorldLandformAppearance.Release(owner);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(texture);Object.DestroyImmediate(material);}
    }

    private static void ValidateDesertTextureImports()
    {
        foreach(string name in new[]{"CrackedClay","DryWash"})
        foreach(bool normal in new[]{false,true})
        {
            string path=DesertSurfaceSetup.Root+"T_"+name+(normal?"_NormalGL.png":"_Albedo.png");
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            Check(importer!=null && importer.textureShape==TextureImporterShape.Texture2D && !importer.isReadable &&
                importer.mipmapEnabled && importer.wrapMode==TextureWrapMode.Repeat && importer.sRGBTexture!=normal,
                "Incorrect dry-surface texture import: "+path);
            Check(importer.GetPlatformTextureSettings("Standalone").format==(normal?TextureImporterFormat.BC5:TextureImporterFormat.BC7),
                "Unexpected dry-surface compression: "+path);
        }
        var material=AssetDatabase.LoadAssetAtPath<Material>(DesertSurfaceSetup.MaterialPath);
        foreach(string slot in new[]{"Clay","Wash"})
            Check(material.GetTexture("_"+slot+"Albedo")!=null && material.GetTexture("_"+slot+"Normal")!=null,
                "Unbound dry-surface textures: "+slot);
    }

    [BurstCompile(FloatMode=FloatMode.Strict, FloatPrecision=FloatPrecision.Standard)]
    private struct RepeatJob : IJobParallelFor
    {
        public int Seed, Reverse;
        public WorldErosionSettings Settings;
        [ReadOnly] public NativeArray<float2> Positions;
        [WriteOnly] public NativeArray<float3> Values;
        public void Execute(int i)
        {
            int index=Reverse==0?i:Positions.Length-1-i;
            float2 p=Positions[index];
            var h=HeightMapGenerator.SampleTerrainHeightNative(p.x,p.y,600f,Seed+60000,.24f,1.3f,Settings);
            Values[i]=new float3(h.Height,h.MountainMask,h.RiverMask);
        }
    }

    private static void ValidateIntegration(int seed,float2 p)
    {
        var s=WorldErosionSettings.Default;
        const int size=32;
        var coord=new ChunkCoord((int)math.floor(p.x/size),(int)math.floor(p.y/size));
        HeightFieldResult Field(ChunkCoord c)=>HeightMapGenerator.GenerateTerrainHeightField(size,seed,600f,c,.24f,1.3f,200f,s);
        var near=Field(coord);var east=Field(new ChunkCoord(coord.x+1,coord.z));var north=Field(new ChunkCoord(coord.x,coord.z+1));
        var again=Field(coord);
        for(int i=1;i<=size+1;i++)
        {
            Check(near.HeightMap[size+1,i]==east.HeightMap[1,i] && near.HeightMap[i,size+1]==north.HeightMap[i,1],"Height seam across chunk boundary.");
            Check(near.SlopeMap[size+1,i]==east.SlopeMap[1,i] && near.SlopeMap[i,size+1]==north.SlopeMap[i,1],"Slope seam across chunk boundary.");
        }
        using var positions=new NativeArray<float2>(97,Allocator.Persistent);
        using var forward=new NativeArray<float3>(97,Allocator.Persistent);
        using var reverse=new NativeArray<float3>(97,Allocator.Persistent);
        var writePositions=positions;
        for(int i=0;i<positions.Length;i++)writePositions[i]=p+new float2(i*3.79f-173.3f,i*-2.13f+73.8f);
        new RepeatJob{Seed=seed,Settings=s,Positions=positions,Values=forward}.Schedule(97,1).Complete();
        new RepeatJob{Seed=seed,Settings=s,Positions=positions,Values=reverse,Reverse=1}.Schedule(97,17).Complete();
        for(int i=0;i<97;i++)Check(math.all(math.asuint(forward[i])==math.asuint(reverse[96-i])),"Sampling order/batch size changes results.");
        var ctx=HeightMapGenerator.CreateSamplingContext(seed,.24f,1.3f,s);
        for(int x=1;x<=size+1;x++)for(int z=1;z<=size+1;z++)
        {
            float h=near.HeightMap[x,z];
            Check(h==again.HeightMap[x,z],"Repeated native field differs.");
            Check(math.abs(h-HeightMapGenerator.SampleTerrainHeight(coord.x*size+x-1,coord.z*size+z-1,600f,ctx).Height)<.003f,"Managed/native disagreement.");
        }
        var far=FarTerrainGenerator.Generate(coord,1,size,seed,600f,200f,.3f,9,9,0f,.24f,mountainHorizontalScale:1.3f,erosion:s);
        var macro=FarTerrainGenerator.Generate(new ChunkCoord(coord.x/4,coord.z/4),1,size*4,seed,600f,200f,.3f,9,9,0f,.24f,isMacroTile:true,mountainHorizontalScale:1.3f,erosion:s);
        foreach(var tile in new[]{far,macro})
        {
            int n=tile.HeightGrid.GetLength(0),span=tile==far?size:size*4;
            for(int x=0;x<n;x++)for(int z=0;z<n;z++)
            {
                float wx=tile.ChunkCoord.x*span+x*span/(float)(n-1),wz=tile.ChunkCoord.z*span+z*span/(float)(n-1);
                Check(math.abs(tile.HeightGrid[x,z]-HeightMapGenerator.SampleTerrainHeight(wx,wz,600f,ctx).Height)<.003f,"Far/macro generator differs.");
            }
        }
        var collision=ColliderMeshGenerator.GenerateColliderMesh(near.HeightMap,200f,4,.3f);
        for(int x=0;x<9;x++)for(int z=0;z<9;z++)Check(math.abs(collision.vertices[z*9+x].y-near.HeightMap[x*4+1,z*4+1]*60f)<.001f,"Collider differs.");
        var generation=WorldGenerationConfiguration.Default;generation.Seed=seed;generation.ChunkSize=size;
        generation.SampleScale=600;generation.WorldScale=.3f;generation.MeshHeightMultiplier=200;
        generation.MountainHorizontalScale=1.3f;generation.Erosion=s;
        generation.Water=new TerrainWaterSettings(14.4f,200,.3f);
        var density=new TerrainBaselineSampler(generation,2);
        for(int x=0;x<=size;x+=4)for(int z=0;z<=size;z+=4)
            Check(math.abs(density.SampleHeight((coord.x*size+x)*.3,(coord.z*size+z)*.3)-near.HeightMap[x+1,z+1]*60f)<.002f,"Excavation baseline differs.");
        var moisture=ClimateGenerator.GenerateTerrainMoistureMap(size,seed,600,2,.05f,10f,coord,s.landforms);
        var temperature=ClimateGenerator.GenerateTerrainTemperatureMap(size,seed,600,2,.05f,10f,coord,s.landforms);
        var biomes=BiomeMapGenerator.GenerateBiomeMap(near.HeightMap,moisture,temperature,near.SlopeMap,near.MountainMaskMap,near.RiverMaskMap,.24f);
        using var nativeH=TerrainMapNativeUtility.CopyFloatMapToNative(near.HeightMap,Allocator.Persistent,out int width,out int height);
        using var nativeS=TerrainMapNativeUtility.CopyFloatMapToNative(near.SlopeMap,Allocator.Persistent,out _,out _);
        using var nativeR=TerrainMapNativeUtility.CopyFloatMapToNative(near.RiverMaskMap,Allocator.Persistent,out _,out _);
        var nativeB=new NativeArray<BiomeType>(width*height,Allocator.Persistent);
        for(int x=0;x<width;x++)for(int z=0;z<height;z++)nativeB[x*height+z]=biomes[x,z];
        SurfaceType[,] surfaces;
        NativeArray<SurfaceType> nativeSurface=default;
        try { surfaces=SurfaceMapGenerator.GenerateSurfaceTypeMap(nativeH,nativeS,nativeR,nativeB,width,height,.24f,
            Allocator.Persistent,out nativeSurface,coord,size,seed,600,s.landforms); }
        finally{nativeB.Dispose();if(nativeSurface.IsCreated)nativeSurface.Dispose();}
        using var mo=ClimateGenerator.CreateOctaveOffsets(seed+1000,1,Allocator.Persistent);
        using var te=ClimateGenerator.CreateOctaveOffsets(seed+2000,1,Allocator.Persistent);
        using var habitat=new TreeHabitatSampler(seed,600,2,.05f,10f,200,.24f,1.3f,s);
        var snow=MountainSnow.Generate(near.HeightMap,near.MountainMaskMap,near.RiverMaskMap,temperature,moisture,
            size,coord,seed,600,new TerrainWaterSettings(14.4f,200,.3f),1.3f,s);
        for(int x=1;x<=size;x+=4)for(int z=1;z<=size;z+=4)
        {
            float wx=coord.x*size+x-1,wz=coord.z*size+z-1;
            Check(math.abs(moisture[x,z]-ClimateGenerator.SampleWorldClimate01(wx,wz,seed,600,false,.05f,10,1,mo,s.landforms))<1e-6f,"Moisture point/map mismatch.");
            Check(math.abs(temperature[x,z]-ClimateGenerator.SampleWorldClimate01(wx,wz,seed,600,true,.05f,10,1,te,s.landforms))<1e-6f,"Temperature point/map mismatch.");
            var ecological=habitat.Sample(wx,wz);
            Check(math.abs(ecological.Moisture-moisture[x,z])<1e-6f && math.abs(ecological.Temperature-temperature[x,z])<1e-6f,"Distant placement climate mismatch.");
            Check(ecological.Biome==biomes[x,z] && ecological.Surface==surfaces[x,z],"Detailed/distant ecological surface mismatch.");
            int pixelX=(x-1)/4,pixelZ=(z-1)/4,pixel=pixelZ*9+pixelX;
            if(temperature[x,z]>.7f && (biomes[x,z]==BiomeType.Desert || biomes[x,z]==BiomeType.Rock))
            {
                var weights=far.ControlMapsRawData.Maps[0][pixel];var second=far.ControlMapsRawData.Maps[1][pixel];
                Color32 expected=default,expectedSecond=default;
                var surface=surfaces[x,z];
                if(surface==SurfaceType.Rock)expected.a=255;
                else if(surface==SurfaceType.Sand)expected.r=255;
                else if(surface==SurfaceType.Cliff)expectedSecond.g=255;
                else if(surface==SurfaceType.DryWash)expected.g=255;
                else if(surface==SurfaceType.CrackedClay)expectedSecond.b=255;
                else continue;
                MountainSnow.Apply(ref expected,ref expectedSecond,snow[x,z]);
                Check(Math.Abs(weights.r-expected.r)<=1 && Math.Abs(weights.a-expected.a)<=1 && Math.Abs(weights.g-expected.g)<=1 &&
                    Math.Abs(second.r-expectedSecond.r)<=1 && Math.Abs(second.g-expectedSecond.g)<=1 && Math.Abs(second.b-expectedSecond.b)<=1,
                    $"Near/far desert material mismatch at {wx},{wz}: {surface}, expected {expected}/{expectedSecond}, actual {weights}/{second}.");
            }
        }
        Debug.Log($"INTEGRATION seed {seed}, chunk {coord}: repeat bits, seams, point/native/far/macro/collider/density/climate pass.");
    }

    private static void RenderPreviews()
    {
        bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var scene=EditorSceneManager.NewPreviewScene();var cleanup=new List<Object>();
        var ambient=RenderSettings.ambientLight;var ambientMode=RenderSettings.ambientMode;var sun=RenderSettings.sun;bool fog=RenderSettings.fog;
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=null;
            RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.34f,.36f,.39f);
            var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat");
            Check(source!=null,"Missing terrain material for rendering.");
            var lightObject=new GameObject("Landform sun");SceneManager.MoveGameObjectToScene(lightObject,scene);cleanup.Add(lightObject);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;
            light.color=new Color(1f,.94f,.82f);light.transform.rotation=Quaternion.Euler(38,-125,0);RenderSettings.sun=light;
            var cameraObject=new GameObject("Landform camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);cleanup.Add(cameraObject);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cameraType=CameraType.Preview;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.50f,.65f,.73f);camera.farClipPlane=3000;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            foreach(var site in sites)
            {
                int span=site.label=="Dunes" || site.label=="DryWashes"?512:1024;
                var coord=new ChunkCoord((int)math.floor(site.position.x/span),(int)math.floor(site.position.y/span));
                var s=WorldErosionSettings.Default;
                var tile=FarTerrainGenerator.Generate(coord,1,span,site.seed,600,200,.3f,257,128,0,.24f,isMacroTile:true,
                    mountainHorizontalScale:1.3f,climateOctaves:2,climatePersistence:.05f,climateLacunarity:10f,erosion:s);
                var mesh=tile.TerrainMeshData.CreateMesh();cleanup.Add(mesh);
                Check(mesh.indexFormat==IndexFormat.UInt32 && mesh.GetIndices(0).Max()==mesh.vertexCount-1,
                    "Dense landform mesh indices wrapped at the 16-bit vertex limit.");
                var material=new Material(source);cleanup.Add(material);material.SetFloat("_ReceiveShadows",0);
                material.SetVector("_TerrainHorizonParams",Vector4.zero);
                for(int i=0;i<3;i++)
                {
                    var t=new Texture2D(tile.ControlMapsRawData.Width,tile.ControlMapsRawData.Height,TextureFormat.RGBA32,false,true);
                    t.SetPixels32(tile.ControlMapsRawData.Maps[i]);t.Apply();t.wrapMode=TextureWrapMode.Clamp;cleanup.Add(t);
                    material.SetTexture("_ControlMap"+i,t);
                }
                var terrain=new GameObject(site.label);SceneManager.MoveGameObjectToScene(terrain,scene);cleanup.Add(terrain);
                terrain.AddComponent<MeshFilter>().sharedMesh=mesh;terrain.AddComponent<MeshRenderer>().sharedMaterial=material;
                terrain.transform.position=new Vector3((coord.x*span+span*.5f)*.3f,0,(coord.z*span+span*.5f)*.3f);
                var generation=WorldGenerationConfiguration.Default;generation.Seed=site.seed;generation.SampleScale=600;generation.WorldScale=.3f;generation.Erosion=s;
                WorldLandformAppearance.Bind(cleanup,generation);
                float targetY=tile.HeightGrid[tile.HeightGrid.GetLength(0)/2,tile.HeightGrid.GetLength(1)/2]*60;
                var target=terrain.transform.position+Vector3.up*targetY;
                camera.transform.position=target+new Vector3(210,145,235)*(span/1024f);camera.transform.LookAt(target);camera.fieldOfView=48;
                Capture(camera,Path.Combine(output,site.label+".png"));
                terrain.SetActive(false);
            }
            var basinSite=sites.First(t=>t.label=="DryBasin");
            var appearance=WorldGenerationConfiguration.Default;appearance.Seed=basinSite.seed;appearance.SampleScale=600;appearance.WorldScale=.3f;
            WorldLandformAppearance.Bind(cleanup,appearance);
            var panel=new GameObject("Dry sediment closeups");SceneManager.MoveGameObjectToScene(panel,scene);cleanup.Add(panel);
            var panelMesh=new Mesh();cleanup.Add(panelMesh);
            panelMesh.vertices=new[]{new Vector3(-4,0,-4),new Vector3(-4,0,4),new Vector3(4,0,4),new Vector3(4,0,-4)};
            panelMesh.normals=Enumerable.Repeat(Vector3.up,4).ToArray();
            panelMesh.uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
            panelMesh.triangles=new[]{0,1,2,0,2,3};panelMesh.RecalculateBounds();
            panel.AddComponent<MeshFilter>().sharedMesh=panelMesh;var panelRenderer=panel.AddComponent<MeshRenderer>();
            panel.transform.position=new Vector3(basinSite.position.x*.3f,20,basinSite.position.y*.3f);
            var sedimentMaterial=new Material(source);cleanup.Add(sedimentMaterial);panelRenderer.sharedMaterial=sedimentMaterial;
            sedimentMaterial.SetFloat("_ReceiveShadows",0);sedimentMaterial.SetVector("_TerrainHorizonParams",Vector4.zero);
            Texture2D Solid(Color color)
            {var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);texture.SetPixels(new[]{color,color,color,color});texture.Apply();texture.wrapMode=TextureWrapMode.Clamp;cleanup.Add(texture);return texture;}
            var empty=Solid(Color.clear);sedimentMaterial.SetTexture("_ControlMap2",empty);
            camera.transform.position=panel.transform.position+new Vector3(4.8f,5.6f,6.3f);camera.transform.LookAt(panel.transform.position);
            foreach(bool clay in new[]{true,false})
            {
                sedimentMaterial.SetTexture("_ControlMap0",clay?empty:Solid(new Color(0,1,0,0)));
                sedimentMaterial.SetTexture("_ControlMap1",clay?Solid(new Color(0,0,1,0)):empty);
                Capture(camera,Path.Combine(output,clay?"CrackedClay_Close.png":"DryWash_Close.png"));
            }
            Check(!ShaderUtil.ShaderHasError(source.shader),"Terrain shader failed to compile.");
            var rockShader=Shader.Find("Custom/WeatheredRock");Check(rockShader!=null && !ShaderUtil.ShaderHasError(rockShader),"Rock shader failed to compile.");
        }
        finally
        {
            WorldLandformAppearance.Release(cleanup);
            foreach(var obj in cleanup)Object.DestroyImmediate(obj);
            EditorSceneManager.ClosePreviewScene(scene);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;
            RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=ambientMode;RenderSettings.sun=sun;RenderSettings.fog=fog;ShaderUtil.allowAsyncCompilation=async;
        }
    }
    private static void Capture(Camera camera,string path)
    {
        var target=RenderTexture.GetTemporary(1280,800,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var previous=RenderTexture.active;var texture=new Texture2D(1280,800,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,1280,800),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            var pixels=texture.GetPixels32();Check(pixels.Count(p=>p.r>180 && p.b>180 && p.g<50)<pixels.Length/100,"Shader error pixels in preview.");
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(texture);}
    }
}
