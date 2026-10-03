using System;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

// Explicit editor sampling only. Never runs during world streaming or each repaint.
public sealed class BiomeTransitionPreview : EditorWindow
{
    private int seed=1456789, octaves=2, spanChunks=16;
    private float sampleScale=600, persistence=.05f, lacunarity=10, heightMultiplier=200, mountainWidth=1.3f, waterLevel=.24f;
    private WorldErosionSettings erosion=WorldErosionSettings.Default;
    private Vector2 center=Vector2.zero;
    private Texture2D texture;
    private Vector2 generatedCenter;
    private float generatedSpan;
    private string generatedSettings;
    private byte[,] membership;
    private BiomeType[,] biomes;
    private const int Resolution=256, ChunkSize=128;
    private string hover="Generate a map or read the scene settings first.";

    [MenuItem("Tools/Terrain/Preview Forest–Grassland Transitions")]
    public static void Open() => GetWindow<BiomeTransitionPreview>("Biome transitions");
    private void OnDisable() { if(texture!=null) DestroyImmediate(texture); }
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Yellow: meadow; green: forest; cyan: mixed habitat. Other colors are unmodified biomes. " +
            "This samples global fields directly; loaded chunks interpolate their sampled fields.",MessageType.Info);
        if(GUILayout.Button("Read current scene's WorldManager settings")) ReadSceneSettings();
        seed=EditorGUILayout.IntField("Seed",seed);
        sampleScale=Mathf.Max(.01f,EditorGUILayout.FloatField("Sample scale",sampleScale));
        center=EditorGUILayout.Vector2Field("Center (terrain samples)",center);
        spanChunks=EditorGUILayout.IntSlider("Map span (chunks)",spanChunks,2,64);
        EditorGUILayout.BeginHorizontal();
        if(GUILayout.Button("Find an eligible border")) FindBorder();
        if(GUILayout.Button("Generate map")) Generate();
        EditorGUILayout.EndHorizontal();
        if(texture!=null)
        {
            Rect rect=GUILayoutUtility.GetAspectRect(1);
            GUI.DrawTexture(rect,texture,ScaleMode.ScaleToFit,false);
            if(rect.Contains(Event.current.mousePosition))
            {
                int x=Mathf.Clamp((int)((Event.current.mousePosition.x-rect.x)/rect.width*Resolution),0,Resolution-1);
                int z=Mathf.Clamp((int)((1f-(Event.current.mousePosition.y-rect.y)/rect.height)*Resolution),0,Resolution-1);
                float span=generatedSpan;
                Vector2 p=generatedCenter-Vector2.one*span*.5f+new Vector2(x,z)*(span/(Resolution-1));
                hover=$"Samples {p.x:0}, {p.y:0}: {biomes[x,z]}";
                if(membership[x,z]!=0) hover+=$"; forest {BiomeTransitionPolicy.ForestWeight(membership[x,z],biomes[x,z]):0.000}";
                else hover+="; outside this pair";
                Repaint();
            }
            if(GUILayout.Button("Export PNG to ArtReferences/BiomeTransitions")) Export();
        }
        EditorGUILayout.LabelField(hover,EditorStyles.wordWrappedLabel);
    }

    public void ReadSceneSettings()
    {
        var world=UnityEngine.Object.FindAnyObjectByType<WorldManager>();
        if(world==null) {hover="Open a scene containing WorldManager to copy its inputs.";return;}
        T Read<T>(string name)=>(T)typeof(WorldManager).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
        seed=Read<int>("worldSeed");octaves=Read<int>("octaves");sampleScale=Read<float>("sampleScale");
        persistence=Read<float>("persistence");lacunarity=Read<float>("lacunarity");
        heightMultiplier=Read<float>("meshHeightMultiplier");mountainWidth=Read<float>("mountainWidth");
        waterLevel=new TerrainWaterSettings(Read<float>("globalWaterY"),heightMultiplier,Read<float>("worldScale")).WaterLevel;
        erosion=Read<WorldErosionSettings>("erosion").Sanitized();
        hover="Copied terrain and climate settings from the current scene.";
    }

    private sealed class Sampler : IDisposable
    {
        private readonly BiomeTransitionPreview owner;
        private readonly TerrainHeightSamplingContext terrain;
        private readonly NativeArray<float2> moistureOffsets,temperatureOffsets;
        private readonly float climateMax;
        public Sampler(BiomeTransitionPreview owner)
        {
            this.owner=owner;terrain=HeightMapGenerator.CreateSamplingContext(owner.seed,owner.waterLevel,owner.mountainWidth,owner.erosion);
            int octaves=ClimateGenerator.GetClimateOctaveCount(owner.octaves);
            climateMax=ClimateGenerator.GetMaxPossibleNoise(octaves,owner.persistence);
            moistureOffsets=ClimateGenerator.CreateOctaveOffsets(owner.seed+1000,octaves,Allocator.Persistent);
            temperatureOffsets=ClimateGenerator.CreateOctaveOffsets(owner.seed+2000,octaves,Allocator.Persistent);
        }
        private TerrainHeightSample Height(float x,float z)=>HeightMapGenerator.SampleTerrainHeightNative(x,z,owner.sampleScale,
            terrain.RiverSeed,owner.waterLevel,owner.mountainWidth,terrain.Erosion);
        public void Read(float x,float z,out BiomeType biome,out byte member)
        {
            var h=Height(x,z);
            float dx=(Height(x+4,z).Height-Height(x-4,z).Height)/8f;
            float dz=(Height(x,z+4).Height-Height(x,z-4).Height)/8f;
            float slope=TerrainSlopePolicy.FromGradient(math.sqrt(dx*dx+dz*dz),owner.heightMultiplier);
            float moisture=ClimateGenerator.SampleClimate01(x,z,owner.seed+1000,owner.sampleScale*10f,
                owner.persistence,owner.lacunarity,climateMax,moistureOffsets);
            float temperature=ClimateGenerator.SampleClimate01(x,z,owner.seed+2000,owner.sampleScale*12f,
                owner.persistence,owner.lacunarity,climateMax,temperatureOffsets);
            biome=BiomeClassifier.Classify(h.Height,moisture,temperature,slope,h.MountainMask,h.RiverMask,owner.waterLevel);
            var surface=SurfaceTypeClassifier.Classify(h.Height,slope,h.RiverMask,biome,owner.waterLevel);
            member=BiomeTransitionPolicy.Encode(BiomeTransitionPolicy.Evaluate(biome,surface,moisture,temperature,
                h.Height,h.MountainMask,slope,h.RiverMask,owner.waterLevel));
        }
        public void Dispose() {moistureOffsets.Dispose();temperatureOffsets.Dispose();}
    }

    public void FindBorder()
    {
        using var sampler=new Sampler(this);
        float best=float.MaxValue;Vector2 chosen=center;bool found=false;
        // Bounded explicit search. Prefer memberships near .5 and close to the entered center.
        for(int x=-32;x<=32;x++) for(int z=-32;z<=32;z++)
        {
            Vector2 p=center+new Vector2(x,z)*ChunkSize*2;
            sampler.Read(p.x,p.y,out var biome,out byte member);
            if(!BiomeTransitionPolicy.IsMixed(member)) continue;
            float score=Mathf.Abs(BiomeTransitionPolicy.ForestWeight(member,biome)-.5f)*10000f+new Vector2(x,z).magnitude;
            if(score>=best) continue;best=score;chosen=p;found=true;
        }
        if(found) {center=chosen;hover=$"Found an eligible forest–grassland border near {center}.";}
        else hover="No eligible border found in the search area. Move the center or change the seed.";
    }

    public void Generate()
    {
        if(texture!=null) DestroyImmediate(texture);
        texture=new Texture2D(Resolution,Resolution,TextureFormat.RGBA32,false) {filterMode=FilterMode.Point};
        membership=new byte[Resolution,Resolution];biomes=new BiomeType[Resolution,Resolution];
        using var sampler=new Sampler(this);
        float span=spanChunks*ChunkSize;
        generatedCenter=center;generatedSpan=span;
        generatedSettings=$"Seed {seed}; center samples {center}; span {span}; sample scale {sampleScale}; water {waterLevel}; mountain width {mountainWidth}.\n";
        int mixed=0;
        for(int x=0;x<Resolution;x++) for(int z=0;z<Resolution;z++)
        {
            Vector2 p=center-Vector2.one*span*.5f+new Vector2(x,z)*(span/(Resolution-1));
            sampler.Read(p.x,p.y,out var biome,out byte member);
            biomes[x,z]=biome;membership[x,z]=member;
            Color color=BiomeClassifier.GenerateColorFromBiomeType(biome);
            if(member!=0)
            {
                float w=BiomeTransitionPolicy.ForestWeight(member,biome);
                color=Color.Lerp(new Color(.72f,.78f,.32f),new Color(.08f,.30f,.16f),w);
                color=Color.Lerp(color,new Color(.12f,.80f,.87f),4*w*(1-w)*.7f);
            }
            if(BiomeTransitionPolicy.IsMixed(member)) mixed++;
            texture.SetPixel(x,z,color);
        }
        texture.Apply();hover=$"Generated {Resolution}² samples; {mixed} mixed points. Center {center}; span {span} samples.";
        Debug.Log("TRANSITION MAP: "+hover);
    }
    public void ValidateWorldConsumers()
    {
        var coord=new ChunkCoord(Mathf.FloorToInt(center.x/ChunkSize),Mathf.FloorToInt(center.y/ChunkSize));
        BiomeTransitionSeamValidation.ValidateRealChunks(seed,sampleScale,octaves,persistence,lacunarity,
            waterLevel,mountainWidth,heightMultiplier,erosion,coord);
        const int resolution=17;
        var far=FarTerrainGenerator.Generate(coord,0,ChunkSize,seed,sampleScale,heightMultiplier,.3f,9,resolution,4,
            waterLevel,true,1,mountainWidth,MountainSnow.DefaultRenderCoverageGamma,octaves,persistence,lacunarity,erosion);
        using var sampler=new Sampler(this);
        int mixed=0;
        for(int x=0;x<resolution;x++) for(int z=0;z<resolution;z++)
        {
            float wx=coord.x*ChunkSize+x*ChunkSize/(float)(resolution-1);
            float wz=coord.z*ChunkSize+z*ChunkSize/(float)(resolution-1);
            sampler.Read(wx,wz,out var biome,out byte member);
            if(member==0) continue;
            byte expected=(byte)Mathf.RoundToInt(BiomeTransitionPolicy.ForestWeight(member,biome)*255f);
            if(far.ControlMapsRawData.Maps[2][z*resolution+x].r!=expected)
                throw new InvalidOperationException("Far terrain did not consume forest membership at "+wx+", "+wz);
            if(BiomeTransitionPolicy.IsMixed(member)) mixed++;
        }
        if(mixed==0) throw new InvalidOperationException("Far terrain fixture did not exercise mixed habitat.");
        Debug.Log($"TRANSITION FAR TERRAIN PASS: actual Burst control-map job, {mixed} mixed samples, shared quantized membership.");
    }

    public void Export()
    {
        if(texture==null) return;
        Directory.CreateDirectory("ArtReferences/BiomeTransitions");
        File.WriteAllBytes("ArtReferences/BiomeTransitions/WorldMembership.png",texture.EncodeToPNG());
        File.WriteAllText("ArtReferences/BiomeTransitions/WorldMembership.txt",
            generatedSettings+
            "Yellow: meadow; green: forest; cyan: mixed membership. Other colors: categorical biomes.\n"+hover);
    }
}
