using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SandstoneKitValidation
{
    private const string Pending = "FS.Sandstone.Validation";
    private static int checks, walks;
    static SandstoneKitValidation() => EditorApplication.playModeStateChanged += State;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Near(Vector3 a, Vector3 b, string message) => Check((a - b).sqrMagnitude < .000004f, message + ": " + a + " / " + b);
    private static BuildDefinition Definition(SandstoneKitSetup.Part p) => AssetDatabase.LoadAssetAtPath<BuildDefinition>(BuildingPrototypeSetup.Folder + "/" + p.slug + ".asset");

    public static void InstallAndRunBatch()
    {
        try
        {
            checks = 0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var c = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            var originals = new Dictionary<string, string>();
            foreach (var d in c.presets)
                if (!d.contentId.StartsWith("build.stone.ss", StringComparison.Ordinal)) originals[AssetDatabase.GetAssetPath(d)] = File.ReadAllText(AssetDatabase.GetAssetPath(d));
            SandstoneKitSetup.Install(); int count = c.presets.Length;
            var guids = new Dictionary<string, string>();
            foreach (var p in SandstoneKitSetup.Source().parts) guids[p.slug] = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Definition(p)));
            SandstoneKitSetup.Install(); Check(c.presets.Length == count, "Repeat install changed catalog length");
            foreach (var p in SandstoneKitSetup.Source().parts)
                Check(guids[p.slug] == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Definition(p))), "Repeat install changed GUID");
            foreach (var pair in originals) Check(File.ReadAllText(pair.Key) == pair.Value, "Existing definition changed: " + pair.Key);
            Assets(c); Render(c);
            SessionState.SetInt(Pending + ".checks", checks); SessionState.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void Assets(BuildCatalog c)
    {
        var source = SandstoneKitSetup.Source(); Check(source.parts.Length == 12 && source.trianglesSelectable == 180, "Geometry budget changed");
        var ids = new HashSet<string>(); int choices = 0;
        foreach (var d in c.presets) Check(d && ids.Add(d.contentId), "Null/duplicate catalog entry");
        choices=SandstoneKitSetup.PresentCount(c);
        Check(choices == 11, "Expected eleven sandstone menu choices");
        Material shared = null;
        foreach (var p in source.parts)
        {
            var d = Definition(p); Check(d && d.mesh && d.material && d.authoringPrefab, "Missing sandstone asset: " + p.tag);
            if (shared == null) shared = d.material;
            Check(d.material == shared && shared.enableInstancing && shared.shader.name == "Custom/BakedStoneWallMatte", "Shared matte material missing");
            Near(d.LocalBounds.size, new Vector3(p.size[0], p.size[1], p.size[2]), "Logical dimensions");
            Near(d.mesh.bounds.min, d.LocalBounds.min, "Visual minimum"); Near(d.mesh.bounds.max, d.LocalBounds.max, "Visual maximum");
            Check(d.mesh.triangles.Length / 3 == p.triangles && d.mesh.subMeshCount == 1, "Runtime geometry grew: " + p.tag);
            Check(d.preserveAuthoredMesh && d.jointCoverVariants.Length == 0 && d.uvVariants.Length == 1, "Automatic geometry or missing UV representation");
            var prefab = d.authoringPrefab;
            Check(prefab.GetComponent<MeshFilter>().sharedMesh == d.mesh && prefab.GetComponent<MeshRenderer>().sharedMaterial == shared, "Prefab render references");
            Check(prefab.GetComponents<Collider>().Length == p.solids.Length, "Prefab solid decomposition");
            Check(d.occupiedVolumes.Length + d.solidBoxes.Length == p.solids.Length, "Pooled solid decomposition");
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/" + BuildUiContent.ThumbnailKey(d)), "Missing icon");
            Check(!BuildUiContent.Description(d).StartsWith("Choose this component", StringComparison.Ordinal), "Missing description");
            var v = d.mesh.vertices; var n = d.mesh.normals; var uv = d.mesh.uv; var tangents = d.mesh.tangents; var t = d.mesh.triangles;
            Check(v.Length == n.Length && v.Length == uv.Length && v.Length == tangents.Length, "Vertex attributes missing");
            for (int i = 0; i < v.Length; i++)
            {
                Check(float.IsFinite(uv[i].x) && float.IsFinite(uv[i].y) && uv[i].x >= 0 && uv[i].x <= 1 && uv[i].y >= 0 && uv[i].y <= 1, "Invalid atlas UV");
                Vector3 tangent = tangents[i];
                Check(Mathf.Abs(n[i].magnitude - 1) < .003f && Mathf.Abs(tangent.magnitude - 1) < .003f && Mathf.Abs(Vector3.Dot(n[i], tangent)) < .003f, "Invalid tangent basis");
            }
            for (int i = 0; i < t.Length; i += 3)
            {
                var cross = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                Check(cross.sqrMagnitude > 1e-13f && Vector3.Dot(cross, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) > 0, "Inverted/degenerate visible triangle");
                var a = uv[t[i + 1]] - uv[t[i]]; var b = uv[t[i + 2]] - uv[t[i]];
                Check(Mathf.Abs(a.x * b.y - a.y * b.x) > 1e-12f, "Collapsed UV triangle");
            }
            foreach (var volume in d.occupiedVolumes)
            {
                var hv = volume.vertices; var ht = volume.mesh.triangles; float signed = 0;
                var edges = new Dictionary<string, int>();
                for (int i = 0; i < ht.Length; i += 3)
                {
                    signed += Vector3.Dot(hv[ht[i]], Vector3.Cross(hv[ht[i + 1]], hv[ht[i + 2]])) / 6;
                    var normal = Vector3.Cross(hv[ht[i + 1]] - hv[ht[i]], hv[ht[i + 2]] - hv[ht[i]]).normalized;
                    foreach (var point in hv) Check(Vector3.Dot(normal, point - hv[ht[i]]) < .0001f, "Non-convex occupied hull");
                    foreach (var edge in new[] { (ht[i], ht[i + 1]), (ht[i + 1], ht[i + 2]), (ht[i + 2], ht[i]) })
                    { string key = Math.Min(edge.Item1, edge.Item2) + "/" + Math.Max(edge.Item1, edge.Item2); edges.TryGetValue(key, out int amount); edges[key] = amount + 1; }
                }
                Check(signed > 0, "Inverted hull"); foreach (int amount in edges.Values) Check(amount == 2, "Open occupied hull");
            }
            // Independent triangle ray intersections catch render holes even if colliders are correct.
            if (p.tag == "SS07") { Check(!RayMesh(d.mesh, new Vector3(2, 1, -2), Vector3.forward, out _), "Visible doorway filled"); Check(RayMesh(d.mesh, new Vector3(2, 2.5f, -2), Vector3.forward, out _), "Visible lintel absent"); }
            if (p.tag == "SS08") Check(!RayMesh(d.mesh, new Vector3(2, 1.75f, -2), Vector3.forward, out _), "Visible window filled");
            if (p.tag == "SS09") { Check(RayMesh(d.mesh, new Vector3(2, 1.375f, 2), Vector3.back, out var depth), "Visible niche back absent"); Check(Mathf.Abs(depth - 2) < .001f, "Visible niche depth"); Check(d.wallOpenings.Length == 0, "Blind niche labelled as a through opening"); }
            if (d.IsHalfGable) Check(d.flipVariant && d.flipVariant.flipVariant == d && d.flipVariant.gableSlope != d.gableSlope && BuildUiContent.Category(d) == 6, "Gable flip/category missing");
        }
        foreach (string path in new[] { SandstoneKitSetup.Colour, SandstoneKitSetup.Normal })
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            bool normal = path == SandstoneKitSetup.Normal;
            Check(ti && ti.mipmapEnabled && ti.wrapMode == TextureWrapMode.Clamp && ti.anisoLevel == 4 && ti.sRGBTexture != normal, "Texture sampling/colour space");
            Check(ti.GetPlatformTextureSettings("Standalone").format == (normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7), "Texture format");
        }
    }

    private static bool RayMesh(Mesh mesh, Vector3 origin, Vector3 direction, out float distance)
    {
        distance = float.PositiveInfinity; var v = mesh.vertices; var t = mesh.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 e1 = v[t[i + 1]] - v[t[i]], e2 = v[t[i + 2]] - v[t[i]], p = Vector3.Cross(direction, e2);
            float determinant = Vector3.Dot(e1, p); if (Mathf.Abs(determinant) < 1e-8f) continue;
            Vector3 q = origin - v[t[i]]; float u = Vector3.Dot(q, p) / determinant; if (u < 0 || u > 1) continue;
            Vector3 cross = Vector3.Cross(q, e1); float w = Vector3.Dot(direction, cross) / determinant; if (w < 0 || u + w > 1) continue;
            float hit = Vector3.Dot(e2, cross) / determinant; if (hit >= 0) distance = Mathf.Min(distance, hit);
        }
        return float.IsFinite(distance);
    }

    private static void State(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); checks = SessionState.GetInt(Pending + ".checks", 0); walks = 0;
        try
        {
            var c = Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            for (int yaw = 0; yaw < 8; yaw++) Runtime(c, yaw);
            File.WriteAllText(SandstoneKitSetup.Art + "/unity-validation.json", "{\"passed\":true,\"checks\":" + checks + ",\"parts\":11,\"representations\":12,\"headings\":8,\"doorWalks\":" + walks + ",\"triangles\":180,\"urpRenders\":true}");
            Debug.Log("SANDSTONE PASS: " + checks + " checks; eleven choices, 180 triangles, eight headings, real splayed window, solid-backed niche and " + walks + " doorway walks.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void Runtime(BuildCatalog c, int yaw)
    {
        var host = new GameObject("Sandstone placement"); var world = host.AddComponent<BuildWorld>(); world.enabled = false;
        var q = BuildGeometry.Rotation(yaw); var origin = new Vector3(1000 + yaw * 40, 50, 1000);
        try
        {
            foreach (var p in SandstoneKitSetup.Source().parts)
            {
                var d = Definition(p);
                var aim = origin + q * new Vector3(d.LocalBounds.center.x, d.LocalBounds.min.y, d.LocalBounds.center.z);
                var preview = BuildPlacement.Solve(d, aim, Vector3.up, null, null, yaw, 0, default);
                world.Validate(ref preview); Check(preview.Valid, "Free placement failed: " + p.tag);
                var piece = world.Commit(preview); Check(piece != null && piece.Resolved.mesh == d.mesh, "Authored mesh lost on commit");
                Vector3 at = piece.Origin;
                using (var gameplay = new BuildGameplay(world.Session))
                {
                    gameplay.Update(at, 32, 40, 32); Physics.SyncTransforms();
                    Check(gameplay.TryGetProxy(piece.Id, out _), "Pooled collision missing");
                    bool Hit(Vector3 from, Vector3 dir, out RaycastHit hit) => Physics.Raycast(at + q * from, q * dir, out hit, 5, 1 << GameplayLayers.WorldSolid, QueryTriggerInteraction.Ignore);
                    if (p.tag == "SS07")
                    {
                        foreach (float x in new[] { 1.45f, 2, 2.55f }) foreach (float y in new[] { .1f, 1, 2.15f }) Check(!Hit(new Vector3(x, y, -2), Vector3.forward, out _), "Door collider fills opening");
                        Check(Hit(new Vector3(2, 2.5f, -2), Vector3.forward, out _), "Lintel collider missing");
                        Check(Hit(new Vector3(.5f, 1, -2), Vector3.forward, out _), "Jamb collider missing");
                        Walk(piece, q, false); Walk(piece, q, true);
                    }
                    else if (p.tag == "SS08")
                    {
                        Check(!Hit(new Vector3(2, 1.75f, -2), Vector3.forward, out _), "Window collider fills opening");
                        foreach (float z in new[] { -.2f, .2f })
                        {
                            Check(Hit(new Vector3(2, 1.75f, z), Vector3.right, out var reveal), "Splayed jamb missing");
                            Near(reveal.point, at + q * new Vector3(2.25f + .5f * (z + .25f), 1.75f, z), "Splayed collision profile");
                        }
                        Check(Hit(new Vector3(2, 1, -2), Vector3.forward, out _), "Window sill wall missing");
                    }
                    else if (p.tag == "SS09")
                    {
                        Check(Hit(new Vector3(2, 1.375f, 2), Vector3.back, out var back), "Niche backing collision missing");
                        Near(back.point, at + q * new Vector3(2, 1.375f, 0), "Niche depth must be 0.25 m");
                        Check(Hit(new Vector3(2, 1.375f, -2), Vector3.forward, out var exterior), "Niche became a through window");
                        Near(exterior.point, at + q * new Vector3(2, 1.375f, -.25f), "Niche exterior backing");
                    }
                    else if (d.IsHalfGable)
                    {
                        bool falling = d.gableSlope == GableSlope.Falling;
                        Check(!Hit(new Vector3(falling ? 1.5f : .5f, 1.5f, -2), Vector3.forward, out _), "Gable empty half filled");
                        Check(Hit(new Vector3(falling ? .5f : 1.5f, .5f, -2), Vector3.forward, out _), "Gable solid half missing");
                    }
                    else
                    {
                        Vector3 centre = d.LocalBounds.center;
                        Check(Hit(centre + Vector3.up * 4, Vector3.down, out var top), "Solid piece collision missing");
                        Near(top.point, at + q * new Vector3(centre.x, d.LocalBounds.max.y, centre.z), "Top datum incorrect");
                    }
                }
                Check(world.Remove(piece.Id), "Removal failed");
            }
            var wall = c.Find("build.stone.ss01-bedded-sandstone");
            var baseWall = world.Session.Add(wall, world.Session.CreateFrame(origin, yaw), default, 0, false);
            var stack = BuildPlacement.Solve(wall, origin + q * new Vector3(2, 3, 0), Vector3.up, baseWall, world.Session.Frame(baseWall.OwnFrameId), yaw, 0, default);
            world.Validate(ref stack); Check(stack.Valid, "Wall stacking rejected"); Near(stack.Origin, origin + Vector3.up * 3, "3 m sandstone storey datum");
            foreach (var d in new[] { c.Find("build.stone.ss11-sandstone-gable-rising"), c.Find("build.stone.ss11-sandstone-gable-rising").flipVariant })
            {
                bool falling = d.gableSlope == GableSlope.Falling;
                var gable = BuildPlacement.Solve(d, origin + q * new Vector3(falling ? 3 : 1, 2.9f, 0), q * Vector3.forward, baseWall, world.Session.Frame(baseWall.OwnFrameId), yaw, 0, default);
                world.Validate(ref gable); Check(gable.Valid, "Gable placement rejected");
                Near(gable.Origin, origin + q * new Vector3(falling ? 2 : 0, 3, 0), "Paired gable roof datum");
            }
        }
        finally { Object.DestroyImmediate(host); Physics.SyncTransforms(); }
    }

    private static void Walk(BuildPieceRecord piece, Quaternion q, bool reverse)
    {
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cube); pad.layer = GameplayLayers.WorldSolid;
        pad.transform.SetPositionAndRotation(piece.Origin + q * new Vector3(2, -.125f, 0), q); pad.transform.localScale = new Vector3(6, .25f, 6);
        var rig = new GameObject("Sandstone doorway traversal"); var cc = rig.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.center = new Vector3(0, .9f, 0); cc.radius = .4f; cc.skinWidth = .08f; cc.stepOffset = .3f; cc.minMoveDistance = 0;
        rig.transform.SetPositionAndRotation(piece.Origin + q * new Vector3(2, .08f, reverse ? 1.5f : -1.5f), q);
        var motor = rig.AddComponent<CharacterMotor>(); typeof(CharacterMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(motor, null); Physics.SyncTransforms();
        try
        {
            for (int i = 0; i < 20; i++) motor.Simulate(default, 1f / 60);
            for (int i = 0; i < 52; i++) motor.Simulate(new CharacterMoveCommand(new Vector2(0, reverse ? -1 : 1), false, false, false), 1f / 60);
            var local = Quaternion.Inverse(q) * (rig.transform.position - piece.Origin);
            Check(reverse ? local.z < -.8f : local.z > .8f, "Character stuck in sandstone doorway");
            Check(Mathf.Abs(local.y) < .15f, "Doorway changed walking height"); walks++;
        }
        finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(pad); Physics.SyncTransforms(); }
    }

    private static void Render(BuildCatalog c)
    {
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
        var oldAmbient = RenderSettings.ambientLight; var oldMode = RenderSettings.ambientMode; var oldSun = RenderSettings.sun;
        bool oldAsync = ShaderUtil.allowAsyncCompilation; var previous = RenderTexture.active;
        var root = new GameObject("Sandstone render"); var cameraObj = new GameObject("Sandstone camera"); var lightObj = new GameObject("Sandstone sun");
        var cam = cameraObj.AddComponent<Camera>(); cam.enabled = false; cam.orthographic = true; cam.aspect = 1.6f;
        cam.nearClipPlane = .01f; cam.farClipPlane = 200; cam.cullingMask = 1 << 31;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .20f, .21f);
        cameraObj.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var light = lightObj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f; light.shadows = LightShadows.None;
        light.cullingMask = 1 << 31; light.transform.rotation = Quaternion.Euler(40, -35, 0);
        var target = new RenderTexture(1920, 1200, 24, RenderTextureFormat.ARGB32); target.Create();
        Action<ScriptableRenderContext, Camera> submit = null;
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Check(pipeline, "URP pipeline missing"); GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.4f, .4f, .4f); RenderSettings.sun = light; ShaderUtil.allowAsyncCompilation = false;
            var material = c.Find("build.stone.ss01-bedded-sandstone").material;
            for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
            Check(!ShaderUtil.ShaderHasError(material.shader), "Shader compilation failed");
            var objects = new List<GameObject>();
            foreach (var p in SandstoneKitSetup.Source().parts)
            {
                var d = Definition(p); var ob = new GameObject(p.tag) { layer = 31 }; ob.transform.SetParent(root.transform);
                ob.AddComponent<MeshFilter>().sharedMesh = d.mesh; ob.AddComponent<MeshRenderer>().sharedMaterial = material; objects.Add(ob);
                cam.orthographicSize = Mathf.Max(1.2f, d.LocalBounds.size.magnitude * .43f);
                foreach (bool back in new[] { false, true })
                {
                    cam.transform.position = d.mesh.bounds.center + (back ? new Vector3(-4, 2, 8) : new Vector3(4, 2, -8)); cam.transform.LookAt(d.mesh.bounds.center);
                    Capture(cam, target, p.tag + (back ? "-unity-back.png" : "-unity-front.png"));
                }
                ob.SetActive(false);
            }
            for (int i = 0; i < objects.Count; i++) { objects[i].transform.position = new Vector3(i % 4 * 6, 0, i / 4 * 6); objects[i].SetActive(true); }
            cam.orthographicSize = 12; cam.transform.position = new Vector3(31, 29, -31); cam.transform.LookAt(new Vector3(10, 1, 7));
            Capture(cam, target, "sandstone-unity-overview.png"); foreach (var ob in objects) ob.SetActive(false);
            using var session = new BuildSession(); var frame = session.CreateFrame(Vector3.zero, 0); int index = 0;
            foreach (var p in SandstoneKitSetup.Source().parts)
            { if (p.slope == "falling") continue; session.Add(c.Find(SandstoneKitSetup.Id(p.slug)), frame, new Vector3Int(index % 4 * 24, 0, index / 4 * 24), 0, false); index++; }
            var renderer = new BuildRenderer(session); cam.cullingMask = ~0;
            submit = (_, camera) => { if (camera == cam) renderer.Draw(cam, 150, 0); };
            RenderPipelineManager.beginCameraRendering += submit; Capture(cam, target, "sandstone-instanced.png");
            Check(renderer.DrawCalls > 0 && renderer.DrawCalls <= 11, "Instanced rendering failed");
        }
        finally
        {
            if (submit != null) RenderPipelineManager.beginCameraRendering -= submit;
            GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality;
            RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientMode = oldMode; RenderSettings.sun = oldSun;
            ShaderUtil.allowAsyncCompilation = oldAsync; RenderTexture.active = previous;
            Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObj); Object.DestroyImmediate(lightObj); target.Release(); Object.DestroyImmediate(target);
        }
    }

    private static void Capture(Camera camera, RenderTexture target, string filename)
    {
        for (int i = 0; i < 2; i++) RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            var pixels = image.GetPixels32(); var bg = pixels[0]; int visible = 0, pink = 0;
            foreach (var p in pixels) { if (Math.Abs(p.r - bg.r) + Math.Abs(p.g - bg.g) + Math.Abs(p.b - bg.b) > 12) visible++; if (p.r > 200 && p.b > 200 && p.g < 50) pink++; }
            Check(visible > 1000 && pink < 100, "Blank/error render: " + filename);
            File.WriteAllBytes(SandstoneKitSetup.Art + "/" + filename, image.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(image); }
    }
}
