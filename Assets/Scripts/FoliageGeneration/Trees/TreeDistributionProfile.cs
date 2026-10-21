using UnityEngine;

[CreateAssetMenu(menuName = "First Settlers/Foliage/Tree Distribution Profile")]
public sealed class TreeDistributionProfile : ScriptableObject
{
    public bool enableLayeredDistribution = true;
    [Header("World-space stands (world units)")]
    [Min(24f)] public float standCellSize = 96f;
    public Vector2 standRadiusRange = new Vector2(42f, 72f);
    [Range(0f, 1f)] public float grasslandGroveFraction = .24f;
    [Header("Candidate intensity (per hectare, before competition)")]
    [Min(0f)] public float forestBackground = 12f;
    [Min(0f)] public float forestStand = 90f;
    [Min(0f)] public float grasslandBackground = .8f;
    [Min(0f)] public float grasslandGrove = 26f;
    [Min(0f)] public float snowStand = 12f;
    [Header("Placement")]
    [Min(2f)] public float candidateCellSize = 4.8f;
    [Range(1, 3)] public int competitionCohorts = 2;
    [Min(16), Tooltip("Exceptional per-chunk guard, not a population target. Grassland also respects its existing count guard.")]
    public int maxTreesPerChunk = 64;

    private static float Safe(float value,float fallback,float min,float max) =>
        Mathf.Clamp(float.IsFinite(value)?value:fallback,min,max);

    public TreeDistributionParameters Snapshot(float worldScale)
    {
        float standCell=Safe(standCellSize,96f,24f,512f);
        float a=Safe(standRadiusRange.x,42f,8f,standCell*.8f),b=Safe(standRadiusRange.y,72f,8f,standCell*.8f);
        return new TreeDistributionParameters
        {
            Enabled=enableLayeredDistribution, WorldScale=Safe(worldScale,.3f,.001f,1000f), StandCell=standCell,
            StandRadius=new Vector2(Mathf.Min(a,b),Mathf.Max(a,b)), GroveFraction=Safe(grasslandGroveFraction,.24f,0f,1f),
            ForestBackground=Safe(forestBackground,12f,0f,300f), ForestStand=Safe(forestStand,90f,0f,300f),
            MeadowBackground=Safe(grasslandBackground,.8f,0f,100f), MeadowGrove=Safe(grasslandGrove,26f,0f,200f),
            SnowStand=Safe(snowStand,12f,0f,100f), CandidateCell=Safe(candidateCellSize,4.8f,2f,32f),
            Cohorts=Mathf.Clamp(competitionCohorts,1,3), MaxTrees=Mathf.Clamp(maxTreesPerChunk,16,256)
        };
    }
}

// Immutable worker snapshot. No Unity assets are accessed by the distribution generator.
public struct TreeDistributionParameters
{
    public bool Enabled;
    public float WorldScale, StandCell, GroveFraction, CandidateCell;
    public Vector2 StandRadius;
    public float ForestBackground, ForestStand, MeadowBackground, MeadowGrove, SnowStand;
    public int Cohorts, MaxTrees;
}
