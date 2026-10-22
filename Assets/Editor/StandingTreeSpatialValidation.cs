using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class StandingTreeSpatialValidation
{
    static IEnumerator routine;
    static StandingTreeRenderer renderer;
    static TreeSettings settings;
    static Camera camera;
    static Mesh mesh;
    static Material material;
    static GameObject prefab;
    static int span;
    static bool discard;
    static readonly List<(ChunkCoord chunk, TreeInstanceData tree, Matrix4x4 matrix, float handoff, float load)> input = new();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var pipeline = UnityEditor.AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline; ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.fog = false;
            mesh = new Mesh { vertices = new[] { new Vector3(-.4f, -.4f), new Vector3(.4f, -.4f), new Vector3(-.4f, .4f), new Vector3(.4f, .4f) }, triangles = new[] { 0, 1, 2, 1, 3, 2 } }; mesh.RecalculateBounds();
            material = new Material(Shader.Find("Hidden/StandingTreeSpatialValidation")) { enableInstancing = true };
            prefab = new GameObject("Inactive spatial test source"); prefab.AddComponent<MeshFilter>().sharedMesh = mesh;
            prefab.AddComponent<MeshRenderer>().sharedMaterial = material; prefab.SetActive(false);
            settings = new TreeSettings { treeLOD0GameObjectPrefab = prefab, castTreeShadows = false };
            renderer = new StandingTreeRenderer(settings);
            camera = new GameObject("Spatial instance validation camera").AddComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 16; camera.aspect = 36f / 32; camera.transform.position = new Vector3(0, 0, -30);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            routine = Run(); RenderPipelineManager.beginCameraRendering += Draw; EditorApplication.update += Tick;
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Tick()
    {
        try { if (!routine.MoveNext()) Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Finish(int code)
    {
        EditorApplication.update -= Tick; RenderPipelineManager.beginCameraRendering -= Draw;
        renderer?.Dispose(); if (prefab != null) Object.DestroyImmediate(prefab); if (mesh != null) Object.DestroyImmediate(mesh);
        if (material != null) Object.DestroyImmediate(material); if (camera != null) Object.DestroyImmediate(camera.gameObject);
        if (code == 0)
        {
            try { StandingTreeValidation.Run(); VegetationRefreshValidation.RunBatch(); return; }
            catch (Exception error) { Debug.LogException(error); code = 1; }
        }
        EditorApplication.Exit(code);
    }
    static void Draw(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering != camera) return;
        settings.standingTreeBatchSizeChunks = Math.Max(1, span); renderer.BeginFrame(camera);
        foreach (var item in input)
        {
            if (span == 0) { renderer.Submit(item.tree, item.matrix, item.handoff, item.load); renderer.EndChunk(); }
            else renderer.SubmitSpatial(item.chunk, item.tree, item.matrix, item.handoff, item.load);
        }
        if (discard) renderer.BeginFrame(camera);
        if (span != 0) renderer.EndFrame();
        int calls = renderer.DrawCalls; renderer.EndFrame();
        Check(renderer.DrawCalls == calls, "Ending a tree frame twice submitted duplicate instances");
    }
    static Color32[] Capture(RenderTexture target)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); return pixels.GetPixels32();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
    }
    static IEnumerator Run()
    {
        var floor = typeof(StandingTreeRenderer).GetMethod("FloorRegion", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (int size in new[] { 1, 2, 4, 8 }) foreach (int coordinate in new[] { int.MinValue, -17, -9, -8, -1, 0, 1, 7, 8, int.MaxValue })
            Check((int)floor.Invoke(null, new object[] { coordinate, size }) == (int)Math.Floor(coordinate / (double)size), "Negative or extreme region ownership changed");
        var target = new RenderTexture(720, 640, 24); target.Create();
        foreach (int count in new[] { 128, 1001 })
        {
            input.Clear();
            for (int i = 0; i < count; i++)
            {
                var chunk = count == 1001 ? new ChunkCoord(-1, -1) : new ChunkCoord(i % 9 - 4, i / 9 - 7);
                var tree = new TreeInstanceData(Vector3.zero, Quaternion.identity, Vector3.one,
                    i % 2 == 0 ? WorldFeatureVariant.SpruceTree : WorldFeatureVariant.GrasslandMapleTree,
                    new Color32((byte)(31 + i * 73 % 224), 100, 100, 255), new Color32(100, (byte)(31 + i * 151 % 224), 100, 255), (i % 5) * .2f);
                var matrix = Matrix4x4.Translate(new Vector3(i % 36 - 17.5f, i / 36 - 13.5f, 0));
                input.Add((chunk, tree, matrix, i % 3 * .15f, .4f + i % 4 * .2f));
            }
            var random = new System.Random(83);
            for (int i = input.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (input[i], input[j]) = (input[j], input[i]); }
            span = 0; var reference = Capture(target);
            Check(reference.Count(p => p.r + p.g + p.b > 10) > count * 25, "Spatial validation reference was not rendered");
            yield return null;
            foreach (int size in new[] { 1, 2, 4, 8 })
            {
                span = size; var actual = Capture(target);
                Check(actual.SequenceEqual(reference), $"Spatial size {size} changed per-instance matrices/tints/snow/fades at count {count}");
                Check(renderer.RenderStats.instances == count, "Spatial batching lost or duplicated instances");
                if (count == 1001) Check(renderer.DrawCalls == 3, "Spatial capacity boundary did not split at 500 instances");
                yield return null;
            }
            discard = true; var empty = Capture(target); discard = false;
            Check(empty.All(p => p.r + p.g + p.b == 0), "Abandoned tree submissions survived BeginFrame");
            yield return null;
        }
        target.Release(); Object.DestroyImmediate(target);
        Debug.Log("SPATIAL_TREE_VALIDATION_PASS: shuffled chunks, negative/extreme coordinates, mixed per-instance attributes and fades, 1001 instances, frame reset and duplicate EndFrame.");
    }
}
