using System;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Installed mesh/material checks and actual URP renders, without opening a saved world.</summary>
public static class BakedStoneWallValidation
{
    public const string Output = "ArtReferences/StoneBuildKit/BakedWall";
    private static int checks;
    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new InvalidOperationException(message);
    }
    public static void InstallAndRunBatch()
    {
        try { BakedStoneWallSetup.Install(); Run(); StoneKitValidation.RunInstalledBatch(); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    public static void RunInstalledBatch()
    {
        try { Run(); StoneKitValidation.RunInstalledBatch(); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    private static void Run()
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var d = AssetDatabase.LoadAssetAtPath<BuildDefinition>(BakedStoneWallSetup.Definition);
        Check(d != null && d.uvVariants.Length == 3, "S02 or coherent variants missing");
        Check(d.material.shader.name == "Custom/BakedStoneWallMatte", "S02 shader mismatch");
        Check(d.material.enableInstancing && d.material.GetTexture("_BumpMap") != null, "S02 maps/instancing missing");
        Check(d.LocalBounds == new Bounds(new Vector3(2, 1.5f, 0), new Vector3(4, 3, .5f)), "S02 envelope changed");
        foreach (var mesh in d.uvVariants)
        {
            Check(mesh.GetIndexCount(0) == 36 && mesh.subMeshCount == 1 && !mesh.isReadable, "S02 runtime mesh must be 12 triangles, one non-readable submesh");
            using var data = MeshUtility.AcquireReadOnlyMeshData(mesh);
            var source = data[0];
            using var tangents = new NativeArray<Vector4>(source.vertexCount, Allocator.Temp);
            using var normals = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp);
            source.GetTangents(tangents); source.GetNormals(normals);
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 t = tangents[i];
                Check(Mathf.Abs(t.magnitude - 1) < .001f && Mathf.Abs(Vector3.Dot(t, normals[i])) < .001f && Mathf.Abs(tangents[i].w) == 1,
                    "Invalid normal-map tangent basis");
            }
        }
        var collider = d.authoringPrefab.GetComponent<BoxCollider>();
        Check(collider != null && collider.center == d.LocalBounds.center && collider.size == d.LocalBounds.size, "Wall collision changed");
        Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh == d.mesh &&
              d.authoringPrefab.GetComponent<MeshRenderer>().sharedMaterial == d.material, "Authoring prefab mismatch");
        foreach (var path in new[] { BakedStoneWallSetup.Colour, BakedStoneWallSetup.Normal })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Check(importer.mipmapEnabled && !importer.isReadable && importer.maxTextureSize == 2048 && !importer.alphaIsTransparency,
                "Atlas import settings incorrect");
        }
        Render(d);
        File.WriteAllText(Output + "/unity-validation.json", "{\"passed\":true,\"checks\":" + checks +
            ",\"trianglesPerWall\":12,\"originalTriangles\":2960,\"uvVariants\":3,\"normalLightingVerified\":true,\"instancedRendering\":true}");
        Debug.Log("S02 BAKED WALL PASS: " + checks + " checks; 12 triangles, mapped URP lighting, front/back/ends, joined/stacked and rotated instanced renders.");
    }
    private static void Render(BuildDefinition d)
    {
        var oldPipeline = GraphicsSettings.defaultRenderPipeline;
        var oldQuality = QualitySettings.renderPipeline;
        var oldAmbient = RenderSettings.ambientLight;
        var oldMode = RenderSettings.ambientMode;
        var oldAsync = ShaderUtil.allowAsyncCompilation;
        var oldTarget = RenderTexture.active;
        var root = new GameObject("Baked wall validation fixture");
        var cameraObject = new GameObject("Baked wall camera");
        var lightObject = new GameObject("Baked wall directional light");
        var material = new Material(d.material);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2.1f;
        camera.aspect = 4f / 3; camera.nearClipPlane = .01f; camera.farClipPlane = 100;
        camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.17f, .19f, .20f);
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
        light.intensity = 1.3f; light.shadows = LightShadows.None;
        light.cullingMask = 1 << 31;
        light.transform.rotation = Quaternion.Euler(28, -40, 0);
        var target = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32); target.Create();
        Action<ScriptableRenderContext, Camera> submit = null;
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Check(pipeline != null, "URP pipeline missing");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.3f, .3f, .3f);
            RenderSettings.sun = light;
            ShaderUtil.allowAsyncCompilation = false;
            for (int i = 0; i < material.passCount; i++) ShaderUtil.CompilePass(material, i, true);
            Check(!ShaderUtil.ShaderHasError(material.shader), "Baked wall shader compilation failed");
            var wall = new GameObject("Twelve-triangle wall"); wall.layer = 31; wall.transform.SetParent(root.transform);
            var filter = wall.AddComponent<MeshFilter>(); filter.sharedMesh = d.mesh;
            var renderer = wall.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            camera.transform.position = new Vector3(5, 3.2f, -7); camera.transform.LookAt(d.LocalBounds.center);
            var after = Capture(camera, target, "wall-front.png");
            material.SetFloat("_BumpScale", 0);
            var flat = Capture(camera, target, "wall-normal-disabled.png");
            double difference = 0;
            for (int i = 0; i < after.Length; i++) difference += Math.Abs(after[i].r - flat[i].r) + Math.Abs(after[i].g - flat[i].g) + Math.Abs(after[i].b - flat[i].b);
            Check(difference / after.Length > .02, "Normal map made no measurable lighting difference");
            material.SetFloat("_BumpScale", 1);
            light.transform.rotation = Quaternion.Euler(28, 40, 0);
            Capture(camera, target, "wall-light-opposite.png");
            light.transform.rotation = Quaternion.Euler(28, -40, 0);
            var oldModel = AssetDatabase.LoadAssetAtPath<GameObject>(StoneKitSetup.Models + "/" + StoneKitSetup.Files[0] + ".fbx");
            filter.sharedMesh = oldModel.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(StoneKitSetup.MaterialPath);
            Capture(camera, target, "wall-original-2960-tris.png");
            filter.sharedMesh = d.mesh; renderer.sharedMaterial = material;
            camera.transform.position = new Vector3(-4, 3.6f, 7); camera.transform.LookAt(d.LocalBounds.center);
            light.transform.rotation = Quaternion.Euler(30, 140, 0);
            Capture(camera, target, "wall-back-and-end.png");
            camera.orthographicSize = 1.0f;
            camera.transform.position = new Vector3(2.5f, 1.7f, -2); camera.transform.LookAt(new Vector3(2, 1.5f, 0));
            light.transform.rotation = Quaternion.Euler(28, -40, 0);
            Capture(camera, target, "wall-close.png");
            // Exercise the real placed-piece renderer with mixed UV variants and a 45-degree heading.
            renderer.enabled = false;
            camera.cullingMask = ~0; // RenderMeshInstanced uses the building renderer's default layer.
            using var session = new BuildSession(); var frame = session.CreateFrame(Vector3.zero, 1);
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 3; col++) session.Add(d, frame, new Vector3Int(col * 16, row * 12, 0), 0, false);
            var buildRenderer = new BuildRenderer(session);
            camera.orthographicSize = 4.6f;
            var rotation = BuildGeometry.Rotation(1);
            camera.transform.position = rotation * new Vector3(13, 9, -14);
            camera.transform.LookAt(rotation * new Vector3(6, 3, 0));
            light.transform.rotation = Quaternion.Euler(30, 5, 0);
            submit = (_, renderingCamera) => { if (renderingCamera == camera) buildRenderer.Draw(camera, 100, 0); };
            RenderPipelineManager.beginCameraRendering += submit;
            var joined = Capture(camera, target, "wall-joined-stacked-instanced.png");
            Check(buildRenderer.DrawCalls > 0 && buildRenderer.DrawCalls <= 6, "Instanced wall batch missing");
            Check(!ShaderUtil.ShaderHasError(material.shader), "Instanced shader variant failed");
            int wallPixels = 0;
            var background = joined[0];
            foreach (var pixel in joined)
                if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 12) wallPixels++;
            Check(wallPixels > 1000, "Joined instanced fixture rendered no masonry");
        }
        finally
        {
            if (submit != null) RenderPipelineManager.beginCameraRendering -= submit;
            GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality;
            RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientMode = oldMode;
            ShaderUtil.allowAsyncCompilation = oldAsync; RenderTexture.active = oldTarget;
            Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject); Object.DestroyImmediate(material);
            target.Release(); Object.DestroyImmediate(target);
        }
    }
    private static Color32[] Capture(Camera camera, RenderTexture target, string file)
    {
        for (int i = 0; i < 2; i++) RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            var pixels = image.GetPixels32();
            File.WriteAllBytes(Output + "/" + file, image.EncodeToPNG());
            int pink = 0;
            foreach (var pixel in pixels) if (pixel.r > 200 && pixel.b > 200 && pixel.g < 50) pink++;
            Check(pink < 100, "Error shader in " + file);
            return pixels;
        }
        finally { Object.DestroyImmediate(image); }
    }
}
