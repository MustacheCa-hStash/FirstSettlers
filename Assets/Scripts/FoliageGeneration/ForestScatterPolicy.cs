using UnityEngine;

// CPU reference mirrored by ForestScatterCompact.compute. Rendering controls do
// not enter the placement signature: moving between rings never reseeds foliage.
public static class ForestScatterPolicy
{
    public static float RingSize(GrassSettings grass,int chunkSize,float worldScale) =>
        Mathf.Max(.001f,chunkSize*worldScale)/Mathf.Max(1,grass!=null?grass.subChunksPerChunk:10);
    public static Vector4 Densities(LeafClusterSettings settings) => settings.useDistanceDensity
        ? new Vector4(Mathf.Clamp01(settings.densityRadius3),Mathf.Clamp01(settings.densityRadius6),
            Mathf.Clamp01(settings.densityRadius10),Mathf.Clamp01(settings.densityBeyond10)) : Vector4.one;
    public static Vector4 LodDistances(LeafClusterSettings settings,GrassSettings grass,int size,float scale)
    {
        float start=Mathf.Max(0,settings.lodStart),end=Mathf.Max(start+.01f,settings.lodEnd);
        float coarseStart=0,coarseEnd=1;
        if(settings is FernSettings fern)
        {
            coarseStart=Mathf.Max(end,fern.coarseLodStart);coarseEnd=Mathf.Max(coarseStart+.01f,fern.coarseLodEnd);
            if(fern.matchGrassLodDistances && grass!=null)
            {
                float ring=RingSize(grass,size,scale);
                float center=Mathf.Max(0,grass.activeRingRadius)*Mathf.Max(.001f,size*scale);
                float width=GrassStreamingPolicy.EdgeWidth(grass,size,scale);
                coarseStart=Mathf.Max(.01f,center-width);coarseEnd=Mathf.Max(coarseStart+.01f,center+width);
                // Keep the first tier before billboards even if grass uses only
                // a few large subchunks or a very small detailed-grass radius.
                end=Mathf.Max(.01f,Mathf.Min(ring*6,coarseStart));start=Mathf.Min(ring*3,end*.5f);
            }
        }
        return new Vector4(start,end,coarseStart,coarseEnd);
    }
    public static int SelectLod(float nearRank,float coarseRank,float distance,Vector4 thresholds,bool hasFar,bool hasCoarse)
    {
        if(!hasFar)return 0;
        if(hasCoarse && coarseRank<Blend(distance,thresholds.z,thresholds.w))return 2;
        return nearRank<Blend(distance,thresholds.x,thresholds.y)?1:0;
    }
    static float Blend(float distance,float start,float end) =>
        Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,Mathf.Max(start+.01f,end),distance));
}
