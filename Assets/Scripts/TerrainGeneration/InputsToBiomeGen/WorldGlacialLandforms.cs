using Unity.Mathematics;

public readonly struct GlacialPlan
{
    public readonly float2 Anchor, Downhill;
    public readonly float Along, Across, Envelope, Ice, Bowl;
    public readonly int Kind;
    public GlacialPlan(float2 anchor,float2 downhill,float along,float across,float envelope,float ice,float bowl,int kind)
    { Anchor=anchor; Downhill=downhill; Along=along; Across=across; Envelope=envelope; Ice=ice; Bowl=bowl; Kind=kind; }
}

public readonly struct GlacialSection
{
    public readonly float Floor, RockRise, RockFace, IceBody, IceFace, HangingIce;
    public GlacialSection(float floor,float rockRise,float rockFace,float iceBody,float iceFace,float hangingIce)
    { Floor=floor; RockRise=rockRise; RockFace=rockFace; IceBody=iceBody; IceFace=iceFace; HangingIce=hangingIce; }
}

/// <summary>Seeded cold provinces and finite catchments. No final-biome or neighbor-residency dependency.</summary>
public static class WorldGlacialLandforms
{
    public static float Cold(float2 p,int seed,float scale,in WorldLandformSettings s)
    {
        if(!s.enabled || !s.glaciers || s.glacialStrength<=0f)return 0f;
        float dry=WorldLandforms.Desert(p,seed,scale,s.desertRegionScale).x;
        float wet=1f-math.smoothstep(.015f,.15f,dry);
        if(wet<=0f)return 0f;
        float n=WorldLandforms.Noise(p,math.max(scale,1f)*16f,seed+91001).x+
            WorldLandforms.Noise(p,math.max(scale,1f)*5.5f,seed+91002).x*.12f;
        return math.smoothstep(.28f,.72f,n)*wet*s.glacialStrength;
    }
    public static float Climate(float value,bool temperature,float2 p,int seed,float scale,in WorldLandformSettings s)
    {
        if(!temperature || !s.enabled || !s.glaciers)return value;
        float dry=WorldLandforms.Desert(p,seed,scale,s.desertRegionScale).x;
        value=math.max(0f,value-s.coldExpansion*(1f-math.smoothstep(.35f,.60f,value))*(1f-dry));
        return math.lerp(value,math.min(value,.17f),Cold(p,seed,scale,s)*(1f-math.smoothstep(.55f,.65f,value)));
    }
    public static GlacialPlan Plan(float2 p,int seed,float scale)
    {
        scale=math.max(scale,1f);
        float grid=scale*5.5f;
        int2 cell=(int2)math.floor(p/grid);
        uint h=WorldLandforms.Hash(cell.x,cell.y,seed+92001);
        float2 jitter=new float2((h&255u)/255f,((h>>8)&255u)/255f)-.5f;
        float2 anchor=((float2)cell+.5f+jitter*.12f)*grid;
        // A fixed catchment axis never rotates with tiny local changes in height gradient.
        float angle=((h>>16)&65535u)/65535f*6.2831853f;
        math.sincos(angle,out float sin,out float cos);
        float2 down=new float2(cos,sin),side=new float2(-sin,cos),d=p-anchor;
        float along=math.dot(d,down)/(scale*1.4f),across=math.dot(d,side)/(scale*.65f);
        across=(across+WorldLandforms.Noise(p,scale*1.1f,seed+92002).x*.25f)/
            (1f+WorldLandforms.Noise(p,scale*1.3f,seed+92003).x*.20f);
        int kind=(int)(h&3u);
        float length=kind==2?.78f:1f;
        float radial=math.pow(math.pow(math.abs(along)/length,4f)+math.pow(math.abs(across),4f),.25f);
        float envelope=1f-math.smoothstep(.78f,1.15f,radial);
        // All weights reach zero strictly inside the cell even after rotation and jitter.
        float bowl=1f-math.smoothstep(.25f,1f,math.length(new float2((along+.42f)/.48f,across/.85f)));
        float ice=(1f-math.smoothstep(.28f,.53f,math.abs(across)))*
            math.smoothstep(-.95f,-.7f,along)*(1f-math.smoothstep(.79f,.87f,along));
        if(kind==3)ice*=.12f;
        if(kind==1 || kind==2)
        {
            float sideSign=(h&256u)==0?1f:-1f;
            float ribbon=1f-math.smoothstep(.08f,.18f,math.abs(across-sideSign*(.60f+.18f*along)));
            ribbon*=1f-math.smoothstep(.22f,.54f,math.abs(along+.25f));
            ice=math.max(ice,ribbon*.78f); // Selected hanging ice ribbons drape over a basin shoulder.
        }
        return new GlacialPlan(anchor,down,along,across,envelope,ice,bowl,kind);
    }
    // C2 cap/toe fillets give the height grid enough transition samples. The
    // narrow total face width and central rise stay steep; endpoints and midpoint
    // are unchanged. A two-cell quadratic lip produced a repeated normal comb.
    public static float CliffRamp(float t,float width)
    {
        // Fixed canonical units, never render LOD. Wider travel openings keep
        // their gentle central slope rather than inheriting an ice-sized fillet.
        t=math.saturate(t);float e=math.clamp(8.32f/math.max(width,.001f),.08f,.42f);
        if(t<e){float u=t/e;return e*u*u*u*(1f-.5f*u)/(1f-e);}
        if(t>1f-e){float u=(1f-t)/e;return 1f-e*u*u*u*(1f-.5f*u)/(1f-e);}
        return (t-e*.5f)/(1f-e);
    }
    private static float FaceWeight(float t)=>math.smoothstep(0f,.12f,t)*(1f-math.smoothstep(.88f,1f,t));
    // Broad planar changes of rim direction: deterministic facets, not narrow ribs
    // from erosion or repeated horizontal strata. Coordinates stay catchment-local.
    private static float Facet(float t,uint key)
    {
        int i=(int)math.floor(t);float f=t-i;
        float a=(WorldLandforms.Hash(i,0,(int)key)&65535u)/32767.5f-1f;
        float b=(WorldLandforms.Hash(i+1,0,(int)key)&65535u)/32767.5f-1f;
        return math.lerp(a,b,f);
    }
    public static GlacialSection Section(in GlacialPlan plan,float scale,int seed,in WorldLandformSettings s)
    {
        scale=math.max(scale,1f);float cross=math.abs(plan.Across);
        uint key=WorldLandforms.Hash((int)math.floor(plan.Anchor.x/scale),(int)math.floor(plan.Anchor.y/scale),seed+95001);
        float rib=Facet(plan.Along*3.5f,key),headRib=Facet(plan.Across*3f,key^5317u);
        float sideSign=(key&1u)==0?1f:-1f;
        float opening=(plan.Across*sideSign>0f && plan.Kind!=2)?
            1f-math.smoothstep(.06f,.28f,math.abs(plan.Along-.30f)):0f;
        float width=s.glacialWallWidth*math.lerp(1f,4.5f,opening)*(plan.Kind==3?1.7f:1f);
        float sideT=(cross-(.54f+.065f*rib+(plan.Across>0f?.025f:0f)))/(width/(scale*.65f))+.5f;
        float headT=(-plan.Along-(.64f+.035f*headRib))/(s.glacialWallWidth*1.25f/(scale*1.4f))+.5f;
        float sideRise=CliffRamp(sideT,width)*s.glacialRelief*(.94f+.14f*rib+(plan.Across>0f?.12f:0f))*math.lerp(1f,.55f,opening);
        float headRise=plan.Kind==3?0f:CliffRamp(headT,s.glacialWallWidth*1.25f)*s.glacialRelief*(plan.Kind==2?1.22f:1.08f)*(.96f+.08f*headRib);
        float rock=math.max(sideRise,headRise);
        // A hard max makes a narrow inward crease where two steep faces meet.
        // Round just their overlap, with at most 1.8m added at default scale;
        // zero-height floors and the open pass remain exactly unchanged.
        float join=s.glacialRelief*.10f;
        float overlap=math.saturate(1f-math.abs(sideRise-headRise)/join);
        rock+=join*.25f*overlap*overlap*math.smoothstep(0f,join,math.min(sideRise,headRise));
        float rockFace=math.max(sideRise>=headRise?FaceWeight(sideT):0f,headRise>=sideRise && plan.Kind!=3?FaceWeight(headT):0f);
        float floor=1f-math.smoothstep(.68f,1.08f,cross);
        floor*=1f-math.smoothstep(.80f,1.09f,-plan.Along);
        float iceWidth=s.glacialWallWidth*.45f;
        float iceSideT=(.265f+.018f*rib-cross)/(iceWidth/(scale*.65f))+.5f;
        float iceFrontT=(.66f+.025f*headRib-plan.Along)/(iceWidth/(scale*1.4f))+.5f;
        float iceSide=CliffRamp(iceSideT,iceWidth),iceFront=CliffRamp(iceFrontT,iceWidth);
        float head=math.smoothstep(-.77f,-.46f,plan.Along);
        float body=math.min(iceSide,iceFront)*head;
        float iceFace=math.max(iceSide<=iceFront?FaceWeight(iceSideT):0f,iceFront<=iceSide?FaceWeight(iceFrontT):0f)*head;
        float hanging=0f;
        if(plan.Kind==1 || plan.Kind==2)
        {
            hanging=(1f-math.smoothstep(.065f,.125f,math.abs(plan.Across-sideSign*(.57f+.12f*plan.Along))))*
                (1f-math.smoothstep(.19f,.43f,math.abs(plan.Along+.25f)));
        }
        if(plan.Kind==3){body*=.10f;iceFace*=.10f;}
        return new GlacialSection(floor,rock,rockFace,body,iceFace,hanging);
    }
    public static float Shape(float2 p,WorldBaseSample input,float height,float scale,int seed,float coverage,
        float water,in WorldErosionSettings settings)
    {
        float cold=Cold(p,seed,scale,settings.landforms);
        if(cold<=0f || input.Height<=water+.6f || height<=water+.04f)return height;
        var plan=Plan(p,seed,scale);
        float active=cold*plan.Envelope*math.smoothstep(water+.6f,water+1.6f,input.Height)*
            math.smoothstep(water+.04f,water+1.2f,height);
        if(active<=0f)return height;
        var anchor=WorldTerrainHeight.BroadBase(plan.Anchor,scale,seed,coverage,settings);
        float floor=math.max(water+.8f,anchor.Height-(plan.Kind==3?.16f:.32f));
        float variation=WorldLandforms.Noise(p,math.max(scale,1f)*2.4f,seed+93001).x;
        float longitudinal=floor-plan.Along*.24f+variation*.035f;
        var section=Section(plan,scale,seed,settings.landforms);
        float target=longitudinal-plan.Bowl*.10f;
        float delta=target-height,limit=delta>0f?.50f:1.25f;
        // Only the floor correction is bounded. Rock masses and substantial ice
        // have their own resolved face widths and rise above that corrected floor.
        delta=limit*delta/math.sqrt(limit*limit+delta*delta)*section.Floor;
        float thickness=section.IceBody*settings.landforms.iceFrontHeight*(1f+.10f*variation);
        float seam=1f-math.smoothstep(.012f,.055f,math.abs(WorldLandforms.Noise(p,math.max(scale,1f)*.44f,seed+93002).x));
        thickness+=section.HangingIce*.065f-seam*section.IceBody*.008f;
        return height+(delta+section.RockRise+thickness)*active;
    }
    public static float IceExposure(float2 p,int seed,float scale,float height,float water,in WorldLandformSettings s)
    {
        if(height<=water+1.3f)return 0f;
        float cold=Cold(p,seed,scale,s);
        if(cold<.28f)return 0f;
        var plan=Plan(p,seed,scale);
        var section=Section(plan,scale,seed,s);
        float window=math.smoothstep(-.38f,.05f,WorldLandforms.Noise(p,math.max(scale,1f)*.8f,seed+94001).x);
        float body=math.smoothstep(.002f,.04f,section.IceBody)*math.max(window,section.IceFace);
        return cold*plan.Envelope*math.max(body,section.HangingIce*.85f);
    }
    public static float MoraineExposure(float2 p,int seed,float scale,float height,float water,in WorldLandformSettings s)
    {
        if(height<=water+1.0f)return 0f;
        float cold=Cold(p,seed,scale,s);
        if(cold<.4f)return 0f;
        var plan=Plan(p,seed,scale);float side=math.abs(plan.Across);
        float shoulder=math.smoothstep(.31f,.36f,side)*(1f-math.smoothstep(.46f,.51f,side));
        float broken=math.smoothstep(-.2f,.3f,WorldLandforms.Noise(p,math.max(scale,1f)*.75f,seed+94002).x);
        return cold*plan.Envelope*shoulder*broken*(1f-math.smoothstep(.55f,.9f,math.abs(plan.Along)));
    }
}
