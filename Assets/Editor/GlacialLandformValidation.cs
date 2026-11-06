using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
using Object=UnityEngine.Object;

public static class GlacialLandformValidation
{
    private static int checks;
    private static string output;
    private static readonly StringBuilder report=new();
    private static readonly List<(string label,int seed,float2 p)> sites=new();
    private static readonly List<(string label,int seed,float2 p)> fronts=new();
    private static float frontSlope,rockSlope;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    private static string Arg(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i<0?null:a[i+1];}
    public static void RunBatch()
    {
        try
        {
            checks=0;frontSlope=20;rockSlope=20;report.Clear();sites.Clear();fronts.Clear();output=Arg("-landformOutput");Directory.CreateDirectory(output);
            GlacialSurfaceSetup.Install();var s=TerrainRidgeValidation.SceneSettings();
            ValidateClimate(s);FindSites(s);ValidatePolicies(s);
            foreach(var site in sites)ValidateSite(site.label,site.seed,site.p,s);
            foreach(var face in fronts)report.AppendLine($"Selected {face.label} capture: seed {face.seed}, terrain XZ {face.p}, world XZ {face.p*.3f}.");
            Benchmark(s);
            Render(s);
            report.AppendLine($"GLACIAL LANDFORMS PASS: {checks} checks.");
            File.WriteAllText(Path.Combine(output,"validation.txt"),report.ToString());Debug.Log(report.ToString());EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static void ValidateClimate(WorldErosionSettings s)
    {
        int oldSnow=0,newSnow=0,total=0;
        foreach(int seed in new[]{43,66,67,68})
        {
            using var mo=ClimateGenerator.CreateOctaveOffsets(seed+1000,1,Allocator.TempJob);
            using var te=ClimateGenerator.CreateOctaveOffsets(seed+2000,1,Allocator.TempJob);
            using var samples=new NativeArray<float4>(121*121,Allocator.TempJob);
            new ClimateSurvey{seed=seed,s=s,mo=mo,te=te,result=samples}.Schedule(samples.Length,64).Complete();
            foreach(var v in samples){oldSnow+=(int)v.x;newSnow+=(int)v.y;total++;Check(math.isfinite(v.z),"Nonfinite survey height.");}
        }
        report.AppendLine($"Snow-biome survey (four seeds, actual heights/slopes/climate): {oldSnow} -> {newSnow} of {total}, {100f*oldSnow/total:F2}% -> {100f*newSnow/total:F2}%.");
        Debug.Log(report.ToString());
        Check(newSnow>oldSnow,"Snow presence did not increase.");
        Check(newSnow-oldSnow<total*.05f,"Cold expansion exceeded five percentage points.");
    }
    [BurstCompile(FloatMode=FloatMode.Strict,FloatPrecision=FloatPrecision.Standard)]
    private struct ClimateSurvey:IJobParallelFor
    {
        public int seed;public WorldErosionSettings s;
        [ReadOnly]public NativeArray<float2> mo,te;
        [WriteOnly]public NativeArray<float4> result;
        private float H(float2 p,WorldErosionSettings settings)=>HeightMapGenerator.SampleTerrainHeightNative(p.x,p.y,600,seed+60000,.24f,1.3f,settings).Height;
        private int Snow(float2 p,WorldErosionSettings settings,out float h)
        {
            var sample=HeightMapGenerator.SampleTerrainHeightNative(p.x,p.y,600,seed+60000,.24f,1.3f,settings);h=sample.Height;
            float slope=TerrainSlopePolicy.FromGradient(math.length(new float2(H(p+new float2(4,0),settings)-H(p-new float2(4,0),settings),
                H(p+new float2(0,4),settings)-H(p-new float2(0,4),settings))/8),200);
            float m=ClimateGenerator.SampleWorldClimate01(p.x,p.y,seed,600,false,.05f,10,1,mo,settings.landforms);
            float t=ClimateGenerator.SampleWorldClimate01(p.x,p.y,seed,600,true,.05f,10,1,te,settings.landforms);
            return BiomeClassifier.Classify(h,m,t,slope,sample.MountainMask,sample.RiverMask,.24f)==BiomeType.Snow?1:0;
        }
        public void Execute(int i)
        {
            var p=new float2((i/121-60)*499.3f,(i%121-60)*503.7f);
            var old=s;old.landforms.glaciers=false;
            int a=Snow(p,old,out _),b=Snow(p,s,out float h);result[i]=new float4(a,b,h,0);
        }
    }
    private static void FindSites(WorldErosionSettings s)
    {
        var broad=s;broad.landforms.enabled=false;
        float[] best={-1,-1,-1,-1};var chosen=new (int seed,float2 p)[4];
        foreach(int seed in new[]{43,66,67,68})
        {
          using var te=ClimateGenerator.CreateOctaveOffsets(seed+2000,1,Allocator.TempJob);
          for(int x=-13;x<=13;x++)for(int z=-13;z<=13;z++)
          {
            var plan=WorldGlacialLandforms.Plan(new float2((x+.5f)*3300,(z+.5f)*3300),seed,600);
            float temperature=ClimateGenerator.SampleWorldClimate01(plan.Anchor.x,plan.Anchor.y,seed,600,true,.05f,10,1,te,s.landforms);
            if(temperature>.30f)continue;
            float cold=WorldGlacialLandforms.Cold(plan.Anchor,seed,600,s.landforms);
            var b=WorldTerrainHeight.Base(plan.Anchor,600,seed,1.3f,broad);
            float score=cold*math.smoothstep(1.8f,3f,b.Height);
            if(score>best[plan.Kind] && b.Height>1.8f){best[plan.Kind]=score;chosen[plan.Kind]=(seed,plan.Anchor);}
          }
        }
        string[] labels={"GlacierValley","ShoulderBenches","UpperBowl","MountainPass"};
        for(int k=0;k<4;k++)
        {
            Check(best[k]>.65f,$"No strong cold mountain catchment of kind {k}, best {best[k]}.");
            sites.Add((labels[k],chosen[k].seed,chosen[k].p));
            report.AppendLine($"{labels[k]}: seed {chosen[k].seed}, terrain XZ {chosen[k].p}, cold score {best[k]:F3}.");
        }
    }
    private static void ValidatePolicies(WorldErosionSettings s)
    {
        foreach(float width in new[]{9f,19.8f,44f,55f,198f,500f})
        {
            float profilePrevious=-1;
            for(int i=-10;i<=1010;i++)
            {
                float t=i*.001f,r=WorldGlacialLandforms.CliffRamp(t,width);
                Check(math.isfinite(r) && r>=0 && r<=1 && r>=profilePrevious-1e-6f,"Glacial lip is nonmonotone/nonfinite.");profilePrevious=r;
            }
            Check(WorldGlacialLandforms.CliffRamp(0,width)==0 && WorldGlacialLandforms.CliffRamp(1,width)==1 &&
                math.abs(WorldGlacialLandforms.CliffRamp(.5f,width)-.5f)<1e-6f,"Lip moved the floor, total rise or face midpoint.");
            float e=math.clamp(8.32f/width,.08f,.42f);
            // Second derivatives approach zero at both plateau joins and both
            // fillet/central-face joins; the old quadratic profile had jumps.
            foreach(float t in new[]{0f,e,1-e,1f})
            {
                const float d=.002f;
                float left=(WorldGlacialLandforms.CliffRamp(t,width)-WorldGlacialLandforms.CliffRamp(t-d,width))/d;
                float right=(WorldGlacialLandforms.CliffRamp(t+d,width)-WorldGlacialLandforms.CliffRamp(t,width))/d;
                Check(math.abs(left-right)<.006f,"Lip tangent is discontinuous.");
            }
        }
        var site=sites[0];var input=new WorldBaseSample(2f,float2.zero,.5f,.5f,.1f);
        float previous=0;
        for(int i=0;i<=650;i++)
        {
            float h=.14f+i*.001f;
            float shaped=WorldGlacialLandforms.Shape(site.p,input,h,600,site.seed,1.3f,.24f,s);
            if(i>0)Check(math.abs(shaped-previous)<.005f,"Glacial coastal protection introduces a height step.");
            if(h<=.28f)Check(shaped==h,"Glacial shaping moved submerged/shoreline terrain.");
            previous=shaped;
        }
        var migrated=JsonUtility.FromJson<WorldLandformSettings>("{\"version\":2,\"enabled\":true}").Sanitized();
        Check(migrated.glaciers && migrated.glacialStrength==1 && migrated.coldExpansion>0,"Glacial settings migration failed.");
        Check(migrated.glacialRelief==1.2f && migrated.iceFrontHeight==.65f && migrated.glacialWallWidth==44,"Cliff controls did not migrate.");
        var version3=JsonUtility.FromJson<WorldLandformSettings>("{\"version\":3,\"enabled\":true,\"glaciers\":true,\"glacialStrength\":0.7,\"coldExpansion\":0.013,\"plateauRelief\":2.1}").Sanitized();
        Check(version3.glacialStrength==.7f && version3.coldExpansion==.013f && version3.plateauRelief==2.1f && version3.iceFrontHeight==.65f,"Cliff migration overwrote existing customization.");
        var off=s;off.landforms.glaciers=false;
        using(var heights=new NativeArray<float>(Enumerable.Repeat(4f,25).ToArray(),Allocator.TempJob))
        using(var slopes=new NativeArray<float>(25,Allocator.TempJob))
        using(var rivers=new NativeArray<float>(25,Allocator.TempJob))
        using(var biomes=new NativeArray<BiomeType>(Enumerable.Repeat(BiomeType.Snow,25).ToArray(),Allocator.TempJob))
        {
            // This synthetic 5x5 map is centred on known ice, rather than the
            // potentially rocky corner of the enclosing 128-unit runtime chunk.
            var coord=new ChunkCoord((int)math.floor(site.p.x),(int)math.floor(site.p.y));
            foreach(float temperature in new[]{.9f,.2f})
            {
                using var temperatures=new NativeArray<float>(Enumerable.Repeat(temperature,25).ToArray(),Allocator.TempJob);
                var map=SurfaceMapGenerator.GenerateSurfaceTypeMap(heights,slopes,rivers,biomes,5,5,.24f,Allocator.TempJob,
                    out var native,coord,1,site.seed,600,s.landforms,temperatures);
                try
                {
                    if(temperature>.48f)Check(!native.Any(v=>v==SurfaceType.Ice || v==SurfaceType.Moraine),"Native surface map ignores its warm temperature input.");
                    else Check(native.Any(v=>v==SurfaceType.Ice),"Native surface map lost exposed ice on cold glacier ground.");
                }
                finally{native.Dispose();}
            }
        }
        foreach(int seed in new[]{43,66,67,68,int.MinValue,int.MaxValue})for(int i=-30;i<=30;i++)
        {
            var p=new float2(i*1387.37f,i*-749.19f);
            float dry=WorldLandforms.Desert(p,seed,600,s.landforms.desertRegionScale).x;
            if(dry>.15f)Check(WorldGlacialLandforms.Cold(p,seed,600,s.landforms)==0,"Glaciers entered arid country.");
            Check(WorldGlacialLandforms.Cold(p,seed,600,off.landforms)==0,"Disabled glaciers remain active.");
            var plan=WorldGlacialLandforms.Plan(p,seed,600);
            Check(math.isfinite(plan.Ice) && math.all(math.isfinite(plan.Anchor)),"Nonfinite glacial plan.");
            // Each lattice boundary has a zero-weight guard, preventing hidden seams.
            var edge=new float2(i*3300f,1371.3f);
            Check(WorldGlacialLandforms.Plan(edge,seed,600).Envelope==0 &&
                WorldGlacialLandforms.Plan(edge-new float2(.1f,0),seed,600).Envelope==0,"Glacial cell-edge support leaks.");
        }
        var gen=WorldGenerationConfiguration.Default;var a=TerrainEditIdentity.Create("glacial-validation",gen,TerrainDensityProfile.Default);
        gen.Erosion.landforms.glacialStrength=.7f;
        Check(a.Fingerprint!=TerrainEditIdentity.Create("glacial-validation",gen,TerrainDensityProfile.Default).Fingerprint,"Glacial settings missing from identity.");
        foreach(string field in new[]{"glacialRelief","iceFrontHeight","glacialWallWidth"})
        {
            gen=WorldGenerationConfiguration.Default;object box=gen.Erosion.landforms;
            var info=typeof(WorldLandformSettings).GetField(field);info.SetValue(box,(float)info.GetValue(box)*1.1f);gen.Erosion.landforms=(WorldLandformSettings)box;
            Check(a.Fingerprint!=TerrainEditIdentity.Create("glacial-validation",gen,TerrainDensityProfile.Default).Fingerprint,field+" missing from identity.");
        }
        var material=AssetDatabase.LoadAssetAtPath<Material>(DesertSurfaceSetup.MaterialPath);
        Check(material.GetTexture("_IceAlbedo")!=null && material.GetTexture("_IceNormal")!=null,"Ice textures not bound.");
        var surfaces=new SurfaceType[5,5];var snow=new float2[5,5];
        for(int x=0;x<5;x++)for(int z=0;z<5;z++){surfaces[x,z]=SurfaceType.Ice;snow[x,z]=new float2(1,1);}
        var controls=TerrainControlMapBuilder.BuildRaw(surfaces,new GroundCoverType[5,5],snow);
        Check(controls.Maps.Length==3 && controls.Maps[1].All(c=>c.b==255 && c.r==0),"Snow buried exposed ice or ice allocated a new control map.");
        foreach(string suffix in new[]{"_Albedo.png","_NormalGL.png"})
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(GlacialSurfaceSetup.Root+"T_GlacierIce"+suffix);
            bool normal=suffix.Contains("Normal");
            Check(importer.textureShape==TextureImporterShape.Texture2D && importer.sRGBTexture!=normal &&
                importer.mipmapEnabled && !importer.isReadable && importer.wrapMode==TextureWrapMode.Repeat,"Incorrect ice import settings.");
        }
    }
    private static void ValidateSite(string label,int seed,float2 p,WorldErosionSettings s)
    {
        const int size=128;
        var c=new ChunkCoord((int)math.floor(p.x/size),(int)math.floor(p.y/size));
        var field=HeightMapGenerator.GenerateTerrainHeightField(size,seed,600,c,.24f,1.3f,200,s);
        var again=HeightMapGenerator.GenerateTerrainHeightField(size,seed,600,c,.24f,1.3f,200,s);
        var east=HeightMapGenerator.GenerateTerrainHeightField(size,seed,600,new ChunkCoord(c.x+1,c.z),.24f,1.3f,200,s);
        var north=HeightMapGenerator.GenerateTerrainHeightField(size,seed,600,new ChunkCoord(c.x,c.z+1),.24f,1.3f,200,s);
        var context=HeightMapGenerator.CreateSamplingContext(seed,.24f,1.3f,s);
        using var te=ClimateGenerator.CreateOctaveOffsets(seed+2000,1,Allocator.TempJob);
        using var mo=ClimateGenerator.CreateOctaveOffsets(seed+1000,1,Allocator.TempJob);
        using var forward=new NativeArray<float3>(131,Allocator.TempJob);
        using var reverse=new NativeArray<float3>(131,Allocator.TempJob);
        new Repeat{p=p,seed=seed,s=s,values=forward}.Schedule(131,1).Complete();
        new Repeat{p=p,seed=seed,s=s,reverse=true,values=reverse}.Schedule(131,19).Complete();
        for(int i=0;i<131;i++)Check(math.all(math.asuint(forward[i])==math.asuint(reverse[130-i])),"Glacier generation depends on order/batch size.");
        using var footprint=new NativeArray<float4>(81*81,Allocator.TempJob);
        using var topology=new NativeArray<float4>(81*81,Allocator.TempJob);
        new Footprint{p=p,seed=seed,s=s,te=te,values=footprint,topology=topology}.Schedule(footprint.Length,64).Complete();
        int moraine=0,iceFaces=0;float footprintRise=0;
        for(int i=0;i<footprint.Length;i++)
        {
            var v=footprint[i];
            footprintRise=math.max(footprintRise,v.x);
            if(v.z>.5f)moraine++;
            if(v.y>20f && v.w>.32f)iceFaces++;
            Check(math.all(math.isfinite(v)) && v.x<5f && topology[i].x<.20f,$"Isolated spike in full glacier footprint at {label}, sample {i}: {v}, topology {topology[i]}.");
        }
        report.AppendLine($"{label} full catchment: maximum .3m edge rise {footprintRise:F3}m, {moraine} scoured moraine samples, {iceFaces} exposed ice samples steeper than 20deg.");
        if(label!="MountainPass")
        {
            var plan=WorldGlacialLandforms.Plan(p,seed,600);var side=new float2(-plan.Downhill.y,plan.Downhill.x);
            foreach(bool iceWall in new[]{false,true})
            {
                int best=-1;float slope=iceWall?70:60;
                for(int i=0;i<footprint.Length;i++)
                {
                    bool face=iceWall?topology[i].z>.8f && footprint[i].w>.32f:topology[i].y>.8f && footprint[i].w<.2f;
                    if(face && topology[i].w>.8f && footprint[i].y>slope){best=i;slope=footprint[i].y;}
                }
                Check(best>=0,$"No substantial {(iceWall?"ice":"rock")} wall at {label}.");
                var front=p+plan.Downhill*((best/81-40)*28f)+side*((best%81-40)*16f);
                front=ValidateCliff(label,seed,front,s,iceWall);
                if(!iceWall && slope>rockSlope)
                {
                    string capture="RockWall";
                    fronts.RemoveAll(v=>v.label==capture);fronts.Add((capture,seed,front));
                    rockSlope=slope;
                }
            }
            if(label=="GlacierValley")
            {
                // Capture a downhill terminus with open foreground, rather than
                // putting the observer on the opposite rock rim of a narrow valley.
                float bestSlope=70;float2 terminus=p;bool found=false;
                float H(float2 point)=>HeightMapGenerator.SampleTerrainHeight(point.x,point.y,600,context).Height;
                for(int a=0;a<=60;a++)for(int b=-4;b<=4;b++)
                {
                    float2 point=p+plan.Downhill*((.60f+a*.002f)*840)+side*(b*20);
                    var local=WorldGlacialLandforms.Plan(point,seed,600);var section=WorldGlacialLandforms.Section(local,600,seed,s.landforms);
                    if(section.IceFace<.8f || section.RockRise>.1f)continue;
                    float2 gradient=new float2(H(point+new float2(1,0))-H(point-new float2(1,0)),H(point+new float2(0,1))-H(point-new float2(0,1)))*.5f;
                    float slope=TerrainSlopePolicy.FromGradient(math.length(gradient),200);
                    if(slope>bestSlope && math.dot(-math.normalizesafe(gradient),plan.Downhill)>.8f &&
                        WorldGlacialLandforms.IceExposure(point,seed,600,H(point),.24f,s.landforms)>.5f)
                    {terminus=point;bestSlope=slope;found=true;}
                }
                Check(found,"No steep downhill glacier terminus with an open foreground.");
                terminus=ValidateCliff(label+"Terminus",seed,terminus,s,true);frontSlope=bestSlope;
                fronts.Add(("IceFront",seed,terminus));
            }
        }
        var far=FarTerrainGenerator.Generate(c,1,size,seed,600,200,.3f,33,17,0,.24f,mountainHorizontalScale:1.3f,erosion:s);
        var generation=WorldGenerationConfiguration.Default;generation.Seed=seed;generation.ChunkSize=size;generation.SampleScale=600;
        generation.WorldScale=.3f;generation.MeshHeightMultiplier=200;generation.MountainHorizontalScale=1.3f;
        generation.Erosion=s;generation.Water=new TerrainWaterSettings(14.4f,200,.3f);var density=new TerrainBaselineSampler(generation,2);
        int gentle=0,ice=0,total=0;float maxRise=0;
        for(int x=1;x<=size+1;x++)for(int z=1;z<=size+1;z++)
        {
            float h=field.HeightMap[x,z];float2 wp=new float2(c.x*size+x-1,c.z*size+z-1);
            Check(h==again.HeightMap[x,z],"Repeated native glacier differs.");
            if(x<=size && z<=size)
            {
                maxRise=math.max(maxRise,math.abs(h-field.HeightMap[x+1,z])*60);
                float dx=field.HeightMap[x+1,z]-h,bx=field.HeightMap[x-1,z]-h;
                float dz=field.HeightMap[x,z+1]-h,bz=field.HeightMap[x,z-1]-h;
                float spike=math.max(dx*bx>0?math.min(math.abs(dx),math.abs(bx)):0,dz*bz>0?math.min(math.abs(dz),math.abs(bz)):0)*60;
                Check(spike<.20f,$"Isolated single-cell glacial peak/trough at {label}: {spike}m.");
                if(field.SlopeMap[x,z]<15)gentle++;
                if(WorldLandforms.Surface(wp,seed,600,h,field.SlopeMap[x,z],field.RiverMaskMap[x,z],BiomeType.Snow,.24f,s.landforms)==SurfaceType.Ice)ice++;
                total++;
            }
            if(x%8==1 && z%8==1)
            {
                float temperature=ClimateGenerator.SampleWorldClimate01(wp.x,wp.y,seed,600,true,.05f,10,1,te,s.landforms);
                float moisture=ClimateGenerator.SampleWorldClimate01(wp.x,wp.y,seed,600,false,.05f,10,1,mo,s.landforms);
                var biome=BiomeClassifier.Classify(h,moisture,temperature,field.SlopeMap[x,z],field.MountainMaskMap[x,z],field.RiverMaskMap[x,z],.24f);
                var surface=WorldLandforms.Surface(wp,seed,600,h,field.SlopeMap[x,z],field.RiverMaskMap[x,z],biome,.24f,s.landforms,temperature);
                if(surface==SurfaceType.Ice)
                    Check(far.ControlMapsRawData.Maps[1][((z-1)/8)*17+(x-1)/8].b==255,"Near/far exposed ice material differs.");
                if(surface==SurfaceType.Moraine)
                    Check(far.ControlMapsRawData.Maps[0][((z-1)/8)*17+(x-1)/8].a==255,"Near/far scoured rock material differs.");
                Check(WorldLandforms.Surface(wp,seed,600,h,10,0,BiomeType.Snow,.24f,s.landforms,.9f)!=SurfaceType.Ice,"Exposed ice entered warm climate.");
                Check(math.abs(h-HeightMapGenerator.SampleTerrainHeight(wp.x,wp.y,600,context).Height)<.0003f,"Managed/native glacial surface differs.");
                Check(math.abs(far.HeightGrid[(x-1)/4,(z-1)/4]-h)<.0003f,"Near/far glacial height differs.");
                Check(Math.Abs(density.SampleHeight(wp.x*.3,wp.y*.3)-h*60)<.02,"Glacial excavation baseline differs.");
            }
        }
        for(int i=1;i<=size+1;i++)
        {
            Check(field.HeightMap[size+1,i]==east.HeightMap[1,i],"Glacial X seam differs.");
            Check(field.HeightMap[i,size+1]==north.HeightMap[i,1],"Glacial Z seam differs.");
        }
        Check(maxRise<5f,$"Unresolved cliff rise at {label}: {maxRise}m per .3m edge.");
        if(label!="MountainPass")Check(ice>30 && gentle>total*.30f,$"Insufficient ice/buildable floor at {label}: ice {ice}, gentle {gentle}/{total}.");
        var mesh=ColliderMeshGenerator.GenerateColliderMesh(field.HeightMap,200,ColliderMeshGenerator.RuntimeStep,.3f).CreateMesh();
        var obj=new GameObject("Glacial PhysX fixture"){hideFlags=HideFlags.HideAndDontSave};var collider=obj.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();
        float maxError=0;
        try
        {
            for(int x=0;x<size;x+=3)for(int z=0;z<size;z+=3)
            {
                float a=field.HeightMap[x+1,z+1],b=field.HeightMap[x+1,z+2],d=field.HeightMap[x+2,z+2];
                float expected=(a+.61f*(b-a)+.37f*(d-b))*60;
                var origin=new Vector3((x+.37f-size*.5f)*.3f,expected+100,(z+.61f-size*.5f)*.3f);
                bool hit=collider.Raycast(new Ray(origin,Vector3.down),out var r,300);
                maxError=Mathf.Max(maxError,Mathf.Abs(r.point.y-expected));
                Check(hit && Mathf.Abs(r.point.y-expected)<.002f,"Glacier collider does not match visible surface.");
            }
        }
        finally{Object.DestroyImmediate(obj);Object.DestroyImmediate(mesh);}
        report.AppendLine($"{label}: gentle <15deg {100f*gentle/total:F1}%, exposed ice {ice}/{total}, maximum .3m edge rise {maxRise:F3}m; 1849 PhysX rays pass, max error {maxError:F6}m; near/far/density and seams pass.");
        Debug.Log(report.ToString());
    }
    private static float2 ValidateCliff(string label,int seed,float2 p,WorldErosionSettings s,bool ice)
    {
        var ctx=HeightMapGenerator.CreateSamplingContext(seed,.24f,1.3f,s);
        float H(float2 point)=>HeightMapGenerator.SampleTerrainHeight(point.x,point.y,600,ctx).Height*60;
        float2 gradient=new float2(H(p+new float2(1,0))-H(p-new float2(1,0)),H(p+new float2(0,1))-H(p-new float2(0,1)));
        float2 up=math.normalizesafe(gradient);float threshold=ice?70:60;
        float previous=H(p-up*160),run=0,rise=0,bestRise=0,bestRun=0,bestEnd=0;
        for(int i=1;i<=640;i++)
        {
            float h=H(p+up*(-160+i*.5f)),dh=h-previous;previous=h;
            if(math.degrees(math.atan2(dh,.15f))>=threshold){run+=.15f;rise+=dh;}
            else{run=0;rise=0;}
            if(rise>bestRise){bestRise=rise;bestRun=run;bestEnd=-160+i*.5f;}
        }
        report.AppendLine($"{label} {(ice?"ice":"rock")} wall: sustained >= {threshold}deg rise {bestRise:F1}m over {bestRun:F2}m horizontal, seed {seed}, world XZ {p*.3f}.");
        Check(bestRise>=(ice?20:40),$"{label} {(ice?"ice":"rock")} face is only a short steep sample: rise {bestRise:F1}m.");
        const int size=128;var c=new ChunkCoord((int)math.floor(p.x/size),(int)math.floor(p.y/size));
        var field=HeightMapGenerator.GenerateTerrainHeightField(size,seed,600,c,.24f,1.3f,200,s);
        var far=FarTerrainGenerator.Generate(c,1,size,seed,600,200,.3f,33,17,0,.24f,mountainHorizontalScale:1.3f,erosion:s);
        var generation=WorldGenerationConfiguration.Default;generation.Seed=seed;generation.ChunkSize=size;generation.SampleScale=600;
        generation.WorldScale=.3f;generation.MeshHeightMultiplier=200;generation.MountainHorizontalScale=1.3f;generation.Erosion=s;
        generation.Water=new TerrainWaterSettings(14.4f,200,.3f);var density=new TerrainBaselineSampler(generation,2);
        var mesh=ColliderMeshGenerator.GenerateColliderMesh(field.HeightMap,200,ColliderMeshGenerator.RuntimeStep,.3f).CreateMesh();
        var obj=new GameObject("Steep face PhysX fixture"){hideFlags=HideFlags.HideAndDontSave};var collider=obj.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();
        float maxError=0;int rays=0;
        try
        {
            for(int x=0;x<size;x+=4)for(int z=0;z<size;z+=4)
            {
                float a=field.HeightMap[x+1,z+1],b=field.HeightMap[x+1,z+2],d=field.HeightMap[x+2,z+2];
                float expected=(a+.61f*(b-a)+.37f*(d-b))*60;
                var origin=new Vector3((x+.37f-size*.5f)*.3f,expected+100,(z+.61f-size*.5f)*.3f);
                Check(collider.Raycast(new Ray(origin,Vector3.down),out var r,300) && Mathf.Abs(r.point.y-expected)<.003f,"Steep cliff vertical collider mismatch.");
                maxError=math.max(maxError,math.abs(r.point.y-expected));rays++;
                var normal=r.normal;
                Check(collider.Raycast(new Ray(r.point+normal*3,-normal),out var facing,3.1f) && math.abs(facing.distance-3)<.003f,"Steep cliff face-normal collider mismatch.");rays++;
                float2 wp=new float2(c.x*size+x,c.z*size+z);
                Check(math.abs(far.HeightGrid[x/4,z/4]-a)<.0003f,"Steep near/far canonical height mismatch.");
                Check(Math.Abs(density.SampleHeight(wp.x*.3,wp.y*.3)-a*60)<.02,"Steep cliff density baseline mismatch.");
            }
        }
        finally{Object.DestroyImmediate(obj);Object.DestroyImmediate(mesh);}
        report.AppendLine($"  Steep-face chunk ({c.x},{c.z}): {rays} vertical/face-normal PhysX rays, max vertical error {maxError:F6}m; near/far/density pass.");
        return p+up*(bestEnd-bestRun/.3f*.5f);
    }
    [BurstCompile(FloatMode=FloatMode.Strict,FloatPrecision=FloatPrecision.Standard)]
    private struct Footprint:IJobParallelFor
    {
        public float2 p;public int seed;public WorldErosionSettings s;
        [ReadOnly]public NativeArray<float2> te;
        [WriteOnly]public NativeArray<float4> values;
        [WriteOnly]public NativeArray<float4> topology;
        private float H(float2 point)=>HeightMapGenerator.SampleTerrainHeightNative(point.x,point.y,600,seed+60000,.24f,1.3f,s).Height;
        public void Execute(int i)
        {
            var plan=WorldGlacialLandforms.Plan(p,seed,600);float2 side=new float2(-plan.Downhill.y,plan.Downhill.x);
            var point=p+plan.Downhill*((i/81-40)*28f)+side*((i%81-40)*16f);
            float h=H(point),dx=H(point+new float2(1,0))-h,dz=H(point+new float2(0,1))-h;
            float slope=TerrainSlopePolicy.FromGradient(math.length(new float2(dx,dz)),200);
            float temperature=ClimateGenerator.SampleWorldClimate01(point.x,point.y,seed,600,true,.05f,10,1,te,s.landforms);
            float ice=temperature<.48f?WorldGlacialLandforms.IceExposure(point,seed,600,h,.24f,s.landforms):0;
            values[i]=new float4(math.max(math.abs(dx),math.abs(dz))*60,slope,
                WorldGlacialLandforms.MoraineExposure(point,seed,600,h,.24f,s.landforms),ice);
            float bx=H(point-new float2(1,0))-h,bz=H(point-new float2(0,1))-h;
            float spike=math.max(dx*bx>0?math.min(math.abs(dx),math.abs(bx)):0,dz*bz>0?math.min(math.abs(dz),math.abs(bz)):0)*60;
            var local=WorldGlacialLandforms.Plan(point,seed,600);var section=WorldGlacialLandforms.Section(local,600,seed,s.landforms);
            topology[i]=new float4(spike,section.RockFace,section.IceFace,WorldGlacialLandforms.Cold(point,seed,600,s.landforms)*local.Envelope);
        }
    }
    [BurstCompile(FloatMode=FloatMode.Strict,FloatPrecision=FloatPrecision.Standard)]
    private struct Repeat:IJobParallelFor
    {
        public float2 p;public int seed;public WorldErosionSettings s;public bool reverse;
        [WriteOnly]public NativeArray<float3> values;
        public void Execute(int i)
        {
            int j=reverse?130-i:i;float2 pos=p+new float2(j*13.7f-900,j*-9.3f+700);
            var h=HeightMapGenerator.SampleTerrainHeightNative(pos.x,pos.y,600,seed+60000,.24f,1.3f,s);
            values[i]=new float3(h.Height,h.RiverMask,h.MountainMask);
        }
    }
    private static void Benchmark(WorldErosionSettings s)
    {
        var site=sites[0];var c=new ChunkCoord((int)math.floor(site.p.x/128),(int)math.floor(site.p.y/128));
        var old=s;old.landforms.glaciers=false;var a=new List<double>();var b=new List<double>();
        double Time(WorldErosionSettings settings)
        {
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            HeightMapGenerator.GenerateTerrainHeightField(128,site.seed,600,c,.24f,1.3f,200,settings);
            return (System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        }
        for(int i=0;i<9;i++){double before,after;if(i%2==0){before=Time(old);after=Time(s);}else{after=Time(s);before=Time(old);}if(i>=2){a.Add(before);b.Add(after);}}
        a.Sort();b.Sort();report.AppendLine($"Warmed 128-unit height-field median at cold site: old {a[3]:F3}ms, glacial {b[3]:F3}ms. This is not live-world FPS.");
    }
    private static void Render(WorldErosionSettings s)
    {
        bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var scene=EditorSceneManager.NewPreviewScene();var cleanup=new List<Object>();
        var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;var sun=RenderSettings.sun;bool fog=RenderSettings.fog;
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=null;
            RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.34f,.37f,.42f);
            var source=AssetDatabase.LoadAssetAtPath<Material>(DesertSurfaceSetup.MaterialPath);
            var lo=new GameObject("Glacier sun");SceneManager.MoveGameObjectToScene(lo,scene);cleanup.Add(lo);
            var light=lo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.05f;light.color=new Color(1,.97f,.91f);light.transform.rotation=Quaternion.Euler(32,-125,0);RenderSettings.sun=light;
            var co=new GameObject("Glacier camera");SceneManager.MoveGameObjectToScene(co,scene);cleanup.Add(co);
            var camera=co.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cameraType=CameraType.Preview;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.53f,.66f,.75f);camera.farClipPlane=4000;camera.fieldOfView=48;
            co.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            foreach(var site in sites.Concat(fronts))
            {
                camera.fieldOfView=48;
                bool face=site.label=="IceFront" || site.label=="RockWall";
                int span=face?256:2048;var coord=new ChunkCoord((int)math.floor(site.p.x/span),(int)math.floor(site.p.y/span));
                var pieces=new List<(GameObject obj,MeshRenderer renderer,Material mat)>();
                int radius=face?1:0;
                for(int dx=-radius;dx<=radius;dx++)for(int dz=-radius;dz<=radius;dz++)
                {
                    var tileCoord=new ChunkCoord(coord.x+dx,coord.z+dz);
                    var tile=FarTerrainGenerator.Generate(tileCoord,1,span,site.seed,600,200,.3f,face?257:513,128,0,.24f,isMacroTile:true,
                        mountainHorizontalScale:1.3f,climateOctaves:2,climatePersistence:.05f,climateLacunarity:10,erosion:s);
                    var mesh=tile.TerrainMeshData.CreateMesh();cleanup.Add(mesh);
                    var mat=new Material(source);cleanup.Add(mat);mat.SetFloat("_ReceiveShadows",0);mat.SetVector("_TerrainHorizonParams",Vector4.zero);
                    for(int i=0;i<3;i++)
                    {
                        var t=new Texture2D(tile.ControlMapsRawData.Width,tile.ControlMapsRawData.Height,TextureFormat.RGBA32,false,true);
                        t.SetPixels32(tile.ControlMapsRawData.Maps[i]);t.Apply();t.wrapMode=TextureWrapMode.Clamp;cleanup.Add(t);mat.SetTexture("_ControlMap"+i,t);
                    }
                    var obj=new GameObject(site.label);SceneManager.MoveGameObjectToScene(obj,scene);cleanup.Add(obj);
                    obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;
                    obj.transform.position=new Vector3((tileCoord.x*span+span*.5f)*.3f,0,(tileCoord.z*span+span*.5f)*.3f);
                    pieces.Add((obj,renderer,mat));
                }
                var generation=WorldGenerationConfiguration.Default;generation.Seed=site.seed;generation.SampleScale=600;generation.WorldScale=.3f;generation.Erosion=s;
                generation.Water=new TerrainWaterSettings(14.4f,200,.3f);WorldLandformAppearance.Bind(cleanup,generation);
                var ctx=HeightMapGenerator.CreateSamplingContext(site.seed,.24f,1.3f,s);
                var target=new Vector3(site.p.x*.3f,HeightMapGenerator.SampleTerrainHeight(site.p.x,site.p.y,600,ctx).Height*60,site.p.y*.3f);
                var plan=WorldGlacialLandforms.Plan(site.p,site.seed,600);
                camera.transform.position=target+new Vector3(-plan.Downhill.x*360+210,260,-plan.Downhill.y*360+210)*(face?.25f:1f);camera.transform.LookAt(target);
                Capture(camera,site.label+".png");
                var bare=new Material(Shader.Find("Universal Render Pipeline/Lit"));cleanup.Add(bare);bare.SetColor("_BaseColor",new Color(.48f,.49f,.51f));
                foreach(var piece in pieces)piece.renderer.sharedMaterial=bare;
                Capture(camera,site.label+"_Bare.png");foreach(var piece in pieces)piece.renderer.sharedMaterial=piece.mat;
                camera.transform.position=target+new Vector3(38,26,43);camera.transform.LookAt(target);
                if(face)
                {
                    camera.fieldOfView=70;
                    float H(float2 p)=>HeightMapGenerator.SampleTerrainHeight(p.x,p.y,600,ctx).Height*60;
                    float2 gradient=new float2(H(site.p+new float2(4,0))-H(site.p-new float2(4,0)),H(site.p+new float2(0,4))-H(site.p-new float2(0,4)));
                    float2 down=-math.normalizesafe(gradient,plan.Downhill),eye=site.p+down*(site.label=="RockWall"?233.3333f:83.3333f);
                    var look=target-Vector3.up*(site.label=="IceFront"?6:0);
                    camera.transform.position=new Vector3(eye.x*.3f,H(eye)+5,eye.y*.3f);camera.transform.LookAt(look);
                    report.AppendLine($"{site.label} ground preview: observer world XYZ {camera.transform.position:F2}, looking at {look:F2}.");
                }
                Capture(camera,site.label+"_Close.png");
                if(face){foreach(var piece in pieces)piece.renderer.sharedMaterial=bare;Capture(camera,site.label+"_Close_Bare.png");}
                foreach(var piece in pieces)piece.obj.SetActive(false);
            }
            Check(!ShaderUtil.ShaderHasError(source.shader),"Glacier terrain shader compilation failed: "+string.Join("; ",ShaderUtil.GetShaderMessages(source.shader).Select(m=>m.message+" at "+m.file+":"+m.line)));
            Check(!ShaderUtil.ShaderHasError(Shader.Find("Custom/WeatheredRock")),"Shared rock shader compilation failed.");
        }
        finally
        {
            WorldLandformAppearance.Release(cleanup);foreach(var o in cleanup)Object.DestroyImmediate(o);
            EditorSceneManager.ClosePreviewScene(scene);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;
            RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;RenderSettings.sun=sun;RenderSettings.fog=fog;ShaderUtil.allowAsyncCompilation=async;
        }
    }
    private static void Capture(Camera c,string name)
    {
        var rt=RenderTexture.GetTemporary(1280,800,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var old=RenderTexture.active;var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        try
        {
            c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();
            Check(image.GetPixels32().Count(p=>p.r>180 && p.b>180 && p.g<50)<100,"Shader error pixels in glacier preview.");
            File.WriteAllBytes(Path.Combine(output,name),image.EncodeToPNG());
        }
        finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(image);}
    }
}
