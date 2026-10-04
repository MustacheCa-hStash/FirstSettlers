using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class StandingTreeValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object value, string name) => value.GetType().GetField(name,
        Private | BindingFlags.Public).GetValue(value);
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static TreeInstanceData Tree(ChunkCoord coord, int cell, float snow = 0f) => new(
        new Vector3(cell * 3, 0, 0), Quaternion.identity, Vector3.one, WorldFeatureVariant.SpruceTree,
        new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), snow,
        TreeId.Generated(1937, coord, TreePlacementSource.Forest, cell));

    [MenuItem("Tools/Terrain/Validate Standing Tree Instancing")]
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation = false;
        TreeRegistryValidation.Run();
        DistantTreeGroundingValidation.Run();
        ValidateRegistryConsumption();
        DistantTreeValidation.ValidateStandingHandoff();
        ValidatePrefabAndRendering();
        ValidateLodAndSubmeshes();
        ValidateSceneShaders();
        DistantTreeGpuValidation.Run();
        Debug.Log("STANDING TREE PASS: registry state filtering, shared bark/foliage, instanced snow, wind, " +
            "LOD mask partition, submeshes, bounded batches, unchanged prefab assets and scene shader variants.");
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateRegistryConsumption()
    {
        var coord = new ChunkCoord(-2, 3);
        var registry = new TreeRegistry(1937, 128);
        var input = new[] { Tree(coord, 0), Tree(coord, 1) };
        using var renderer = new DistantTreeManager(new TreeSettings(), 1937, 128, 100, 5, .5f, 2,
            1, 10, .24f, 1.5f, WorldFeatureGenerationSettings.Default, treeRegistry: registry);
        var build = typeof(DistantTreeManager).GetMethod("Build", Private);
        object Build(bool register) => build.Invoke(renderer, new object[] { coord, input, TreePlacementDetail.Distant, register });
        Check(((TreeInstanceData[])Field(Build(true), "Trees")).Length == 2, "Initial registry render snapshot failed.");
        registry.TrySetState(input[0].id, TreeState.Cut);
        var filtered = (TreeInstanceData[])Field(Build(false), "Trees");
        Check(filtered.Length == 1 && filtered[0].id == input[1].id, "Render snapshot ignored cut state.");
        Check(((TreeInstanceData[])Field(Build(true), "Trees")).Length == 1, "Registration resurrected a cut tree.");
        registry.RegisterChunk(coord, Array.Empty<TreeInstanceData>(), TreePlacementDetail.Detailed);
        Check(((TreeInstanceData[])Field(Build(true), "Trees")).Length == 0, "Late worker rendered rejected lower-authority data.");
    }

    private static void ValidatePrefabAndRendering()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Spruce_LOD0_v06.prefab");
        var settings = new TreeSettings { spruceTreePrefab = prefab };
        int objectsBefore = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
        using var renderer = new StandingTreeRenderer(settings);
        Check(Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length == objectsBefore, "Renderer created tree GameObjects.");
        Check(renderer.Supports(WorldFeatureVariant.SpruceTree), "Spruce definition missing.");
        var batches = ((IEnumerable)Field(renderer, "batches")).Cast<object>().ToArray();
        Check(batches.Length >= 2 && batches.Any(b => (bool)Field(b, "Trunk")) && batches.Any(b => !(bool)Field(b, "Trunk")),
            "Spruce bark and foliage were not extracted independently.");
        var cameraObject = new GameObject("Standing tree validation camera");
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
        camera.orthographic = true; camera.orthographicSize = 9; camera.aspect = 1;
        camera.transform.position = new Vector3(0, 6, -24); camera.transform.LookAt(new Vector3(0, 6, 0));
        var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(512, 512, TextureFormat.RGBA32, false);
        var command = new CommandBuffer(); var previousTarget = RenderTexture.active;
        var previousPipeline = GraphicsSettings.defaultRenderPipeline; var previousQuality = QualitySettings.renderPipeline;
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            target.Create(); camera.targetTexture = target; camera.Render();
            foreach (var b in batches)
            {
                var material = (Material)Field(b, "Material");
                if (material.HasProperty("_AmbientStrength")) material.SetFloat("_AmbientStrength", 1f);
                for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
                Check(!ShaderUtil.ShaderHasError(material.shader), material.shader.name + " failed instancing compilation.");
            }
            Color32[] Render(float snow, float time, bool wind)
            {
                renderer.BeginFrame(camera);
                renderer.Submit(Tree(default, 0), Matrix4x4.Translate(Vector3.left * 4), 0, 1);
                renderer.Submit(Tree(default, 1, snow), Matrix4x4.Translate(Vector3.right * 4), 0, 1);
                command.Clear(); command.SetRenderTarget(target); command.ClearRenderTarget(true, true, Color.clear);
                command.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                command.SetGlobalMatrix("unity_MatrixVP", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix);
                command.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                command.SetGlobalVector("_MainLightPosition", new Vector4(0, 1, 0, 0));
                command.SetGlobalVector("_MainLightColor", Vector4.one);
                command.SetGlobalVector("_Time", new Vector4(time / 20, time, time * 2, time * 3));
                foreach (var b in batches)
                {
                    int count = (int)Field(b, "Count"); if (count == 0) continue;
                    var material = (Material)Field(b, "Material");
                    if (material.HasProperty("_WindStrength")) material.SetFloat("_WindStrength", wind ? .3f : 0f);
                    if (material.HasProperty("_WindFlutterStrength")) material.SetFloat("_WindFlutterStrength", wind ? .15f : 0f);
                    var block = new MaterialPropertyBlock();
                    foreach (var pair in new[] { ("_StandingTreeLeaves", "Leaves"), ("_StandingTreeBark", "Bark"),
                        ("_StandingTreeAppearance", "Appearance"), ("_StandingTreeFade", "Fade") })
                        block.SetVectorArray(pair.Item1, (Vector4[])Field(b, pair.Item2));
                    command.DrawMeshInstanced((Mesh)Field(b, "Mesh"), (int)Field(b, "Submesh"), material, 0,
                        (Matrix4x4[])Field(b, "Matrices"), count, block);
                }
                Graphics.ExecuteCommandBuffer(command); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply();
                var result = pixels.GetPixels32();
                renderer.EndChunk();
                Check(renderer.DrawCalls >= 2, "Shared parts were not submitted as instanced draws.");
                return result;
            }
            var dry = Render(0, 0, false); var snowy = Render(1, 0, false);
            int leftChanges = 0, rightChanges = 0, white = 0;
            for (int i = 0; i < dry.Length; i++)
            {
                if (!dry[i].Equals(snowy[i])) { if (i % 512 < 256) leftChanges++; else rightChanges++; }
                if (i % 512 >= 256 && snowy[i].r > 150 && Math.Abs(snowy[i].r - snowy[i].g) < 35 && snowy[i].a > 0) white++;
            }
            Check(leftChanges == 0 && rightChanges > 1000 && white > 1000, $"Instance snow leaked or missing: left={leftChanges}, right={rightChanges}, white={white}.");
            Directory.CreateDirectory("Logs/StandingTreeValidation"); File.WriteAllBytes("Logs/StandingTreeValidation/mixed-snow.png", pixels.EncodeToPNG());
            var windA = Render(1, 1, true); var windB = Render(1, 5, true);
            Check(windA.Where((p, i) => !p.Equals(windB[i])).Count() > 100, "Instancing froze authored spruce wind.");
            renderer.BeginFrame(camera);
            for (int i = 0; i < 1001; i++) renderer.Submit(Tree(default, 0), Matrix4x4.identity, 0, 1);
            renderer.EndChunk();
            Check(renderer.DrawCalls >= 6 && renderer.RenderStats.instances >= 2002, "Batches dropped instances at capacity.");
            Debug.Log($"Spruce instancing: {batches.Length} shared render parts; independent snow changed {rightChanges} pixels with {white} white snow pixels; wind and 1001-tree batch boundaries passed.");
        }
        finally
        {
            RenderTexture.active = previousTarget; GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQuality;
            command.Dispose(); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(cameraObject);
        }
    }

    private static void ValidateLodAndSubmeshes()
    {
        var root = new GameObject("Synthetic tree asset"); var cameraObject = new GameObject("LOD validation camera");
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
        mesh.subMeshCount = 2; mesh.SetTriangles(new[] { 0, 1, 2 }, 0); mesh.SetTriangles(new[] { 0, 2, 1 }, 1); mesh.RecalculateBounds();
        var bark = new Material(Shader.Find("Custom/SpruceBarkSimpleLit"));
        var leaf = new Material(Shader.Find("Custom/SpruceLeafSimpleLitCutout"));
        try
        {
            MeshRenderer Make(string name, float offset)
            {
                var child = new GameObject(name); child.transform.SetParent(root.transform); child.transform.localPosition = Vector3.right * offset;
                child.AddComponent<MeshFilter>().sharedMesh = mesh; var r = child.AddComponent<MeshRenderer>(); r.sharedMaterials = new[] { bark, leaf }; return r;
            }
            var fine = Make("Fine", 2); var coarse = Make("Coarse", 3);
            var group = root.AddComponent<LODGroup>(); group.SetLODs(new[] { new LOD(.5f, new[] { fine }), new LOD(.01f, new[] { coarse }) });
            using var renderer = new StandingTreeRenderer(new TreeSettings { spruceTreePrefab = root });
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            var defs = (IDictionary)Field(renderer, "definitions"); var definition = defs[root];
            camera.orthographicSize = ((Bounds)Field(definition, "Bounds")).size.y * QualitySettings.lodBias; // screen height .5 at LOD boundary
            foreach (float scale in new[] { .98f, 1.02f })
            {
                renderer.BeginFrame(camera); var tree = Tree(default, 0); tree.localScale = Vector3.one * scale;
                renderer.Submit(tree, Matrix4x4.Scale(tree.localScale), 0, 1);
                var active = ((IEnumerable)Field(renderer, "batches")).Cast<object>().Where(b => (int)Field(b, "Count") > 0).ToArray();
                Check(active.Length == 2 && active.All(b => (int)Field(b, "Count") == 2), "Both sides of LOD boundary must submit both submeshes at both levels.");
                var fades = (Vector4[])Field(active[0], "Fade");
                Check(Mathf.Abs(fades[0].x - fades[1].y) < .0001f && fades[0].y == 1 && fades[1].x == 0,
                    "LOD dither intervals leave a hole or overlap.");
                var matrices = (Matrix4x4[])Field(active[0], "Matrices");
                Check(Mathf.Abs(matrices[0].m03 - 2 * scale) < .0001f && Mathf.Abs(matrices[1].m03 - 3 * scale) < .0001f,
                    "Prefab child transforms were lost.");
                renderer.EndChunk();
            }
            Check(!bark.enableInstancing && !leaf.enableInstancing, "Source materials were mutated.");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(mesh); Object.DestroyImmediate(bark); Object.DestroyImmediate(leaf); }
    }

    private static void ValidateSceneShaders()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity", OpenSceneMode.Single);
        var world = Object.FindFirstObjectByType<WorldManager>();
        var settings = (TreeSettings)Field(world, "treeSettings");
        using var renderer = new StandingTreeRenderer(settings);
        var mats = (IDictionary)Field(renderer, "materials");
        foreach (DictionaryEntry entry in mats)
        {
            var source = (Material)entry.Key; var material = (Material)entry.Value;
            if (source.HasProperty("_WindStrength")) Check(source.GetFloat("_WindStrength") == material.GetFloat("_WindStrength"), "Authored wind strength changed.");
            for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
            Check(!ShaderUtil.ShaderHasError(material.shader), "Scene tree shader error: " + material.shader.name);
            Debug.Log("Instanced scene tree material: " + source.name + " / " + material.shader.name);
        }
        Check(renderer.Supports(WorldFeatureVariant.SpruceTree), "SmearScene spruce is not supported.");
    }
}
