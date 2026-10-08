using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Offscreen asset/UI checks only. Does not load a saved world or enter Play mode.</summary>
public static class BuildingPrototypeRenderValidation
{
    private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    [MenuItem("Tools/Building/Render Prototype Checks (synthetic)")]
    public static void Run()
    {
        Directory.CreateDirectory(".utmp/building-prototype");
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        RenderWall(catalog.presets[0]); RenderRoom(catalog); RenderMenu(catalog, 1280, 720); RenderMenu(catalog, 1920, 1080);
        Debug.Log("BUILDING RENDER PASS: textured matte wall authoring pixels, near/far instanced submissions and removal, actual picker at 720p/1080p, contained controls. No live-scene audit or FPS benchmark.");
    }
    private static void RenderRoom(BuildCatalog catalog)
    {
        var session = new BuildSession(); var frame = session.CreateFrame(new Vector3(0,.5f,0), 0);
        var foundation = session.Add(catalog.presets[2], frame, default, 0, true);
        var baseFrame = session.Frame(foundation.OwnFrameId);
        foreach (var p in new[] { new Vector3(2,0,0), new Vector3(0,0,2), new Vector3(2,0,4), new Vector3(4,0,2) })
        {
            var preview = BuildPlacement.Solve(catalog.presets[0], frame.Origin + p, Vector3.up, foundation, baseFrame, 0, 0, default);
            session.Add(preview.Definition, baseFrame, preview.Anchor, preview.YawStep, false);
        }
        foreach (var p in new[] { Vector3.zero, new Vector3(4,0,0), new Vector3(4,0,4), new Vector3(0,0,4) })
        {
            var preview = BuildPlacement.Solve(catalog.presets[3], frame.Origin + p, Vector3.up, foundation, baseFrame, 0, 0, default);
            session.Add(preview.Definition, baseFrame, preview.Anchor, preview.YawStep, false);
        }
        var root = new GameObject("Room authoring fixture");
        var cameraObject = new GameObject("Room authoring camera"); var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 4; camera.aspect = 1.5f;
        camera.transform.position = new Vector3(8,7,-8); camera.transform.LookAt(new Vector3(2,1.25f,2));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f,.19f,.2f); camera.cullingMask = 1 << 31;
        var lightObject = new GameObject("Room light"); var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
        light.intensity = 1.5f; light.shadows = LightShadows.None; light.cullingMask = 1 << 31; lightObject.transform.rotation = Quaternion.Euler(35,-35,0);
        var texture = new RenderTexture(900,600,24,RenderTextureFormat.ARGB32); texture.Create();
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline; var oldTarget = RenderTexture.active;
        try
        {
            int triangles = 0;
            foreach (var piece in session.Pieces.Values)
            {
                var obj = Object.Instantiate(piece.Definition.authoringPrefab, root.transform); obj.layer = 31;
                obj.transform.SetPositionAndRotation(piece.Origin, BuildGeometry.Rotation(piece.WorldYawStep));
                triangles += piece.Definition.mesh.triangles.Length / 3;
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            SavePixels(texture, ".utmp/building-prototype/room.png");
            int objectsBefore = Object.FindObjectsByType<Transform>().Length;
            var renderer = new BuildRenderer(session); renderer.Draw(camera,3000,140);
            int expectedTriangles = (catalog.presets[2].mesh.triangles.Length +
                4 * catalog.presets[0].mesh.triangles.Length + 4 * catalog.presets[3].mesh.triangles.Length) / 3;
            if (triangles != expectedTriangles || renderer.DrawCalls != 3 || Object.FindObjectsByType<Transform>().Length != objectsBefore)
                throw new InvalidOperationException("Nine-piece room geometry/batching changed unexpectedly.");
            Debug.Log($"BUILDING ROOM COST: 9 pieces, {triangles} triangles per geometry pass, 3 spatially grouped instance submissions; no renderer-created objects.");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality; RenderTexture.active = oldTarget;
            Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject); texture.Release(); Object.DestroyImmediate(texture);
        }
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    private static void RenderWall(BuildDefinition wall)
    {
        var cameraObject = new GameObject("Building authoring camera"); var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 2.5f; camera.aspect = 1.5f;
        camera.farClipPlane = 4000;
        camera.transform.position = new Vector3(6, 3.5f, -6); camera.transform.LookAt(wall.LocalBounds.center);
        var target = new RenderTexture(900, 600, 24, RenderTextureFormat.ARGB32); target.Create();
        var command = new CommandBuffer(); var previous = RenderTexture.active; bool async = ShaderUtil.allowAsyncCompilation;
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
        var authoring = Object.Instantiate(wall.authoringPrefab); authoring.layer = 31;
        Action<ScriptableRenderContext, Camera> submitInstances = null;
        var lightObject = new GameObject("Building authoring light"); var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.5f; light.shadows = LightShadows.None;
        light.cullingMask = 1 << 31; lightObject.transform.rotation = Quaternion.Euler(35, -35, 0);
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .19f, .2f);
            camera.cullingMask = 1 << 31;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            int pass = wall.material.FindPass("ForwardLit");
            if (pass < 0) throw new InvalidOperationException("Wall shader has no forward pass.");
            for (int shaderPass = 0; shaderPass < wall.material.passCount; ++shaderPass)
                ShaderUtil.CompilePass(wall.material, shaderPass, true);
            if (ShaderUtil.ShaderHasError(wall.material.shader)) throw new InvalidOperationException("Wall shader failed.");
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            SavePixels(target, ".utmp/building-prototype/wall.png");
            camera.transform.position = new Vector3(-6, 3.5f, 6);
            camera.transform.LookAt(wall.LocalBounds.center);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            SavePixels(target, ".utmp/building-prototype/wall-back.png");
            authoring.GetComponent<MeshRenderer>().enabled = false;
            camera.cullingMask = ~0;
            var session = new BuildSession(); var frame = session.CreateFrame(Vector3.zero, 1);
            session.Add(wall, frame, default, 0, false);
            camera.transform.position = BuildGeometry.Rotation(1) * new Vector3(6, 3.5f, -6);
            camera.transform.LookAt(BuildGeometry.Rotation(1) * wall.LocalBounds.center);
            int before = Object.FindObjectsByType<Transform>().Length;
            var renderer = new BuildRenderer(session); renderer.Draw(camera, 3000, 140);
            if (renderer.DrawCalls != 1 || Object.FindObjectsByType<Transform>().Length != before) throw new InvalidOperationException("Instanced submission created objects or lost visibility.");
            // Submit through the real building path before this camera culls/draws, then inspect its pixels.
            submitInstances = (_, renderingCamera) => { if (renderingCamera == camera) renderer.Draw(camera, 3000, 140); };
            RenderPipelineManager.beginCameraRendering += submitInstances;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            SavePixels(target, ".utmp/building-prototype/wall-instanced.png");
            RenderPipelineManager.beginCameraRendering -= submitInstances;
            submitInstances = null;
            if (ShaderUtil.ShaderHasError(wall.material.shader)) throw new InvalidOperationException("Instanced matte wall shader failed.");
            camera.orthographicSize = 1200;
            camera.transform.position = new Vector3(2, 1.375f, -2000); camera.transform.LookAt(new Vector3(2, 1.375f, 0));
            renderer.Draw(camera, 3000, 140);
            if (renderer.DrawCalls != 1) throw new InvalidOperationException("Distant wall vanished inside the render range.");
            session.Remove(1); renderer.Draw(camera, 3000, 140);
            if (renderer.DrawCalls != 0) throw new InvalidOperationException("Deleted wall remained in distant rendering.");
        }
        finally { if (submitInstances != null) RenderPipelineManager.beginCameraRendering -= submitInstances; GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality; ShaderUtil.allowAsyncCompilation = async; RenderTexture.active = previous; command.Dispose(); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(authoring); Object.DestroyImmediate(lightObject); }
    }
    private static void RenderMenu(BuildCatalog catalog, int width, int height)
    {
        var clone = Object.Instantiate(catalog); clone.panel = Object.Instantiate(catalog.panel);
        var root = new GameObject("Building picker fixture"); var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); target.Create();
        var previous = RenderTexture.active; BuildMenuView view = null;
        try
        {
            clone.panel.targetTexture = target; clone.panel.clearColor = true; clone.panel.colorClearValue = new Color(.16f, .19f, .2f);
            view = new BuildMenuView(clone, root.transform, _ => { }); view.SetState(true, true);
            var document = root.GetComponentInChildren<UIDocument>(); var panel = document.rootVisualElement.panel;
            typeof(PanelSettings).GetMethod("ApplyPanelSettings", Methods).Invoke(clone.panel, null);
            for (int i = 0; i < 5; i++)
            {
                panel.GetType().GetMethod("Update", Methods).Invoke(panel, null);
                panel.GetType().GetMethod("ValidateLayout", Methods).Invoke(panel, null);
                panel.GetType().GetMethod("Repaint", Methods, null, new[] { typeof(Event) }, null).Invoke(panel, new object[] { new Event { type = EventType.Repaint } });
                panel.GetType().GetMethod("Render", Methods).Invoke(panel, null);
            }
            var menu = document.rootVisualElement.Q<VisualElement>("build-menu");
            if (menu.worldBound.width < 200 || !float.IsFinite(menu.worldBound.height)) throw new InvalidOperationException("Menu layout did not resolve.");
            foreach (var button in menu.Query<Button>().ToList())
                if (!menu.worldBound.Contains(button.worldBound.min) || !menu.worldBound.Contains(button.worldBound.max)) throw new InvalidOperationException("Preset button overflow.");
            SavePixels(target, $".utmp/building-prototype/menu-{width}x{height}.png");
        }
        finally { RenderTexture.active = previous; view?.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(clone.panel); Object.DestroyImmediate(clone); target.Release(); Object.DestroyImmediate(target); }
    }
    private static void SavePixels(RenderTexture target, string path)
    {
        RenderTexture.active = target; var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); var data = pixels.GetPixels32();
            int changed = 0; var background = data[0];
            foreach (var p in data) if (Math.Abs(p.r - background.r) + Math.Abs(p.g - background.g) + Math.Abs(p.b - background.b) > 30) changed++;
            if (changed < 1000) throw new InvalidOperationException("Blank render: " + path);
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(pixels); }
    }
}
