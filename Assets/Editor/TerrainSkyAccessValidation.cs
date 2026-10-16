using System;
using System.Diagnostics;
using System.Threading;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

/// <summary>Real density/worker/GPU-cache lifecycle. Rendering is checked separately.</summary>
public static class TerrainSkyAccessValidation
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    internal static void Ready(TerrainSculptingIntegrationValidation.Fixture f)
    {
        f.Ready();var clock=Stopwatch.StartNew();
        do
        {
            f.Tick();
            if(f.Runtime.SkyAccess!=null&&!f.Runtime.SkyAccess.Busy)return;
            if(clock.ElapsedMilliseconds>30000)throw new TimeoutException("Sky cache did not converge: "+f.Runtime.SkyAccess?.Status);
        }while(true);
    }
    [MenuItem("Tools/Terrain/Validate Cached Cave Sky Access")]
    public static void Run()
    {
        checks=0;var settings=ScriptableObject.CreateInstance<TerrainSkyAccessSettings>();settings.blendSeconds=0;
        try
        {
            using(var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10,skyAccess:settings))
            {
                f.Viewer.transform.position=new Vector3(12,7,12);Ready(f);
                var field=f.Runtime.SkyAccess;
                Check(field!=null&&!field.Allocated&&field.ResidentPages==0,"An unchanged landscape allocated cave probe atlases.");
                Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(12,7,12),new double3(4,2,4))),"Sky chamber cut rejected.");Ready(f);
                Check(field.SampleExposure(new double3(12,8,12))<.01,"Sealed chamber receives sky.");
                Check(field.SampleExposure(new double3(12,10.2,12))>.99,"Retained exterior skin was darkened.");
                Check(field.SampleExposure(new double3(12,6,12))<.01,"Sealed floor receives sky.");
                Check(field.ResidentPages>0&&field.ResidentPages<=TerrainSkyAccess.Capacity,"Sparse tile residency is invalid.");
                Check(f.Runtime.DerivedLiveBytes+f.Runtime.DerivedReservedBytes<=128L*1024*1024,"Lighting escaped the existing derived cap.");
                Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,9.5,12),2)),"Sky shaft rejected.");Ready(f);
                float opened=field.SampleExposure(new double3(12,6,12));
                Check(opened>.1&&opened<=1,"Opening failed to illuminate probes beyond the brush bounds: "+opened);
                Check(field.DensityRevision==f.World.Revision&&field.Pending==0,"Sky cache did not reach the committed density revision.");
                Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(24,8.8,12),new double3(2,.6,2))),"Thin roof fixture rejected.");Ready(f);
                Check(field.SampleExposure(new double3(24,9,12))<.01,"Exterior probes leaked through a roof thinner than probe spacing.");
                Check(field.SampleExposure(new double3(24,10.2,12))>.99,"Thin roof darkened its outdoor surface.");
                double trace=field.LastTraceMs;long version=field.DensityRevision;
                settings.caveAmbientMultiplier=.12f;settings.skyResponse=2;f.Tick();
                Vector4 controls=Shader.GetGlobalVector("_TerrainSkyControls");
                Check(Math.Abs(controls.x-.12)<.0001&&Math.Abs(controls.y-2)<.0001,"Live brightness controls were not bound.");
                Check(field.LastTraceMs==trace&&field.DensityRevision==version&&!field.Busy,"Brightness controls rebuilt geometry visibility.");
                settings.enabled=false;f.Tick();Check(Shader.GetGlobalFloat("_TerrainSkyEnabled")==0&&field.SampleExposure(new double3(24,9,12))==1,"Disable did not restore ordinary lighting.");
                settings.enabled=true;Ready(f);Check(field.SampleExposure(new double3(24,9,12))<.01,"Re-enable lost the cached field.");
                f.Viewer.transform.position=new Vector3(400,7,12);Ready(f);
                Check(field.ResidentPages==0&&Shader.GetGlobalFloat("_TerrainSkyEnabled")==0,"Streaming did not retire distant sky pages.");
                f.Viewer.transform.position=new Vector3(12,7,12);Ready(f);
                Check(Math.Abs(field.SampleExposure(new double3(12,6,12))-opened)<.01,"Returning changed reconstructed sky access.");
                Debug.Log($"SKY CACHE MEASURE: {field.ResidentPages} tiles; latest tile trace {field.LastTraceMs:F2} ms, upload {field.LastUploadMs:F2} ms; 24 MiB conservative reservation within 128 MiB derived cap.");
            }
            Check(Shader.GetGlobalFloat("_TerrainSkyEnabled")==0,"Shutdown left a stale global lighting field.");
            using(var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10,skyAccess:settings))
            {
                f.Viewer.transform.position=new Vector3(12,7,12);f.Ready();
                Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(10.5,7,12),new double3(2.4,3.4,3))),"Bright shaft fixture rejected.");Ready(f);
                Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(15.5,7,12),new double3(2.2,2,3))),"Adjacent dark chamber rejected.");Ready(f);
                Check(f.Runtime.SkyAccess.SampleExposure(new double3(12,8,12))>.1,"Bright-side probe is not illuminated.");
                Check(f.Runtime.SkyAccess.SampleExposure(new double3(13.5,8,12))<.01,"Sky leaked horizontally through a sub-spacing stone wall.");
            }
            using(var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>20,skyAccess:settings))
            {
                f.Viewer.transform.position=new Vector3(12,11,12);f.Ready();
                Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(12,17,12),new double3(3,4,3))),"Upper open chamber rejected.");Ready(f);
                Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(12,10.8,12),new double3(3,1.8,3))),"Lower chamber rejected.");Ready(f);
                Check(f.Runtime.SkyAccess.SampleExposure(new double3(12,14,12))>.1,"Upper chamber is not illuminated.");
                Check(f.Runtime.SkyAccess.SampleExposure(new double3(12,12.3,12))<.01,"Sky leaked vertically through a sub-spacing internal ceiling.");
            }
            ValidateStaleAndCapacity(settings);
            Debug.Log($"CACHED SKY ACCESS PASS: {checks} assertions; sealed/open chambers, distant opening dependencies, thin roofs/walls/internal floors, shared admission, live controls, streaming reconstruction, budget, stale work, capacity and teardown.");
        }
        finally{Object.DestroyImmediate(settings);}
    }
    private static void Cut(TerrainDensityWorld world,TerrainDensityBrush brush)
    {using var edit=world.StageExcavation(brush);Check(world.TryCommit(edit),"Sky-only fixture edit rejected.");}
    private static void ValidateStaleAndCapacity(TerrainSkyAccessSettings settings)
    {
        var generator=new TerrainSculptingIntegrationValidation.Height((x,z)=>10);
        using(var world=new TerrainDensityWorld(TerrainDensityProfile.Default,generator))
        using(var requests=new TerrainRequestManager(1,1,1,1,new TerrainWaterSettings(-100,200,.3f)))
        using(var field=new TerrainSkyAccess(world,generator.SampleHeight,requests,settings))
        {
            Cut(world,TerrainDensityBrush.Box(new double3(-12,7,-12),new double3(3,2,3)));
            generator.Block();field.Update(new double3(-12,7,-12),world.Revision,false);
            try
            {
                Check(generator.Entered.Wait(10000),"Delayed sky worker did not start.");
                Check(!field.YieldToTerrain(),"An in-flight lighting snapshot did not yield its admission/headroom to terrain.");
                Cut(world,TerrainDensityBrush.Sphere(new double3(-12,9.5,-12),2));
            }
            finally{generator.Release.Set();}
            var clock=Stopwatch.StartNew();
            do{field.Update(new double3(-12,7,-12),world.Revision,false);Thread.Sleep(1);if(clock.ElapsedMilliseconds>30000)throw new TimeoutException(field.Status);}while(field.Busy);
            Check(field.StaleResults>0&&field.DensityRevision==world.Revision,"Stale visibility work published over a newer edit.");
            Check(field.SampleExposure(new double3(-12,6,-12))>.1,"Negative-coordinate sky reconstruction failed.");
        }
        generator.Entered.Dispose();generator.Release.Dispose();
        var high=new TerrainSculptingIntegrationValidation.Height((x,z)=>1000);
        using(var world=new TerrainDensityWorld(TerrainDensityProfile.Default,high))
        using(var requests=new TerrainRequestManager(1,1,1,1,new TerrainWaterSettings(-100,200,.3f)))
        using(var field=new TerrainSkyAccess(world,high.SampleHeight,requests,settings))
        {
            for(int z=-7;z<=6;z++)for(int x=-7;x<=6;x++)
                Cut(world,TerrainDensityBrush.Sphere(new double3(x*32+16,16,z*32+16),.3));
            field.Update(new double3(0,16,0),world.Revision,false);
            Check(field.VisiblePages==TerrainSkyAccess.Capacity&&field.OmittedPages>0,"Sky page admission did not enforce its fixed capacity.");
            // Disposal interrupts work admitted above and joins it before world teardown.
        }
        high.Entered.Dispose();high.Release.Dispose();
        Check(Shader.GetGlobalFloat("_TerrainSkyEnabled")==0,"Capacity/in-flight teardown left a global field.");
    }
}
