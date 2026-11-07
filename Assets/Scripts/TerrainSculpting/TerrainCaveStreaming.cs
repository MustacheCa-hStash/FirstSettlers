using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

internal readonly struct TerrainCaveTileState
{
    internal readonly ulong Stamp;
    internal readonly bool Collision;
    internal TerrainCaveTileState(ulong stamp,bool collision){Stamp=stamp;Collision=collision;}
}
internal sealed class TerrainCaveTileBuild
{
    internal TerrainSculptMeshData Mesh;
    internal TerrainCaveTileState State;
}

/// <summary>Natural cave shells use fixed 3D tiles, never height-to-floor excavation envelopes.</summary>
internal static class TerrainCaveBatchBuilder
{
    internal const int TileCells=64,MaximumTiles=4096;
    internal const double CollisionLead=96;
    internal static double TileMetres=>TileCells*TerrainDensityProfile.Default.Spacing;
    internal static int3 Tile(double3 p)=>new int3(TerrainDensityCoordinates.FloorIndex(p.x/TileMetres),TerrainDensityCoordinates.FloorIndex(p.y/TileMetres),TerrainDensityCoordinates.FloorIndex(p.z/TileMetres));
    internal static TerrainDensityCellBounds Bounds(int3 key)=>new TerrainDensityCellBounds(key*TileCells,(key+1)*TileCells);
    internal static void Build(TerrainSculptBatch batch,TerrainDensitySnapshot snapshot,Func<double,double,double> height,int chunkSize,
        double3 focus,IReadOnlyList<TerrainSculptGroup> oldGroups,IReadOnlyList<TerrainSculptPin> pins,TerrainSculptBuildBudget budget,
        double3 oldAnchor,long oldRevision,ISet<ChunkCoord> oldChunks,TerrainSculptMeshCache cache,long cacheLimit,
        TerrainSourceSurfaceLayout layout,IReadOnlyDictionary<int3,TerrainCaveTileState> previous)
    {
        budget.Report("Finding connected cave systems");
        var generator=(TerrainCaveDensityGenerator)snapshot.Generator;
        var queried=generator.Query(focus-new double3(256,256,256),focus+new double3(256,256,256));
        var caves=new List<TerrainCaveLayout>();foreach(var cave in queried)
            if(cave.Intersects(focus-new double3(256),focus+new double3(256))&&
                (!cave.IsEnclosed||TerrainSculptCoveragePolicy.Distance(focus,cave.Minimum,cave.Maximum)<=CollisionLead))caves.Add(cave);
        var groups=new List<TerrainSculptGroup>();var candidates=new HashSet<int3>();
        void AddCells(double3 min,double3 max)
        {
            int3 lo=Tile(min-new double3(.6)),hi=Tile(max+new double3(.6));
            for(int z=lo.z;z<=hi.z;z++)for(int y=lo.y;y<=hi.y;y++)for(int x=lo.x;x<=hi.x;x++)
            {budget.Check();candidates.Add(new int3(x,y,z));if(candidates.Count>MaximumTiles)throw new TerrainDensityCapacityException("Natural cave tile admission limit reached.");}
        }
        void AddSurface(double3 min,double3 max)
        {
            double s=snapshot.Profile.Spacing;
            int2 lo=new int2(TerrainDensityCoordinates.FloorIndex(min.x/s),TerrainDensityCoordinates.FloorIndex(min.z/s));
            int2 hi=new int2(TerrainDensityCoordinates.FloorIndex(max.x/s)+1,TerrainDensityCoordinates.FloorIndex(max.z/s)+1);
            lo=new int2(TerrainDensityCoordinates.FloorDiv(lo.x-16,TileCells),TerrainDensityCoordinates.FloorDiv(lo.y-16,TileCells))*TileCells;
            hi=new int2(TerrainDensityCoordinates.FloorDiv(hi.x+16,TileCells)+1,TerrainDensityCoordinates.FloorDiv(hi.y+16,TileCells)+1)*TileCells;
            var group=new TerrainSculptGroup{Footprint=new TerrainSculptFootprint(lo,hi),Minimum=min,Maximum=max,Natural=true};
            for(int i=0;i<groups.Count;)
            {
                var old=groups[i];if(!math.all(group.Footprint.Min<=old.Footprint.Max)||!math.all(group.Footprint.Max>=old.Footprint.Min)){i++;continue;}
                group.Footprint=new TerrainSculptFootprint(math.min(group.Footprint.Min,old.Footprint.Min),math.max(group.Footprint.Max,old.Footprint.Max));
                group.Minimum=math.min(group.Minimum,old.Minimum);group.Maximum=math.max(group.Maximum,old.Maximum);groups.RemoveAt(i);i=0;
            }
            groups.Add(group);
        }
        foreach(var cave in caves)
        {
            budget.Report("Locating cave surface openings");
            budget.Check();if(!cave.Intersects(focus-new double3(256),focus+new double3(256)))continue;
            foreach(var span in cave.Spans)
            {
                AddCells(span.Minimum,span.Maximum);
                if(!span.Entrance)
                {
                    // Planning samples are deliberately coarse. A narrow canonical gully
                    // can still expose a room between them. Conservative native height
                    // bounds make every such breakthrough a real surface aperture too.
                    var min=new double3(span.Minimum.x,0,span.Minimum.z);var max=new double3(span.Maximum.x,0,span.Maximum.z);
                    if(generator.TryGetSurfaceBounds(min,max,out _,out double high)&&-high>span.Maximum.y+1.2)continue;
                    AddSurface(span.Minimum,span.Maximum);continue;
                }
                double3 openingMin=new double3(double.PositiveInfinity),openingMax=new double3(double.NegativeInfinity);
                double2 direction=math.normalize(span.End.xz-span.Start.xz),side=new double2(-direction.y,direction.x);
                double length=math.distance(span.Start.xz,span.End.xz),radius=span.Width*.65;
                for(double d=-radius;d<=length+radius+4.8;d+=4.8)for(int lateral=-1;lateral<=1;lateral++)
                {
                    double2 p=span.Start.xz+direction*d+side*(lateral*radius);double floor=span.Floor(math.clamp(d/length,0,1),lateral*radius);
                    if(height(p.x,p.y)>floor+span.Height+1.8)continue;
                    double3 point=new double3(p.x,floor,p.y);
                    openingMin=math.min(openingMin,point-new double3(4.8,.3,4.8));openingMax=math.max(openingMax,point+new double3(4.8,span.Height+1.5,4.8));
                }
                if(math.all(math.isfinite(openingMin)))AddSurface(openingMin,openingMax);
            }
        }
        foreach(var group in TerrainSculptBatchBuilder.Group(snapshot))
        {
            if(TerrainSculptCoveragePolicy.Distance(focus,group.Minimum,group.Maximum)>288)continue;
            AddCells(group.Minimum,group.Maximum);
            // Deep edits join volume tiles without reconstructing the surface above them.
            bool surface=false;
            for(int z=group.Footprint.Min.y;z<=group.Footprint.Max.y&&!surface;z+=16)
                for(int x=group.Footprint.Min.x;x<=group.Footprint.Max.x&&!surface;x+=16)
                    surface=height(x*snapshot.Profile.Spacing,z*snapshot.Profile.Spacing)<=group.Maximum.y+.6;
            if(surface)AddSurface(group.Minimum,group.Maximum);
        }
        foreach(var group in groups)
        {
            double3 min=new double3(group.Footprint.Min.x*snapshot.Profile.Spacing,-100000,group.Footprint.Min.y*snapshot.Profile.Spacing);
            double3 max=new double3(group.Footprint.Max.x*snapshot.Profile.Spacing,100000,group.Footprint.Max.y*snapshot.Profile.Spacing);
            foreach(var cave in caves)foreach(var span in cave.Spans)if(span.Intersects(min,max))
            {group.Minimum=math.min(group.Minimum,span.Minimum);group.Maximum=math.max(group.Maximum,span.Maximum);}
        }
        // Existing seam/collision transactions handle only the small exterior apertures.
        budget.Report("Preparing cave entrances");
        TerrainSculptBatchBuilder.Build(batch,snapshot,height,chunkSize,focus,oldGroups,pins,budget,oldAnchor,oldRevision,oldChunks,cache,cacheLimit,false,layout,groups);
        var ordered=new List<int3>(candidates);ordered.Sort((a,b)=>math.distancesq((double3)(a*TileCells)*snapshot.Profile.Spacing,focus).CompareTo(math.distancesq((double3)(b*TileCells)*snapshot.Profile.Spacing,focus)));
        batch.CaveCandidates=new HashSet<int3>();
        int tileIndex=0;
        foreach(var key in ordered)
        {
            budget.Report($"Building cave tiles {++tileIndex}/{ordered.Count}");
            budget.Check();var bounds=Bounds(key);bool inSurface=false;
            foreach(var group in batch.Groups)if(math.all(bounds.Min.xz>=group.Footprint.Min)&&math.all(bounds.Max.xz<=group.Footprint.Max)){inSurface=true;break;}
            if(inSurface)continue;
            batch.CaveCandidates.Add(key);
            double3 min=(double3)bounds.Min*snapshot.Profile.Spacing,max=(double3)bounds.Max*snapshot.Profile.Spacing;
            bool collision=TerrainSculptCoveragePolicy.Distance(focus,min,max)<=CollisionLead;
            if(pins!=null)foreach(var pin in pins)if(math.all(pin.Minimum<=max)&&math.all(pin.Maximum>=min))collision=true;
            ulong stamp=Stamp(snapshot,bounds);var state=new TerrainCaveTileState(stamp,collision);
            // Keep prepared collision while the connected shell is resident. Avoid
            // tearing it down behind the player and rebuilding it on a short return.
            if(previous!=null&&previous.TryGetValue(key,out var retained)&&retained.Collision)
            {collision=true;state=new TerrainCaveTileState(stamp,true);}
            if(previous!=null&&previous.TryGetValue(key,out var old)&&old.Stamp==stamp&&old.Collision==collision)
            {batch.ReuseCaveTiles.Add(key);continue;}
            long start=budget.ChargedBytes,persistent=budget.PersistentBytes;
            var mesh=TerrainDensityMesher.BuildCached(snapshot,bounds,budget,null);
            mesh=Interior(mesh,budget);
            mesh=TerrainCaveMeshSimplifier.Simplify(mesh,snapshot,budget);
            budget.CompleteScope(start,mesh.PayloadBytes*2,persistent);
            batch.CaveTiles.Add(key,new TerrainCaveTileBuild{Mesh=mesh,State=state});
        }
        // Producer-computed conservative original-height bounds give omitted/untraced
        // lighting pages an underground fallback without sampling terrain on the main thread.
        budget.Report("Preparing cave lighting bounds");
        batch.CaveSky=new Dictionary<int3,float>();var skyHeights=new Dictionary<int2,float>();
        foreach(var cave in caves)foreach(var span in cave.Spans)
        {
            int3 lo=(int3)math.floor(span.Minimum/TerrainSkyAccess.TileMetres),hi=(int3)math.floor(span.Maximum/TerrainSkyAccess.TileMetres);
            for(int z=lo.z;z<=hi.z;z++)for(int x=lo.x;x<=hi.x;x++)
            {
                budget.Check();var xz=new int2(x,z);
                if(!skyHeights.TryGetValue(xz,out float minimumHeight))
                {
                    var min=new double3(x*TerrainSkyAccess.TileMetres,0,z*TerrainSkyAccess.TileMetres);var max=min+new double3(TerrainSkyAccess.TileMetres,0,TerrainSkyAccess.TileMetres);
                    if(!generator.TryGetSurfaceBounds(min,max,out _,out double high))continue;
                    minimumHeight=(float)(Math.Floor(-high*1000)/1000);skyHeights.Add(xz,minimumHeight);
                }
                for(int y=lo.y;y<=hi.y;y++)
                {batch.CaveSky[new int3(x,y,z)]=minimumHeight;if(batch.CaveSky.Count>4096)throw new TerrainDensityCapacityException("Natural cave sky fallback capacity reached.");}
            }
        }
        budget.ChargePersistent(batch.CaveSky.Count*48L);
        batch.Charge=budget.PeakBytes;
    }
    internal static ulong Stamp(TerrainDensitySnapshot snapshot,TerrainDensityCellBounds bounds)
    {
        int3 lo=TerrainDensityCoordinates.BrickOf(bounds.Min-1),hi=TerrainDensityCoordinates.BrickOf(bounds.Max+1);
        ulong stamp=1469598103934665603;
        for(int z=lo.z;z<=hi.z;z++)for(int y=lo.y;y<=hi.y;y++)for(int x=lo.x;x<=hi.x;x++)
        {snapshot.Bricks.TryGetValue(new int3(x,y,z),out var brick);unchecked{stamp=(stamp^(ulong)(brick?.Identity??0))*1099511628211;}}
        return stamp;
    }
    private static TerrainSculptMeshData Interior(TerrainSculptMeshData source,TerrainSculptBuildBudget budget)
    {
        var output=new TerrainSculptMeshBuffer(budget:budget);var data=new List<float2>();var vertices=new Dictionary<int,int>();
        int Vertex(int i)
        {if(vertices.TryGetValue(i,out int index))return index;index=output.AddStored(source.Positions[i],source.Normals[i]);data.Add(source.SurfaceData[i]);vertices.Add(i,index);return index;}
        for(int i=0;i<source.Triangles.Length;i+=3)
        {
            int a=source.Triangles[i],b=source.Triangles[i+1],c=source.Triangles[i+2];
            if(Math.Max(source.SurfaceData[a].x,Math.Max(source.SurfaceData[b].x,source.SurfaceData[c].x))<.5f)continue;
            output.TriangleOriented(Vertex(a),Vertex(b),Vertex(c));
        }
        return output.Finish(source.SampledBricks,source.SkippedBricks,surfaceData:data.ToArray());
    }
}

/// <summary>One independently owned 3D shell tile. No source heightfield runtime is required.</summary>
internal sealed class TerrainCaveTileSurface:IDisposable
{
    private GameObject root;
    private Mesh mesh;
    private MeshCollider collider;
    private MeshRenderer renderer;
    internal readonly TerrainCaveTileState State;
    internal readonly long Charge;
    internal readonly int Triangles;
    internal bool CollisionPrepared=>!State.Collision||mesh==null||collider!=null;
    internal TerrainCaveTileSurface(int3 key,TerrainCaveTileBuild build,Material material,TerrainSculptSurfaceSettings appearance)
    {
        State=build.State;Charge=build.Mesh.PayloadBytes*2+4096;Triangles=build.Mesh.TriangleCount;
        if(Triangles==0)return;
        try
        {
            root=new GameObject($"Cave_{key.x}_{key.y}_{key.z}");root.SetActive(false);root.layer=GameplayLayers.WorldSolid;root.AddComponent<WorldGroundSurface>();
            // World-zero conversion matches the existing exterior surface's float conversion.
            mesh=build.Mesh.CreateMesh(new double3(0),root.name);root.AddComponent<MeshFilter>().sharedMesh=mesh;
            renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
            if(appearance!=null){var block=new MaterialPropertyBlock();appearance.Apply(block);renderer.SetPropertyBlock(block);}
        }
        catch{Dispose();throw;}
    }
    internal void PrepareCollision(){if(root!=null&&State.Collision&&collider==null){collider=root.AddComponent<MeshCollider>();collider.sharedMesh=mesh;}}
    internal void Publish()
    {
        if(!CollisionPrepared)throw new InvalidOperationException("Cave collision must be cooked before publication.");
        if(root==null)return;root.SetActive(true);
        LocalFlameGeometry.Set(this,new[]{new LocalFlameGeometry.Part(mesh,Matrix4x4.identity,renderer.sharedMaterial,renderer:renderer)});
    }
    internal void Hide(){if(root!=null)root.SetActive(false);LocalFlameGeometry.Remove(this);}
    public void Dispose(){Hide();if(collider!=null)collider.sharedMesh=null;TerrainAssetOwner.Destroy(root);TerrainAssetOwner.Destroy(mesh);root=null;mesh=null;collider=null;renderer=null;}
}
