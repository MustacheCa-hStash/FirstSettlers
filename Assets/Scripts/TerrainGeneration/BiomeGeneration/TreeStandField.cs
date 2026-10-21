using UnityEngine;

/// <summary>Stateless world-space stand records, shared by tree placement and forest-floor fields.</summary>
public static class TreeStandField
{
    public struct Sample
    {
        public float Forest, Meadow, Clearing, Age;
        public uint Palette;
    }

    public static uint Hash(int x, int z, int seed)
    {
        unchecked
        {
            uint h=(uint)seed ^ (uint)x*374761393u ^ (uint)z*668265263u;
            h=(h^(h>>13))*1274126177u;
            return h^(h>>16);
        }
    }
    public static float Roll(uint hash) => (hash & 0x00ffffffu) / 16777216f;
    public static float Noise(float x,float z,float frequency,int seed) =>
        Mathf.Clamp01(AnalyticValueNoise2D.Sample(x*frequency,z*frequency,seed).Value*.5f+.5f);

    public static Sample Evaluate(float sampleX,float sampleZ,int seed,TreeDistributionParameters settings) =>
        new Evaluator(seed,settings).Evaluate(sampleX,sampleZ);

    public sealed class Evaluator
    {
        private struct Stand { public float X,Z,Radius,Aspect,Cos,Sin; public uint Palette; public bool Meadow; }
        private readonly System.Collections.Generic.Dictionary<long,Stand> records=new();
        private readonly int seed;
        private readonly TreeDistributionParameters settings;
        public Evaluator(int seed,TreeDistributionParameters settings){this.seed=seed;this.settings=settings;}
        private Stand Get(int x,int z)
        {
            long key=((long)x<<32)|(uint)z;
            if(records.TryGetValue(key,out var stand))return stand;
            float angle=Roll(Hash(x,z,seed+87004))*Mathf.PI*2f;
            stand=new Stand{X=(x+Mathf.Lerp(.18f,.82f,Roll(Hash(x,z,seed+87001))))*settings.StandCell,
                Z=(z+Mathf.Lerp(.18f,.82f,Roll(Hash(x,z,seed+87002))))*settings.StandCell,
                Radius=Mathf.Lerp(settings.StandRadius.x,settings.StandRadius.y,Roll(Hash(x,z,seed+87003))),
                Aspect=Mathf.Lerp(.65f,1f,Roll(Hash(x,z,seed+87005))),Cos=Mathf.Cos(angle),Sin=Mathf.Sin(angle),
                Palette=Hash(x,z,seed+87001),Meadow=Roll(Hash(x,z,seed+87007))<settings.GroveFraction};
            records.Add(key,stand);return stand;
        }
        public Sample Evaluate(float sampleX,float sampleZ)
        {
            float x=sampleX*settings.WorldScale,z=sampleZ*settings.WorldScale;
            int cx=Mathf.FloorToInt(x/settings.StandCell),cz=Mathf.FloorToInt(z/settings.StandCell);
            float best=0f,second=0f,meadow=0f;
            uint palette=0,alternate=0;
            float warp=(Noise(x,z,1f/32f,seed+87006)-.5f)*.24f;
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
            {
                var stand=Get(cx+dx,cz+dz);
                float ox=x-stand.X,oz=z-stand.Z;
                float u=(stand.Cos*ox+stand.Sin*oz)/stand.Radius;
                float v=(-stand.Sin*ox+stand.Cos*oz)/(stand.Radius*stand.Aspect);
                float distance=Mathf.Sqrt(u*u+v*v)+warp;
                float weight=1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.30f,1.08f,distance));
                if(stand.Meadow)meadow=Mathf.Max(meadow,weight);
                if(weight>best){second=best;alternate=palette;best=weight;palette=stand.Palette;}
                else if(weight>second){second=weight;alternate=stand.Palette;}
            }
            float clearing=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.62f,.88f,Noise(x,z,1f/80f,seed+87008)));
            if(second>0f && Roll(Hash(Mathf.FloorToInt(x/4.8f),Mathf.FloorToInt(z/4.8f),seed+87009))<.35f*second/(best+second))palette=alternate;
            return new Sample{Forest=Mathf.Clamp01(best+second*.15f)*(1f-clearing),Meadow=meadow*(1f-clearing),
                Clearing=clearing,Palette=palette,Age=.25f+.75f*Roll(palette^0x85ebca6bu)};
        }
    }
}
