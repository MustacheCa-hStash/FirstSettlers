using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// No saved scenes, cameras, render captures or Play mode. Exercises service contracts with synthetic inputs.
public static class WorldArchitectureValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
    private static T Field<T>(object value, string field) => (T)value.GetType().GetField(field, Private).GetValue(value);
    [MenuItem("Tools/Terrain/Validate World Architecture (synthetic)")]
    public static void Run()
    {
        ValidateSpecies(); ValidateConfiguration(); ValidateCoverage(); ValidateHandoffs(); ValidatePublisher(); ValidatePool();
        FoliageOptimizationValidation.Run();
        Debug.Log("WORLD ARCHITECTURE PASS: catalog/fallbacks, grouped configuration, worker snapshots, coverage, handoffs, publication budgets, pooling and foliage regressions.");
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    private static Mesh Triangle() => new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward }, triangles = new[] { 0,1,2 } };
    private static void ValidateSpecies()
    {
        var forest = new GameObject("Forest fallback"); var meadow = new GameObject("Meadow fallback"); var specific = new GameObject("Species override");
        var card = ScriptableObject.CreateInstance<WorldObjectDefinition>(); var authored = ScriptableObject.CreateInstance<WorldObjectDefinition>();
        var mesh = Triangle(); var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        try
        {
            foreach (var prefab in new[] { forest, meadow, specific })
            { prefab.AddComponent<MeshFilter>().sharedMesh = mesh; prefab.AddComponent<MeshRenderer>().sharedMaterial = material; }
            var authoring = meadow.AddComponent<TreeGameplayAuthoring>(); Set(authoring,"queryDefinition",authored);
            Set(card,"treeUniformScaleRange",new Vector2(3,5)); Set(card,"forestTreeExclusionRadiusRange",new Vector2(4,6));
            Set(card,"grasslandTreeExclusionRadiusRange",new Vector2(7,9));
            var settings = new TreeSettings { treeLOD0GameObjectPrefab = forest, grasslandTreeFallbackPrefab = meadow,
                treeBillboardPrefab = forest, grasslandTreeBillboardFallbackPrefab = meadow, mapleTreeDefinition = card };
            var variants = new HashSet<WorldFeatureVariant>();
            foreach (var species in TreeSpeciesCatalog.All)
            {
                Check(variants.Add(species.Variant),"Duplicate species binding.");
                Check(settings.GetNearPrefab(species.Variant) == (species.IsGrassland ? meadow : forest),"Near habitat fallback changed.");
                Check(species.BillboardPrefab(settings) == (species.IsGrassland ? meadow : forest),"Billboard habitat fallback changed.");
                Check(!string.IsNullOrWhiteSpace(species.DisplayName),"Species display identity missing.");
            }
            Check(variants.Count == 12 && !TreeSpeciesCatalog.TryGet(WorldFeatureVariant.Boulder,out _),"Catalog includes non-tree content or misses tree variants.");
            Check(settings.GetDefinition(WorldFeatureVariant.MapleTree) == card && settings.GetDefinition(WorldFeatureVariant.GrasslandMapleTree) == card,
                "Forest/meadow variants no longer share their species card.");
            Check(settings.GetDefinition(WorldFeatureVariant.GrasslandOakTree) == authored && settings.GetDefinition(WorldFeatureVariant.Boulder) == null,
                "Prefab datacard fallback or non-tree rejection changed.");
            settings.mapleTreePrefab = specific; settings.mapleTreeBillboardPrefab = specific;
            Check(settings.GetNearPrefab(WorldFeatureVariant.MapleTree) == specific && TreeSpeciesCatalog.All[0].BillboardPrefab(settings) == specific,
                "Species-specific prefabs did not override habitat fallbacks.");
            var snapshot = TreeGenerationSnapshot.Create(settings);
            Check(snapshot.GetTreeUniformScaleRange(WorldFeatureVariant.MapleTree) == new Vector2(3,5) &&
                snapshot.GetTreeExclusionRadiusRange(WorldFeatureVariant.MapleTree) == new Vector2(4,6) &&
                snapshot.GetTreeExclusionRadiusRange(WorldFeatureVariant.GrasslandMapleTree) == new Vector2(7,9),"Worker snapshot lost species or habitat values.");
            Set(card,"treeUniformScaleRange",new Vector2(9,10));
            Check(snapshot.GetTreeUniformScaleRange(WorldFeatureVariant.MapleTree) == new Vector2(3,5),"Worker snapshot retained mutable card values.");
            using var standing = new StandingTreeRenderer(settings);
            foreach (var variant in variants) Check(standing.Supports(variant),"Standing renderer missed catalog variant: " + variant);
            using var distant = new DistantTreeManager(settings,1937,16,10,3,.5f,2,1,10,.24f,1,snapshot);
            Check(Field<System.Collections.IDictionary>(distant,"batches").Count == 12,"Distant renderer missed catalog bindings.");
        }
        finally { Object.DestroyImmediate(forest); Object.DestroyImmediate(meadow); Object.DestroyImmediate(specific); Object.DestroyImmediate(card); Object.DestroyImmediate(authored); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
    }
    private static void ValidateConfiguration()
    {
        var root = new GameObject("Configuration fixture"); root.SetActive(false);
        var world = root.AddComponent<WorldManager>(); var mesh = Triangle(); var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        var prefab = new GameObject("Configuration asset"); prefab.AddComponent<MeshFilter>().sharedMesh=mesh; prefab.AddComponent<MeshRenderer>().sharedMaterial=material;
        try
        {
            Set(world,"worldSeed",917); Set(world,"chunkSize",16); Set(world,"worldScale",.5f); Set(world,"sampleScale",13f);
            Set(world,"meshHeightMultiplier",18f); Set(world,"globalWaterY",2.7f); Set(world,"viewer",root.transform);
            Set(world,"viewDistance",7); Set(world,"colliderDistance",2); Set(world,"enableFarTerrain",false);
            Set(world,"farTerrainStartRing",9); Set(world,"farTerrainMacroTileSize",4); Set(world,"farTerrainHeightGridResolution",11);
            Set(world,"farTerrainControlMapResolution",19); Set(world,"farTerrainSkirtDepth",8f);
            Set(world,"maxActiveTerrainDataJobs",2); Set(world,"maxActiveFarTerrainJobs",1); Set(world,"maxActiveMeshJobs",5); Set(world,"maxActiveColliderJobs",3);
            Set(world,"maxTerrainDataResultsAppliedPerFrame",3); Set(world,"maxFarTerrainResultsAppliedPerFrame",2);
            Set(world,"maxLODMeshResultsAppliedPerFrame",7); Set(world,"maxColliderResultsAppliedPerFrame",4);
            Set(world,"completedRequestApplyBudgetMsPerFrame",3.7f); Set(world,"terrainDataApplyBudgetMsPerFrame",.8f);
            Set(world,"farTerrainApplyBudgetMsPerFrame",.2f); Set(world,"lodMeshApplyBudgetMsPerFrame",1.1f); Set(world,"colliderApplyBudgetMsPerFrame",.4f);
            Set(world,"maxVisibleChunkContentUpdatesPerFrame",17); Set(world,"maxRenderVisibilityChecksPerFrame",29);
            Set(world,"maxFarTerrainTileContentUpdatesPerFrame",6); Set(world,"visibleChunkContentBudgetMsPerFrame",.9f);
            Set(world,"farTerrainTileContentBudgetMsPerFrame",.45f); Set(world,"foliageFrustumPaddingChunks",.6f);
            Set(world,"grassSettings",new GrassSettings { grassPrefab=prefab, billboardGrassPrefab=prefab });
            Set(world,"treeSettings",null); Set(world,"terrainMaterial",material); Set(world,"waterMaterial",material);
            var config = (WorldConfiguration)typeof(WorldManager).GetMethod("BuildConfiguration",Private).Invoke(world,null);
            Check(config.Generation.Seed==917 && config.Generation.ChunkSize==16 && config.Generation.WorldScale==.5f && config.Generation.SampleScale==13,
                "Generation fields were swapped or omitted.");
            Check(Mathf.Abs(config.Generation.Water.SurfaceY-2.7f)<.0001f && config.Generation.MeshHeightMultiplier==18,"Water/height scaling changed during composition.");
            Check(config.Coverage.ViewDistance==7 && config.Coverage.ColliderDistance==2 && !config.Coverage.EnableFarTerrain && config.Coverage.FarTerrainHeightGridResolution==11 &&
                config.Coverage.FarTerrainControlMapResolution==19 && config.Coverage.FarTerrainSkirtDepth==8,"Coverage grouping changed inspector values.");
            Check(config.Workers.TerrainData==2 && config.Workers.FarTerrain==1 && config.Workers.LodMesh==5 && config.Workers.Collider==3,"Worker limit mapping changed.");
            Check(config.Publication.TerrainDataCount==3 && config.Publication.FarTerrainCount==2 && config.Publication.LodMeshCount==7 && config.Publication.ColliderCount==4 &&
                config.Publication.TotalMs==3.7f && config.Publication.TerrainDataMs==.8f && config.Publication.FarTerrainMs==.2f && config.Publication.LodMeshMs==1.1f && config.Publication.ColliderMs==.4f,
                "Publication count/time budgets were swapped.");
            Check(config.Content.MaxVisibleChunkUpdates==17 && config.Content.MaxRenderVisibilityChecks==29 && config.Content.MaxFarTileUpdates==6 &&
                config.Content.VisibleChunkMs==.9f && config.Content.FarTileMs==.45f && config.Content.FoliageFrustumPaddingChunks==.6f,"Content budgets changed.");
            Check(config.Scene.Viewer==root.transform && config.Rendering.TerrainMaterial==material && config.Rendering.WaterMaterial==material &&
                config.Foliage.Grass==Field<GrassSettings>(world,"grassSettings"),"Authored references were not transported.");
            var manager = new ChunkManager(config);
            try
            {
                var requests = Field<TerrainRequestManager>(manager,"terrainRequestManager");
                Check(Field<int>(requests,"maxActiveMeshJobs")==5 && Field<int>(requests,"maxActiveColliderJobs")==3,"Grouped worker limits did not reach requests.");
                var publisher = Field<TerrainResultPublisher>(manager,"resultPublisher");
                Check(Field<TerrainPublicationBudget>(publisher,"budget").LodMeshCount==7,"Grouped publication budget did not reach its owner.");
                config.Generation = WorldGenerationConfiguration.Default; config.Publication = TerrainPublicationBudget.Default;
                Check(manager.GetChunkWorldSize()==8 && Field<TerrainPublicationBudget>(publisher,"budget").LodMeshCount==7,"Running world retained mutable configuration groups.");
            }
            finally { manager.Dispose(); }
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(prefab); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
    }
    private static void ValidateCoverage()
    {
        var policy = new TerrainCoveragePolicy(TerrainCoverageConfiguration.Default);
        foreach (var viewer in new[] { new ChunkCoord(0,0),new ChunkCoord(-3,5),new ChunkCoord(7,-9) })
            for (int x=-80;x<=80;x+=3) for(int z=-80;z<=80;z+=3)
            {
                var target=new ChunkCoord(viewer.x+x,viewer.z+z);
                if (!policy.TryGetFarTerrainPatch(viewer,target,out var patch)) continue;
                int minX=patch.Origin.x*patch.SizeInChunks,minZ=patch.Origin.z*patch.SizeInChunks;
                Check(target.x>=minX && target.x<minX+patch.SizeInChunks && target.z>=minZ && target.z<minZ+patch.SizeInChunks,"Negative/aligned patch ownership changed.");
                int start=8, size=4; while(size<patch.SizeInChunks){start+=size*2;size*=2;}
                int dx=Mathf.Max(minX-viewer.x,viewer.x-(minX+patch.SizeInChunks-1),0);
                int dz=Mathf.Max(minZ-viewer.z,viewer.z-(minZ+patch.SizeInChunks-1),0);
                Check(Mathf.Max(dx,dz)>=start,"A far leaf crosses its near boundary.");
            }
    }
    private static ChunkRuntime Normal(GameObject root, Mesh mesh)
    {
        var runtime=(ChunkRuntime)FormatterServices.GetUninitializedObject(typeof(ChunkRuntime));
        Set(runtime,"root",root); Set(runtime,"terrainMeshFilter",root.AddComponent<MeshFilter>()); root.GetComponent<MeshFilter>().sharedMesh=mesh;
        Set(runtime,"terrainMeshRenderer",root.AddComponent<MeshRenderer>()); Set(runtime,"renderVisible",true);
        return runtime;
    }
    private static FarTerrainTileRuntime Far(GameObject root, Mesh mesh)
    {
        var runtime=(FarTerrainTileRuntime)FormatterServices.GetUninitializedObject(typeof(FarTerrainTileRuntime));
        Set(runtime,"root",root); Set(runtime,"meshFilter",root.AddComponent<MeshFilter>()); root.GetComponent<MeshFilter>().sharedMesh=mesh;
        Set(runtime,"meshRenderer",root.AddComponent<MeshRenderer>()); return runtime;
    }
    private static void ValidateHandoffs()
    {
        var mesh=Triangle(); var roots=new List<GameObject>();
        GameObject Root(string name){var r=new GameObject(name);roots.Add(r);return r;}
        var normal=new Dictionary<ChunkCoord,ChunkRuntime>();var far=new Dictionary<FarTerrainPatchKey,FarTerrainTileRuntime>();
        int normalReleased=0,farReleased=0;var active=new HashSet<ChunkCoord>();var desiredFar=new HashSet<FarTerrainPatchKey>();
        var coord=new ChunkCoord(8,0);var patch=new FarTerrainPatchKey(new ChunkCoord(2,0),4);
        try
        {
            var handoffs=new TerrainHandoffCoordinator(new TerrainCoveragePolicy(TerrainCoverageConfiguration.Default),normal,far,(c,r)=>normalReleased++,r=>farReleased++);
            normal[coord]=Normal(Root("outgoing normal"),mesh);far[patch]=Far(Root("incoming far"),null);desiredFar.Add(patch);
            handoffs.Reconcile(active);handoffs.Update(default,active,desiredFar);
            Check(normal.Count==1 && normalReleased==0,"Normal terrain released before far mesh attachment.");
            Field<MeshFilter>(far[patch],"meshFilter").sharedMesh=mesh;handoffs.Update(default,active,desiredFar);
            Check(normal.Count==0 && normalReleased==1,"Ready far replacement did not release outgoing normal.");
            desiredFar.Clear();active.Add(coord);normal[coord]=Normal(Root("incoming normal"),null);
            handoffs.Reconcile(active);handoffs.Update(default,active,desiredFar);
            Check(far.Count==1 && !Field<MeshRenderer>(normal[coord],"terrainMeshRenderer").enabled,"Far-to-normal wait failed to retain/hide coverage.");
            desiredFar.Add(patch);active.Clear();handoffs.Reconcile(active);handoffs.Update(default,active,desiredFar);
            Check(far.Count==1 && farReleased==0,"Reversing direction discarded the retained far runtime.");
            desiredFar.Clear();active.Add(coord);normal[coord]=Normal(Root("ready normal"),mesh);
            handoffs.Reconcile(active);handoffs.Update(default,active,desiredFar);
            Check(far.Count==0 && farReleased==1 && Field<MeshRenderer>(normal[coord],"terrainMeshRenderer").enabled,"Attached normal replacement failed handoff.");
            var disabled=TerrainCoverageConfiguration.Default;disabled.EnableFarTerrain=false;
            handoffs=new TerrainHandoffCoordinator(new TerrainCoveragePolicy(disabled),normal,far,(c,r)=>normalReleased++,r=>farReleased++);
            active.Clear();handoffs.Reconcile(active);handoffs.Update(default,active,desiredFar);
            Check(normal.Count==0,"Outgoing chunk without a macro replacement leaked its runtime.");
            var coarse=new FarTerrainPatchKey(new ChunkCoord(1,0),8);far[coarse]=Far(Root("coarse outgoing"),mesh);far[patch]=Far(Root("fine incoming"),null);
            desiredFar.Add(patch);handoffs.Update(default,active,desiredFar);
            Check(far.ContainsKey(coarse),"Far leaf released before overlapping fine replacement attached.");
            Field<MeshFilter>(far[patch],"meshFilter").sharedMesh=mesh;handoffs.Update(default,active,desiredFar);
            Check(!far.ContainsKey(coarse) && far.ContainsKey(patch),"Far leaf handoff failed to converge.");
        }
        finally { foreach(var root in roots)Object.DestroyImmediate(root);Object.DestroyImmediate(mesh); }
    }
    private static MeshData Data() => new MeshData(new[]{Vector3.zero,Vector3.right,Vector3.forward},new[]{Vector3.up,Vector3.up,Vector3.up},new Vector2[3],new Color[3],new[]{0,1,2});
    private static void ValidatePublisher()
    {
        using var requests=new TerrainRequestManager(1,1,1,1,new TerrainWaterSettings(2.4f,10,1));
        var record=new ChunkRecord(default);var patch=new FarTerrainPatchKey(new ChunkCoord(2,0),4);var tile=new FarTerrainTileRecord(patch);
        var budget=new TerrainPublicationBudget{TerrainDataCount=1,FarTerrainCount=1,LodMeshCount=1,ColliderCount=1};
        var wake=new List<ChunkCoord>();int farWake=0;
        var publisher=new TerrainResultPublisher(requests,budget,c=>record,k=>tile,r=>tile.IsRequestCurrent(r.RequestVersion),wake.Add,k=>farWake++);
        var meshes=Field<Queue<MeshRequestResult>>(requests,"completedMeshResults");
        int stale=record.BeginMeshRequest(0),current=record.BeginMeshRequest(0),other=record.BeginMeshRequest(1);
        meshes.Enqueue(new MeshRequestResult(default,0,stale,Data(),new WaterMeshData(0)));
        meshes.Enqueue(new MeshRequestResult(default,0,current,Data(),new WaterMeshData(0)));
        meshes.Enqueue(new MeshRequestResult(default,1,other,Data(),new WaterMeshData(0)));
        int farVersion=tile.BeginRequest();Field<Queue<FarTerrainRequestResult>>(requests,"completedFarTerrainResults").Enqueue(
            new FarTerrainRequestResult(patch.Origin,farVersion,true,4,Data(),new ControlMapPixelData(1,1,1)));
        try
        {
            publisher.Update();Check(meshes.Count==2 && !record.TryGetLODTerrainMesh(0,out _),"Stale result overwrote the current LOD or count budget failed.");
            Check(farWake==0 && requests.CompletedFarTerrainResultCount==1,"Far publication overtook remaining near results.");
            publisher.Update();Check(record.TryGetLODTerrainMesh(0,out _) && meshes.Count==1,"Current LOD was not applied under its budget.");
            publisher.Update();Check(record.TryGetLODTerrainMesh(1,out _) && wake.Count==2 && tile.HasTerrain && farWake==1,"Result routing or far priority did not converge.");
        }
        finally
        {
            foreach(int lod in new[]{0,1})
            { if(record.TryGetLODTerrainMesh(lod,out var terrain))Object.DestroyImmediate(terrain);if(record.TryGetLODWaterMesh(lod,out var water))Object.DestroyImmediate(water); }
            if(tile.TryGetTerrainMesh(out var farMesh))Object.DestroyImmediate(farMesh);
            if(tile.ControlMapData!=null)foreach(var map in tile.ControlMapData)Object.DestroyImmediate(map);record.Dispose();
        }
    }
    private static void ValidatePool()
    {
        var material=new Material(Shader.Find("Hidden/InternalErrorShader"));
        var generation=WorldGenerationConfiguration.Default;generation.ChunkSize=16;generation.WorldScale=.5f;
        using var pool=new TerrainRuntimePool(generation,new WorldRenderingConfiguration{TerrainMaterial=material,WaterMaterial=material},null);
        var a=new ChunkRecord(default);var b=new ChunkRecord(new ChunkCoord(-2,3));
        try
        {
            var first=pool.Acquire(a);pool.Release(first);var second=pool.Acquire(b);
            Check(ReferenceEquals(first,second) && a.ActiveRuntime==null && b.ActiveRuntime==second && second.RootTransform.position==new Vector3(-12,0,28),"Normal runtime reuse lost identity, origin or ownership.");
            pool.Release(second);
            var far=pool.Acquire(new FarTerrainTileRecord(new FarTerrainPatchKey(default,4)));pool.Release(far);
            var reused=pool.Acquire(new FarTerrainTileRecord(new FarTerrainPatchKey(new ChunkCoord(-1,2),8)));
            Check(ReferenceEquals(far,reused),"Far runtime pool stopped reusing detached objects.");pool.Release(reused);
        }
        finally { a.Dispose();b.Dispose();Object.DestroyImmediate(material); }
    }
}
