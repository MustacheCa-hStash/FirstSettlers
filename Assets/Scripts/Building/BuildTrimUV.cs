using UnityEngine;

/// <summary>Matches Tools/BuildingTrimUV.py. Stable board crops stay inside one padded wood strip.</summary>
public static class BuildTrimUV
{
    public static float Noise(int seed,int slot)
    {
        unchecked
        {
            uint x=(uint)seed*0x45d9f3bu+(uint)(slot+1)*0x9e3779b9u;
            x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;x^=x>>16;
            return (x&0xffffffu)/16777216f;
        }
    }
    public static Vector2 Sample(Vector3 p,Vector3 normal,Bounds reference,int grain,int seed)
    {
        int dominant=0;for(int i=1;i<3;i++)if(Mathf.Abs(normal[i])>Mathf.Abs(normal[dominant]))dominant=i;
        int a=-1,b=-1;for(int i=0;i<3;i++)if(i!=dominant){if(a<0)a=i;else b=i;}
        Vector3 lo=reference.min,size=reference.size;
        if(dominant==grain)
        {
            if(Noise(seed,7)>.5f)(a,b)=(b,a);
            int tile=Noise(seed,8)<.5f?0:1;
            return new Vector2(.018f+tile*.25f+Noise(seed,9)*.03f+(p[a]-lo[a])*.5f,.272f+Noise(seed,10)*.03f+(p[b]-lo[b])*.5f);
        }
        a=grain;for(int i=0;i<3;i++)if(i!=dominant && i!=grain)b=i;
        float span=Mathf.Min(.87f,Mathf.Max(.06f,size[a]*(.18f+.04f*Noise(seed,0))));
        float position=(p[a]-lo[a])/Mathf.Max(.000001f,size[a]);if(Noise(seed,1)>.5f)position=1-position;
        float across=p[b]-lo[b];if(Noise(seed,3)>.5f)across=size[b]-across;
        return new Vector2(.02f+Noise(seed,2)*(.96f-span)+position*span,.772f+Noise(seed,4)*.013f+dominant*.002f+across*.20f);
    }
    public static int Variation(Vector3 origin,int yaw,int count)
    {
        if(count<=1)return 0;var p=BuildGeometry.Ticks(origin);
        unchecked{return (int)((uint)(p.x*73856093^p.y*19349663^p.z*83492791^yaw*1640531513)%(uint)count);}
    }
    public static Mesh Core(BuildPieceRecord p)
    {
        var variants=p.Definition.uvVariants;
        return variants!=null && variants.Length>0?variants[Variation(p.Origin,p.WorldYawStep,variants.Length)]:p.Definition.mesh;
    }
}
