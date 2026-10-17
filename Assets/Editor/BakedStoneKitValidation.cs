using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Validate baked solids, shared atlas, intact gate and cap gaps, lighting and real instancing.</summary>
public static class BakedStoneKitValidation
{
    public const string Output = "ArtReferences/StoneBuildKit/BakedKit";
    private static int checks;
    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new InvalidOperationException(message);
    }
    public static void InstallAndRunBatch()
    {
        try { StoneKitSetup.Install(); Run(); BakedStoneWallValidation.RunInstalledBatch(); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    public static void RunInstalledBatch()
    {
        try { Run(); BakedStoneWallValidation.RunInstalledBatch(); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    private static void Run()
    {
        checks = 0;
        Directory.CreateDirectory(Output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(BakedStoneKitSetup.MaterialPath);
        Check(material != null && material.shader.name == "Custom/BakedStoneWallMatte", "Shared baked material missing");
        Check(material.enableInstancing && material.GetTexture("_BumpMap") != null, "Normal map or instancing missing");
        var definitions = new BuildDefinition[5];
        for (int index = 0; index < 5; index++)
        {
            var d = definitions[index] = catalog.Find(StoneKitSetup.Ids[index + 1]);
            Check(d != null && d.material == material && d.uvVariants.Length == 3, "Shared atlas/variants missing");
            foreach (var mesh in d.uvVariants) Geometry(mesh, index);
            Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh == d.mesh &&
                  d.authoringPrefab.GetComponent<MeshRenderer>().sharedMaterial == material, "Prefab mismatch");
            var colliders = d.authoringPrefab.GetComponents<BoxCollider>();
            Check(colliders.Length == (index == 4 ? 2 : 1), "Physical solid count changed");
        }
        foreach (var path in new[] { BakedStoneKitSetup.Colour, BakedStoneKitSetup.Normal })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Check(importer.maxTextureSize == 2048 && importer.mipmapEnabled && !importer.isReadable && !importer.alphaIsTransparency,
                "Shared atlas import settings changed");
        }
        Render(definitions, material);
        File.WriteAllText(Output + "/unity-validation.json", "{\"passed\":true,\"checks\":" + checks +
            ",\"triangles\":[12,12,72,12,24],\"previousTriangles\":[716,2168,264,852,584],\"sharedTexturePairs\":1,\"uvVariantsPerPiece\":3,\"gateOpeningPreserved\":true,\"sixCapstonesPreserved\":true,\"instancedRendering\":true}");
        Debug.Log("BAKED STONE KIT PASS: " + checks + " checks; five pieces, 132 triangles total, shared atlas, closed solids, retained gate/cap gaps, lighting and instancing.");
    }
    private static void Geometry(Mesh mesh, int index)
    {
        Check(!mesh.isReadable && mesh.subMeshCount == 1 && mesh.GetIndexCount(0) == BakedStoneKitSetup.Triangles[index] * 3,
            "Minimal geometry budget changed");
        using var data = MeshUtility.AcquireReadOnlyMeshData(mesh);
        var source = data[0];
        using var positions = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp);
        using var normals = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp);
        using var tangents = new NativeArray<Vector4>(source.vertexCount, Allocator.Temp);
        using var triangles = new NativeArray<int>(source.GetSubMesh(0).indexCount, Allocator.Temp);
        source.GetVertices(positions); source.GetNormals(normals); source.GetTangents(tangents); source.GetIndices(triangles, 0);
        // Welding by position is only for topology checks: actual vertices stay split at hard normals/UV seams.
        var welded = new Dictionary<Vector3, int>();
        var remap = new int[source.vertexCount];
        for (int i = 0; i < positions.Length; i++)
        {
            if (!welded.TryGetValue(positions[i], out int id)) { id = welded.Count; welded.Add(positions[i], id); }
            remap[i] = id;
            Vector3 tangent = tangents[i];
            Check(Mathf.Abs(tangent.magnitude - 1) < .001f && Mathf.Abs(Vector3.Dot(tangent, normals[i])) < .001f &&
                  Mathf.Abs(tangents[i].w) == 1, "Invalid tangent basis");
        }
        var edges = new Dictionary<(int, int), int>();
        var parent = new int[welded.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Root(int i) { while (parent[i] != i) i = parent[i]; return i; }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            if (index == 4)
            {
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                for (int j = 0; j < 3; j++) { float x = positions[triangles[i + j]].x; lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
                Check(hi <= 1.37501f || lo >= 2.62499f, "Visual triangle closes the gate opening");
            }
            for (int j = 0; j < 3; j++)
            {
                int a = remap[triangles[i + j]], b = remap[triangles[i + (j + 1) % 3]];
                parent[Root(a)] = Root(b);
                var key = (Math.Min(a, b), Math.Max(a, b)); edges.TryGetValue(key, out int count); edges[key] = count + 1;
            }
        }
        foreach (int count in edges.Values) Check(count == 2, "Baked solid has an open/nonmanifold edge");
        var components = new HashSet<int>(); for (int i = 0; i < parent.Length; i++) components.Add(Root(i));
        Check(components.Count == (index == 2 ? 6 : index == 4 ? 2 : 1), "Capstones or gate wings merged unexpectedly");
    }
    private static void Render(BuildDefinition[] definitions, Material shared)
    {
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
        var oldAmbient = RenderSettings.ambientLight; var oldMode = RenderSettings.ambientMode;
        var oldAsync = ShaderUtil.allowAsyncCompilation; var oldTarget = RenderTexture.active;
        var cameraObject = new GameObject("Baked kit camera");
        var lightObject = new GameObject("Baked kit light");
        var obj = new GameObject("Baked kit preview"); obj.layer = 31;
        var filter = obj.AddComponent<MeshFilter>(); var renderer = obj.AddComponent<MeshRenderer>();
        var material = new Material(shared);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.aspect = 4f / 3; camera.nearClipPlane = .01f; camera.farClipPlane = 200;
        camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.17f, .19f, .20f);
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
        light.shadows = LightShadows.None; light.cullingMask = 1 << 31;
        var target = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32); target.Create();
        Action<ScriptableRenderContext, Camera> submit = null;
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Check(pipeline != null, "URP pipeline missing");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.3f, .3f, .3f); RenderSettings.sun = light;
            ShaderUtil.allowAsyncCompilation = false;
            for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
            Check(!ShaderUtil.ShaderHasError(material.shader), "Baked stone shader failed");
            double totalNormalDifference = 0;
            for (int i = 0; i < definitions.Length; i++)
            {
                var d = definitions[i]; string tag = BakedStoneKitSetup.Tags[i];
                var bounds = d.LocalBounds;
                camera.orthographicSize = Mathf.Max(bounds.size.y, Mathf.Max(bounds.size.x, bounds.size.z) * .7f) * .8f + .2f;
                camera.transform.position = bounds.center + new Vector3(5, 4, -7).normalized * 12;
                camera.transform.LookAt(bounds.center);
                light.transform.rotation = Quaternion.Euler(30, -35, 0);
                filter.sharedMesh = d.mesh; renderer.sharedMaterial = material;
                var after = Capture(camera, target, tag + "-baked.png");
                material.SetFloat("_BumpScale", 0);
                var flat = Capture(camera, target, tag + "-normal-disabled.png");
                double difference = 0;
                for (int j = 0; j < after.Length; j++) difference += Math.Abs(after[j].r - flat[j].r) + Math.Abs(after[j].g - flat[j].g) + Math.Abs(after[j].b - flat[j].b);
                totalNormalDifference += difference / after.Length;
                material.SetFloat("_BumpScale", 1);
                light.transform.rotation = Quaternion.Euler(30, 35, 0);
                Capture(camera, target, tag + "-opposite-light.png");
                light.transform.rotation = Quaternion.Euler(30, -35, 0);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(StoneKitSetup.Models + "/" + StoneKitSetup.Files[i + 1] + ".fbx");
                filter.sharedMesh = source.GetComponentInChildren<MeshFilter>(true).sharedMesh;
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(StoneKitSetup.MaterialPath);
                Capture(camera, target, tag + "-original.png");
                filter.sharedMesh = d.mesh; renderer.sharedMaterial = material;
                camera.transform.position = bounds.center + new Vector3(-5, -3, 7).normalized * 12; camera.transform.LookAt(bounds.center);
                light.transform.rotation = Quaternion.Euler(-30, 145, 0);
                Capture(camera, target, tag + "-reverse-underside.png");
            }
            Check(totalNormalDifference > .05, "Baked normals made no measurable lighting difference");
            renderer.enabled = false; camera.cullingMask = ~0;
            using var session = new BuildSession(); var frame = session.CreateFrame(Vector3.zero, 1);
            for (int i = 0; i < definitions.Length; i++)
                for (int variation = 0; variation < 3; variation++)
                    session.Add(definitions[i], frame, new Vector3Int(i * 24, 0, variation * 22), 0, false);
            var buildRenderer = new BuildRenderer(session);
            var rotation = BuildGeometry.Rotation(1);
            camera.orthographicSize = 13; camera.transform.position = rotation * new Vector3(26, 22, -28);
            camera.transform.LookAt(rotation * new Vector3(14, 1, 6)); light.transform.rotation = Quaternion.Euler(35, 5, 0);
            submit = (_, renderingCamera) => { if (renderingCamera == camera) buildRenderer.Draw(camera, 200, 0); };
            RenderPipelineManager.beginCameraRendering += submit;
            Capture(camera, target, "kit-instanced.png");
            Check(buildRenderer.DrawCalls > 0 && !ShaderUtil.ShaderHasError(shared.shader), "Baked kit instancing failed");
        }
        finally
        {
            if (submit != null) RenderPipelineManager.beginCameraRendering -= submit;
            GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality;
            RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientMode = oldMode;
            ShaderUtil.allowAsyncCompilation = oldAsync; RenderTexture.active = oldTarget;
            Object.DestroyImmediate(obj); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject); Object.DestroyImmediate(material);
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
            var pixels = image.GetPixels32(); File.WriteAllBytes(Output + "/" + file, image.EncodeToPNG());
            int pink = 0, visible = 0; var background = pixels[0];
            foreach (var pixel in pixels)
            {
                if (pixel.r > 200 && pixel.b > 200 && pixel.g < 50) pink++;
                if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 12) visible++;
            }
            Check(pink < 100 && visible > 500, "Missing/error geometry in " + file);
            return pixels;
        }
        finally { Object.DestroyImmediate(image); }
    }
}
