using System;
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>The original landscape remains distinct from the natural underground baseline.</summary>
public interface ITerrainSurfaceReference
{
    double SampleSurfaceDensity(double3 position);
}

/// <summary>A bounded, immutable air volume with an independently controlled walking floor.</summary>
public readonly struct TerrainCaveSpan
{
    public readonly double3 Start, End;
    public readonly double Width, Height;
    public readonly int Seed;
    public readonly bool Entrance;
    public readonly bool Natural;
    private readonly double3 minimum,maximum;
    private readonly double2 direction;
    private readonly double length;
    public double3 Minimum => minimum;
    public double3 Maximum => maximum;

    public TerrainCaveSpan(double3 start,double3 end,double width,double height,int seed,bool entrance=false,bool natural=false)
    {
        if(!math.all(math.isfinite(start))||!math.all(math.isfinite(end))||!double.IsFinite(width)||!double.IsFinite(height)||width<2||height<2.5)
            throw new ArgumentException("Cave spans require finite floors, width >= 2 m and height >= 2.5 m.");
        if(math.length(end.xz-start.xz)<.01)throw new ArgumentException("A cave span needs horizontal extent.");
        Start=start;End=end;Width=width;Height=height;Seed=seed;Entrance=entrance;Natural=natural;
        minimum=math.min(start,end)-new double3(width*.65+.3,.3,width*.65+.3);
        maximum=math.max(start,end)+new double3(width*.65+.3,height+(natural?3:1.5),width*.65+.3);
        length=math.length(end.xz-start.xz);direction=(end.xz-start.xz)/length;
    }

    public double Floor(double t,double lateral)
    {
        // Detail has a continuous derivative and remains well below the controller step.
        double phase=(Seed&1023)*.03125;
        if(Natural)
        {
            double3 p=math.lerp(Start,End,t);
            double detail=.03*Math.Sin(p.x*.11+p.z*.07+phase)*Math.Sin(p.z*.09-p.x*.04);
            double shoulder=math.smoothstep(Width*.22,Width*.5,Math.Abs(lateral));
            return p.y+detail+shoulder*(.55+.15*Math.Sin(p.x*.17+p.z*.13+phase));
        }
        double distance=length*t;
        double wave=.035*Math.Sin(distance*.43+phase)*Math.Sin(distance*.17+phase*.7);
        double shoulders=.12*math.smoothstep(Width*.22,Width*.48,Math.Abs(lateral));
        return math.lerp(Start.y,End.y,t)+wave+shoulders;
    }
    public double AboveFloor(double3 p)
    {
        double t=math.clamp(math.dot(p.xz-Start.xz,direction)/length,0,1);
        double lateral=math.dot(p.xz-math.lerp(Start.xz,End.xz,t),new double2(-direction.y,direction.x));
        return p.y-Floor(t,lateral);
    }

    /// <summary>Positive inside air. Shape bounds include every possible deformation.</summary>
    public double Air(double3 p)
    {
        double along=math.dot(p.xz-Start.xz,direction),t=math.clamp(along/length,0,1);
        double lateral=math.dot(p.xz-math.lerp(Start.xz,End.xz,t),new double2(-direction.y,direction.x));
        double endDistance=Math.Max(0,Math.Max(-along,along-length));
        double phase=(Seed&1023)*.03125;
        if(Natural)
        {
            // World-space geological bands stay coherent across route subdivisions.
            double broad=Math.Sin(p.x*.027+p.z*.019+phase),secondary=Math.Sin(p.z*.051-p.x*.013+phase*.7);
            double naturalRadius=Width*(.5+.065*broad+.035*secondary);
            double naturalDrift=Width*.025*Math.Sin(p.x*.033-p.z*.021+phase);
            double strata=Math.Sin(p.y*.8+Math.Sin(p.x*.04+p.z*.03)+phase);
            double rough=.12*strata*Math.Sin(p.x*.19+p.z*.23+phase);
            double naturalSide=naturalRadius-Math.Sqrt((lateral-naturalDrift)*(lateral-naturalDrift)+endDistance*endDistance)+rough;
            double naturalAbove=p.y-Floor(t,lateral);
            double naturalArch=Height*(.52+.48*Math.Sqrt(Math.Max(0,1-Math.Pow(Math.Min(1,Math.Abs(lateral-naturalDrift)/Math.Max(1,naturalRadius)),2))));
            if((Seed&0x40000000)!=0)naturalArch=Height*(.88-.32*Math.Min(1,Math.Abs(lateral-naturalDrift)/Math.Max(1,naturalRadius)));
            double naturalRoof=naturalArch+1.0*Math.Sin(p.x*.041+p.z*.035+phase)+.35*secondary;
            return Math.Min(naturalSide,Math.Min(naturalAbove,naturalRoof-naturalAbove));
        }
        double wall=.10*Width*Math.Sin(along*.075+phase)*Math.Sin(along*.039-phase);
        double radius=Width*.5+wall;
        double drift=.05*Width*Math.Sin(along*.055+phase);
        double roughness=.18*Math.Sin(p.x*.7+p.z*.23+phase)*Math.Sin(p.y*.4+phase);
        double side=radius-Math.Sqrt((lateral-drift)*(lateral-drift)+endDistance*endDistance)+roughness;
        double floor=Floor(t,lateral),above=p.y-floor;
        // Broad roof arch, with separate metre-scale roof variation. Floor never inherits it.
        double arch=Height*(.68+.32*Math.Sqrt(Math.Max(0,1-Math.Pow(Math.Min(1,Math.Abs(lateral)/Math.Max(1,radius)),2))));
        double roof=arch+.45*Math.Sin(along*.12+phase)*Math.Sin(lateral*.21-phase)+.4*Math.Sin(lateral*.12+phase);
        return Math.Min(side,Math.Min(above,roof-above));
    }

    public bool Intersects(double3 min,double3 max)=>math.all(Minimum<=max)&&math.all(Maximum>=min);
    public double MinimumAir(double3 min,double3 max)
    {
        double distance=0;
        for(int z=0;z<2;z++)for(int x=0;x<2;x++)
        {
            double2 p=new double2(x==0?min.x:max.x,z==0?min.z:max.z);
            double along=math.dot(p-Start.xz,direction),t=math.clamp(along/length,0,1);
            distance=Math.Max(distance,math.distance(p,math.lerp(Start.xz,End.xz,t)));
        }
        double floorMin=Math.Min(Start.y,End.y)-.035,floorMax=Math.Max(Start.y,End.y)+(Natural?.74:.155);
        return Math.Min(Width*(Natural?.3:.35)-.18-distance,Math.Min(min.y-floorMax,floorMin+Height*(Natural?.52:.68)-(Natural?1.4:.85)-max.y));
    }
}

public readonly struct TerrainCaveFormation
{
    public readonly double3 Base,Tip;
    public readonly double Radius;
    public readonly bool Boulder;
    public TerrainCaveFormation(double3 anchor,double3 tip,double radius,bool boulder=false)
    {
        if(!math.all(math.isfinite(anchor))||!math.all(math.isfinite(tip))||!double.IsFinite(radius)||radius<=0||Math.Abs(anchor.y-tip.y)<.01)
            throw new ArgumentException("Formation requires finite endpoints, positive radius and nonzero height.");
        Base=anchor;Tip=tip;Radius=radius;Boulder=boulder;
    }
    public double3 Minimum=>math.min(Base,Tip)-new double3(Radius,0,Radius);
    public double3 Maximum=>math.max(Base,Tip)+new double3(Radius,0,Radius);
    public bool Intersects(double3 min,double3 max)=>math.all(Minimum<=max)&&math.all(Maximum>=min);
    // Negative inside rock; subtraction from the cave air remains excavatable.
    public double Distance(double3 p)
    {
        if(Boulder)
        {
            double3 center=(Base+Tip)*.5,radii=new double3(Radius,Math.Abs(Tip.y-Base.y)*.5,Radius*.8);
            return (math.length((p-center)/radii)-1)*math.cmin(radii);
        }
        double t=math.clamp((p.y-Base.y)/(Tip.y-Base.y),0,1);
        double radius=Radius*Math.Pow(1-t,1.35);
        return Math.Max(math.length(p.xz-Base.xz)-radius,Math.Max(Minimum.y-p.y,p.y-Maximum.y));
    }
}

public sealed class TerrainCaveLayout
{
    private readonly TerrainCaveSpan[] spans;
    public IReadOnlyList<TerrainCaveSpan> Spans { get; }
    public readonly int2 Cell;
    public readonly double3 Minimum,Maximum;
    public readonly double3 Entrance,Destination;
    public readonly double RouteLength;
    public IReadOnlyList<double3> Entrances { get; }
    public bool IsEnclosed=>Entrances.Count==0;
    public IReadOnlyList<TerrainCaveFormation> Formations { get; }
    public TerrainCaveLayout(int2 cell,IEnumerable<TerrainCaveSpan> spans,double3 entrance,double3 destination,IEnumerable<double3> entrances=null,IEnumerable<TerrainCaveFormation> formations=null)
    {
        Cell=cell;this.spans=new List<TerrainCaveSpan>(spans).ToArray();
        Spans=Array.AsReadOnly(this.spans);
        if(this.spans.Length==0)throw new ArgumentException("A cave needs at least one span.");
        Minimum=new double3(double.PositiveInfinity);Maximum=new double3(double.NegativeInfinity);
        foreach(var span in this.spans){Minimum=math.min(Minimum,span.Minimum);Maximum=math.max(Maximum,span.Maximum);RouteLength+=math.distance(span.Start,span.End);}
        Entrance=entrance;Destination=destination;
        Entrances=Array.AsReadOnly(entrances==null?new[]{entrance,destination}:new List<double3>(entrances).ToArray());
        Formations=Array.AsReadOnly(formations==null?Array.Empty<TerrainCaveFormation>():new List<TerrainCaveFormation>(formations).ToArray());
    }
    public double Air(double3 p)
    {
        double air=double.NegativeInfinity,floorLimit=double.NegativeInfinity;
        foreach(var span in spans)
        {
            if(!span.Intersects(p-new double3(1.3),p+new double3(1.3)))continue;
            // A bounded smooth union rounds wall/ceiling joins without making floors rough.
            double value=span.Air(p);
            floorLimit=Math.Max(floorLimit,span.AboveFloor(p));
            if(double.IsNegativeInfinity(air)){air=value;continue;}
            const double blend=.6;
            double h=math.clamp(.5+.5*(air-value)/blend,0,1);
            air=math.lerp(value,air,h)+blend*h*(1-h);
        }
        // Round wall/ceiling joins without excavating little pits at coincident floors.
        air=Math.Min(air,floorLimit);
        if(air> -1.3)foreach(var formation in Formations)
            if(formation.Intersects(p-new double3(1.3),p+new double3(1.3)))air=Math.Min(air,formation.Distance(p));
        return air;
    }
    public bool Intersects(double3 min,double3 max)=>math.all(Minimum<=max)&&math.all(Maximum>=min);

    /// <summary>Fixed first-phase fixture: cliff entry, main chamber, loop and a walkable ground exit.</summary>
    public static TerrainCaveLayout Representative(double3 origin)
    {
        var spans=new List<TerrainCaveSpan>();int seed=731;
        void Add(double3 a,double3 b,double width,double height,bool entrance=false)=>spans.Add(new TerrainCaveSpan(origin+a,origin+b,width,height,seed++,entrance));
        Add(new double3(-12,0,0),new double3(32,0,0),8,6,true);
        Add(new double3(32,0,0),new double3(72,-1,12),9,7);
        Add(new double3(72,-1,12),new double3(98,-1,12),36,19);
        Add(new double3(98,-1,12),new double3(132,0,0),8,6);
        Add(new double3(72,-1,12),new double3(91,0,49),6,5);
        Add(new double3(91,0,49),new double3(132,0,0),6,5);
        Add(new double3(132,0,0),new double3(182,8,0),8,6,true);
        return new TerrainCaveLayout(default,spans,origin+new double3(-10,.15,0),origin+new double3(180,8.15,0));
    }
}

/// <summary>Natural air composed before sparse player overrides; never advertises height-only density.</summary>
public sealed class TerrainCaveDensityGenerator : ITerrainDensityGenerator,ITerrainDensityBounds,ITerrainSurfaceReference,ITerrainColumnDensityGenerator
{
    private readonly ITerrainHeightDensityGenerator surface;
    private readonly ITerrainDensityBounds surfaceBounds;
    private readonly Func<double3,double3,IReadOnlyList<TerrainCaveLayout>> layouts;
    private readonly Func<double3,double> air;
    public TerrainCaveDensityGenerator(ITerrainHeightDensityGenerator surface,Func<double3,double3,IReadOnlyList<TerrainCaveLayout>> layouts,Func<double3,double> air=null)
    {this.surface=surface??throw new ArgumentNullException(nameof(surface));surfaceBounds=surface as ITerrainDensityBounds;this.layouts=layouts??throw new ArgumentNullException(nameof(layouts));this.air=air;}
    public IReadOnlyList<TerrainCaveLayout> Query(double3 min,double3 max)=>layouts(min,max);
    public bool TryGetSurfaceBounds(double3 min,double3 max,out double low,out double high)
    {low=double.NegativeInfinity;high=double.PositiveInfinity;return surfaceBounds!=null&&surfaceBounds.TryGetDensityBounds(min,max,out low,out high);}
    public double SampleSurfaceDensity(double3 p)=>surface.SampleDensity(p);
    public double SampleSurfaceHeight(double x,double z)=>surface.SampleHeight(x,z);
    public double SampleDensity(double3 p)=>SampleDensityWithSurface(p,SampleSurfaceHeight(p.x,p.z));
    public double SampleDensityWithSurface(double3 p,double surfaceHeight)
    {
        double value=p.y-surfaceHeight;
        if(air!=null)return Math.Max(value,air(p));
        foreach(var cave in Query(p-new double3(1.3),p+new double3(1.3)))if(cave.Intersects(p-new double3(1.3),p+new double3(1.3)))value=Math.Max(value,cave.Air(p));
        return value;
    }
    public bool TryGetDensityBounds(double3 min,double3 max,out double low,out double high)
    {
        low=double.NegativeInfinity;high=double.PositiveInfinity;
        if(surfaceBounds==null||!surfaceBounds.TryGetDensityBounds(min,max,out low,out high))return false;
        foreach(var cave in Query(min-new double3(1.3),max+new double3(1.3)))
        {
            bool unformed=true;foreach(var formation in cave.Formations)if(formation.Intersects(min,max)){unformed=false;break;}
            foreach(var span in cave.Spans)if(span.Intersects(min-new double3(1.3),max+new double3(1.3)))
            {
                // Negative air distances outside a span's bounded evaluation domain are
                // deliberately omitted. Only a proven all-air box may raise the lower bound.
                double inside=span.MinimumAir(min,max);if(unformed&&inside>0)low=Math.Max(low,inside);
                high=Math.Max(high,span.Width+span.Height+cave.Spans.Count*.6);
            }
        }
        return true;
    }
}
