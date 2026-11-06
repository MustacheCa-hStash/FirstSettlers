using System;
using System.Collections.Generic;
using Unity.Mathematics;

public sealed class TerrainSculptGuardException:ArgumentException
{
    public TerrainSculptBuildOutcome Outcome { get; }
    public TerrainSculptGuardException(TerrainSculptBuildOutcome outcome,string message):base(message) { Outcome=outcome; }
}

/// <summary>Bounded pure planning on the pending immutable snapshot, before extraction.
/// Expanding guards never stages a brush again or publishes a partial successor.</summary>
internal static class TerrainSculptPatchPlanner
{
    internal const int MaximumReplans=2,MaximumVisitedBricks=65536;
    internal static TerrainDensityCellBounds Envelope(TerrainSculptGroup group,TerrainSculptFootprint domain,
        Func<double,double,double> height,TerrainSourceSurfaceLayout layout,TerrainSculptBuildBudget budget,int extraGuard=0)
    {
        double low=group.Minimum.y,high=group.Maximum.y,s=layout.Spacing;
        long columns=((long)domain.Max.x-domain.Min.x+1)*((long)domain.Max.y-domain.Min.y+1);
        if(columns>2000000)throw new TerrainDensityCapacityException();
        for(int z=domain.Min.y;z<=domain.Max.y;z++)for(int x=domain.Min.x;x<=domain.Max.x;x++)
        {
            budget?.Check();double h=height(x*s,z*s);
            if(!math.isfinite(h))throw new ArgumentException("Nonfinite baseline height.");
            low=Math.Min(low,h-2*s);high=Math.Max(high,h+2*s);
        }
        int guard=checked(layout.GuardCells+extraGuard);
        return new TerrainDensityCellBounds(new int3(domain.Min.x,checked(TerrainDensityCoordinates.FloorIndex(low/s)-guard),domain.Min.y),
            new int3(domain.Max.x,checked(TerrainDensityCoordinates.FloorIndex(high/s)+guard+1),domain.Max.y));
    }

    internal static void PrepareGroups(ITerrainDensityField snapshot,List<TerrainSculptGroup> groups,
        Func<double,double,double> height,TerrainSourceSurfaceLayout layout,TerrainSculptBuildBudget budget,Func<TerrainSculptGroup,bool> relevant=null)
    {
        for(int attempt=0;;attempt++)
        {
            bool expanded=false;
            foreach(var group in groups)
            {
                if(relevant!=null&&!relevant(group))continue;
                var f=group.Footprint;
                var envelope=Envelope(group,f,height,layout,budget,attempt*16);
                var inner=new TerrainDensityCellBounds(new int3(f.Min.x+layout.CollarCells,envelope.Min.y,f.Min.y+layout.CollarCells),
                    new int3(f.Max.x-layout.CollarCells,envelope.Max.y,f.Max.y-layout.CollarCells));
                try { TerrainSculptPatchBuilder.ValidateEnvelope(snapshot,f,inner,height,inner); }
                catch(TerrainSculptGuardException ex) when(attempt<MaximumReplans)
                {
                    if(ex.Outcome==TerrainSculptBuildOutcome.NeedsHorizontalGuard)
                    { group.Footprint=new TerrainSculptFootprint(checked(f.Min-16),checked(f.Max+16));expanded=true; }
                    else if(ex.Outcome==TerrainSculptBuildOutcome.NeedsVerticalGuard)
                    {
                        // Persist the validated expansion in the group's planning bounds.
                        group.Minimum=math.min(group.Minimum,(double3)envelope.Min*layout.Spacing-new double3(0,16*layout.Spacing,0));
                        group.Maximum=math.max(group.Maximum,(double3)envelope.Max*layout.Spacing+new double3(0,16*layout.Spacing,0));expanded=true;
                    }
                    else throw;
                }
            }
            if(!expanded)return;
            // Rectangle expansion can connect two prior groups. Merge before deriving any
            // windows, so baseline masks and collision never overlap.
            for(int i=0;i<groups.Count;i++)for(int j=i+1;j<groups.Count;)
            {
                var a=groups[i];var b=groups[j];
                if(math.all(a.Footprint.Min<=b.Footprint.Max)&&math.all(b.Footprint.Min<=a.Footprint.Max))
                {
                    a.Footprint=new TerrainSculptFootprint(math.min(a.Footprint.Min,b.Footprint.Min),math.max(a.Footprint.Max,b.Footprint.Max));
                    a.Minimum=math.min(a.Minimum,b.Minimum);a.Maximum=math.max(a.Maximum,b.Maximum);groups.RemoveAt(j);i=-1;break;
                }
                j++;
            }
            groups.Sort((a,b)=>a.Footprint.Min.x!=b.Footprint.Min.x?a.Footprint.Min.x.CompareTo(b.Footprint.Min.x):a.Footprint.Min.y.CompareTo(b.Footprint.Min.y));
        }
    }
}
