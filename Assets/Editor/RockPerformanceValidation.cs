using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class RockPerformanceValidation
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    [MenuItem("Tools/Terrain/Log Live Rock Performance")]
    public static void LogLive()
    {
        var world=Object.FindAnyObjectByType<WorldManager>();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var chunks=world!=null?typeof(WorldManager).GetField("chunkManager",flags).GetValue(world):null;
        var foliage=chunks!=null?(FoliageManager)typeof(ChunkManager).GetField("foliageManager",flags).GetValue(chunks):null;
        Debug.Log(foliage!=null?foliage.RockPerformanceSummary:"Enter Play mode with a world to inspect live rock counts.");
    }
    public static void Run()
    {
        var prefab=Resources.Load<GameObject>("Rocks/Fieldstone_Bare");var preset=prefab.GetComponent<RockPreset>();
        Check(preset.distantMesh!=null && preset.distantMesh.GetIndexCount(0)<prefab.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0),"Distant rock mesh missing or not cheaper");
        var root=new GameObject("Rock performance fixture");
        var settings=new TreeSettings();using var renderer=new RockWorldRenderer(settings);using var pool=new RockColliderPool(settings);
        var cameraObject=new GameObject("Rock performance camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        camera.transform.position=new Vector3(0,2,-8);camera.transform.LookAt(new Vector3(0,.4f,0));camera.farClipPlane=400;
        try
        {
            var sets=new RockRenderSet[113];
            for(int c=0;c<sets.Length;c++)
            {
                var set=sets[c]=new RockRenderSet();
                set.Add(prefab,new RockInstanceData(Vector3.zero,Quaternion.identity,Vector3.one,WorldFeatureVariant.GrasslandBoulder,0),root.transform);
                renderer.Register(set);
            }
            renderer.Draw(Vector3.zero,null,false);
            Check(renderer.DrawCalls==1 && renderer.VisibleGeometry.instances==113,"Cross-chunk grouping did not merge draws");
            var more=new RockRenderSet();for(int i=0;i<1040;i++)more.Add(prefab,new RockInstanceData(Vector3.zero,Quaternion.identity,Vector3.one,WorldFeatureVariant.GrasslandBoulder,0),root.transform);renderer.Register(more);
            renderer.Draw(Vector3.zero,null,false);Check(renderer.DrawCalls==2 && renderer.VisibleGeometry.instances==1153,"1023 instance global split");renderer.Unregister(more);
            renderer.Draw(new Vector3(100,0,0),null,false);Check(renderer.VisibleGeometry.vertices==113*preset.distantMesh.vertexCount,"Distant LOD selection");
            renderer.Draw(Vector3.zero,camera,false);Check(renderer.VisibleGeometry.instances==113,"Near visible culling");
            var hidden=new RockRenderSet();hidden.Add(prefab,new RockInstanceData(new Vector3(0,0,-25),Quaternion.identity,Vector3.one,WorldFeatureVariant.GrasslandBoulder,0),root.transform);renderer.Register(hidden);
            renderer.Draw(Vector3.zero,camera,false);Check(renderer.VisibleGeometry.instances==113 && renderer.ShadowGeometry.instances==1,"Offscreen shadow or frustum culling");
            hidden.Enabled=false;renderer.Draw(Vector3.zero,camera,false);Check(renderer.ShadowGeometry.instances==0,"Disabled streamed set still rendered");
            var distantSmall=new RockRenderSet();distantSmall.Add(prefab,new RockInstanceData(new Vector3(0,0,-100),Quaternion.identity,Vector3.one,WorldFeatureVariant.GrasslandBoulder,0),root.transform);renderer.Register(distantSmall);
            renderer.Draw(Vector3.zero,camera,false);Check(renderer.ShadowGeometry.instances==0,"Tiny distant stones still cast shadows");
            var distantLarge=new RockRenderSet();distantLarge.Add(Resources.Load<GameObject>("Rocks/LongBoulder_Bare"),new RockInstanceData(new Vector3(0,0,-100),Quaternion.identity,Vector3.one,WorldFeatureVariant.GrasslandLargeBoulder,0),root.transform);renderer.Register(distantLarge);
            renderer.Draw(Vector3.zero,camera,false);Check(renderer.ShadowGeometry.instances==1,"Boulder shadow range was lost");
            for(int i=0;i<100;i++)renderer.Draw(Vector3.zero,camera,false);
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)renderer.Draw(Vector3.zero,camera,false);
            Check(GC.GetAllocatedBytesForCurrentThread()==before,"Steady rock culling/submission allocates");
            settings.enableRockRendering=false;renderer.Draw(Vector3.zero,camera,false);Check(renderer.DrawCalls==0,"Render A/B toggle");
            foreach(var set in sets)renderer.Unregister(set);renderer.Unregister(hidden);renderer.Unregister(distantSmall);renderer.Unregister(distantLarge);Check(renderer.ResidentInstances==0,"Renderer unload leak");
            for(int c=0;c<113;c++)
            {
                var set=new RockRenderSet();set.Add(prefab,new RockInstanceData(new Vector3(c*38.4f,0,0),Quaternion.identity,Vector3.one,WorldFeatureVariant.GrasslandBoulder,0),root.transform);pool.Register(set);
            }
            pool.Update(Vector3.zero,0);int scans=pool.ScanCount;
            Check(pool.LastScanEntries==1,"Collision scan visited distant chunks");
            for(int i=1;i<1000;i++)pool.Update(Vector3.zero,i*.2);
            Check(pool.ScanCount==scans,"Stationary collision pool repeatedly rescanned the world");
            settings.enableRockColliders=false;pool.Update(Vector3.zero,201);Check(pool.ActiveCount==0,"Collider A/B toggle");
            Debug.Log("ROCK_PERFORMANCE_VALIDATION_PASS: cross-chunk grouping, tight culling, distant LOD, offscreen shadows, 1023 splitting, zero steady allocations, unload, stationary scan suppression and independent A/B toggles.");
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);}
    }
}
