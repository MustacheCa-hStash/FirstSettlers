using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ForestVarietyWorldStudy
{
    public const string Pilot = "Assets/Scenes/ForestVarietyPilot.unity";
    const string Original = "Assets/Scenes/SmearScene.unity";
    const string Distribution = "Assets/ScriptableObjects/ForestVarietyDistribution.asset";
    const string RouteAsset = "Tools/ForestVarietyRoute.json";
    const string Output = ".utmp/forest-variety";
    const string Pending = "ForestVarietyWorldStudy.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator capture;
    static int previousFrame;

    [Serializable] sealed class Route
    {
        public Vector3[] ground;
        public Vector3 forward;
    }

    static ForestVarietyWorldStudy()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            string mode = SessionState.GetString(Pending, "");
            if (mode.Length == 0) return;
            SessionState.EraseString(Pending);
            capture = Capture(mode);
            previousFrame = -1;
            EditorApplication.update += Tick;
        };
    }

    public static void CreatePilotBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(Original);
            var world = Object.FindAnyObjectByType<WorldManager>();
            var settings = (TreeSettings)Field(world, "treeSettings");
            var route = FindRoute(world);
            File.WriteAllText(RouteAsset, JsonUtility.ToJson(route, true));
            var profile = AssetDatabase.LoadAssetAtPath<TreeDistributionProfile>(Distribution);
            if (profile == null)
            {
                profile = Object.Instantiate(settings.distributionProfile);
                profile.name = "ForestVarietyDistribution";
                AssetDatabase.CreateAsset(profile, Distribution);
            }
            else EditorUtility.CopySerialized(settings.distributionProfile, profile);
            profile.name = "ForestVarietyDistribution";
            profile.forestStand = 120;
            profile.youngTreeShare = .15f;
            EditorUtility.SetDirty(profile);
            settings.distributionProfile = profile;
            settings.beechTreePrefab = Prefab("Beech_Stand_v01");
            settings.beechTreeBillboardPrefab = Prefab("BeechHQ_OctaImpostor_Runtime");
            settings.oakTreePrefab = settings.grasslandOakTreePrefab = Prefab("Oak_OpenCrown_v05");
            settings.oakTreeBillboardPrefab = settings.grasslandOakTreeBillboardPrefab = Prefab("OakOpenCrownHQ_OctaImpostor_Runtime");
            settings.grasslandWillowTreePrefab = Prefab("Willow_River_v01");
            settings.grasslandWillowTreeBillboardPrefab = Prefab("WillowHQ_OctaImpostor_Runtime");
            settings.distantTreeOutlineLibrary = AssetDatabase.LoadAssetAtPath<TreeImpostorOutlineLibrary>(ForestVarietyStudy.OutlineAsset)
                ?? throw new InvalidOperationException("Missing pilot impostor outlines.");
            settings.distantTreeThinningStartChunks = 7;
            ((ForestLightingSettings)Field(world, "forestLighting")).enabled = true;
            world.Viewer.SetPositionAndRotation(route.ground[0] + Vector3.up*.1f, Quaternion.LookRotation(route.forward));
            if (!EditorSceneManager.SaveScene(world.gameObject.scene, Pilot, saveAsCopy: true))
                throw new InvalidOperationException("Could not save the forest pilot scene.");
            AssetDatabase.SaveAssets();
            Debug.Log($"FOREST VARIETY PILOT ROUTE {route.ground[0]} to {route.ground[^1]}, heading {route.forward}");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static GameObject Prefab(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{name}.prefab")
        ?? throw new InvalidOperationException("Missing pilot prefab " + name);

    public static void BaselineBatch() => StartCapture("baseline");
    public static void FormsBatch() => StartCapture("forms");
    public static void FullerBatch() => StartCapture("fuller");

    static void StartCapture(string mode)
    {
        try
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene(mode == "baseline" ? Original : Pilot);
            var world = Object.FindAnyObjectByType<WorldManager>();
            var settings = (TreeSettings)Field(world, "treeSettings");
            if (mode == "forms")
                settings.distributionProfile = AssetDatabase.LoadAssetAtPath<TreeDistributionProfile>(
                    "Assets/Resources/Foliage/TreeDistribution.asset");
            var route = JsonUtility.FromJson<Route>(File.ReadAllText(RouteAsset));
            world.Viewer.SetPositionAndRotation(route.ground[0] + Vector3.up*.1f, Quaternion.LookRotation(route.forward));
            typeof(WorldManager).GetField("logTerrainGenerationProfile", Private).SetValue(world, false);
            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            SessionState.SetString(Pending, mode);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void Tick()
    {
        if (Time.frameCount == previousFrame) return;
        previousFrame = Time.frameCount;
        try
        {
            if (capture.MoveNext()) return;
            EditorApplication.update -= Tick;
            Debug.Log("FOREST VARIETY WORLD CAPTURE PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);

    static Route FindRoute(WorldManager world)
    {
        var configuration = (WorldConfiguration)typeof(WorldManager).GetMethod("BuildConfiguration", Private).Invoke(world, null);
        var g = configuration.Generation;
        var settings = configuration.Foliage.Trees;
        var distribution = TreeGenerationSnapshot.Create(settings, g.WorldScale);
        using var sampler = new TreeHabitatSampler(g.Seed,g.SampleScale,g.Octaves,g.Persistence,g.Lacunarity,
            g.MeshHeightMultiplier,g.Water.WaterLevel,g.MountainHorizontalScale,g.Erosion);
        Vector3 origin = world.Viewer.position / g.WorldScale;
        var directions = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left,
            new Vector3(1,0,1).normalized, new Vector3(-1,0,1).normalized,
            new Vector3(1,0,-1).normalized, new Vector3(-1,0,-1).normalized };
        float chunkMeters = g.ChunkSize * g.WorldScale;
        var placements = new Dictionary<ChunkCoord, TreeInstanceData[]>();
        Route best = null;
        int bestScore = 0, tested = 0;
        for (int ring = 0; ring <= 40; ++ring)
        for (int x = -ring; x <= ring; ++x)
        for (int z = -ring; z <= ring; ++z)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(z)) != ring) continue;
            var start = origin + new Vector3(x * 128, 0, z * 128);
            var meadow = sampler.Sample(start.x,start.z);
            if (meadow.Biome != BiomeType.Grassland || meadow.Surface != SurfaceType.Grass || meadow.Slope > 10) continue;
            foreach (var direction in directions)
            {
                var end = start + direction * 384;
                var forest = sampler.Sample(end.x,end.z);
                if (forest.Biome != BiomeType.Forest || forest.Surface != SurfaceType.Grass || forest.Slope > 17) continue;
                var beyond = end + direction * 96;
                if (sampler.Sample(beyond.x,beyond.z).Biome != BiomeType.Forest) continue;
                var path = new Vector3[25];
                bool passable = true;
                for (int i = 0; i < path.Length; ++i)
                {
                    Vector3 point = Vector3.Lerp(start, end, i / 24f);
                    var habitat = sampler.Sample(point.x,point.z);
                    if (habitat.Surface != SurfaceType.Grass || habitat.Slope > 21)
                    { passable = false; break; }
                    path[i] = new Vector3(point.x,habitat.Height*g.MeshHeightMultiplier,point.z)*g.WorldScale;
                }
                if (!passable) continue;
                int edge = Canopies(path[0], direction, 55, 200, out _);
                int interior = Canopies(path[^1], direction, 5, 130, out int candidateForms);
                int score = 3 * interior + 4 * candidateForms + Math.Min(26, edge) * 2;
                if (edge >= 10 && interior >= 16 && score > bestScore)
                {
                    bestScore = score;
                    best = new Route { ground = path, forward = direction };
                    Debug.Log($"FOREST VARIETY ROUTE CANDIDATE {path[0]} to {path[^1]}: {edge} edge canopies, {interior} forest canopies, {candidateForms} new forms in the view.");
                }
                if (++tested >= 90 && best != null) return best;
            }
        }
        return best ?? throw new InvalidOperationException("Could not locate a walkable meadow-to-forest route facing a stand.");

        int Canopies(Vector3 ground, Vector3 forward, float minDepth, float maxDepth, out int candidateForms)
        {
            var center = ground + forward * (minDepth + maxDepth) * .5f;
            float radius = maxDepth * .7f;
            int count = 0;
            candidateForms = 0;
            for (int x = Mathf.FloorToInt((center.x-radius)/chunkMeters); x <= Mathf.FloorToInt((center.x+radius)/chunkMeters); ++x)
            for (int z = Mathf.FloorToInt((center.z-radius)/chunkMeters); z <= Mathf.FloorToInt((center.z+radius)/chunkMeters); ++z)
            {
                var coord = new ChunkCoord(x,z);
                if (!placements.TryGetValue(coord, out var trees))
                {
                    trees = DistantTreePlacement.Generate(coord, g.ChunkSize,g.Seed,g.SampleScale,g.Octaves,
                        g.Persistence,g.Lacunarity,g.WorldScale,g.MeshHeightMultiplier,g.Water.WaterLevel,
                        g.MountainHorizontalScale,settings.seedOffset,distribution,g.Erosion);
                    placements.Add(coord,trees);
                }
                var chunk = new Vector3((x+.5f)*chunkMeters,0,(z+.5f)*chunkMeters);
                foreach (var tree in trees)
                {
                    var root = chunk + tree.localPosition;
                    var delta = root-ground;
                    float depth = Vector3.Dot(delta,forward);
                    if (depth < minDepth || depth > maxDepth || Math.Abs(delta.x*forward.z-delta.z*forward.x) > .63f*depth) continue;
                    var top = root + Vector3.up * 9;
                    var eye = ground + Vector3.up * 1.7f;
                    bool visible = true;
                    for (int sample = 1; sample <= 5; ++sample)
                    {
                        var point = Vector3.Lerp(eye,top,sample/6f);
                        if (sampler.Sample(point.x/g.WorldScale,point.z/g.WorldScale).Height*g.MeshHeightMultiplier*g.WorldScale <= point.y) continue;
                        visible = false;
                        break;
                    }
                    if (!visible) continue;
                    count++;
                    if (tree.variant is WorldFeatureVariant.BeechTree or WorldFeatureVariant.OakTree or
                        WorldFeatureVariant.GrasslandOakTree or WorldFeatureVariant.GrasslandWillowTree)
                        candidateForms++;
                }
            }
            return count;
        }
    }

    static IEnumerator Capture(string mode)
    {
        var route = JsonUtility.FromJson<Route>(File.ReadAllText(RouteAsset));
        var world = Object.FindAnyObjectByType<WorldManager>();
        var camera = (Camera)Field(world, "viewerCamera");
        var chunks = (ChunkManager)Field(world, "chunkManager");
        var trees = (DistantTreeManager)Field(chunks, "distantTrees");
        int viewDistance = (int)Field(world, "viewDistance");
        var settings = (TreeSettings)Field(world, "treeSettings");
        var viewer = world.Viewer;
        foreach (var script in viewer.GetComponentsInChildren<MonoBehaviour>())
            if (script != world && script is not BuildWorld) script.enabled = false;
        if (GameTimeManager.Instance)
        {
            GameTimeManager.Instance.Pause();
            GameTimeManager.Instance.SetTime(0, 14, 0);
        }
        QualitySettings.SetQualityLevel(Array.IndexOf(QualitySettings.names, "PC"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        ShaderUtil.allowAsyncCompilation = false;
        using var treeUpdate = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "FS.DistantTrees.Update", 1);
        using var csv = new StreamWriter($"{Output}/world-{mode}.csv");
        csv.WriteLine("phase,treeUpdateMs,nearCandidates,nearDraws,nearTriangles,farCandidates");
        var target = new RenderTexture(2560,1440,24);
        target.Create();
        camera.targetTexture = target;
        camera.fieldOfView = 60;
        Debug.Log($"FOREST VARIETY WORLD {mode} seed route={route.ground[0]} → {route.ground[^1]}, stand={settings.distributionProfile.forestStand}, shadow={settings.castTreeShadows}, GPU={SystemInfo.graphicsDeviceName}");
        void Place(float t)
        {
            float index = t * (route.ground.Length-1);
            int segment = Mathf.Min((int)index, route.ground.Length-2);
            Vector3 ground = Vector3.Lerp(route.ground[segment],route.ground[segment+1],index-segment);
            viewer.position = ground + Vector3.up*.1f;
            camera.transform.position = ground + Vector3.up*1.7f;
            camera.transform.rotation = Quaternion.LookRotation(route.forward + Vector3.up*.045f);
        }
        void Record(string phase)
        {
            csv.WriteLine(FormattableString.Invariant($"{phase},{treeUpdate.LastValue/1000000.0:F4},{trees.GpuMeshCandidates},{trees.MeshDrawCalls},{trees.MeshRenderStats.triangles},{trees.GpuCandidates}"));
        }
        Place(0);
        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 22) yield return null;
        CaptureFrame(camera, target, trees, chunks, viewer.position, viewDistance, $"{Output}/{mode}-meadow.png");
        start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 4)
        {
            yield return null;
            Record("meadow");
        }
        start = Time.realtimeSinceStartup;
        const float walkSeconds = 8;
        while (Time.realtimeSinceStartup - start < walkSeconds)
        {
            Place(Mathf.Clamp01((Time.realtimeSinceStartup-start)/walkSeconds));
            yield return null;
            Record("walk");
        }
        Place(1);
        start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 8) yield return null;
        CaptureFrame(camera, target, trees, chunks, viewer.position, viewDistance, $"{Output}/{mode}-forest.png");
        start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 4)
        {
            yield return null;
            Record("forest");
        }
        var textureProperties = new[] { "_AlbedoCoverage","_SurfaceAtlas","_DepthAtlas","_MaterialIdAtlas","_AmbientAtlas" };
        var textures = TreeSpeciesCatalog.All.Select(s => s.BillboardPrefab(settings))
            .Where(prefab => prefab).Distinct().Select(prefab => prefab.GetComponentInChildren<MeshRenderer>())
            .Where(renderer => renderer).SelectMany(renderer => textureProperties.Select(property =>
                renderer.sharedMaterial.GetTexture(property) as Texture2D)).Where(texture => texture).Distinct();
        long bytes = 0;
        foreach (var texture in textures)
            for (int mip = 0; mip < texture.mipmapCount; ++mip)
                bytes += GraphicsFormatUtility.ComputeMipmapSize(Math.Max(1,texture.width>>mip), Math.Max(1,texture.height>>mip), texture.graphicsFormat);
        var placements = world.Trees.Records.Select(record =>
        {
            var root = record.WorldPosition;
            return FormattableString.Invariant($"{root.x:F4},{root.y:F4},{root.z:F4},{record.Placement.variant}");
        }).OrderBy(record => record, StringComparer.Ordinal);
        File.WriteAllLines($"{Output}/placements-{mode}.csv", placements);
        var placementSettings = TreeGenerationSnapshot.Create(settings);
        int youngTrees = world.Trees.Records.Count(record =>
            record.Placement.localScale.x < placementSettings.GetTreeUniformScaleRange(record.Placement.variant).x * .6f);
        Debug.Log($"FOREST VARIETY WORLD {mode}: {world.Trees.Count} registered trees, {youngTrees} young trees, atlas payload {bytes / 1048576.0:F1} MiB");
        camera.targetTexture = null;
        target.Release();
        Object.Destroy(target);
    }

    static void CaptureFrame(Camera camera, RenderTexture source, DistantTreeManager trees,
        ChunkManager chunks, Vector3 viewer, int viewDistance, string path)
    {
        var previous = RenderTexture.active;
        var pixels = new Texture2D(source.width,source.height,TextureFormat.RGB24,false);
        // Batch Play Mode does not include the world's immediate tree submissions
        // in the camera target. Replay them during the explicit still render.
        void SubmitTrees(ScriptableRenderContext _, Camera view)
        {
            if (view == camera) trees.Update(chunks, viewer, view, viewDistance);
        }
        try
        {
            RenderPipelineManager.beginCameraRendering += SubmitTrees;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = source });
            RenderTexture.active = source;
            pixels.ReadPixels(new Rect(0,0,source.width,source.height),0,0);
            File.WriteAllBytes(path,pixels.EncodeToPNG());
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= SubmitTrees;
            RenderTexture.active = previous;
            Object.Destroy(pixels);
        }
    }
}
