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
    private const float PackedMatureRadius = 3.8f;
    private const float PackedYoungRadius = 1.25f;
    private static readonly ProfilerMarker GenerationMarker=new("FS.Trees.GenerateStands");
    private sealed class Candidate
    {
        public int X,Z,Cohort,State;
        public uint Priority;
        public bool Eligible, Young, EdgeHabitat;
        public float XZx,XZz,Radius,CompetitionRadius,Scale,SnowCoverage;
        public WorldFeatureVariant Variant, EdgeVariant;
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
        float forestYoung=ForestYoungDensity;
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
                float clearing = settings.PackedWoodland && !taiga ? 1f : 1f-stand.Clearing;
                density=forestMature*clearing*slope*riverWeight;
                youngDensity=forestYoung*clearing*slope*riverWeight;
                float speciesRoll=Roll(Hash(x,z,88007));
                candidate.Variant=settings.UseWoodlandSpeciesMix ? WoodlandForest(site,stand,speciesRoll,0f) :
                    taiga?WorldFeatureVariant.SpruceTree:ForestSpecies(stand.Palette,speciesRoll);
                // The grassland side of an ecotone never admits forest white pine either.
                if(site.Biome==BiomeType.Grassland && candidate.Variant==WorldFeatureVariant.WhitePineTree)
                    candidate.Variant=WorldFeatureVariant.MapleTree;
                if (settings.UseWoodlandSpeciesMix && settings.VariedWoodlandEdges && !taiga)
                {
                    float edge = Mathf.Max(1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.54f,.94f,forest)),
                        .65f*(1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.38f,.83f,stand.Forest))));
                    candidate.EdgeVariant=WoodlandForest(site,stand,speciesRoll,edge,settings.PackedWoodland);
                    if (site.Biome==BiomeType.Grassland && candidate.EdgeVariant==WorldFeatureVariant.WhitePineTree)
                        candidate.EdgeVariant=WorldFeatureVariant.MapleTree;
                }
            }
            else
            {
                if(site.River>=.86f || features.maxGrasslandTreesPerChunk<=0)return candidate;
                float riparian=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.50f,.82f,site.River));
                density=(settings.MeadowBackground+settings.MeadowGrove*Mathf.Max(stand.Meadow,riparian*.7f))*slope*(1f-stand.Clearing);
                float youngGrove=Mathf.Max(Mathf.Sqrt(stand.Meadow)*.55f,riparian*.55f);
                youngDensity=share*(settings.MeadowBackground+settings.MeadowGrove*youngGrove)*slope*(1f-stand.Clearing);
                float speciesRoll=Roll(Hash(x,z,88007));
                candidate.Variant=settings.UseWoodlandSpeciesMix ? WoodlandMeadow(speciesRoll,riparian,false,false) :
                    MeadowSpecies(stand.Palette,speciesRoll,riparian,site.Moisture);
                if (settings.UseWoodlandSpeciesMix && settings.VariedWoodlandEdges)
                    candidate.EdgeVariant=WoodlandMeadow(speciesRoll,riparian,false,true);
            }
        }
        float matureDensity=site.Biome==BiomeType.Snow ? density : density*(1f-share);
        if(rank>=Mathf.Min(1f,(matureDensity+youngDensity)*area))return candidate;
        bool young=rank>=matureDensity*area;
        candidate.Young = young;
        candidate.EdgeHabitat = site.Biome is BiomeType.Forest or BiomeType.Grassland;
        if(young)
        {
            float mix=Roll(Hash(x,z,88012));
            bool meadow=TreeSpeciesCatalog.IsGrassland(candidate.Variant);
            float riparian=Mathf.InverseLerp(.50f,.82f,site.River);
            candidate.Variant=settings.UseWoodlandSpeciesMix
                ? meadow ? WoodlandMeadow(mix,riparian,true,false) : WoodlandYoung(site,candidate.Variant,mix)
                : meadow
                ? candidate.Variant==WorldFeatureVariant.GrasslandWillowTree ? candidate.Variant :
                    mix<.65f ? WorldFeatureVariant.GrasslandMapleTree : WorldFeatureVariant.GrasslandBirchAspenTree
                : site.Biome==BiomeType.Taiga || mix<.55f ? WorldFeatureVariant.SpruceTree : WorldFeatureVariant.BeechTree;
            candidate.EdgeVariant=meadow && settings.UseWoodlandSpeciesMix && settings.VariedWoodlandEdges
                ? WoodlandMeadow(mix,riparian,true,true) : WorldFeatureVariant.None;
        }
        var spacingVariant = settings.PackedWoodland && candidate.EdgeVariant != WorldFeatureVariant.None
            ? candidate.EdgeVariant : candidate.Variant;
        candidate.Radius=features.GetTreeExclusionRadius(spacingVariant,Roll(Hash(x,z,88008)));
        var range=features.GetTreeUniformScaleRange(candidate.Variant);
        candidate.Scale=Mathf.Lerp(range.x,range.y,Mathf.Clamp01(stand.Age*.6f+Roll(Hash(x,z,88009))*.4f));
        if(young)
        {
            candidate.Scale*=Mathf.Lerp(.23f,.38f,Roll(Hash(x,z,88013)));
            candidate.Radius=Mathf.Min(candidate.Radius,3f/settings.WorldScale);
        }
        if (settings.PackedWoodland && candidate.EdgeHabitat &&
            !TreeSpeciesCatalog.IsGrassland(spacingVariant) &&
            spacingVariant is not (WorldFeatureVariant.SpruceTree or WorldFeatureVariant.WhitePineTree))
            candidate.Radius = Mathf.Min(candidate.Radius,
                (young ? PackedYoungRadius : PackedMatureRadius) / settings.WorldScale);
        candidate.CompetitionRadius=candidate.Radius;
        if (site.Biome==BiomeType.Forest && !TreeSpeciesCatalog.IsGrassland(candidate.Variant) &&
            settings.ForestCoreSpacing>0 && settings.ForestCoreSpacing<1)
        {
            float core=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.38f,.88f,stand.Forest));
            candidate.CompetitionRadius*=Mathf.Lerp(1f,settings.ForestCoreSpacing,core);
        }
        candidate.Eligible=true;
        return candidate;
    }

    private float ForestYoungDensity =>
        (settings.ForestBackground+settings.ForestStand*(settings.PackedWoodland ? .68f : .33f))*settings.YoungTreeShare;

    static WorldFeatureVariant WoodlandForest(TreeHabitatSample site, TreeStandField.Sample stand, float roll, float edge, bool packed = false)
    {
        if(site.Biome==BiomeType.Taiga)
            return roll<.79f ? WorldFeatureVariant.SpruceTree :
                roll<.94f ? WorldFeatureVariant.WhitePineTree : WorldFeatureVariant.BirchAspenTree;
        float upland=Mathf.InverseLerp(.25f,.62f,site.Mountain)*
            (1f-Mathf.InverseLerp(.40f,.64f,site.Temperature));
        float coolTrees=.19f*upland;
        if (edge > 0 && site.Biome == BiomeType.Forest)
        {
            float sheltered = Mathf.InverseLerp(.33f,.66f,site.Moisture) *
                (1f-Mathf.InverseLerp(.47f,.69f,site.Temperature));
            coolTrees += edge * sheltered * .27f;
        }
        if(roll<coolTrees)
            return roll<coolTrees*Mathf.Lerp(.65f,.29f,edge) ? WorldFeatureVariant.SpruceTree : WorldFeatureVariant.WhitePineTree;
        roll=(roll-coolTrees)/(1f-coolTrees);
        // A shared stand palette favors cohorts, with oak stronger on warm,
        // drier ground and beech stronger in the cooler, mesic interiors.
        float dryWarm=Mathf.Clamp01((site.Temperature-site.Moisture)*1.6f+.40f);
        float oak=Mathf.Lerp(.18f,.46f,dryWarm)*Mathf.Lerp(.70f,1.35f,Roll(stand.Palette))*Mathf.Lerp(1f,.54f,edge);
        float pioneer=Mathf.Lerp(Mathf.Lerp(.10f,.025f,stand.Forest),packed ? .04f : .17f,edge);
        float sugarMaple = Mathf.Lerp(.10f,packed ? .30f : .22f,edge);
        if(roll<oak)return WorldFeatureVariant.OakTree;
        if(roll<oak+pioneer)return WorldFeatureVariant.BirchAspenTree;
        if(roll<oak+pioneer+sugarMaple)return WorldFeatureVariant.SugarMapleTree;
        if(roll<oak+pioneer+sugarMaple+.08f)return WorldFeatureVariant.MapleTree;
        return WorldFeatureVariant.BeechTree;
    }

    static WorldFeatureVariant WoodlandMeadow(float roll, float riparian, bool young, bool variedEdge)
    {
        // Aspen uses the existing grassland birch/aspen slot. Keep waterways
        // within the same woodland palette and meadows predominantly open.
        float aspen=Mathf.Lerp(variedEdge ? .26f : .14f,variedEdge ? .53f : .38f,Mathf.Clamp01(riparian));
        if(roll<aspen)return WorldFeatureVariant.GrasslandBirchAspenTree;
        if(!young && roll>(variedEdge ? .70f : .42f))return WorldFeatureVariant.GrasslandOakTree;
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

    private float CrownYaw(Candidate candidate, WorldFeatureVariant variant)
    {
        float roll = Roll(Hash(candidate.X, candidate.Z, 88010));
        bool oak = variant is WorldFeatureVariant.OakTree or WorldFeatureVariant.GrasslandOakTree;
        if (!settings.UseWoodlandSpeciesMix || !settings.VariedWoodlandEdges ||
            (!oak && variant != WorldFeatureVariant.BeechTree)) return roll * 360f;

        float x = candidate.XZx, z = candidate.XZz;
        float cover = stands.Evaluate(x, z).Forest;
        if (cover < .24f || cover > .86f) return roll * 360f;

        float offset = 12f / settings.WorldScale;
        var opening = new Vector2(
            stands.Evaluate(x - offset, z).Forest - stands.Evaluate(x + offset, z).Forest,
            stands.Evaluate(x, z - offset).Forest - stands.Evaluate(x, z + offset).Forest);
        if (opening.sqrMagnitude < .01f) return roll * 360f;

        // These angles describe the fuller lower boughs of the Woodland meshes.
        float fullerSide = oak ? 112.5f : 90f;
        return Mathf.Atan2(opening.x, opening.y) * Mathf.Rad2Deg - fullerSide + Mathf.Lerp(-40f, 40f, roll);
    }

    private bool Accepted(Candidate candidate)
    {
        if(!candidate.Eligible)return false;
        if(candidate.State!=0)return candidate.State==1;
        float searchRadius=candidate.CompetitionRadius+maxRadius;
        int reach=Mathf.CeilToInt(searchRadius/cell)+1;
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
            if(dx*dx+dz*dz>=searchRadius*searchRadius)continue;
            var other=Get(x,z);
            if(!other.Eligible)continue;
            float radius=candidate.CompetitionRadius+other.CompetitionRadius;
            Stats.CompetitionTests++;
            if(dx*dx+dz*dz>=radius*radius)continue;
            // Within a cohort use a local priority rule; recurse only into earlier cohorts.
            // Depth is bounded by Cohorts, so results never depend on generation order/loaded chunks.
            if(other.Cohort==candidate.Cohort?Earlier(other,candidate):Accepted(other))
            {candidate.State=2;return false;}
        }
        candidate.State=1;return true;
    }
    public void Append(WorldFeaturePlan plan,ChunkCoord coord,int chunkSize,bool keepWoodlandEdge = true)
    {
        using var marker=GenerationMarker.Auto();
        Stats=default;
        float minX=coord.x*chunkSize,minZ=coord.z*chunkSize,maxX=minX+chunkSize,maxZ=minZ+chunkSize;
        int firstX=Mathf.FloorToInt(minX/cell),firstZ=Mathf.FloorToInt(minZ/cell);
        int lastX=Mathf.FloorToInt(maxX/cell),lastZ=Mathf.FloorToInt(maxZ/cell);
        int stride=lastX-firstX+1,meadowCount=0;
        bool infill = settings.WoodlandEdgeInfill > 0 && settings.UseWoodlandSpeciesMix;
        var young = infill ? new List<(int Index, Candidate Source)>() : null;
        var crowns = infill ? new List<WoodlandEdgeField.Crown>() : null;
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
            // Publish the edge variant used for display.
            var variant = candidate.EdgeVariant == WorldFeatureVariant.None ? candidate.Variant : candidate.EdgeVariant;
            if (infill && candidate.EdgeHabitat)
            {
                if (candidate.Young)
                {
                    if (!settings.PackedWoodland && Roll(Hash(x,z,89131)) < settings.WoodlandEdgeInfill*.78f)
                        young.Add((plan.Placements.Count,candidate));
                }
                else AddCrown(crowns,candidate,variant);
            }
            plan.Placements.Add(new WorldFeaturePlacement(WorldFeatureType.Tree,variant,lx,lz,
                Quaternion.Euler(0,CrownYaw(candidate,variant),0),Vector3.one*candidate.Scale,candidate.Radius,
                candidate.Radius*1.6f,snowCoverage:candidate.SnowCoverage,
                treeId:TreeId.Generated(seed,coord,TreePlacementSource.Stand,(z-firstZ)*stride+x-firstX)));
            Stats.Trees++;if(meadow)meadowCount++;
        }
        if (infill && (settings.PackedWoodland || keepWoodlandEdge || young.Count > 0))
        {
            Vector2 origin = new Vector2(minX,minZ)*settings.WorldScale;
            float size = chunkSize*settings.WorldScale;
            AddNeighborCrowns(crowns,origin,size);
            if (crowns.Count >= 2)
            {
                var edge = new WoodlandEdgeField(crowns,seed,settings.WoodlandEdgeInfill,settings.PackedWoodland);
                if (settings.PackedWoodland) AddEdgeTrees(plan, coord, edge, origin, size);
                foreach (var (index,source) in young)
                    MoveYoungTree(plan,index,source,edge,origin,size);
                if (keepWoodlandEdge) plan.WoodlandEdge = edge;
            }
        }
        plan.TreeDistributionStats=Stats;
    }

    private void AddCrown(List<WoodlandEdgeField.Crown> crowns,Candidate candidate,WorldFeatureVariant variant)
    {
        var dimensions = features.GetWoodlandCrown(variant)*candidate.Scale;
        crowns.Add(new WoodlandEdgeField.Crown(new Vector2(candidate.XZx,candidate.XZz)*settings.WorldScale,
            Mathf.Clamp(dimensions.x*.85f,1.5f,WoodlandEdgeField.MaxCrownRadius), dimensions.y));
    }

    private void AddNeighborCrowns(List<WoodlandEdgeField.Crown> crowns,Vector2 origin,float size)
    {
        const float halo = WoodlandEdgeField.Neighborhood + WoodlandEdgeField.MaxCrownRadius;
        float share = settings.YoungTreeShare;
        float forestUpper = (settings.ForestBackground+settings.ForestStand)*(1f-share) +
            ForestYoungDensity;
        float area = settings.CandidateCell*settings.CandidateCell/10000f;
        float upper = Mathf.Max(forestUpper,Mathf.Max(settings.MeadowBackground+settings.MeadowGrove,settings.SnowStand))*area;
        float spacing = cell * settings.WorldScale;
        int firstX = Mathf.FloorToInt((origin.x-halo)/spacing), lastX = Mathf.FloorToInt((origin.x+size+halo)/spacing);
        int firstZ = Mathf.FloorToInt((origin.y-halo)/spacing), lastZ = Mathf.FloorToInt((origin.y+size+halo)/spacing);
        for (int z=firstZ; z<=lastZ; z++)
            for (int x=firstX; x<=lastX; x++)
            {
                if (Roll(Hash(x,z,88005)) >= upper) continue;
                float px = (x+.1f+.8f*Roll(Hash(x,z,88001)))*spacing - origin.x;
                float pz = (z+.1f+.8f*Roll(Hash(x,z,88002)))*spacing - origin.y;
                if (px>=0 && pz>=0 && px<size && pz<size) continue;
                float dx = Mathf.Max(0f,Mathf.Max(-px,px-size)), dz = Mathf.Max(0f,Mathf.Max(-pz,pz-size));
                if (dx*dx+dz*dz >= halo*halo) continue;
                var candidate = Get(x,z);
                if (candidate.Young || !candidate.EdgeHabitat || !Accepted(candidate)) continue;
                var variant = candidate.EdgeVariant == WorldFeatureVariant.None ? candidate.Variant : candidate.EdgeVariant;
                AddCrown(crowns,candidate,variant);
            }
    }

    private void MoveYoungTree(WorldFeaturePlan plan,int index,Candidate source,WoodlandEdgeField edge,Vector2 origin,float size)
    {
        var placement = plan.Placements[index];
        var original = new Vector2(placement.sampleX,placement.sampleZ)*settings.WorldScale;
        float bestScore = Mathf.Max(.13f,edge.Gap(origin+original)+.065f);
        Vector2 best = original;
        TreeHabitatSample bestHabitat = default;
        float angle = Roll(Hash(source.X,source.Z,89132))*Mathf.PI*2f;
        for (int i=0; i<24; i++)
        {
            float reach = Mathf.Lerp(3f,16f,Mathf.Sqrt((i+.5f)/24f));
            float heading = angle+i*2.399963f;
            var point = original + new Vector2(Mathf.Cos(heading),Mathf.Sin(heading))*reach;
            if (point.x < 3f || point.y < 3f || point.x >= size-3f || point.y >= size-3f) continue;
            float score = edge.Gap(origin+point);
            if (score <= bestScore) continue;
            bool blocked = false;
            for (int j=0; j<plan.Placements.Count; j++)
            {
                if (j==index) continue;
                var other = plan.Placements[j];
                var delta = point-new Vector2(other.sampleX,other.sampleZ)*settings.WorldScale;
                float separation = other.featureType==WorldFeatureType.Tree ? 3f : other.exclusionRadius*settings.WorldScale + 1.2f;
                if (delta.sqrMagnitude < separation*separation) { blocked=true; break; }
            }
            if (blocked) continue;
            var world = (origin+point)/settings.WorldScale;
            var site = habitat(world.x,world.y);
            Stats.HabitatQueries++;
            if (site.Surface!=SurfaceType.Grass || site.Height<=water || site.River>=.45f || site.Slope>=26f ||
                site.Biome is not (BiomeType.Forest or BiomeType.Grassland)) continue;
            bestScore = score;
            best = point;
            bestHabitat = site;
        }
        if (best==original) return;
        float mix = Roll(Hash(source.X,source.Z,89133));
        placement.variant = mix < .68f ? WorldFeatureVariant.BeechTree :
            mix < .82f && bestHabitat.Moisture>.48f ? WorldFeatureVariant.BirchAspenTree :
            bestHabitat.Moisture>.57f ? WorldFeatureVariant.SugarMapleTree : WorldFeatureVariant.MapleTree;
        float top = features.GetWoodlandCrown(placement.variant).z;
        placement.sampleX = best.x/settings.WorldScale;
        placement.sampleZ = best.y/settings.WorldScale;
        placement.scale = Vector3.one*(Mathf.Lerp(4f,7f,Roll(Hash(source.X,source.Z,89134)))/top);
        plan.Placements[index] = placement;
    }

    private void AddEdgeTrees(WorldFeaturePlan plan, ChunkCoord coord, WoodlandEdgeField edge, Vector2 origin, float size)
    {
        // Bounded jitter on a world grid keeps edge-tree roots at least 1.2 m apart,
        // including across seams, while allowing their crowns to overlap.
        const float spacing = 3f, rootRadius = .55f;
        float scale = settings.WorldScale;
        int firstX = Mathf.FloorToInt(origin.x / spacing), firstZ = Mathf.FloorToInt(origin.y / spacing);
        int lastX = Mathf.FloorToInt((origin.x + size) / spacing), lastZ = Mathf.FloorToInt((origin.y + size) / spacing);
        int stride = lastX - firstX + 1;
        for (int z = firstZ; z <= lastZ; z++)
        for (int x = firstX; x <= lastX; x++)
        {
            var point = new Vector2(x + .2f + .6f * Roll(Hash(x,z,89201)),
                                    z + .2f + .6f * Roll(Hash(x,z,89202))) * spacing;
            var local = point - origin;
            if (local.x < 0f || local.y < 0f || local.x >= size || local.y >= size) continue;
            float group = edge.Group(point);
            float cover = edge.Gap(point, treeBand: true) * edge.Strength;
            if (cover < .08f) continue;
            var sample = point / scale;
            var site = habitat(sample.x, sample.y);
            Stats.HabitatQueries++;
            if (site.Surface != SurfaceType.Grass || site.Height <= water || site.River >= .45f ||
                site.Slope >= TerrainSlopePolicy.ForestMaxDegrees ||
                site.Biome is not (BiomeType.Forest or BiomeType.Grassland)) continue;
            // Shrubs carry the screen between tree clumps. Adjacent cells share
            // the same broad patches, but draw their heights independently.
            float edgeDensity = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.32f, .67f, group)) *
                (site.Biome == BiomeType.Grassland ? 5.5f : 2.5f);
            if (Roll(Hash(x,z,89203)) > Mathf.Clamp01(cover * edgeDensity)) continue;
            if (OverlapsStandTree(sample, rootRadius / scale)) continue;
            var root = local / scale;
            bool overlapsBoulder = false;
            foreach (var rock in plan.Placements)
            {
                if (rock.featureType != WorldFeatureType.Boulder) continue;
                float separation = rock.exclusionRadius + rootRadius / scale;
                if ((root - new Vector2(rock.sampleX, rock.sampleZ)).sqrMagnitude >= separation * separation) continue;
                overlapsBoulder = true;
                break;
            }
            if (overlapsBoulder) continue;
            float mix = Roll(Hash(x,z,89204));
            float age = Roll(Hash(x,z,89205));
            var variant = mix < .60f ? WorldFeatureVariant.WoodlandEdgeBeech :
                mix < .79f ? WorldFeatureVariant.MapleTree :
                mix < .91f ? WorldFeatureVariant.SugarMapleTree :
                age > .72f ? WorldFeatureVariant.OakTree : WorldFeatureVariant.BirchAspenTree;
            float height = Mathf.Lerp(3f, 10f, age);
            if (age > .9f && group > .58f) height += 3f;
            plan.Placements.Add(new WorldFeaturePlacement(WorldFeatureType.Tree, variant, root.x, root.y,
                Quaternion.Euler(0f, 360f * Roll(Hash(x,z,89206)), 0f),
                Vector3.one * (height / features.GetWoodlandCrown(variant).z), rootRadius / scale, 1.6f * rootRadius / scale,
                treeId: TreeId.Generated(seed, coord, TreePlacementSource.WoodlandEdge, (z-firstZ)*stride+x-firstX)));
            Stats.Trees++;
        }
    }

    private bool OverlapsStandTree(Vector2 point, float radius)
    {
        float reach = radius + maxRadius;
        for (int z=Mathf.FloorToInt((point.y-reach)/cell); z<=Mathf.FloorToInt((point.y+reach)/cell); z++)
        for (int x=Mathf.FloorToInt((point.x-reach)/cell); x<=Mathf.FloorToInt((point.x+reach)/cell); x++)
        {
            var other = Get(x,z);
            if (!other.Eligible) continue;
            var offset = point-new Vector2(other.XZx,other.XZz);
            float separation = radius+other.Radius;
            if (offset.sqrMagnitude < separation*separation && Accepted(other)) return true;
        }
        return false;
    }
}
