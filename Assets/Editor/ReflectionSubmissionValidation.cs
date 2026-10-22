using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class ReflectionSubmissionValidation
{
    static IEnumerator routine;
    static Camera camera;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.fog = false;
            camera = new GameObject("Main test camera").AddComponent<Camera>();
            camera.enabled = false; camera.transform.position = new Vector3(0, 2, -10);
            camera.transform.LookAt(new Vector3(0, 2, 0)); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            routine = Run(); EditorApplication.update += Tick;
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void Tick()
    {
        try { if (routine.MoveNext()) return; EditorApplication.update -= Tick; EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.update -= Tick; EditorApplication.Exit(1); }
    }
    static IEnumerator Run()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh = cube.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(cube);
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { enableInstancing = true };
        material.SetColor("_BaseColor", Color.red);
        var fixedObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fixedObject.transform.position = new Vector3(2, 2, 0);
        fixedObject.GetComponent<MeshRenderer>().sharedMaterial = material;
        var target = new RenderTexture(256, 256, 24); target.Create(); camera.targetTexture = target;
        var reflection = new GameObject("Reflection fixture").AddComponent<PlanarWaterReflection>();
        reflection.Configure(camera, 0, .5f);
        var matrix = new[] { Matrix4x4.TRS(new Vector3(-2, 2, 0), Quaternion.identity, Vector3.one) };
        using var report = new StreamWriter(".utmp/vegetation-performance/reflection-submissions.csv");
        report.WriteLine("iteration,reflection,instancedPixels,rendererPixels");
        for (int iteration = 0; iteration < 6; iteration++)
        {
            bool reflected = iteration % 2 == 1;
            Graphics.DrawMeshInstanced(mesh, 0, material, matrix, 1, null, ShadowCastingMode.Off, false, 0, camera);
            if (reflected)
            {
                typeof(PlanarWaterReflection).GetMethod("EnsureTexture", Private).Invoke(reflection, null);
                typeof(PlanarWaterReflection).GetMethod("RenderReflection", Private).Invoke(reflection, null);
            }
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var texture = new Texture2D(256, 256, TextureFormat.RGB24, false);
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); texture.Apply();
            var pixels = texture.GetPixels32(); int left = 0, right = 0;
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                if (pixels[y * 256 + x].r > 64) { if (x < 128) left++; else right++; }
            report.WriteLine($"{iteration},{reflected},{left},{right}"); report.Flush();
            File.WriteAllBytes($".utmp/vegetation-performance/reflection-{iteration}.png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture); RenderTexture.active = null;
            yield return null;
        }
        Debug.Log("REFLECTION_SUBMISSION_VALIDATION_COMPLETE");
    }
}
