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

/// <summary>Actual imported walls, URP renders and eight-heading Play-mode support/collision checks.</summary>
[InitializeOnLoad]
public static class LimestoneKitValidation
{
    public const string Output = "ArtReferences/LimestoneBuildKit/Models";
    private const string Pending = "FS.Limestone.Validation";
    private static int checks;
    private static readonly List<string> errors = new();
    static LimestoneKitValidation() => EditorApplication.playModeStateChanged += OnState;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
    private static void Near(Vector3 a, Vector3 b, string message) => Check((a - b).sqrMagnitude < .000001f, message + ": " + a + " / " + b);
    public static void InstallAndRunBatch() => Run(true);
    public static void RunInstalledBatch() => Run(false);
    private static void Run(bool install)
    {
        try
        {
            checks = 0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            if (install)
            {
                var originals = new Dictionary<string, string>();
                foreach (var d in catalog.presets) if (Array.IndexOf(LimestoneKitSetup.Ids, d.contentId) < 0)
                    originals.Add(AssetDatabase.GetAssetPath(d), File.ReadAllText(AssetDatabase.GetAssetPath(d)));
                LimestoneKitSetup.Install();
                var guids = new string[3];
                for (int i = 0; i < 3; i++) guids[i] = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog.Find(LimestoneKitSetup.Ids[i])));
                int count = catalog.presets.Length;
                LimestoneKitSetup.Install();
                Check(catalog.presets.Length == count, "Repeat import duplicated catalog entries");
                for (int i = 0; i < 3; i++) Check(guids[i] == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog.Find(LimestoneKitSetup.Ids[i]))), "Definition GUID changed on reinstall");
                foreach (var original in originals) Check(File.ReadAllText(original.Key) == original.Value, "Unrelated definition changed: " + original.Key);
            }
            Assets(catalog);
            Render(catalog);
            SessionState.SetInt(Pending + ".checks", checks);
            SessionState.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void Assets(BuildCatalog catalog)
    {
        var ids = new HashSet<string>();
        foreach (var d in catalog.presets) Check(d != null && ids.Add(d.contentId), "Missing/duplicate catalog definition");
        var shared = AssetDatabase.LoadAssetAtPath<Material>(LimestoneKitSetup.MaterialPath);
        Check(shared != null && shared.shader.name == "Custom/BakedStoneWallMatte" && shared.enableInstancing, "Shared matte material missing");
        Check(shared.GetTexture("_BumpMap") != null && shared.GetTexture("_BaseMap") != null && shared.GetFloat("_AlphaClip") == 0, "Opaque colour/AO + normal pair missing");
        for (int part = 0; part < 3; part++)
        {
            var d = catalog.Find(LimestoneKitSetup.Ids[part]); float width = part == 2 ? 2 : 4;
            Check(d != null && d.kind == BuildPartKind.Wall && d.material == shared, "Limestone definition missing");
            Near(d.LocalBounds.min, new Vector3(0, 0, -.125f), "Wall base/thickness centreline");
            Near(d.LocalBounds.max, new Vector3(width, 3, .125f), "Wall size");
            Check(d.StackRise == 3 && d.WallEndInset == 0 && d.preserveAuthoredMesh, "Full-span fitting contract missing");
            Check(d.jointCoverVariants.Length == 0 && !d.canGroundOnTerrain, "Limestone gained timber covers or new grounding rules");
            Check(d.jointSockets.Length == (int)(width * 4) + 1, "Incorrect short/full span sockets");
            Check(d.uvVariants.Length == 3 && d.mesh == d.uvVariants[0], "UV variants missing");
            var colliders = d.authoringPrefab.GetComponents<BoxCollider>();
            Check(colliders.Length == 1 && colliders[0].center == d.LocalBounds.center && colliders[0].size == d.LocalBounds.size, "Authoring collider mismatch");
            Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh == d.mesh && d.authoringPrefab.GetComponent<MeshRenderer>().sharedMaterial == shared, "Prefab visual references mismatch");
            Check(BuildUiContent.Category(d) == 2 && BuildUiContent.Description(d).Contains("limestone"), "Wall menu metadata missing");
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/" + BuildUiContent.ThumbnailKey(d)) != null, "Actual-mesh icon missing");
            Vector3[] firstVertices = null; Vector2[] firstUvs = null;
            foreach (var mesh in d.uvVariants)
            {
                Check(mesh.subMeshCount == 1 && mesh.GetIndexCount(0) == 36, "Runtime wall must be one closed 12-triangle solid");
                Near(mesh.bounds.min, d.LocalBounds.min, "Imported min"); Near(mesh.bounds.max, d.LocalBounds.max, "Imported max");
                using var data = MeshUtility.AcquireReadOnlyMeshData(mesh); var source = data[0];
                using var vertices = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp);
                using var normals = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp);
                using var tangents = new NativeArray<Vector4>(source.vertexCount, Allocator.Temp);
                using var uvs = new NativeArray<Vector2>(source.vertexCount, Allocator.Temp);
                using var indices = new NativeArray<int>(36, Allocator.Temp);
                source.GetVertices(vertices); source.GetNormals(normals); source.GetTangents(tangents); source.GetUVs(0, uvs); source.GetIndices(indices, 0);
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 tangent = tangents[i];
                    Check(Mathf.Abs(tangent.magnitude - 1) < .001f && Mathf.Abs(Vector3.Dot(tangent, normals[i])) < .001f && Mathf.Abs(tangents[i].w) == 1, "Invalid normal tangent basis");
                    Check(float.IsFinite(uvs[i].x) && float.IsFinite(uvs[i].y) && uvs[i].x > .034f && uvs[i].x < .966f && uvs[i].y > .034f && uvs[i].y < .966f, "UV escaped atlas padding");
                }
                double volume = 0;
                var edges = new Dictionary<string, int>();
                string Point(Vector3 v) => $"{Mathf.RoundToInt(v.x * 10000)},{Mathf.RoundToInt(v.y * 10000)},{Mathf.RoundToInt(v.z * 10000)}";
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                    Check(Vector3.Dot(Vector3.Cross(b - a, c - a), normals[ia] + normals[ib] + normals[ic]) > 0, "Inverted wall face");
                    Vector2 u = uvs[ib] - uvs[ia], v = uvs[ic] - uvs[ia];
                    Check(Mathf.Abs(u.x * v.y - u.y * v.x) > 1e-10f, "Collapsed wall UV triangle");
                    volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                    foreach (var pair in new[] { (a, b), (b, c), (c, a) })
                    {
                        string p = Point(pair.Item1), q = Point(pair.Item2);
                        string key = string.CompareOrdinal(p, q) < 0 ? p + "/" + q : q + "/" + p;
                        edges.TryGetValue(key, out int count); edges[key] = count + 1;
                    }
                }
                Check(Math.Abs(volume - width * 3 * .25) < .001, "Incorrect solid volume");
                foreach (int count in edges.Values) Check(count == 2, "Wall is not closed/manifold");
                if (firstVertices == null) { firstVertices = vertices.ToArray(); firstUvs = uvs.ToArray(); }
                else
                {
                    int changed = 0;
                    for (int i = 0; i < vertices.Length; i++) { Near(vertices[i], firstVertices[i], "UV variant changed geometry"); if ((uvs[i] - firstUvs[i]).sqrMagnitude > .000001f) changed++; }
                    Check(changed > vertices.Length * .9f, "Variant did not change UV mapping");
                }
            }
        }
        foreach (string path in new[] { LimestoneKitSetup.Colour, LimestoneKitSetup.Normal })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Check(importer.mipmapEnabled && !importer.isReadable && importer.maxTextureSize == 2048 && importer.anisoLevel == 4 && !importer.alphaIsTransparency, "Texture filtering/AO import changed");
            var platform = importer.GetPlatformTextureSettings("Standalone");
            Check(platform.overridden && platform.format == (path == LimestoneKitSetup.Normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7), "Texture compression mismatch");
        }
    }

    private static void OnState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); checks = SessionState.GetInt(Pending + ".checks", 0); errors.Clear();
        void Log(string message, string trace, LogType type) { if (type is LogType.Error or LogType.Exception or LogType.Assert) errors.Add(message); }
        Application.logMessageReceived += Log;
        try
        {
            var catalog = Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            foreach (var id in LimestoneKitSetup.Ids) for (int yaw = 0; yaw < 8; yaw++) Runtime(catalog, catalog.Find(id), yaw);
            Check(errors.Count == 0, "Runtime errors: " + string.Join(" | ", errors));
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/unity-validation.json", "{\"passed\":true,\"checks\":" + checks + ",\"parts\":3,\"uvVariants\":9,\"trianglesPerWall\":12,\"headings\":8,\"placementCases\":24,\"sharedAtlasSize\":2048,\"urpRenders\":true,\"instancedRendering\":true}");
            Debug.Log("LIMESTONE PASS: " + checks + " checks; 9 imported layouts, 24 support/collision cases, short-span endpoints, shared maps and URP renders.");
            Application.logMessageReceived -= Log; EditorApplication.Exit(0);
        }
        catch (Exception ex) { Application.logMessageReceived -= Log; Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void Runtime(BuildCatalog catalog, BuildDefinition d, int yaw)
    {
        using var session = new BuildSession();
        var origin = new Vector3(200 + yaw * 30, 30, 200); var q = BuildGeometry.Rotation(yaw);
        var frame = session.CreateFrame(origin, yaw);
        var footing = session.Add(catalog.Find("build.prototype.foundation"), frame, default, 0, true);
        var first = session.Add(d, frame, default, 0, false);
        float width = d.LocalBounds.size.x;
        var stack = BuildPlacement.Solve(d, origin + q * new Vector3(width / 2, 3, 0), Vector3.up, first, session.Frame(first.OwnFrameId), yaw, 0, default);
        Near(stack.Origin, origin + Vector3.up * 3, "Three-metre stack datum");
        var end = BuildPlacement.Solve(d, origin + q * new Vector3(width, 1, 0), q * Vector3.right, first, session.Frame(first.OwnFrameId), yaw, 0, default);
        Near(end.Origin, origin + q * new Vector3(width, 0, 0), "Wall endpoint span");
        var upper = session.Add(d, frame, new Vector3Int(0, 12, 0), 0, false);
        Check(first.Supported && upper.Supported, "Stack lost foundation support");
        foreach (var record in new[] { first, upper })
        {
            Check(Array.IndexOf(d.uvVariants, record.Resolved.mesh) >= 0, "Fitting replaced authored limestone");
            Check(record.Resolved.auxiliaryMesh == null && record.Resolved.attachments.Length == 0, "Limestone gained timber junction parts");
        }
        using (var gameplay = new BuildGameplay(session))
        {
            gameplay.Update(origin + q * new Vector3(width / 2, 1, 0), 32, 40, 32); Physics.SyncTransforms();
            Check(gameplay.TryGetProxy(first.Id, out var proxy), "Pooled limestone collider missing");
            var collider = proxy.GetComponent<BoxCollider>();
            Check(collider != null && collider.enabled, "Wall collider missing");
            Near(collider.size, d.LocalBounds.size, "Runtime wall collider size");
            var ray = new Ray(origin + q * new Vector3(width / 2, 1.5f, -2), q * Vector3.forward);
            Check(collider.Raycast(ray, out var hit, 4), "Wall does not block traversal ray");
            Check(Mathf.Abs(hit.distance - 1.875f) < .001f, "Wall thickness changed in collision");
        }
        session.Remove(footing.Id);
        Check(!first.Supported && !upper.Supported, "Walls retained support after footing removal");
    }

    private static void Render(BuildCatalog catalog)
    {
        Directory.CreateDirectory(Output);
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
        var oldAmbient = RenderSettings.ambientLight; var oldMode = RenderSettings.ambientMode; var oldSun = RenderSettings.sun;
        bool oldAsync = ShaderUtil.allowAsyncCompilation; var oldTarget = RenderTexture.active;
        var root = new GameObject("Limestone render fixture");
        var cameraObject = new GameObject("Limestone render camera"); var lightObject = new GameObject("Limestone render light");
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
        camera.nearClipPlane = .01f; camera.farClipPlane = 100; camera.aspect = 16f / 9; camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.17f, .19f, .20f);
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
        light.shadows = LightShadows.None; light.cullingMask = 1 << 31; light.transform.rotation = Quaternion.Euler(28, -40, 0);
        var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32); target.Create();
        var material = new Material(catalog.Find(LimestoneKitSetup.Ids[0]).material);
        Action<ScriptableRenderContext, Camera> submit = null;
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Check(pipeline != null, "URP pipeline missing");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.3f, .3f, .3f); RenderSettings.sun = light;
            ShaderUtil.allowAsyncCompilation = false;
            for (int i = 0; i < material.passCount; i++) ShaderUtil.CompilePass(material, i, true);
            Check(!ShaderUtil.ShaderHasError(material.shader), "Limestone shader failed compilation");
            var renderers = new List<MeshRenderer>();
            for (int part = 0; part < 3; part++)
            {
                var d = catalog.Find(LimestoneKitSetup.Ids[part]);
                var wall = new GameObject("L0" + (part + 1)) { layer = 31 }; wall.transform.SetParent(root.transform);
                wall.AddComponent<MeshFilter>().sharedMesh = d.mesh;
                var renderer = wall.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderers.Add(renderer);
                camera.orthographicSize = 2.3f;
                var centre = d.LocalBounds.center;
                camera.transform.position = centre + new Vector3(4, 2, -8); camera.transform.LookAt(centre);
                var mapped = Capture(camera, target, "L0" + (part + 1) + "-unity-front.png");
                material.SetFloat("_BumpScale", 0);
                var flat = Capture(camera, target, "L0" + (part + 1) + "-unity-normal-disabled.png");
                double difference = 0;
                for (int i = 0; i < flat.Length; i++) difference += Math.Abs(mapped[i].r - flat[i].r) + Math.Abs(mapped[i].g - flat[i].g) + Math.Abs(mapped[i].b - flat[i].b);
                Check(difference / flat.Length > .1, "Normal map made no measurable lighting difference");
                material.SetFloat("_BumpScale", 1);
                camera.transform.position = centre + new Vector3(-4, 2, 8); camera.transform.LookAt(centre);
                light.transform.rotation = Quaternion.Euler(30, 140, 0);
                Capture(camera, target, "L0" + (part + 1) + "-unity-back.png");
                light.transform.rotation = Quaternion.Euler(28, -40, 0);
                wall.transform.position = new Vector3(part * 5, 0, 0); renderer.enabled = false;
            }
            foreach (var renderer in renderers) renderer.enabled = true;
            camera.orthographicSize = 4.1f; camera.transform.position = new Vector3(13, 6, -13); camera.transform.LookAt(new Vector3(6, 1.5f, 0));
            Capture(camera, target, "limestone-unity-overview.png");
            foreach (var renderer in renderers) renderer.enabled = false;
            using var session = new BuildSession(); var frame = session.CreateFrame(Vector3.zero, 1);
            for (int row = 0; row < 2; row++) for (int col = 0; col < 3; col++)
                session.Add(catalog.Find(LimestoneKitSetup.Ids[col]), frame, new Vector3Int(col * 16, row * 12, 0), 0, false);
            var buildingRenderer = new BuildRenderer(session);
            try
            {
                camera.cullingMask = ~0; camera.orthographicSize = 4.5f; var rotation = BuildGeometry.Rotation(1);
                camera.transform.position = rotation * new Vector3(13, 9, -14); camera.transform.LookAt(rotation * new Vector3(5, 3, 0));
                submit = (_, current) => { if (current == camera) buildingRenderer.Draw(camera, 100, 0); };
                RenderPipelineManager.beginCameraRendering += submit;
                Capture(camera, target, "limestone-joined-stacked-instanced.png");
                Check(buildingRenderer.DrawCalls > 0 && buildingRenderer.DrawCalls <= 6, "Placed limestone instanced draw missing");
            }
            finally { if (submit != null) RenderPipelineManager.beginCameraRendering -= submit; }
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality;
            RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientMode = oldMode; RenderSettings.sun = oldSun;
            ShaderUtil.allowAsyncCompilation = oldAsync; RenderTexture.active = oldTarget;
            Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject); Object.DestroyImmediate(material);
            target.Release(); Object.DestroyImmediate(target);
        }
    }

    private static Color32[] Capture(Camera camera, RenderTexture target, string filename)
    {
        for (int i = 0; i < 2; i++) RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            var pixels = image.GetPixels32(); int pink = 0, visible = 0; var background = pixels[0];
            foreach (var pixel in pixels)
            {
                if (pixel.r > 200 && pixel.b > 200 && pixel.g < 50) pink++;
                if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 12) visible++;
            }
            Check(pink < 100 && visible > 1000, "Blank/error render: " + filename);
            File.WriteAllBytes(Output + "/" + filename, image.EncodeToPNG());
            return pixels;
        }
        finally { Object.DestroyImmediate(image); }
    }
}
