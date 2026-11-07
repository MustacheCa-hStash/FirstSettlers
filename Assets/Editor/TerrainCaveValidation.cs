using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

/// <summary>Production density extraction, physics and motor checks in an isolated project.</summary>
public static class TerrainCaveValidation
{
    private static int checks;
    private static readonly TerrainDensityProfile P=TerrainDensityProfile.Default;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    internal sealed class Surface:ITerrainHeightDensityGenerator,ITerrainDensityBounds
    {
        public readonly Func<double,double,double> Height;
        public Surface(Func<double,double,double> height){Height=height;}
        public double SampleHeight(double x,double z)=>Height(x,z);
        public double SampleDensity(double3 p)=>p.y-Height(p.x,p.z);
        public bool TryGetDensityBounds(double3 min,double3 max,out double low,out double high)
        {
            double lo=double.PositiveInfinity,hi=double.NegativeInfinity;
            // Fixture landscapes are affine in X, with explicit breaks at integer positions.
            for(double x=Math.Floor(min.x);x<=Math.Ceiling(max.x);x+=1)
            {double h=Height(x,min.z);lo=Math.Min(lo,h);hi=Math.Max(hi,h);}
            low=min.y-hi;high=max.y-lo;return true;
        }
    }
    [MenuItem("Tools/Terrain/Validate Procedural Caves")]
    public static void Run()
    {
        checks=0;var watch=Stopwatch.StartNew();
        ValidateGeometry();ValidateMeshAndMotor();
        Debug.Log($"PROCEDURAL CAVES P1 COMPLETE: {checks} checks in {watch.Elapsed.TotalSeconds:0.00}s; bounded density, both entrances, smooth route, native mesh normals and real motor traversal.");
        ValidateStreaming();
        ValidateEnclosedMining();
        ValidateFormationField();
        ValidatePlanning();
    }
    public static void RunBatch()
    {
        try{Run();EditorApplication.Exit(0);}
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    public static void RunTravelBatch()
    {
        try
        {
            checks=0;
            var generation=TerrainSculptingValidation.ReadSceneGeneration(Path.Combine(TerrainSculptingValidation.SourceRoot(),"Assets/Scenes/SmearScene.unity"));
            generation.Caves=TerrainCaveSettings.Default;generation.Caves.enabled=true;
            ValidateRefinedCoverage();ValidateColdTravel(generation);EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    private static void ValidateGeometry()
    {
        var layout=TerrainCaveLayout.Representative(new double3(0,30,0));
        var surface=new Surface((x,z)=>x<0?29.8:x<2?29.8+x*12.1:x<142?54:Math.Max(37.5,54-(x-142)*.45));
        var caves=new[]{layout};var generator=new TerrainCaveDensityGenerator(surface,(a,b)=>caves);
        Check(!((object)generator is ITerrainHeightDensityGenerator),"Cave baseline must never use the height-only halo shortcut.");
        Check(layout.Maximum.x-layout.Minimum.x>200,"Representative cave needs substantial world-space extent.");
        Check(layout.RouteLength>300,"Representative cave needs a connected journey and a loop.");
        Check(generator.SampleDensity(layout.Entrance+new double3(0,1,0))>0,"Cliff approach is open.");
        Check(generator.SampleDensity(layout.Destination+new double3(0,1,0))>0,"Ground exit is open.");
        Check(generator.SampleDensity(new double3(85,32,12))>0,"Landmark chamber is air.");
        Check(generator.SampleDensity(new double3(85,52,12))<0,"Chamber retains a roof.");
        Check(generator.SampleDensity(new double3(85,20,12))<0,"Chamber retains a floor.");
        foreach(var span in layout.Spans)
        {
            double length=math.length(span.End.xz-span.Start.xz);
            Check(Math.Abs(span.End.y-span.Start.y)/length<.19,"Main routes stay below the walking gradient budget.");
            for(double distance=0;distance<=length;distance+=.3)
            {
                double t=distance/length;double3 p=math.lerp(span.Start,span.End,t);double floor=span.Floor(t,0);
                Check(generator.SampleDensity(new double3(p.x,floor+2.1,p.z))>0,"Standing route must remain clear after final deformation.");
                Check(Math.Abs(span.Floor(math.min(1,t+.1/length),0)-floor)<.03,"No accidental floor steps.");
            }
        }
        for(int i=0;i<200;i++)
        {
            double3 min=new double3((i*31%220)-20,(i*11%40)+20,(i*19%100)-25),max=min+4.8;
            Check(generator.TryGetDensityBounds(min,max,out double low,out double high),"Bounds are available.");
            for(int j=0;j<9;j++)
            {double3 p=math.lerp(min,max,new double3((j*3%11)/10d,(j*7%11)/10d,(j*5%11)/10d));double d=generator.SampleDensity(p);Check(d>=low-1e-8&&d<=high+1e-8,$"Conservative bounds include interior features: {min}..{max}, p={p}, d={d}, bounds={low}..{high}.");}
        }
        using var world=new TerrainDensityWorld(P,generator);
        Check(world.ModifiedBrickCount==0,"Natural cave allocates no edit payload.");
        using(var edit=world.StageExcavation(TerrainDensityBrush.Sphere(new double3(50,31,15),1)))Check(world.TryCommit(edit),"Natural cave accepts sparse excavation overrides.");
        Check(world.ModifiedBrickCount>0&&world.ModifiedBrickCount<100,"Natural cave does not become stored edit payload.");
    }
    private static void ValidateMeshAndMotor()
    {
        var span=new TerrainCaveSpan(new double3(-10,30,0),new double3(30,31,0),8,6,91);
        var cave=new TerrainCaveLayout(default,new[]{span},span.Start,span.End);
        var generator=new TerrainCaveDensityGenerator(new Surface((x,z)=>45),(a,b)=>new[]{cave});
        using var world=new TerrainDensityWorld(P,generator);using var snapshot=world.CaptureSnapshot();
        var meshes=new List<Mesh>();var roots=new List<GameObject>();
        var player=new GameObject("Cave native capsule");
        try
        {
            // Two actual adjacent mesh tiles exercise the signed-coordinate seam.
            for(int tile=-1;tile<=0;tile++)
            {
                var bounds=new TerrainDensityCellBounds(new int3(tile*64,96,-32),new int3((tile+1)*64,128,32));
                var data=TerrainDensityMesher.Build(snapshot,bounds);
                Check(data.TriangleCount>1000,"Extracted cave needs real floor/wall geometry.");
                for(int i=0;i<data.Normals.Length;i++)Check(math.all(math.isfinite(data.Normals[i]))&&math.lengthsq(data.Normals[i])>.9,"Cave normals are finite and normalized.");
                var mesh=data.CreateMesh(new double3(0),"Cave native tile");meshes.Add(mesh);
                var root=new GameObject("Cave native collision");roots.Add(root);root.layer=GameplayLayers.WorldSolid;root.AddComponent<WorldGroundSurface>();root.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
            var controller=player.AddComponent<CharacterController>();controller.height=1.8f;controller.radius=.4f;controller.center=Vector3.up*.9f;controller.stepOffset=.3f;controller.skinWidth=.08f;
            player.transform.position=new Vector3(-7,30.25f,0);var motor=player.AddComponent<CharacterMotor>();
            typeof(CharacterMotor).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,null);
            Physics.SyncTransforms();
            var command=new CharacterMoveCommand(new Vector2(1,0),false,false,false);
            // Motor advances world X when facing identity. Test both floor quality and a tile seam.
            for(int frame=0;frame<250;frame++){motor.Simulate(command,1f/60);Physics.SyncTransforms();}
            Check(player.transform.position.x>7,"Actual player motor crosses the cave tile boundary.");
            Check(player.transform.position.y>29.5f&&player.transform.position.y<32,"Player remains on the visible cave floor.");
            Check(Physics.Raycast(new Vector3(0,32,0),Vector3.down,out var hit,4,1<<GameplayLayers.WorldSolid,QueryTriggerInteraction.Ignore),"Published cave floor supports grounding queries.");
            Check(hit.normal.y>.99,"Walking floor remains smooth after extraction.");
        }
        finally{Object.DestroyImmediate(player);foreach(var root in roots)Object.DestroyImmediate(root);foreach(var mesh in meshes)Object.DestroyImmediate(mesh);}
    }

    private static void ValidateStreaming()
    {
        var surface=new Surface((x,z)=>x<0?29.8:x<2?29.8+x*12.1:x<142?54:Math.Max(37.5,54-(x-142)*.45));
        var layout=TerrainCaveLayout.Representative(new double3(0,30,0));
        var caveSet=new[]{layout};var generator=new TerrainCaveDensityGenerator(surface,(a,b)=>layout.Intersects(a,b)?caveSet:Array.Empty<TerrainCaveLayout>());
        using var host=new Host(surface,generator,new Vector3(8,30.2f,0));
        var watch=Stopwatch.StartNew();
        host.Ready(()=>{if(host.Viewer.transform.position.x<18)host.Viewer.transform.position+=Vector3.right*.1f;});
        Debug.Log($"CAVE INITIAL RESIDENCY: {host.Runtime.DerivedLiveBytes/1048576d:0.0} MiB, {host.Runtime.LiveRenderTriangles} triangles, {host.Runtime.LiveChunkCount} exterior chunks, {host.Runtime.LiveCaveTiles} volume tiles.");
        Check(host.Runtime.LiveCaveTiles>0,"Buried shells must publish separate 3D tiles.");
        Check(host.World.ModifiedBrickCount==0,"Natural publication must not synthesize edit records.");
        Check(host.Runtime.Suppression.TileCount>0,"Natural surface openings suppress unsupported grass and decoration.");
        Check(!host.Runtime.Suppression.Contains(85,12),"Vegetation survives on an intact cavern roof.");
        Check(host.Runtime.LiveChunkCount<30,"Only entrance columns should replace source surfaces.");
        Check(host.Runtime.CanMove(new double3(8,30.2,0),new double3(8.1,30.2,0),.4,1.8),"Published cave movement is ready.");
        Check(Physics.Raycast(new Vector3(-10,32,0),Vector3.right,out var wall,60,GameplayLayers.SolidSurfaceMask)&&wall.point.x>35,"Source collider must not close the cliff entrance.");
        host.Viewer.transform.position=new Vector3(76,29.2f,12);host.Ready();
        Check(Physics.Raycast(new Vector3(85,33,12),Vector3.up,out var roof,40,GameplayLayers.SolidSurfaceMask)&&roof.point.y<52,"Natural room has its own retained ceiling.");
        Check(Physics.Raycast(new Vector3(85,33,12),Vector3.down,out var floor,10,GameplayLayers.SolidSurfaceMask)&&floor.point.y>28,"Natural room floor is published.");
        // Enter a buried tile, then cut and revisit without treating it as a surface column.
        Check(host.Runtime.CanMove(new double3(76,29.2,12),new double3(76.1,29.2,12),.4,1.8),"Streaming prepares buried destination collision.");
        var player=new GameObject("Surface-to-volume cave walker");
        try
        {
            var cc=player.AddComponent<CharacterController>();cc.radius=.4f;cc.stepOffset=.3f;cc.skinWidth=.08f;
            player.transform.SetPositionAndRotation(new Vector3(50,29.7f,5.4f),Quaternion.LookRotation(new Vector3(40,0,12)));
            var motor=player.AddComponent<CharacterMotor>();typeof(CharacterMotor).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,null);motor.TerrainGuard=host.Runtime;Physics.SyncTransforms();
            for(int frame=0;frame<210;frame++){motor.Simulate(new CharacterMoveCommand(Vector2.up,false,false,false),1/60f);Physics.SyncTransforms();}
            Check(player.transform.position.x>64&&player.transform.position.y>28,"Actual motor crosses the exterior-patch/volume-tile seam.");
        }
        finally{Object.DestroyImmediate(player);}
        Check(host.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(80,29,31),1)),"Natural-cave excavation is admitted.");host.Ready();
        Check(host.World.Revision==1,"Excavation commits once over the natural baseline.");
        long stable=host.Runtime.DerivedLiveBytes;host.Ready();Check(host.Runtime.DerivedLiveBytes==stable,"Unchanged coverage has stable residency.");
        host.Viewer.transform.position=new Vector3(900,80,900);host.Ready();Check(host.Runtime.LiveCaveTiles==0,"Distant natural shell tiles retire.");
        host.Viewer.transform.position=new Vector3(76,29.2f,12);host.Ready();Check(host.Runtime.LiveCaveTiles>0&&host.World.Revision==1,"Returning regenerates natural geometry and retains session edits.");
        Debug.Log($"PROCEDURAL CAVES P2 COMPLETE: streamed representative cave, real entrance/roof/floor collision, buried movement, cut, retirement/return; {host.Runtime.LiveCaveTiles} volume tiles, {host.Runtime.LiveChunkCount} exterior chunks, {host.Runtime.LiveRenderTriangles} render triangles, {host.Runtime.DerivedLiveBytes/1048576d:0.0} MiB live derived; fixture total {watch.Elapsed.TotalSeconds:0.00}s.");
        var shallow=new TerrainCaveLayout(default,new[]{new TerrainCaveSpan(new double3(500,30,500),new double3(540,30,500),8,6,12)},new double3(500,30,500),new double3(540,30,500));
        var shallowSet=new[]{shallow};var shallowSurface=new Surface((x,z)=>35);
        using var exposed=new Host(shallowSurface,new TerrainCaveDensityGenerator(shallowSurface,(a,b)=>shallow.Intersects(a,b)?shallowSet:Array.Empty<TerrainCaveLayout>()),new Vector3(510,30.2f,500));
        exposed.Ready();Check(exposed.Runtime.LiveChunkCount>0,"Canonical surface contact is integrated even when a span was not planned as an entrance.");
        Check(Physics.Raycast(new Vector3(520,40,500),Vector3.down,out var breakthrough,20,GameplayLayers.SolidSurfaceMask)&&breakthrough.point.y<31,"An unmarked natural breakthrough removes the original blocking terrain collider.");
    }

    private static void ValidateEnclosedMining()
    {
        var surface=new Surface((x,z)=>70);
        var span=new TerrainCaveSpan(new double3(0,30,0),new double3(30,30,0),8,6,191,false,true);
        var cave=new TerrainCaveLayout(default,new[]{span},span.Start,span.End,Array.Empty<double3>());
        var generator=new TerrainCaveDensityGenerator(surface,(a,b)=>cave.Intersects(a,b)?new[]{cave}:Array.Empty<TerrainCaveLayout>());
        using var host=new Host(surface,generator,new Vector3(-140,31.3f,0));host.Ready();
        Check(host.Runtime.LiveCaveTiles==0,"Distant enclosed systems retain density without preparing their meshes.");
        host.Viewer.transform.position=new Vector3(-5.2f,31.3f,0);host.Ready();
        Check(cave.IsEnclosed&&cave.Entrances.Count==0,"Enclosed cave has no invented entrance metadata.");
        Check(host.Runtime.LiveChunkCount==0,"A fully enclosed cave leaves the original heightfield untouched.");
        Check(generator.SampleDensity(new double3(8,31.3,0))>0&&generator.SampleDensity(new double3(8,60,0))<0,"Undiscovered air exists under an intact roof.");
        Check(host.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(-4.2,31.3,0),1.6)),"Mining into an enclosed cave is admitted.");host.Ready();
        Check(Physics.Raycast(new Vector3(-5.2f,31.3f,0),Vector3.right,out var hit,60,GameplayLayers.SolidSurfaceMask)&&hit.point.x>20,"Mining removes the sealed wall and exposes the real cave collider.");
        Check(host.Runtime.LiveChunkCount==0&&host.World.ModifiedBrickCount>0,"Underground discovery allocates sparse edits without a surface aperture.");
        Debug.Log("CAVE ENCLOSED MINING: preexisting sealed air, untouched exterior, sparse excavation and physical breakthrough passed.");
    }

    private static void ValidateFormationField()
    {
        var span=new TerrainCaveSpan(new double3(0,30,0),new double3(30,30,0),24,14,191,false,true);
        var formation=new TerrainCaveFormation(new double3(8,30,7),new double3(8,33.2,7),.9);
        var plain=new TerrainCaveLayout(default,new[]{span},span.Start,span.End,Array.Empty<double3>());
        var cave=new TerrainCaveLayout(default,new[]{span},span.Start,span.End,Array.Empty<double3>(),new[]{formation});
        Check(plain.Air(new double3(8,31.6,7))>0&&cave.Air(new double3(8,31.6,7))<0,"Formation subtracts solid rock from preexisting air.");
        var surface=new Surface((x,z)=>70);var generator=new TerrainCaveDensityGenerator(surface,(a,b)=>new[]{cave});
        var lo=new double3(7.9,31.4,6.9);var hi=new double3(8.1,31.8,7.1);
        Check(generator.TryGetDensityBounds(lo,hi,out double low,out _)&&low<0,"Formation prevents an invalid all-air brick proof.");
        using var host=new Host(surface,generator,new Vector3(8,30.4f,0));host.Ready();
        Check(Physics.Raycast(new Vector3(8,31.6f,4),Vector3.forward,out var hit,20,GameplayLayers.SolidSurfaceMask)&&hit.point.z<7,"Formation supplies real collision.");
        Check(host.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(8,31.6,7),1)),"Formation is excavatable.");host.Ready();
        Check(Physics.Raycast(new Vector3(8,31.6f,4),Vector3.forward,out hit,20,GameplayLayers.SolidSurfaceMask)&&hit.point.z>8,"Excavation removes formation collision.");
        Debug.Log("CAVE FORMATIONS: bounded stone subtraction, real collision and excavation passed.");
    }

    private static void ValidatePlanning()
    {
        var settings=TerrainCaveSettings.Default;settings.enabled=true;settings.probability=1;
        double[] Flat(IReadOnlyList<double2> p){var h=new double[p.Count];for(int i=0;i<h.Length;i++)h[i]=200;return h;}
        int systems=0,formationCount=0;var previews=new List<(string label,TerrainCaveLayout cave)>();
        foreach(int seed in new[]{1,-98371,int.MaxValue})
        {
            var forward=new TerrainCavePlanner(seed,14.4,settings,Flat,(x,z)=>200);
            var reverse=new TerrainCavePlanner(seed,14.4,settings,Flat,(x,z)=>200);
            var cells=new[]{new int2(-1,-1),new int2(0,0),new int2(1,0)};
            for(int i=cells.Length-1;i>=0;i--){double3 p=new double3(cells[i].x*768+384,190,cells[i].y*768+384);reverse.Query(p-1,p+1);}
            foreach(var cell in cells)
            {
                double3 p=new double3(cell.x*768+384,190,cell.y*768+384);var a=forward.Query(p-1,p+1);var b=reverse.Query(p-1,p+1);
                Check(a.Count==1&&b.Count==1,"Eligible flat highlands generate a substantial cave.");systems++;
                if(previews.Count<5)previews.Add(($"Seed {seed}, cell {cell.x},{cell.y}",a[0]));
                Check(a[0].RouteLength>400&&a[0].Maximum.x-a[0].Minimum.x>100,"Systems have world-scale exploration length.");
                Check(a[0].IsEnclosed,"Flat highlands produce enclosed underground networks without manufactured entrances.");
                formationCount+=a[0].Formations.Count;
                Check(a[0].Formations.Count==b[0].Formations.Count,"Formation placement is independent of discovery order.");
                Check(a[0].Spans.Count==b[0].Spans.Count,"Plan topology does not depend on discovery order.");
                for(int i=0;i<a[0].Spans.Count;i++)
                {
                    var span=a[0].Spans[i];var other=b[0].Spans[i];
                    Check(math.all(span.Start==other.Start)&&math.all(span.End==other.End)&&span.Width==other.Width,"Reversed sampling regenerates identical plans.");
                    Check(Math.Abs(span.End.y-span.Start.y)/math.distance(span.Start.xz,span.End.xz)<.18,"Generated routes preserve comfortable gradients.");
                    for(int j=0;j<=20;j++)
                    {
                        double t=j/20d;var route=math.lerp(span.Start,span.End,t);route.y=span.Floor(t,0)+2.1;
                        Check(forward.Air(route)>0,"Final generated density preserves standing route clearance.");
                    }
                    Check(span.Minimum.y>14.7,"Dry generated caves respect the water datum.");
                }
            }
            for(int i=0;i<80;i++){var p=new double3(i*768+384,190,384);forward.Query(p-1,p+1);}
            Check(forward.CachedCells<=TerrainCavePlanner.Capacity,"World-cell plan residency is bounded.");
            Check(!forward.FindNearest(new double3(384,190,384),out _),"Entrance finder never selects a fully enclosed flat-highland system.");
        }
        Check(formationCount>0,"Seeded geological networks place formations outside walking bands.");
        var generation=TerrainSculptingValidation.ReadSceneGeneration(Path.Combine(TerrainSculptingValidation.SourceRoot(),"Assets/Scenes/SmearScene.unity"));
        generation.Caves=settings;generation.Caves.probability=.65f;
        var canonical=new TerrainBaselineSampler(generation,16);var native=new TerrainCavePlanner(generation,canonical);
        var watch=Stopwatch.StartNew();bool found=native.FindNearest(new double3(0,100,0),out var entrance);
        double searchSeconds=watch.Elapsed.TotalSeconds;
        Check(found,"Current scene seed must expose at least one dry, covered cave in the bounded discovery search.");
        var layout=native.Query(entrance-1,entrance+1)[0];
        previews.Add(($"Native scene seed {generation.Seed}",layout));WritePlanPreviews(previews);
        ValidateNativeHost(generation,entrance,layout);
        Debug.Log($"PROCEDURAL CAVES P3 COMPLETE: {systems} deterministic fixture systems across three seeds and signed cells, bounded cache and final route clearance; native scene seed {generation.Seed} cave entrance {entrance}, {layout.RouteLength:0} m routes, {layout.Spans.Count} spans; bounded search {searchSeconds:0.00}s.");
    }
    private static void WritePlanPreviews(List<(string label,TerrainCaveLayout cave)> plans)
    {
        string N(double value)=>value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
        var svg=new System.Text.StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='1000' height='720' viewBox='0 0 1000 720'><rect width='1000' height='720' fill='#10191e'/><g font-family='sans-serif' fill='#e4eced'><text x='24' y='30' font-size='20'>Natural cave networks — schematic plan views</text><text x='24' y='52' font-size='12'>Widths shown; vertical formation, surface terrain and rock detail require rendered review. Green: terrain connection.</text>");
        for(int i=0;i<plans.Count;i++)
        {
            var cave=plans[i].cave;double ox=18+(i%3)*328,oy=74+(i/3)*320;
            svg.Append($"<rect x='{N(ox)}' y='{N(oy)}' width='310' height='304' rx='8' fill='#1b2b32'/><text x='{N(ox+12)}' y='{N(oy+22)}' font-size='13'>{plans[i].label}</text><text x='{N(ox+12)}' y='{N(oy+40)}' font-size='11'>{(cave.IsEnclosed?"Enclosed":cave.Entrances.Count+" entrance(s)")} · {cave.RouteLength:0} m routes</text>");
            double extent=Math.Max(cave.Maximum.x-cave.Minimum.x,cave.Maximum.z-cave.Minimum.z),scale=240/extent;
            double X(double x)=>ox+35+(x-cave.Minimum.x)*scale;
            double Y(double z)=>oy+58+(cave.Maximum.z-z)*scale;
            var spans=new List<TerrainCaveSpan>(cave.Spans);spans.Sort((a,b)=>b.Width.CompareTo(a.Width));
            foreach(var span in spans)svg.Append($"<path d='M {N(X(span.Start.x))} {N(Y(span.Start.z))} L {N(X(span.End.x))} {N(Y(span.End.z))}' stroke='{(span.Entrance?"#85cdb5":"#b3c2c9")}' stroke-width='{N(span.Width*scale)}' stroke-linecap='round' fill='none'/>");
            foreach(var p in cave.Entrances)svg.Append($"<circle cx='{N(X(p.x))}' cy='{N(Y(p.z))}' r='4' fill='#59efae'/>");
            svg.Append($"<path d='M {N(ox+14)} {N(oy+280)} h {N(100*scale)}' stroke='#e4eced' stroke-width='2'/><text x='{N(ox+14)}' y='{N(oy+295)}' font-size='10'>100 m</text>");
        }
        svg.Append("</g></svg>");
        string directory=Path.Combine(TerrainSculptingValidation.SourceRoot(),"Documentation/Images/ProceduralCaves");Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"NaturalNetworkPlans.svg"),svg.ToString());
    }

    private static void ValidateNativeHost(WorldGenerationConfiguration generation,double3 entrance,TerrainCaveLayout layout)
    {
        var viewer=new GameObject("Native regional cave host");var material=new Material(Shader.Find("Hidden/InternalErrorShader"));ChunkManager manager=null;
        try
        {
            viewer.transform.position=(Vector3)(float3)(entrance+new double3(0,.2,0));
            var config=new WorldConfiguration{Generation=generation,Scene=new WorldSceneReferences{Viewer=viewer.transform},Rendering=new WorldRenderingConfiguration{TerrainMaterial=material,WaterMaterial=material,HorizonShadows=new TerrainHorizonShadowSettings{enabled=false}}};
            var coverage=TerrainCoverageConfiguration.Default;coverage.ViewDistance=2;coverage.ColliderDistance=2;coverage.EnableFarTerrain=false;config.Coverage=coverage;
            // Natural caves are independent of development excavation controls.
            var sculpt=TerrainSculptConfiguration.Default;sculpt.Enabled=false;config.Sculpting=sculpt;
            int frame=0;manager=new ChunkManager(config,()=>++frame);
            var watch=Stopwatch.StartNew();
            do{manager.UpdateActiveChunks();Thread.Sleep(1);}while((manager.Sculpting.Busy||manager.Sculpting.PublishedCoverageRevision<0)&&watch.Elapsed.TotalSeconds<120);
            Check(manager.Caves!=null&&manager.Sculpting!=null,"Production composition enables caves without sculpt controls.");
            Check(!manager.Sculpting.Busy&&manager.Sculpting.Status=="Ready"&&manager.Sculpting.LiveCaveTiles>0,$"Native regional cave publication converges: {manager.Sculpting.Status}.");
            ValidateNativeSeams(generation,entrance);
            var target=layout.Spans[5].Start+new double3(0,.25,0);
            Check(manager.Sculpting.RequestTeleport(target),"Production cave destination preparation is admitted.");watch.Restart();
            do{manager.UpdateActiveChunks();Thread.Sleep(1);}while(manager.Sculpting.Busy&&watch.Elapsed.TotalSeconds<120);
            Check(!manager.Sculpting.Busy&&math.distance((double3)(float3)viewer.transform.position,target)<.01,$"Native destination preparation completes: {manager.Sculpting.Status}.");
            Physics.SyncTransforms();Check(Physics.Raycast(viewer.transform.position+Vector3.up,Vector3.down,out var floor,4,GameplayLayers.SolidSurfaceMask),"Production cave destination has actual published floor collision.");
            var walkSpan=layout.Spans[5];
            for(int step=1;step<=400;step++)
            {
                manager.UpdateActiveChunks();double t=step/400d;
                double3 next=math.lerp(walkSpan.Start,walkSpan.End,t);next.y=walkSpan.Floor(t,0)+.25;
                Check(manager.Sculpting.CanMove((float3)viewer.transform.position,next,.4,1.8),"Walking at normal speed has ready cave collision: "+manager.Sculpting.Status);
                viewer.transform.position=(Vector3)(float3)next;Thread.Sleep(20);
            }
            watch.Restart();do{manager.UpdateActiveChunks();Thread.Sleep(1);}while(manager.Sculpting.Busy&&watch.Elapsed.TotalSeconds<120);
            ValidateNativeSculptInput(manager,viewer);
            Debug.Log($"CAVE NATIVE HOST: {manager.Sculpting.LiveCaveTiles} volume tiles, {manager.Sculpting.LiveChunkCount} exterior chunks; {manager.Sculpting.LiveRenderTriangles} triangles; {manager.Sculpting.DerivedLiveBytes/1048576d:0.0} MiB derived including sky reservation.");
        }
        finally{manager?.Dispose();Object.DestroyImmediate(material);Object.DestroyImmediate(viewer);}
    }

    private static void ValidateNativeSculptInput(ChunkManager manager,GameObject viewer)
    {
        var cameraObject=new GameObject("Native cave excavation camera");
        var controller=viewer.AddComponent<TerrainSculptController>();
        try
        {
            var camera=cameraObject.AddComponent<Camera>();controller.Configure(manager.Sculpting,camera);controller.Enter();
            Vector3 center=viewer.transform.position+Vector3.up*1.4f;double clock=0;
            foreach(bool floorTarget in new[]{true,false})
            {
                Physics.SyncTransforms();RaycastHit hit=default;bool found=false;float nearest=60;
                foreach(var direction in floorTarget?new[]{Vector3.down}:new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
                    if(Physics.Raycast(center,direction,out var candidate,60,GameplayLayers.SolidSurfaceMask)&&candidate.distance<nearest&&(floorTarget||Math.Abs(candidate.normal.y)<.7f))
                    {hit=candidate;nearest=candidate.distance;found=true;}
                Check(found,"Native cave excavation target exists (floor="+floorTarget+").");
                camera.transform.position=hit.point+hit.normal*1.5f;camera.transform.forward=-hit.normal;
                using var coveragePin=manager.Sculpting.PinCollision(new TerrainSculptPin((float3)center-1,(float3)center+1));
                manager.UpdateActiveChunks();
                controller.ProcessFrame(clock++);Check(controller.CanExcavate,"Native cave aim accepts published terrain: "+controller.Status);
                int before=manager.Sculpting.CommittedEdits;
                controller.Submit(new TerrainSculptInputCommand(true,true));controller.ProcessFrame(clock++);
                var watch=Stopwatch.StartNew();do{manager.UpdateActiveChunks();Thread.Sleep(1);}while(manager.Sculpting.Busy&&watch.Elapsed.TotalSeconds<120);
                Check(manager.Sculpting.CommittedEdits==before+1,"Native cave floor/wall interaction commits: "+manager.Sculpting.Status);
            }
            Debug.Log("CAVE NATIVE SCULPT INPUT: real camera aim, floor and wall excavation passed.");
        }
        finally{Object.DestroyImmediate(controller);Object.DestroyImmediate(cameraObject);}
    }

    private static void ValidateNativeSeams(WorldGenerationConfiguration generation,double3 location)
    {
        var edges=new Dictionary<(Vector3,Vector3),int>();
        int Compare(Vector3 a,Vector3 b){int c=a.x.CompareTo(b.x);if(c==0)c=a.y.CompareTo(b.y);return c==0?a.z.CompareTo(b.z):c;}
        foreach(var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if(!filter.name.StartsWith("Cave_")&&!filter.name.StartsWith("Sculpt_"))continue;
            var mesh=filter.sharedMesh;if(mesh==null)continue;
            var vertices=mesh.vertices;var triangles=mesh.triangles;
            void Edge(int a,int b)
            {
                var p=filter.transform.TransformPoint(vertices[a]);var q=filter.transform.TransformPoint(vertices[b]);
                if(Compare(p,q)>0){var swap=p;p=q;q=swap;}
                var key=(p,q);edges.TryGetValue(key,out int count);edges[key]=count+1;
            }
            for(int i=0;i<triangles.Length;i+=3){Edge(triangles[i],triangles[i+1]);Edge(triangles[i+1],triangles[i+2]);Edge(triangles[i+2],triangles[i]);}
        }
        var baseline=new TerrainBaselineSampler(generation,32);int cracks=0;
        foreach(var pair in edges)
        {
            if(pair.Value!=1)continue;
            var p=(pair.Key.Item1+pair.Key.Item2)*.5f;
            if(Vector3.Distance(p,(Vector3)(float3)location)>45||p.y>baseline.SampleHeight(p.x,p.z)-2)continue;
            if(cracks++<12)Debug.Log($"CAVE OPEN EDGE: {pair.Key.Item1:R} -> {pair.Key.Item2:R}");
        }
        Check(cracks==0,$"Native cave near the reported seam has {cracks} unmatched underground mesh edges.");
    }

    private static void ValidateColdTravel(WorldGenerationConfiguration generation)
    {
        var viewer=new GameObject("Cold cave travel host");var material=new Material(Shader.Find("Hidden/InternalErrorShader"));ChunkManager manager=null;
        try
        {
            var baseline=new TerrainBaselineSampler(generation,32);
            double2 startXZ=new double2(16,29);bool dry=false;
            for(int radius=0;radius<=192&&!dry;radius+=16)
                for(int z=-radius;z<=radius&&!dry;z+=16)for(int x=-radius;x<=radius&&!dry;x+=16)
                {
                    if(Math.Max(Math.Abs(x),Math.Abs(z))!=radius)continue;
                    var p=new double2(16+x,29+z);
                    if(baseline.SampleHeight(p.x,p.y)<generation.Water.SurfaceY+3||baseline.SampleHeight(p.x+2,p.y)<generation.Water.SurfaceY+3)continue;
                    startXZ=p;dry=true;
                }
            Check(dry,"Starting region has dry terrain for a pre-travel excavation.");
            viewer.transform.position=new Vector3((float)startXZ.x,(float)baseline.SampleHeight(startXZ.x,startXZ.y)+1,(float)startXZ.y);
            var config=new WorldConfiguration{Generation=generation,Scene=new WorldSceneReferences{Viewer=viewer.transform},Rendering=new WorldRenderingConfiguration{TerrainMaterial=material,WaterMaterial=material,HorizonShadows=new TerrainHorizonShadowSettings{enabled=false}}};
            var coverage=TerrainCoverageConfiguration.Default;coverage.ViewDistance=100;coverage.ColliderDistance=5;coverage.EnableFarTerrain=true;config.Coverage=coverage;
            int frame=0;manager=new ChunkManager(config,()=>++frame);
            var watch=Stopwatch.StartNew();double nextLog=5;int maximumPreparation=0;
            var preparation=(HashSet<ChunkCoord>)typeof(ChunkManager).GetField("sculptPreparationChunks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
            void Tick()
            {
                manager.UpdateActiveChunks();Thread.Sleep(1);
                maximumPreparation=Math.Max(maximumPreparation,preparation.Count);
                Check(preparation.Count<=64,"Cave handoff retains the existing source-preparation bound.");
                if(watch.Elapsed.TotalSeconds<nextLog)return;
                nextLog=watch.Elapsed.TotalSeconds+5;
                Debug.Log($"CAVE COLD TRAVEL {watch.Elapsed.TotalSeconds:0.0}s: {manager.CaveDiscoveryStatus}; sculpt {manager.Sculpting.Status}; jobs {manager.Sculpting.ActiveJobs}.");
            }
            do{Tick();}while((manager.Sculpting.Busy||manager.Sculpting.PublishedCoverageRevision<0)&&watch.Elapsed.TotalSeconds<120);
            Check(!manager.Sculpting.Busy,"Starting-area cave coverage converges.");
            var editPoint=new double3(startXZ.x+2,baseline.SampleHeight(startXZ.x+2,startXZ.y)-.25,startXZ.y);
            Check(manager.Sculpting.TryExcavate(TerrainDensityBrush.Sphere(editPoint,1)),"A surface edit before cave travel is admitted.");
            do{Tick();}while(manager.Sculpting.Busy&&watch.Elapsed.TotalSeconds<120);
            Check(manager.Sculpting.CommittedEdits==1,$"Surface edit completes before cave search: {manager.Sculpting.Status}.");
            var planner=new TerrainCavePlanner(generation,baseline);
            Check(planner.FindNearest((float3)viewer.transform.position,out var entrance),"A destination exists for the distant-terrain handoff regression.");
            var cave=planner.Query(entrance-1,entrance+1)[0];
            var policy=new TerrainCoveragePolicy(coverage);var neededFar=new HashSet<FarTerrainPatchKey>();
            double chunkWidth=generation.ChunkSize*(double)generation.WorldScale;
            var originCoord=new ChunkCoord((int)Math.Floor(startXZ.x/chunkWidth),(int)Math.Floor(startXZ.y/chunkWidth));
            foreach(var end in new[]{cave.Entrance,cave.Destination})
            {
                var center=new ChunkCoord((int)Math.Floor(end.x/chunkWidth),(int)Math.Floor(end.z/chunkWidth));
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)
                    if(policy.TryGetFarTerrainPatch(originCoord,new ChunkCoord(center.x+x,center.z+z),out var patch))neededFar.Add(patch);
            }
            var far=(Dictionary<FarTerrainPatchKey,FarTerrainTileRuntime>)typeof(ChunkManager).GetField("loadedFarTerrainTiles",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
            bool FarReady(){foreach(var patch in neededFar)if(!far.TryGetValue(patch,out var tile)||!tile.HasTerrainMesh)return false;return true;}
            do{Tick();}while(!FarReady()&&watch.Elapsed.TotalSeconds<120);
            Check(neededFar.Count>0&&FarReady(),$"Distant coverage at both cave entrances is loaded before travel ({far.Count} far tiles).");
            Debug.Log($"CAVE COLD TRAVEL: {far.Count} loaded far tiles; {neededFar.Count} entrance patches ready before search.");
            Check(manager.RequestCaveSearch(),"Inspector cave search is admitted from starting area.");
            var start=viewer.transform.position;watch.Restart();nextLog=5;
            do{Tick();}while(!manager.CaveDiscoveryStatus.StartsWith("Arrived")&&!manager.CaveDiscoveryStatus.Contains("failed")&&watch.Elapsed.TotalSeconds<180);
            Check(manager.CaveDiscoveryStatus.StartsWith("Arrived"),$"Cold long-distance cave travel converges: {manager.CaveDiscoveryStatus}; {manager.Sculpting.Status}.");
            double arrivalSeconds=watch.Elapsed.TotalSeconds;
            Check(Vector3.Distance(start,viewer.transform.position)>500,"Cold travel reaches a distant cave with far terrain enabled.");
            Physics.SyncTransforms();Check(Physics.Raycast(viewer.transform.position+Vector3.up,Vector3.down,out _,4,GameplayLayers.SolidSurfaceMask),"Cold cave travel publishes destination collision before arrival.");
            do{Tick();}while(manager.Sculpting.Busy&&watch.Elapsed.TotalSeconds<180);
            Check(!manager.Sculpting.Busy,"Post-arrival cave coverage converges.");
            Check(manager.RequestCaveSearch(),"A subsequent cave request can start after arrival.");
            var cancellationWatch=Stopwatch.StartNew();
            do{Tick();}while(!manager.Sculpting.PendingDestination.HasValue&&cancellationWatch.Elapsed.TotalSeconds<5);
            Check(manager.Sculpting.PendingDestination.HasValue,"Cached cave discovery reaches destination preparation.");
            Vector3 beforeCancel=viewer.transform.position;
            manager.Sculpting.CancelTeleport();Tick();
            Check(manager.CaveDiscoveryStatus=="Cave travel failed: Travel canceled","Cancellation retains its actual result when coverage immediately starts another worker.");
            Check(viewer.transform.position==beforeCancel,"Canceled travel does not move the player.");
            Debug.Log($"PROCEDURAL CAVES TRAVEL COMPLETE: {checks} checks; {arrivalSeconds:0.00}s cold search-to-arrival; one existing surface edit, 100-chunk view, 5-chunk collision, far terrain already loaded; {manager.Sculpting.LiveCaveTiles} cave tiles, {manager.Sculpting.LiveChunkCount} source chunks; peak {maximumPreparation}/64 prepared sources.");
        }
        finally{manager?.Dispose();Object.DestroyImmediate(material);Object.DestroyImmediate(viewer);}
    }

    private static void ValidateRefinedCoverage()
    {
        var protectedChunks=new[]{new ChunkCoord(26,-14),new ChunkCoord(-51,32),new ChunkCoord(70,70)};
        bool Contains(FarTerrainPatchKey p,ChunkCoord c)=>c.x>=p.Origin.x*p.SizeInChunks&&c.x<(p.Origin.x+1)*p.SizeInChunks&&c.z>=p.Origin.z*p.SizeInChunks&&c.z<(p.Origin.z+1)*p.SizeInChunks;
        bool Refine(FarTerrainPatchKey p){foreach(var c in protectedChunks)if(Contains(p,c))return true;return false;}
        var policy=new TerrainCoveragePolicy(TerrainCoverageConfiguration.Default,Refine);
        foreach(var viewer in new[]{new ChunkCoord(0,0),new ChunkCoord(-3,5)})
        {
            var leaves=new HashSet<FarTerrainPatchKey>();
            for(int z=-90;z<=90;z++)for(int x=-90;x<=90;x++)
            {
                var target=new ChunkCoord(x,z);
                if(!policy.TryGetFarTerrainPatch(viewer,target,out var leaf))continue;
                Check(Contains(leaf,target),"Refined far coverage retains signed/aligned ownership.");
                Check(!Refine(leaf)||leaf.SizeInChunks==4,"Cave openings descend to the bounded base leaf.");
                leaves.Add(leaf);
            }
            var list=new List<FarTerrainPatchKey>(leaves);
            for(int i=0;i<list.Count;i++)for(int j=0;j<i;j++)
                Check(!TerrainCoveragePolicy.FarTerrainPatchesOverlap(list[i],list[j]),"Refined far leaves form a nonoverlapping partition.");
        }
        Debug.Log("CAVE REFINED COVERAGE: signed ownership, base-leaf refinement and nonoverlapping distant coverage passed.");
    }

    internal sealed class Host:IDisposable
    {
        internal readonly GameObject Viewer=new GameObject("Natural cave streaming viewer");
        internal readonly TerrainDensityWorld World;
        internal readonly TerrainSculptRuntime Runtime;
        private readonly TerrainRequestManager requests=new TerrainRequestManager(1,1,1,1,new TerrainWaterSettings(-100,200,.3f));
        private readonly Surface surface;
        private readonly Material material;
        private readonly Dictionary<ChunkCoord,ChunkRuntime> chunks=new Dictionary<ChunkCoord,ChunkRuntime>();
        private readonly List<ChunkRecord> records=new List<ChunkRecord>();
        private readonly List<Mesh> meshes=new List<Mesh>();
        internal Host(Surface surface,TerrainCaveDensityGenerator generator,Vector3 position,Material terrainMaterial=null,TerrainSkyAccessSettings sky=null)
        {
            this.surface=surface;material=terrainMaterial!=null?new Material(terrainMaterial):new Material(Shader.Find("Hidden/InternalErrorShader"));Viewer.transform.position=position;World=new TerrainDensityWorld(P,generator);
            var config=TerrainSculptConfiguration.Default;config.DerivedBytes=256L*1024*1024;
            config.SurfaceAppearance=Resources.Load<TerrainSculptSurfaceSettings>("TerrainSculpting/ExcavatedStone");config.SkyAccess=sky;
            Runtime=new TerrainSculptRuntime(World,surface.SampleHeight,128,requests,Find,Viewer.transform,config,useDefaultSkyAccess:false,naturalMaterial:material);
        }
        private ChunkRuntime Find(ChunkCoord coord)
        {
            if(chunks.TryGetValue(coord,out var runtime))return runtime;
            const int size=128,width=size+3;var h=new float[width,width];var map=new float[width,width];
            for(int z=0;z<width;z++)for(int x=0;x<width;x++)h[x,z]=(float)(surface.SampleHeight((coord.x*size+x-1)*P.Spacing,(coord.z*size+z-1)*P.Spacing)/60);
            var record=new ChunkRecord(coord);records.Add(record);
            Check(record.TryCompleteTerrainDataRequest(record.BeginTerrainDataRequest(),h,map,map,map,new BiomeType[width,width],new SurfaceType[width,width],new WaterState[width,width],new GroundCoverType[width,width],new WorldFeaturePlan(width,width),map,Array.Empty<Texture2D>()),"Natural source data completes.");
            runtime=new ChunkRuntime(record,size,(float)P.Spacing,null,material,material,true);chunks.Add(coord,runtime);
            var domain=new TerrainSculptFootprint(new int2(coord.x,coord.z)*size,new int2(coord.x+1,coord.z+1)*size);
            var mesh=TerrainSculptPatchBuilder.BuildBaseline(P,domain,surface.SampleHeight).CreateMesh((float3)runtime.RootTransform.position,"Natural cave original surface");
            meshes.Add(mesh);runtime.SetMeshes(mesh,null,0);runtime.ApplyCollider(mesh);runtime.SetVisible(true);return runtime;
        }
        internal void Ready(Action simulate=null,double timeoutSeconds=90)
        {
            var watch=Stopwatch.StartNew();do{Runtime.Update();simulate?.Invoke();Thread.Sleep(1);}while(Runtime.Busy&&watch.Elapsed.TotalSeconds<timeoutSeconds);
            Check(!Runtime.Busy&&Runtime.Status=="Ready",$"Cave host did not become ready: {Runtime.Status} ({watch.Elapsed.TotalSeconds:0.0}s).");
            Physics.SyncTransforms();
        }
        public void Dispose(){Runtime.Dispose();requests.Dispose();requests.WaitForActiveRequestsToFinish();foreach(var chunk in chunks.Values)chunk.DestroyRuntime();foreach(var mesh in meshes)Object.DestroyImmediate(mesh);foreach(var record in records)record.Dispose();World.Dispose();Object.DestroyImmediate(material);Object.DestroyImmediate(Viewer);}
    }
}
