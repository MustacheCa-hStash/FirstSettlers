using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;

/// <summary>Growing connected excavations, using real asynchronous publication and collision.</summary>
public static class TerrainSculptingPerformanceValidation
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    [MenuItem("Tools/Terrain/Validate Sustained Sculpt Performance")]
    public static void Run()
    {
        ValidateCachedGeometry();
        ValidateConnectedChunkReuse();
        foreach(int width in new[]{2,4,6})
        {
            using var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>24+.12*x+.07*z);
            // These are connected, roofed rooms of increasing area. Seed outside the timed
            // path, as a loaded save would, then measure real successive small/large edits.
            for(int z=0;z<width;z++)for(int x=0;x<width;x++)
            {
                using var edit=f.World.StageExcavation(TerrainDensityBrush.Sphere(new double3(12+x*3,19,12+z*3),2));
                Check(f.World.TryCommit(edit),"Room seed failed.");
            }
            f.Viewer.transform.position=new Vector3(12,19,12);f.Ready(timeoutMs:120000);
            double cold=f.Runtime.LastWorkerMs;
            foreach(double radius in new[]{.5,.5,2d})
            {
                var center=new double3(9.9-radius*.3,19,12+(radius==2?1.7:0));
                if(radius==.5&&f.Runtime.CommittedEdits>0)center.z+=.7;
                var watch=Stopwatch.StartNew();
                Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(center,radius)),"Benchmark cut rejected.");
                f.Ready(timeoutMs:120000);
                Check(f.Runtime.LastReusedBricks>0,"Warm excavation failed to reuse any mesh bricks.");
                Debug.Log($"SCULPT SUSTAINED: seeded={width*width}, radius={radius}, coldWorker={cold:F2}, total={watch.Elapsed.TotalMilliseconds:F2}, worker={f.Runtime.LastWorkerMs:F2}, extract={f.Runtime.LastExtractMs:F2}, assembly={f.Runtime.LastAssemblyMs:F2}, bricks={f.Runtime.LastExtractedBricks}/{f.Runtime.LastReusedBricks}, stage={f.Runtime.LastMeshStageMs:F2}, cook={f.Runtime.LastCookMs:F2}, commit={f.Runtime.LastCommitMs:F2}, triangles={f.Runtime.LiveRenderTriangles}/{f.Runtime.LiveCollisionTriangles}");
            }
        }
        Debug.Log("TERRAIN SCULPTING PERFORMANCE PASS: growing connected rooms and successive small/large edits committed.");
    }
    public static void RunBatch()
    {
        try{Run();EditorApplication.Exit(0);}
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    private static void ValidateCachedGeometry()
    {
        var p=TerrainDensityProfile.Default;
        using var world=new TerrainDensityWorld(p,new TerrainSculptingIntegrationValidation.Height((x,z)=>10));
        var footprint=new TerrainSculptFootprint(new int2(0),new int2(64));
        var builderType=typeof(TerrainDensityMesher).Assembly.GetType("TerrainSculptMeshCache+Builder");
        var build=typeof(TerrainSculptPatchBuilder).GetMethod("BuildPatchCached",BindingFlags.Static|BindingFlags.NonPublic);
        var finish=builderType.GetMethod("Finish",BindingFlags.Instance|BindingFlags.NonPublic);
        object previous=null;
        for(int phase=0;phase<6;phase++)
        {
            if(phase==0||phase==2||phase==3)
            {
                using var edit=world.StageExcavation(TerrainDensityBrush.Sphere(new double3(9.6+(phase==0?0:2),7,9.6+(phase==3?1:0)),phase==0?2:.5));
                Check(world.TryCommit(edit),"Cache fixture edit failed.");
            }
            using var snapshot=world.CaptureSnapshot();
            ITerrainDensityField field=phase<4?(ITerrainDensityField)snapshot:
                new TerrainSculptVisualField(snapshot,new TerrainSculptCoveragePolicy(new double3(220+(phase-4)*12,7,9.6)));
            var budget=new TerrainSculptBuildBudget(128L*1024*1024);
            var builder=Activator.CreateInstance(builderType,BindingFlags.Instance|BindingFlags.NonPublic,null,
                new object[]{previous,snapshot,budget,16L*1024*1024},null);
            foreach(int step in new[]{1,8})
            {
                var plain=TerrainSculptPatchBuilder.BuildPatch(field,footprint,0,40,(x,z)=>10,step);
                var cached=(TerrainSculptMeshData)build.Invoke(null,new object[]{field,footprint,0,40,(Func<double,double,double>)((x,z)=>10),step,budget,builder,null});
                SameGeometry(plain,new[]{cached});
                var windows=new List<TerrainSculptMeshData>();
                for(int z=0;z<2;z++)for(int x=0;x<2;x++)
                {
                    var window=new TerrainSculptFootprint(new int2(x,z)*32,new int2(x+1,z+1)*32);
                    windows.Add((TerrainSculptMeshData)build.Invoke(null,new object[]{field,footprint,0,40,(Func<double,double,double>)((a,b)=>10),step,budget,builder,window}));
                }
                SameGeometry(plain,windows);
            }
            if(phase==1)Check((int)builderType.GetProperty("Built",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(builder)==0,"Unchanged density remeshed cached bricks.");
            previous=finish.Invoke(builder,null);
        }
        Debug.Log("SCULPT CACHE GEOMETRY PASS: cold/warm/edited/faded fine and coarse collars, changed coverage anchors, exact triangle positions/winding, normals and source-chunk windows match uncached extraction.");
    }
    private static void SameGeometry(TerrainSculptMeshData expected,IReadOnlyList<TerrainSculptMeshData> pieces)
    {
        var ids=new Dictionary<double3,int>();var normals=new Dictionary<double3,float3>();
        for(int i=0;i<expected.Positions.Length;i++)
        {ids[expected.Positions[i]]=i;normals[expected.Positions[i]]=expected.Normals[i];}
        (int,int,int) Triangle(int a,int b,int c)=>a<b&&a<c?(a,b,c):b<c?(b,c,a):(c,a,b);
        var triangles=new HashSet<(int,int,int)>();
        for(int i=0;i<expected.Triangles.Length;i+=3)
            Check(triangles.Add(Triangle(expected.Triangles[i],expected.Triangles[i+1],expected.Triangles[i+2])),"Duplicate reference triangle.");
        foreach(var mesh in pieces)
        {
            var remap=new int[mesh.Positions.Length];
            for(int i=0;i<remap.Length;i++)
            {
                Check(ids.TryGetValue(mesh.Positions[i],out remap[i]),"Cache/window changed a vertex position.");
                Check(math.distance(normals[mesh.Positions[i]],mesh.Normals[i])<1e-6,"Cache/window changed a seam normal.");
            }
            for(int i=0;i<mesh.Triangles.Length;i+=3)
                Check(triangles.Remove(Triangle(remap[mesh.Triangles[i]],remap[mesh.Triangles[i+1]],remap[mesh.Triangles[i+2]])),"Cache/window changed triangle topology or winding.");
        }
        Check(triangles.Count==0,"Cache/window lost triangles.");
    }
    private static void ValidateConnectedChunkReuse()
    {
        using var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10);
        for(int i=0;i<28;i++)
        {
            using var edit=f.World.StageExcavation(TerrainDensityBrush.Sphere(new double3(12+i*3,7,12),2));
            Check(f.World.TryCommit(edit),"Long-cave seed failed.");
        }
        f.Viewer.transform.position=new Vector3(12,7,12);f.Ready();
        var live=(Dictionary<ChunkCoord,TerrainSculptChunkSurface>)typeof(TerrainSculptRuntime).GetField("live",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f.Runtime);
        var distant=live[new ChunkCoord(2,0)];var near=live[new ChunkCoord(0,0)];
        Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(9.7,7,12),.5)),"Local cave cut rejected.");f.Ready();
        Check(ReferenceEquals(distant,live[new ChunkCoord(2,0)])&&!ReferenceEquals(near,live[new ChunkCoord(0,0)]),"Local cut rebuilt a distant part of the connected cave.");
        near=live[new ChunkCoord(0,0)];var middle=live[new ChunkCoord(1,0)];
        Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(128*TerrainDensityProfile.Default.Spacing,5,12),.5)),"Chunk-boundary cut rejected.");f.Ready();
        Check(!ReferenceEquals(near,live[new ChunkCoord(0,0)])&&!ReferenceEquals(middle,live[new ChunkCoord(1,0)]),"Edit failed to invalidate both sides of a source-chunk boundary.");
        Check(ReferenceEquals(distant,live[new ChunkCoord(2,0)]),"Boundary cut rebuilt the untouched distant chunk.");
        Check(Physics.Raycast(new Vector3(84,13,12),Vector3.down,out var roof,5,GameplayLayers.SolidSurfaceMask)&&Math.Abs(roof.point.y-10)<.001,"Distant retained cave lost its roof collider.");
        Debug.Log("SCULPT LOCAL REBUILD PASS: connected 80 m cave retains distant Unity meshes/colliders; source-border edit replaces both neighboring chunks and preserves the remote roof.");
    }
}
