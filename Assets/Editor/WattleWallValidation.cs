using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>GPU cutout/pass checks and real off-screen instanced weave shadows in an empty isolated scene.</summary>
public static class WattleWallValidation
{
    private const string Output = ".utmp/building-prototype";
    private static ShadowFixture shadowFixture;
    private static int stage, frames;
    private static readonly float[] shadowMeans = new float[4];
    private static readonly string[] ShadowNames = { "no-caster", "opaque-caster", "cutout-caster", "reversed-cutout-caster" };

    public static void RunPreviewBatch()
    {
        try { ValidatePreview(); Debug.Log("WATTLE PREVIEW PASS: green/red weave visible from both sides at all eight headings; shared preview assets unchanged."); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    private static void ValidatePreview()
    {
        Directory.CreateDirectory(Output);
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        var wall = catalog.Find(WattleWallSetup.ContentId);
        var host = new GameObject("Wattle preview material fixture");
        var controller = host.AddComponent<BuildingController>(); var world = host.GetComponent<BuildWorld>();
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        if (world.Session == null) typeof(BuildWorld).GetMethod("Awake",fields).Invoke(world,null);
        typeof(BuildingController).GetField("world",fields).SetValue(controller,world);
        typeof(BuildingController).GetField("selected",fields).SetValue(controller,wall);
        var resolve = typeof(BuildingController).GetMethod("GetPreviewMaterial",fields);
        var cameraObject = new GameObject("Wattle preview GPU camera"); var camera = cameraObject.AddComponent<Camera>(); camera.enabled=false;
        camera.orthographic=true; camera.orthographicSize=1.6f; camera.aspect=1.5f; camera.nearClipPlane=.1f; camera.farClipPlane=20;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.magenta;
        var target = new RenderTexture(900,600,24,RenderTextureFormat.ARGB32); target.Create();
        var oldPipeline=GraphicsSettings.defaultRenderPipeline; var oldQuality=QualitySettings.renderPipeline;
        bool oldAsync=ShaderUtil.allowAsyncCompilation;
        int missing=0, legacyMissing=0;
        var previews=new Material[2];
        try
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=pipeline; ShaderUtil.allowAsyncCompilation=false;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
            foreach (bool valid in new[] { true,false })
            {
                var shared=valid ? catalog.validPreview : catalog.invalidPreview;
                float originalCull=shared.GetFloat("_Cull");
                var material=(Material)resolve.Invoke(controller,new object[] { valid });
                previews[valid ? 0 : 1]=material;
                if ((Material)resolve.Invoke(controller,new object[] { valid }) != material) throw new InvalidOperationException("Preview allocated a new material on each frame.");
                for (int yaw=0; yaw<8; yaw++)
                foreach (bool back in new[] { false,true })
                {
                    Quaternion rotation=BuildGeometry.Rotation(yaw);
                    camera.transform.position=rotation * new Vector3(1.75f,1.375f,back ? 6 : -6);
                    camera.transform.LookAt(rotation * wall.LocalBounds.center);
                    var matrix=Matrix4x4.TRS(Vector3.zero,rotation,Vector3.one);
                    int legacy=Draw(shared,matrix,yaw,back,valid,"legacy");
                    int chosen=Draw(material,matrix,yaw,back,valid,"chosen");
                    if (legacy<450) legacyMissing++;
                    if (chosen<450) missing++;
                    Debug.Log($"WATTLE PREVIEW COVERAGE: valid={valid}, yaw={yaw}, back={back}, legacy={legacy}/484, chosen={chosen}/484");
                }
                if (shared.GetFloat("_Cull") != originalCull) throw new InvalidOperationException("Shared preview asset was changed.");
            }
            Debug.Log($"WATTLE PREVIEW RESULT: legacy missing in {legacyMissing}/32 views; chosen missing in {missing}/32 views.");
            if (missing!=0) throw new InvalidOperationException("Wattle preview loses its weave from some view directions.");
            typeof(BuildingController).GetField("selected",fields).SetValue(controller,catalog.presets[0]);
            if ((Material)resolve.Invoke(controller,new object[] { true }) != catalog.validPreview)
                throw new InvalidOperationException("Opaque full-wall preview material changed unnecessarily.");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline=oldPipeline; QualitySettings.renderPipeline=oldQuality; ShaderUtil.allowAsyncCompilation=oldAsync;
            // Edit-mode fixtures do not run MonoBehaviour lifecycle callbacks unless activated in Play mode.
            typeof(BuildingController).GetMethod("OnDestroy",fields).Invoke(controller,null);
            Object.DestroyImmediate(host); Object.DestroyImmediate(cameraObject); target.Release(); Object.DestroyImmediate(target);
        }
        if (previews[0]!=null || previews[1]!=null) throw new InvalidOperationException("Controller destruction leaked cached preview materials.");
        int Draw(Material material, Matrix4x4 matrix, int yaw, bool back, bool valid, string mode)
        {
            int pass=material.FindPass("ForwardLit");
            if (pass<0) throw new InvalidOperationException("Preview shader has no forward pass.");
            ShaderUtil.CompilePass(material,pass,true);
            using var cmd=new CommandBuffer(); cmd.SetRenderTarget(target); cmd.ClearRenderTarget(true,true,Color.magenta);
            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,camera.projectionMatrix);
            cmd.DrawMesh(wall.mesh,matrix,material,0,pass); Graphics.ExecuteCommandBuffer(cmd);
            var pixels=Read(target);
            try
            {
                if (yaw==0) File.WriteAllBytes($"{Output}/wattle-preview-{mode}-{(valid ? "green" : "red")}-{(back ? "back" : "front")}.png",pixels.EncodeToPNG());
                int count=0; var colors=pixels.GetPixels32();
                foreach (float x in new[] { .875f,2.625f }) foreach (float y in new[] { .75f,2.125f })
                {
                    Vector3 p=camera.WorldToViewportPoint(matrix.MultiplyPoint3x4(new Vector3(x,y,.125f)));
                    int sx=Mathf.RoundToInt(p.x*target.width), sy=Mathf.RoundToInt(p.y*target.height);
                    for (int dy=-5; dy<=5; dy++) for (int dx=-5; dx<=5; dx++)
                    {
                        Color32 color=colors[(sy+dy)*target.width+sx+dx];
                        if (Math.Abs(color.r-255)+color.g+Math.Abs(color.b-255)>30) count++;
                    }
                }
                return count;
            }
            finally { Object.DestroyImmediate(pixels); }
        }
    }

    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildingPrototypeSetup.CreateAssets();
            BuildingPrototypeValidation.Run();
            var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            var before = (BuildDefinition[])catalog.presets.Clone();
            WattleWallSetup.Rebuild(); WattleWallSetup.Rebuild();
            if (catalog.presets.Length != before.Length) throw new InvalidOperationException("Wattle rebuild duplicated a picker entry.");
            for (int i = 0; i < before.Length; ++i)
                if (catalog.presets[i] != before[i]) throw new InvalidOperationException("Wattle rebuild changed catalog order/identity.");
            BuildingPrototypeRenderValidation.Run();
            shadowFixture = new ShadowFixture(catalog.Find(WattleWallSetup.ContentId));
            stage = frames = 0; EditorApplication.update += Tick;
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void Tick()
    {
        try
        {
            shadowFixture.Configure(stage); shadowFixture.Render();
            if (++frames < 4) return;
            shadowMeans[stage] = shadowFixture.Capture(ShadowNames[stage]);
            Debug.Log($"WATTLE SHADOW SAMPLE: {ShadowNames[stage]}, mean={shadowMeans[stage]:F4}, casterInView={shadowFixture.InView}, submissions={shadowFixture.Renderer.DrawCalls}");
            if (++stage < ShadowNames.Length) { frames = 0; return; }
            float contrast = shadowMeans[0] - shadowMeans[1];
            if (contrast < .08f) throw new InvalidOperationException("Shadow fixture has no useful opaque-caster contrast.");
            if (shadowMeans[2] < shadowMeans[1] + contrast*.07f || shadowMeans[2] > shadowMeans[0] - contrast*.10f)
                throw new InvalidOperationException("The actual instanced cutout shadow did not transmit light through weave holes.");
            if (Mathf.Abs(shadowMeans[2]-shadowMeans[3]) > .02f)
                throw new InvalidOperationException("Reversing the sheet changed its two-sided cutout shadows.");
            shadowFixture.Dispose(); shadowFixture = null;
            ValidatePasses(AssetDatabase.LoadAssetAtPath<BuildDefinition>(WattleWallSetup.DefinitionPath));
            Debug.Log("WATTLE GPU PASS: forward/back visibility, matching forward/shadow/depth/depth-normal alpha masks, back-face normals, real instanced submissions, opaque rails and two-sided off-screen cutout shadows verified.");
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void Finish(Exception exception)
    {
        EditorApplication.update -= Tick;
        shadowFixture?.Dispose(); shadowFixture = null;
        if (exception != null) Debug.LogException(exception);
        EditorApplication.Exit(exception == null ? 0 : 1);
    }

    private static void ValidatePasses(BuildDefinition wall)
    {
        Directory.CreateDirectory(Output);
        var host = new GameObject("Wattle direct-pass camera"); var camera = host.AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 1.6f; camera.aspect = 1.5f; camera.nearClipPlane = .1f; camera.farClipPlane = 20;
        var target = new RenderTexture(900,600,24,RenderTextureFormat.ARGB32); target.Create();
        var opaque = new Material(wall.material); opaque.SetFloat("_AlphaClip",0); opaque.DisableKeyword("_ALPHATEST_ON");
        var reveal = new Material(Shader.Find("Hidden/Building/WattleDepthReveal"));
        reveal.SetColor("_Color",Color.green);
        var background = new Mesh();
        background.vertices = new[] { Vector3.zero, new Vector3(3.5f,0,0), new Vector3(3.5f,2.75f,0), new Vector3(0,2.75f,0) };
        background.triangles = new[] { 0,1,2,0,2,3 }; background.colors = new[] { Color.white,Color.white,Color.white,Color.white };
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
        bool oldAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline; ShaderUtil.allowAsyncCompilation = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.magenta;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            for (int pass = 0; pass < wall.material.passCount; ++pass)
            { ShaderUtil.CompilePass(wall.material,pass,true); ShaderUtil.CompilePass(opaque,pass,true); }
            if (ShaderUtil.ShaderHasError(wall.material.shader)) throw new InvalidOperationException("Matte cutout shader has compile errors.");
            foreach (bool back in new[] { false,true })
            {
                camera.transform.position = new Vector3(1.75f,1.375f,back ? 6 : -6);
                camera.transform.LookAt(new Vector3(1.75f,1.375f,.125f));
                bool[] reference = null;
                foreach (string passName in new[] { "ForwardLit","DepthOnly","DepthNormals","ShadowCaster" })
                {
                    bool[] solid = DrawPass(opaque,passName,back);
                    bool[] cut = DrawPass(wall.material,passName,back);
                    int solidCount = 0, holes = 0, mismatch = 0;
                    for (int i = 0; i < cut.Length; ++i)
                    {
                        if (solid[i]) { solidCount++; if (!cut[i]) holes++; }
                        if (reference != null && cut[i] != reference[i]) mismatch++;
                    }
                    if (solidCount < 100000 || holes < solidCount*.10f || holes > solidCount*.35f)
                        throw new InvalidOperationException($"{passName} {(back ? "back" : "front")} failed visible cutout coverage: solid={solidCount}, holes={holes}.");
                    if (reference == null) reference = cut;
                    else if (mismatch > solidCount*.025f) throw new InvalidOperationException($"{passName} cutout mask differs from forward by {mismatch} pixels.");
                    Debug.Log($"WATTLE PASS MASK: {passName}, back={back}, solid={solidCount}, holes={holes}, mismatch={mismatch}");
                }
            }
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality; ShaderUtil.allowAsyncCompilation = oldAsync;
            Object.DestroyImmediate(host); Object.DestroyImmediate(opaque); Object.DestroyImmediate(reveal); Object.DestroyImmediate(background);
            target.Release(); Object.DestroyImmediate(target);
        }

        bool[] DrawPass(Material material, string passName, bool back)
        {
            int pass = material.FindPass(passName);
            if (pass < 0) throw new InvalidOperationException("Missing wood pass: " + passName);
            bool depthOnly = passName == "DepthOnly" || passName == "ShadowCaster";
            using var cmd = new CommandBuffer();
            cmd.SetRenderTarget(target); cmd.ClearRenderTarget(true,true,Color.magenta);
            // As in URP ScriptableRenderer.SetCameraMatrices, this API expects
            // the non-GPU projection and performs device conversion itself.
            // Passing a GPU-adjusted projection here reverses depth twice.
            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,camera.projectionMatrix);
            cmd.SetGlobalVector("_ShadowBias",Vector4.zero);
            cmd.SetGlobalVector("_LightDirection",back ? Vector3.forward : Vector3.back);
            cmd.DrawMesh(wall.mesh,Matrix4x4.identity,material,0,pass);
            if (depthOnly) cmd.DrawMesh(background,Matrix4x4.Translate(new Vector3(0,0,back ? -.75f : .75f)),reveal,0,0);
            Graphics.ExecuteCommandBuffer(cmd);
            var pixels = Read(target);
            try
            {
                string suffix = back ? "back" : "front";
                string mode = material == opaque ? "solid" : "cutout";
                File.WriteAllBytes($"{Output}/wattle-{passName}-{suffix}-{mode}.png",pixels.EncodeToPNG());
                var colors = pixels.GetPixels32(); var mask = new bool[colors.Length];
                for (int i = 0; i < colors.Length; ++i)
                {
                    bool magenta = colors[i].r > 220 && colors[i].b > 220 && colors[i].g < 25;
                    mask[i] = depthOnly ? magenta : !magenta;
                }
                // Depth-reveal coverage outside the actual wall must not count the clear background.
                Vector3 corner0 = camera.WorldToViewportPoint(Vector3.zero), corner1 = camera.WorldToViewportPoint(new Vector3(3.5f,2.75f,0));
                int x0 = Mathf.CeilToInt(Mathf.Min(corner0.x,corner1.x)*target.width)+2, x1 = Mathf.FloorToInt(Mathf.Max(corner0.x,corner1.x)*target.width)-2;
                int y0 = Mathf.CeilToInt(Mathf.Min(corner0.y,corner1.y)*target.height)+2, y1 = Mathf.FloorToInt(Mathf.Max(corner0.y,corner1.y)*target.height)-2;
                for (int y = 0; y < target.height; ++y)
                    for (int x = 0; x < target.width; ++x) if (x < x0 || x > x1 || y < y0 || y > y1) mask[y*target.width+x] = false;
                if (passName == "DepthNormals" && material != opaque && !Shader.IsKeywordEnabled("_GBUFFER_NORMALS_OCT"))
                {
                    double blue = 0; int samples = 0;
                    for (int i = 0; i < mask.Length; ++i) if (mask[i]) { blue += colors[i].b; samples++; }
                    double mean = blue / Math.Max(1,samples);
                    if ((back && mean < 200) || (!back && mean > 40))
                        throw new InvalidOperationException($"Two-sided normals face the wrong way: back={back}, mean Z channel={mean:F2}.");
                    Debug.Log($"WATTLE NORMAL SIDE: back={back}, mean Z channel={mean:F2}");
                }
                return mask;
            }
            finally { Object.DestroyImmediate(pixels); }
        }
    }

    private static Texture2D Read(RenderTexture target)
    {
        var oldTarget = RenderTexture.active; var pixels = new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply(); return pixels; }
        finally { RenderTexture.active = oldTarget; }
    }

    private sealed class ShadowFixture : IDisposable
    {
        private readonly GameObject cameraObject, sunObject, receiver;
        private readonly Camera camera;
        private readonly RenderTexture target;
        private readonly Mesh canopy, reversed;
        private readonly Material opaque, receiverMaterial;
        private readonly BuildDefinition definition;
        private readonly BuildDefinition original;
        private readonly BuildSession session = new();
        private readonly Action<ScriptableRenderContext,Camera> submit;
        private readonly RenderPipelineAsset oldPipeline = GraphicsSettings.defaultRenderPipeline, oldQuality = QualitySettings.renderPipeline;
        private readonly Light oldSun = RenderSettings.sun;
        private readonly AmbientMode oldAmbientMode = RenderSettings.ambientMode;
        private readonly Color oldAmbient = RenderSettings.ambientLight;
        private readonly bool oldFog = RenderSettings.fog;
        private float shadowRange;
        public readonly BuildRenderer Renderer;
        public bool InView => GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), new Bounds(new Vector3(1.75f,2.875f,1.375f),new Vector3(3.5f,.25f,2.75f)));

        public ShadowFixture(BuildDefinition wall)
        {
            original = wall;
            canopy = Object.Instantiate(wall.mesh); var rotation = Quaternion.Euler(90,0,0);
            var vertices = canopy.vertices; var normals = canopy.normals;
            for (int i = 0; i < vertices.Length; ++i) { vertices[i] = rotation*vertices[i]; normals[i] = rotation*normals[i]; }
            canopy.vertices = vertices; canopy.normals = normals; canopy.RecalculateBounds();
            reversed = Object.Instantiate(canopy); var reverseNormals = reversed.normals; var reverseIndices = reversed.triangles;
            for (int i = 0; i < reverseNormals.Length; ++i) reverseNormals[i] = -reverseNormals[i];
            for (int i = 0; i < reverseIndices.Length; i += 3) (reverseIndices[i+1],reverseIndices[i+2]) = (reverseIndices[i+2],reverseIndices[i+1]);
            reversed.normals = reverseNormals; reversed.triangles = reverseIndices;
            opaque = new Material(wall.material); opaque.SetFloat("_AlphaClip",0); opaque.DisableKeyword("_ALPHATEST_ON");
            definition = ScriptableObject.CreateInstance<BuildDefinition>(); definition.kind = BuildPartKind.Floor;
            definition.sizeUnits = new Vector3Int(14,1,11); definition.minimumUnits = new Vector3Int(0,-1,0);
            definition.mesh = canopy; definition.material = wall.material;
            var frame = session.CreateFrame(new Vector3(0,3,0),0); session.Add(definition,frame,default,0,true);
            Renderer = new BuildRenderer(session);
            cameraObject = new GameObject("Wattle shadow receiver camera"); camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 1.1f; camera.aspect = 1.5f; camera.nearClipPlane = .05f; camera.farClipPlane = 15;
            camera.transform.position = new Vector3(1.75f,2,1.375f); camera.transform.rotation = Quaternion.Euler(90,0,0);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1;
            target = new RenderTexture(768,512,24,RenderTextureFormat.ARGB32); target.Create();
            receiver = GameObject.CreatePrimitive(PrimitiveType.Plane); receiver.name = "Weave shadow receiver"; receiver.layer = 0;
            receiver.transform.position = new Vector3(1.75f,0,1.375f); receiver.transform.localScale = Vector3.one*.45f;
            receiverMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); receiverMaterial.SetColor("_BaseColor",Color.white); receiverMaterial.SetFloat("_Smoothness",0);
            receiver.GetComponent<MeshRenderer>().sharedMaterial = receiverMaterial;
            sunObject = new GameObject("Wattle shadow sun"); var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.5f; sun.shadows = LightShadows.Hard; sun.shadowBias = .05f; sun.shadowNormalBias = 0;
            sun.cullingMask = 1; sun.transform.rotation = Quaternion.Euler(90,0,0);
            RenderSettings.sun = sun; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.04f,.04f,.04f); RenderSettings.fog = false;
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            submit = (_,cam) => { if (cam == camera) Renderer.Draw(camera,3000,shadowRange); };
            RenderPipelineManager.beginCameraRendering += submit;
        }

        public void Configure(int index)
        {
            shadowRange = index == 0 ? 0 : 140;
            definition.material = index == 1 ? opaque : original.material;
            definition.mesh = index == 3 ? reversed : canopy;
            if (InView) throw new InvalidOperationException("Weave canopy should be an off-screen ShadowsOnly caster.");
        }
        public void Render() => RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        public float Capture(string name)
        {
            var pixels = Read(target);
            try
            {
                File.WriteAllBytes($"{Output}/wattle-shadow-{name}.png",pixels.EncodeToPNG());
                float sum = 0; int count = 0;
                for (int y = 30; y < target.height-30; ++y)
                    for (int x = 30; x < target.width-30; ++x)
                    { Color c = pixels.GetPixel(x,y); sum += c.r*.2126f+c.g*.7152f+c.b*.0722f; count++; }
                return sum/count;
            }
            finally { Object.DestroyImmediate(pixels); }
        }
        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= submit;
            GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality;
            RenderSettings.sun = oldSun; RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbient; RenderSettings.fog = oldFog;
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(sunObject); Object.DestroyImmediate(receiver);
            Object.DestroyImmediate(receiverMaterial); Object.DestroyImmediate(opaque); Object.DestroyImmediate(canopy); Object.DestroyImmediate(reversed); Object.DestroyImmediate(definition);
            target.Release(); Object.DestroyImmediate(target);
        }
    }
}
