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
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Explicit, offline diagnostics. Does not save the scene or alter authored assets.
[InitializeOnLoad]
public static class VegetationPerformanceInvestigation
{
    const string Pending = "FS.VegetationInvestigation";
    const string CompareBatches = "FS.VegetationInvestigation.CompareBatches";
    const string Folder = ".utmp/vegetation-performance";
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator routine;
    static int lastFrame = -1, errors;
    static readonly List<ProfilerRecorder> recorders = new();
    static readonly string[] markers = { "FS.Streaming.WorldManager.Update", "FS.DistantTrees.Update",
        "FS.Streaming.UpdateGrassStreaming", "FS.Flame.Update", "FS.DistantTrees.RefreshGroundTargets",
        "FS.Streaming.RebuildActiveChunkSet" };
    static VegetationPerformanceInvestigation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            routine = ProfileWorld();
            EditorApplication.update += Tick;
        };
    }
    static object Field(object value, string name) => value.GetType().GetField(name, Private).GetValue(value);
    public static void RunAssetAuditBatch()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity");
            Audit(Object.FindAnyObjectByType<WorldManager>());
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    public static void RunBatch()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity");
            var manager = Object.FindAnyObjectByType<WorldManager>();
            typeof(WorldManager).GetField("logTerrainGenerationProfile", Private).SetValue(manager, false);
            Audit(manager);
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    public static void RunBatchingWorldBatch()
    {
        SessionState.SetBool(CompareBatches, true);
        RunBatch();
    }
    static void Tick()
    {
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (routine.MoveNext()) return;
            Finish(0);
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Finish(int code)
    {
        foreach (var recorder in recorders) recorder.Dispose();
        recorders.Clear();
        EditorApplication.update -= Tick;
        Debug.Log("VEGETATION_INVESTIGATION_COMPLETE errors=" + errors);
        EditorApplication.Exit(code);
    }
    static void Audit(WorldManager manager)
    {
        var settings = (TreeSettings)Field(manager, "treeSettings");
        using var writer = new StreamWriter(Folder + "/assets.csv");
        writer.WriteLine("species,representation,mesh,vertices,triangles,shader,textureMiB,atlasSizes,textureFormats,prefabLodLevels");
        var allTextures = new HashSet<Texture>();
        foreach (var species in TreeSpeciesCatalog.All)
        foreach (bool far in new[] { false, true })
        {
            var prefab = far ? species.BillboardPrefab(settings) : species.NearPrefab(settings);
            if (prefab == null) continue;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null) continue;
                var textures = new HashSet<Texture>();
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    foreach (var property in material.GetTexturePropertyNames())
                        if (material.GetTexture(property) is Texture texture) textures.Add(texture);
                }
                long bytes = textures.Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
                foreach (var texture in textures) allTextures.Add(texture);
                writer.WriteLine($"{species.Variant},{(far ? "far" : filter.name)},{mesh.name},{mesh.vertexCount},{mesh.triangles.Length / 3},{string.Join("|", renderer.sharedMaterials.Where(m => m != null).Select(m => m.shader.name))},{bytes / 1048576.0:F2},{string.Join("|", textures.Select(t => t.width + "x" + t.height))},{string.Join("|", textures.Select(t => t.graphicsFormat.ToString()))},{prefab.GetComponentInChildren<LODGroup>(true)?.GetLODs().Length ?? 0}");
            }
        }
        File.WriteAllText(Folder + "/environment.txt", $"{SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}; {SystemInfo.graphicsMemorySize} MiB VRAM\nUnique tree texture runtime memory: {allTextures.Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t)) / 1048576.0:F2} MiB\n");
    }
    static IEnumerator ProfileWorld()
    {
        bool compareBatches = SessionState.GetBool(CompareBatches, false);
        SessionState.SetBool(CompareBatches, false);
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (type is LogType.Exception or LogType.Error) { errors++; File.AppendAllText(Folder + "/errors.txt", message + "\n" + stack + "\n"); }
        };
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        ShaderUtil.allowAsyncCompilation = false;
        var manager = Object.FindAnyObjectByType<WorldManager>();
        var treeSettings = (TreeSettings)Field(manager, "treeSettings");
        var camera = (Camera)Field(manager, "viewerCamera");
        var viewer = manager.Viewer;
        foreach (var script in viewer.GetComponentsInChildren<MonoBehaviour>())
            if (script != manager && script is not BuildWorld) script.enabled = false;
        var target = new RenderTexture(1280, 720, 24);
        target.Create(); camera.targetTexture = target;
        var chunks = (ChunkManager)Field(manager, "chunkManager");
        var trees = (DistantTreeManager)Field(chunks, "distantTrees");
        int changes = 0, geometryChanges = 0;
        manager.Trees.ChunkChanged += _ => changes++;
        LocalFlameGeometry.Changed += _ => geometryChanges++;
        foreach (var marker in markers) recorders.Add(ProfilerRecorder.StartNew(ProfilerCategory.Scripts, marker, 1));
        var sourceObject = new GameObject("Investigation torch");
        var source = sourceObject.AddComponent<LocalFlameSource>();
        source.stationary = true; source.range = 8; source.coverageWorld = viewer.GetComponentInChildren<BuildWorld>();
        var flame = LocalFlameLighting.Ensure(); flame.targetCamera = camera;
        var config = (WorldConfiguration)typeof(WorldManager).GetMethod("BuildConfiguration", Private).Invoke(manager, null);
        var g = config.Generation;
        var points = new List<(string name, Vector3 point)>();
        using (var sampler = new TreeHabitatSampler(g.Seed, g.SampleScale, g.Octaves, g.Persistence, g.Lacunarity,
            g.MeshHeightMultiplier, g.Water.WaterLevel, g.MountainHorizontalScale, g.Erosion))
        {
            foreach (var biome in new[] { BiomeType.Grassland, BiomeType.Forest })
            {
                bool found = false;
                for (int ring = 0; ring <= 40 && !found; ring++)
                for (int x = -ring; x <= ring && !found; x++)
                for (int z = -ring; z <= ring && !found; z++)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(z)) != ring) continue;
                    float sx = viewer.position.x / g.WorldScale + x * 128, sz = viewer.position.z / g.WorldScale + z * 128;
                    var habitat = sampler.Sample(sx, sz);
                    if (habitat.Biome != biome || habitat.Surface != SurfaceType.Grass || habitat.Slope > .2f) continue;
                    if (sampler.Sample(sx + 64, sz).Biome != biome || sampler.Sample(sx - 64, sz).Biome != biome ||
                        sampler.Sample(sx, sz + 64).Biome != biome || sampler.Sample(sx, sz - 64).Biome != biome) continue;
                    points.Add((biome.ToString(), new Vector3(sx * g.WorldScale, habitat.Height * g.MeshHeightMultiplier * g.WorldScale + 1.7f, sz * g.WorldScale)));
                    found = true;
                }
                if (!found) throw new Exception("No representative " + biome + " point found");
            }
        }
        Quaternion rotation = Quaternion.Euler(0, 0, 0);
        using var writer = new StreamWriter(Folder + (compareBatches ? "/world-spatial.csv" : "/world-frames.csv"));
        writer.WriteLine("frame,phase,deltaMs,treesRegistered,registryChanges,geometryChanges,nearInstances,nearVertices,farSubmitted,torchActive,torchRefresh,torchPending,coverageSuppressed," + string.Join(",", markers) + ",nearDrawCalls");
        int frame = 0;
        foreach (var point in points)
        {
            Vector3 original = point.point;
            viewer.position = original;
            camera.transform.position = original;
            camera.transform.rotation = rotation;
            sourceObject.transform.position = original;
            float start = Time.realtimeSinceStartup;
            Debug.Log("VEGETATION_PROFILE_START " + point.name + " viewer=" + original);
            while (Time.realtimeSinceStartup - start < 100)
            {
                float elapsed = Time.realtimeSinceStartup - start;
                string phase = point.name + (elapsed < 65 ? "-settle" : elapsed < 75 ? "-stationary" : elapsed < 85 ? "-rotate" : "-small-movement");
                if (compareBatches)
                {
                    treeSettings.standingTreeBatchSizeChunks = elapsed >= 65 && elapsed < 70 ? 1 : 4;
                    if (elapsed >= 65 && elapsed < 75) phase = point.name + (elapsed < 70 ? "-stationary-chunk" : "-stationary-spatial");
                }
                if (elapsed >= 75)
                    camera.transform.rotation = rotation * Quaternion.Euler(0, Mathf.Sin((elapsed - 75) * .8f) * 25, 0);
                if (elapsed >= 85)
                    viewer.position = original + new Vector3(Mathf.Sin((elapsed - 85) * .8f) * 2.5f, 0, 0);
                writer.WriteLine($"{frame},{phase},{Time.unscaledDeltaTime * 1000:F3},{manager.Trees.Count},{changes},{geometryChanges},{trees.MeshRenderStats.instances},{trees.MeshRenderStats.vertices},{trees.RenderStats.instances},{flame.ActiveLights},{flame.StaticRefreshes},{flame.PendingRefreshes},{flame.CoverageSuppressed}," + string.Join(",", recorders.Select(r => r.LastValue / 1000000.0)) + $",{trees.MeshDrawCalls}");
                changes = geometryChanges = 0;
                if (frame++ % 1000 == 0) { writer.Flush(); Debug.Log($"VEGETATION_PROFILE_PROGRESS {phase}: {manager.Trees.Count} trees; {trees.MeshRenderStats.vertices} near vertices; {trees.RenderStats.instances} far submitted"); }
                yield return null;
            }
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Folder + "/" + point.name + ".png", pixels.EncodeToPNG());
            Object.Destroy(pixels); RenderTexture.active = null;
        }
        writer.Flush();
        yield return null;
    }
}
