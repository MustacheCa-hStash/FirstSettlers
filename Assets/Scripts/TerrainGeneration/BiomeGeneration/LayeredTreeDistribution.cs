using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public struct TreeDistributionStats
{
    public int Candidates, HabitatQueries, CompetitionTests, Trees, Capped;
}

/// <summary>World-lattice candidates and finite priority cohorts: deterministic without loaded neighbours or recursive chunk generation.</summary>
public sealed class LayeredTreeDistribution
{
    private static readonly ProfilerMarker GenerationMarker=new("FS.Trees.GenerateStands");
    private sealed class Candidate
    {
        public int X,Z,Cohort,State;
        public uint Priority;
        public bool Eligible;
        public float XZx,XZz,Radius,Scale,SnowCoverage;
        public WorldFeatureVariant Variant;
    }
    private readonly Dictionary<long,Candidate> candidates=new();
    private readonly Func<float,float,TreeHabitatSample> habitat;
    private readonly WorldFeatureGenerationSettings features;
    private readonly TreeDistributionParameters settings;
    private readonly TreeStandField.Evaluator stands;
    private readonly int seed;
    private readonly float cell,maxRadius,water;
    public TreeDistributionStats Stats;
    public LayeredTreeDistribution(int seed,WorldFeatureGenerationSettings features,Func<float,float,TreeHabitatSample> habitat,float waterLevel)
    {
        this.seed=seed;this.features=features;settings=features.treeDistribution;this.habitat=habitat;water=waterLevel;
        cell=settings.CandidateCell/settings.WorldScale;
        stands=new TreeStandField.Evaluator(seed,settings);
        foreach(var species in TreeSpeciesCatalog.All)
            maxRadius=Mathf.Max(maxRadius,features.GetTreeExclusionRadiusRange(species.Variant).y);
    }
    private uint Hash(int x,int z,int offset)=>TreeStandField.Hash(x,z,unchecked(seed+offset));
    private static float Roll(uint hash)=>TreeStandField.Roll(hash);
    private static long Key(int x,int z)=>((long)x<<32)|(uint)z;
    private Candidate Get(int x,int z)
    {
        long key=Key(x,z);
        if(candidates.TryGetValue(key,out var candidate))return candidate;
        candidate=new Candidate{X=x,Z=z,Priority=Hash(x,z,88003),Cohort=(int)(Hash(x,z,88004)%(uint)settings.Cohorts),
            XZx=(x+.1f+.8f*Roll(Hash(x,z,88001)))*cell,XZz=(z+.1f+.8f*Roll(Hash(x,z,88002)))*cell};
        candidates.Add(key,candidate);Stats.Candidates++;
        var stand=stands.Evaluate(candidate.XZx,candidate.XZz);
        float rank=Roll(Hash(x,z,88005)),area=settings.CandidateCell*settings.CandidateCell/10000f;
        float share=settings.YoungTreeShare;
        float forestMature=settings.ForestBackground+settings.ForestStand*stand.Forest;
        float forestYoung=(settings.ForestBackground+settings.ForestStand*.33f)*share;
        float upper=Mathf.Max(forestMature*(1f-share)+forestYoung,
            Mathf.Max(settings.MeadowBackground+settings.MeadowGrove,settings.SnowStand));
        if(rank>=Mathf.Min(1f,upper*area))return candidate; // No erosion/climate sample for most lattice cells.
        Stats.HabitatQueries++;
        var site=habitat(candidate.XZx,candidate.XZz);
        if(site.Height<=water || site.Surface!=SurfaceType.Grass && site.Surface!=SurfaceType.Snow)return candidate;
        if(site.Biome!=BiomeType.Snow && site.Mountain>.30f && site.Height>
            Mathf.Lerp(.8f,.62f,Mathf.InverseLerp(.30f,.45f,site.Mountain)))return candidate;
        float density, youngDensity=0f;
        if(site.Biome==BiomeType.Snow && site.Surface==SurfaceType.Snow)
        {
            if(site.Height>=.8f || site.Slope>=35f || site.River>=.32f)return candidate;
            density=settings.SnowStand*stand.Forest*Mathf.InverseLerp(.10f,.17f,site.Temperature)*
                Mathf.InverseLerp(.35f,.65f,site.Moisture)*(1f-Mathf.InverseLerp(.62f,.8f,site.Height))*
                (1f-Mathf.InverseLerp(20f,35f,site.Slope));
            candidate.Variant=WorldFeatureVariant.SpruceTree;
            candidate.SnowCoverage=Mathf.Lerp(.8f,1f,Roll(Hash(x,z,88011)));
        }
        else
        {
            if(site.Surface!=SurfaceType.Grass || site.Slope>=TerrainSlopePolicy.ForestMaxDegrees)return candidate;
            bool taiga=site.Biome==BiomeType.Taiga;
            if(!taiga && site.Biome!=BiomeType.Forest && site.Biome!=BiomeType.Grassland)return candidate;
            float forest=BiomeTransitionPolicy.Evaluate(site.Biome,site.Surface,site.Moisture,site.Temperature,
                site.Height,site.Mountain,site.Slope,site.River,water).Forest;
            bool useForest=taiga || Roll(Hash(x,z,88006))<forest;
            float slope=Mathf.InverseLerp(TerrainSlopePolicy.ForestMaxDegrees,TerrainSlopePolicy.ForestFadeStartDegrees,site.Slope);
            if(useForest)
            {
                if(site.River>=.64f)return candidate;
                float riverWeight=1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.52f,.64f,site.River));
                density=forestMature*(1f-stand.Clearing)*slope*riverWeight;
                youngDensity=forestYoung*(1f-stand.Clearing)*slope*riverWeight;
                float speciesRoll=Roll(Hash(x,z,88007));
                candidate.Variant=settings.UseWoodlandSpeciesMix ? WoodlandForest(site,stand,speciesRoll) :
                    taiga?WorldFeatureVariant.SpruceTree:ForestSpecies(stand.Palette,speciesRoll);
                // The grassland side of an ecotone never admits forest white pine either.
                if(site.Biome==BiomeType.Grassland && candidate.Variant==WorldFeatureVariant.WhitePineTree)
                    candidate.Variant=WorldFeatureVariant.MapleTree;
            }
            else
            {
                if(site.River>=.86f || features.maxGrasslandTreesPerChunk<=0)return candidate;
                float riparian=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.50f,.82f,site.River));
                density=(settings.MeadowBackground+settings.MeadowGrove*Mathf.Max(stand.Meadow,riparian*.7f))*slope*(1f-stand.Clearing);
                float youngGrove=Mathf.Max(Mathf.Sqrt(stand.Meadow)*.55f,riparian*.55f);
                youngDensity=share*(settings.MeadowBackground+settings.MeadowGrove*youngGrove)*slope*(1f-stand.Clearing);
                float speciesRoll=Roll(Hash(x,z,88007));
                candidate.Variant=settings.UseWoodlandSpeciesMix ? WoodlandMeadow(speciesRoll,riparian,false) :
                    MeadowSpecies(stand.Palette,speciesRoll,riparian,site.Moisture);
            }
        }
        float matureDensity=site.Biome==BiomeType.Snow ? density : density*(1f-share);
        if(rank>=Mathf.Min(1f,(matureDensity+youngDensity)*area))return candidate;
        bool young=rank>=matureDensity*area;
        if(young)
        {
            float mix=Roll(Hash(x,z,88012));
            bool meadow=TreeSpeciesCatalog.IsGrassland(candidate.Variant);
            candidate.Variant=settings.UseWoodlandSpeciesMix
                ? meadow ? WoodlandMeadow(mix,Mathf.InverseLerp(.50f,.82f,site.River),true) : WoodlandYoung(site,candidate.Variant,mix)
                : meadow
                ? candidate.Variant==WorldFeatureVariant.GrasslandWillowTree ? candidate.Variant :
                    mix<.65f ? WorldFeatureVariant.GrasslandMapleTree : WorldFeatureVariant.GrasslandBirchAspenTree
                : site.Biome==BiomeType.Taiga || mix<.55f ? WorldFeatureVariant.SpruceTree : WorldFeatureVariant.BeechTree;
        }
        candidate.Radius=features.GetTreeExclusionRadius(candidate.Variant,Roll(Hash(x,z,88008)));
        var range=features.GetTreeUniformScaleRange(candidate.Variant);
        candidate.Scale=Mathf.Lerp(range.x,range.y,Mathf.Clamp01(stand.Age*.6f+Roll(Hash(x,z,88009))*.4f));
        if(young)
        {
            candidate.Scale*=Mathf.Lerp(.23f,.38f,Roll(Hash(x,z,88013)));
            candidate.Radius=Mathf.Min(candidate.Radius,3f/settings.WorldScale);
        }
        candidate.Eligible=true;
        return candidate;
    }

    static WorldFeatureVariant WoodlandForest(TreeHabitatSample site, TreeStandField.Sample stand, float roll)
    {
        if(site.Biome==BiomeType.Taiga)
            return roll<.79f ? WorldFeatureVariant.SpruceTree :
                roll<.94f ? WorldFeatureVariant.WhitePineTree : WorldFeatureVariant.BirchAspenTree;
        float upland=Mathf.InverseLerp(.25f,.62f,site.Mountain)*
            (1f-Mathf.InverseLerp(.40f,.64f,site.Temperature));
        float coolTrees=.19f*upland;
        if(roll<coolTrees)
            return roll<coolTrees*.65f ? WorldFeatureVariant.SpruceTree : WorldFeatureVariant.WhitePineTree;
        roll=(roll-coolTrees)/(1f-coolTrees);
        // A shared stand palette favors cohorts, with oak stronger on warm,
        // drier ground and beech stronger in the cooler, mesic interiors.
        float dryWarm=Mathf.Clamp01((site.Temperature-site.Moisture)*1.6f+.40f);
        float oak=Mathf.Lerp(.18f,.46f,dryWarm)*Mathf.Lerp(.70f,1.35f,Roll(stand.Palette));
        float pioneer=Mathf.Lerp(.10f,.025f,stand.Forest);
        if(roll<oak)return WorldFeatureVariant.OakTree;
        if(roll<oak+pioneer)return WorldFeatureVariant.BirchAspenTree;
        if(roll<oak+pioneer+.10f)return WorldFeatureVariant.SugarMapleTree;
        if(roll<oak+pioneer+.18f)return WorldFeatureVariant.MapleTree;
        return WorldFeatureVariant.BeechTree;
    }

    static WorldFeatureVariant WoodlandMeadow(float roll, float riparian, bool young)
    {
        // Aspen uses the existing grassland birch/aspen slot. Keep waterways
        // within the same woodland palette and meadows predominantly open.
        float aspen=Mathf.Lerp(.14f,.38f,Mathf.Clamp01(riparian));
        if(roll<aspen)return WorldFeatureVariant.GrasslandBirchAspenTree;
        if(!young && roll>.42f)return WorldFeatureVariant.GrasslandOakTree;
        return WorldFeatureVariant.GrasslandMapleTree;
    }

    static WorldFeatureVariant WoodlandYoung(TreeHabitatSample site, WorldFeatureVariant mature, float roll)
    {
        if(site.Biome==BiomeType.Taiga)return mature;
        if(roll<.65f)return WorldFeatureVariant.BeechTree;
        return roll<.83f ? WorldFeatureVariant.SugarMapleTree : WorldFeatureVariant.MapleTree;
    }
    private static WorldFeatureVariant ForestSpecies(uint palette,float rank)
    {
        float roll=Roll(palette);
        WorldFeatureVariant main=roll<.22f?WorldFeatureVariant.MapleTree:roll<.42f?WorldFeatureVariant.SugarMapleTree:
            roll<.62f?WorldFeatureVariant.BeechTree:roll<.76f?WorldFeatureVariant.SpruceTree:
            roll<.86f?WorldFeatureVariant.WhitePineTree:roll<.94f?WorldFeatureVariant.BirchAspenTree:WorldFeatureVariant.OakTree;
        if(rank<.78f)return main;
        if(rank>=.98f)return WorldFeatureVariant.BirchAspenTree;
        return main switch
        {
            WorldFeatureVariant.MapleTree=>WorldFeatureVariant.SugarMapleTree,
            WorldFeatureVariant.SugarMapleTree or WorldFeatureVariant.BeechTree=>WorldFeatureVariant.MapleTree,
            WorldFeatureVariant.WhitePineTree=>WorldFeatureVariant.OakTree,
            WorldFeatureVariant.OakTree=>WorldFeatureVariant.MapleTree,
            _=>WorldFeatureVariant.BirchAspenTree
        };
    }
    private static WorldFeatureVariant MeadowSpecies(uint palette,float rank,float riparian,float moisture)
    {
        if(riparian>.25f && moisture>.6f && rank<.65f)return WorldFeatureVariant.GrasslandWillowTree;
        float roll=Roll(palette);
        if(rank<.80f)return roll<.60f?WorldFeatureVariant.GrasslandOakTree:roll<.85f?WorldFeatureVariant.GrasslandMapleTree:WorldFeatureVariant.GrasslandBirchAspenTree;
        return roll<.60f?WorldFeatureVariant.GrasslandMapleTree:WorldFeatureVariant.GrasslandBirchAspenTree;
    }
    private static bool Earlier(Candidate a,Candidate b)=>a.Priority<b.Priority || a.Priority==b.Priority && (a.Z<b.Z || a.Z==b.Z && a.X<b.X);
    private bool Accepted(Candidate candidate)
    {
        if(!candidate.Eligible)return false;
        if(candidate.State!=0)return candidate.State==1;
        int reach=Mathf.CeilToInt((candidate.Radius+maxRadius)/cell)+1;
        for(int z=candidate.Z-reach;z<=candidate.Z+reach;z++)for(int x=candidate.X-reach;x<=candidate.X+reach;x++)
        {
            if(x==candidate.X && z==candidate.Z)continue;
            uint cohort=Hash(x,z,88004)%(uint)settings.Cohorts;
            if(cohort>candidate.Cohort)continue;
            uint priority=Hash(x,z,88003);
            if(cohort==candidate.Cohort && priority>candidate.Priority)continue;
            // Reject distant cells before querying habitat or constructing a neighbour record.
            float nx=(x+.1f+.8f*Roll(Hash(x,z,88001)))*cell,nz=(z+.1f+.8f*Roll(Hash(x,z,88002)))*cell;
            float dx=candidate.XZx-nx,dz=candidate.XZz-nz;
            if(dx*dx+dz*dz>=(candidate.Radius+maxRadius)*(candidate.Radius+maxRadius))continue;
            var other=Get(x,z);
            if(!other.Eligible)continue;
            float radius=candidate.Radius+other.Radius;
            Stats.CompetitionTests++;
            if(dx*dx+dz*dz>=radius*radius)continue;
            // Within a cohort use a local priority rule; recurse only into earlier cohorts.
            // Depth is bounded by Cohorts, so results never depend on generation order/loaded chunks.
            if(other.Cohort==candidate.Cohort?Earlier(other,candidate):Accepted(other))
            {candidate.State=2;return false;}
        }
        candidate.State=1;return true;
    }
    public void Append(WorldFeaturePlan plan,ChunkCoord coord,int chunkSize)
    {
        using var marker=GenerationMarker.Auto();
        Stats=default;
        float minX=coord.x*chunkSize,minZ=coord.z*chunkSize,maxX=minX+chunkSize,maxZ=minZ+chunkSize;
        int firstX=Mathf.FloorToInt(minX/cell),firstZ=Mathf.FloorToInt(minZ/cell);
        int lastX=Mathf.FloorToInt(maxX/cell),lastZ=Mathf.FloorToInt(maxZ/cell);
        int stride=lastX-firstX+1,meadowCount=0;
        for(int z=firstZ;z<=lastZ;z++)for(int x=firstX;x<=lastX;x++)
        {
            var candidate=Get(x,z);
            if(candidate.XZx<minX || candidate.XZx>=maxX || candidate.XZz<minZ || candidate.XZz>=maxZ || !Accepted(candidate))continue;
            float lx=candidate.XZx-minX,lz=candidate.XZz-minZ;
            bool blocked=false;
            foreach(var rock in plan.Placements)
            {
                if(rock.featureType!=WorldFeatureType.Boulder)continue;
                float radius=rock.exclusionRadius+candidate.Radius,dx=lx-rock.sampleX,dz=lz-rock.sampleZ;
                if(dx*dx+dz*dz<radius*radius){blocked=true;break;}
            }
            if(blocked)continue;
            bool meadow=TreeSpeciesCatalog.IsGrassland(candidate.Variant);
            if(Stats.Trees>=settings.MaxTrees || meadow && meadowCount>=features.maxGrasslandTreesPerChunk)
            {Stats.Capped++;continue;}
            plan.Placements.Add(new WorldFeaturePlacement(WorldFeatureType.Tree,candidate.Variant,lx,lz,
                Quaternion.Euler(0,Roll(Hash(x,z,88010))*360f,0),Vector3.one*candidate.Scale,candidate.Radius,
                candidate.Radius*1.6f,snowCoverage:candidate.SnowCoverage,
                treeId:TreeId.Generated(seed,coord,TreePlacementSource.Stand,(z-firstZ)*stride+x-firstX)));
            Stats.Trees++;if(meadow)meadowCount++;
        }
        plan.TreeDistributionStats=Stats;
    }
}
