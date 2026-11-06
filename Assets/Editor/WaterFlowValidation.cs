using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Repeatable GPU captures in Tools/ValidateWaterFlow.ps1's isolated project.</summary>
public static class WaterFlowValidation
{
    static string output;
    static Camera camera;
    static RenderTexture target;
    static Material waterMaterial;
    static GameObject water;
    static PlanarWaterReflection reflection;
    static readonly System.Text.StringBuilder report = new();
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static string Argument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        if (i < 0 || i + 1 >= args.Length) throw new Exception("Missing " + name);
        return args[i + 1];
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        report.AppendLine("PASS: " + message);
    }

    static void Run()
    {
        string root = Argument("-waterSourceRoot");
        output = Argument("-waterOutput");
        Directory.CreateDirectory(output);
        ShaderUtil.allowAsyncCompilation = false;
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.fog = false;

        // Only the clock differs: these shader copies make before/after captures
        // repeatable without adding a test clock or shader variant to production.
        string previousShader = Path.Combine(root, ".utmp/water-flow/BeforeGerstner.shader");
        if (!File.Exists(previousShader)) previousShader = Path.Combine(root, ".utmp/water-flow/Before.shader");
        Shader before = TimedShader(File.ReadAllText(previousShader), "Before");
        Shader after = TimedShader(File.ReadAllText("Assets/Shaders/S_River/MurkyPlanarWater.shader"), "After");
        Shader geometry = TimedShader(File.ReadAllText("Assets/Shaders/S_River/MurkyPlanarWater.shader"), "Geometry", true);
        var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WaterMeshBaseMaterial.mat");
        waterMaterial = Object.Instantiate(sourceMaterial);
        waterMaterial.shader = after;
        Fixture();

        foreach (var view in new[] { "Near", "Grazing", "Middle", "Far" })
        {
            Pose(view);
            waterMaterial.shader = before;
            var baseline = Capture(view + "-Before-t0", 0);
            waterMaterial.shader = after;
            // Validate the current material tuning rather than a stale fixture preset.
            waterMaterial.CopyPropertiesFromMaterial(sourceMaterial);
            var a = Capture(view + "-After-t0", 0);
            var b = Capture(view + "-After-t2", 2);
            float motion = Difference(a, b);
            float change = Difference(a, baseline);
            report.AppendLine($"{view}: mean RGB motion={motion:F7}, before/after={change:F7}");
            if (view == "Near" || view == "Grazing") Check(motion > 0.000005f, view + " water visibly changes over time");
            if (view == "Far") Check(motion < 0.000001f, "Far water remains stable beyond ripple fade");
            Object.DestroyImmediate(baseline); Object.DestroyImmediate(a); Object.DestroyImmediate(b);
        }

        Pose("Near");
        waterMaterial.shader = geometry;
        var displaced = Capture(null, 1);
        Vector2 heights = HeightRange(displaced);
        report.AppendLine($"GPU geometric height range: {heights.x:F5} .. {heights.y:F5} metres");
        Check(heights.x < -.02f && heights.y > .02f && heights.x >= -.161f && heights.y <= .161f,
            "GPU vertices form bounded troughs and crests above and below the mean plane");
        Object.DestroyImmediate(displaced);
        waterMaterial.SetFloat("_WaveAmplitude", 0);
        var flat = Capture(null, 1);
        Vector2 flatHeights = HeightRange(flat);
        Check(Mathf.Abs(flatHeights.x) < .000001f && Mathf.Abs(flatHeights.y) < .000001f,
            "Zero amplitude restores an exactly flat geometric surface");
        Object.DestroyImmediate(flat);
        waterMaterial.CopyPropertiesFromMaterial(sourceMaterial);
        waterMaterial.shader = after;
        for (int frame = 0; frame < 24; frame++)
        {
            var animation = Capture("Flow-" + frame.ToString("D2"), frame / 6f);
            Object.DestroyImmediate(animation);
        }
        waterMaterial.SetFloat("_FlowSpeed", 0);
        waterMaterial.SetFloat("_WaveSpeed", 0);
        var frozenA = Capture(null, 0);
        var frozenB = Capture(null, 15);
        Check(Difference(frozenA, frozenB) < 0.000001f, "Zero current and wave speeds freeze geometry and ripples");
        Object.DestroyImmediate(frozenA); Object.DestroyImmediate(frozenB);
        waterMaterial.SetFloat("_FlowSpeed", sourceMaterial.GetFloat("_FlowSpeed"));
        waterMaterial.SetFloat("_WaveSpeed", sourceMaterial.GetFloat("_WaveSpeed"));

        // Same world surface, different mesh origins/UVs: catches chunk-local flow
        // coordinates, double rendering, and uncovered edges in the actual GPU output.
        var whole = Capture(null, 1);
        water.SetActive(false);
        var left = Surface("Left chunk", -70, 0, waterMaterial);
        var right = Surface("Right chunk", 0, 70, waterMaterial);
        var split = Capture("Near-SplitChunks-t1", 1);
        Check(Difference(whole, split) < 0.00001f, "Split chunks match the uninterrupted water surface");
        Object.DestroyImmediate(whole); Object.DestroyImmediate(split);
        Object.DestroyImmediate(left); Object.DestroyImmediate(right); water.SetActive(true);

        waterMaterial.SetVector("_FlowDirection", Vector4.zero);
        var fallback = Capture("Near-ZeroDirection", 1);
        Check(Finite(fallback), "A zero flow direction gives a finite fallback");
        Object.DestroyImmediate(fallback);
        waterMaterial.SetVector("_FlowDirection", new Vector4(.8f, .6f, 0, 0));

        Shader.SetGlobalFloat("_WaterReflectionValid", 0);
        var noReflection = Capture("Near-NoReflection", 1, false);
        Check(Finite(noReflection), "Water renders without a reflection texture");
        Object.DestroyImmediate(noReflection);
        Shader.SetGlobalFloat("_WaterNormalValid", 0);
        var flatA = Capture(null, 0, false);
        var flatB = Capture(null, 15, false);
        Check(Difference(flatA, flatB) > .000005f, "Geometric waves remain animated without a ripple texture");
        Object.DestroyImmediate(flatA); Object.DestroyImmediate(flatB);
        Shader.SetGlobalFloat("_WaterNormalValid", 1);

        // Also compile and render the actual shipping shader, with Unity's clock.
        waterMaterial.shader = Shader.Find("FirstSettlers/Murky Planar Water");
        var shipping = Capture("Near-ProductionShader", 0);
        Check(Finite(shipping) && !ShaderUtil.ShaderHasError(waterMaterial.shader), "Production water shader compiles and renders on the GPU");
        Object.DestroyImmediate(shipping);
        foreach (var shader in new[] { before, after, geometry })
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Check(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, shader.name + ": " + message.message);

        Object.DestroyImmediate(reflection.gameObject);
        Object.DestroyImmediate(waterMaterial);
        target.Release(); Object.DestroyImmediate(target);
        File.WriteAllText(Path.Combine(output, "validation.txt"), report.ToString());
        WaterMeshValidation.Run();
        WaterWaveMeshValidation.Run();
        WaterReflectionVisibilityValidation.Run();
        // Existing fog validation restores a saved scene and reads the real rock
        // shader from an FBX renderer. Supply those dependencies in this fixture.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/WaterFlowValidation/FogRestore.unity");
        var rock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VincentPrefabs/Boulders/Boulder1.fbx");
        rock.GetComponentInChildren<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Rocks/Stone_Bare.mat");
        DistantFogValidation.Run();
        report.AppendLine("PASS: existing water mesh/global-water, reflection visibility, and distant-fog GPU regressions");
        report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName);
        report.AppendLine("Synthetic pond fixture; deterministic variants substitute only _Time.y. Not a gameplay or performance benchmark.");
        File.WriteAllText(Path.Combine(output, "validation.txt"), report.ToString());
        Debug.Log("WATER FLOW GPU PASS\n" + report);
    }

    static Shader TimedShader(string source, string name, bool geometryOnly = false)
    {
        source = source.Replace("#include \"WaterWaves.hlsl\"", File.ReadAllText("Assets/Shaders/S_River/WaterWaves.hlsl"));
        if (geometryOnly)
        {
            int start = source.IndexOf("half4 Frag(", StringComparison.Ordinal);
            int end = source.IndexOf("ENDHLSL", start, StringComparison.Ordinal);
            source = source.Substring(0, start) + "float4 Frag(Varyings input) : SV_Target { return float4(input.positionWS.y - input.restWS.y, 1, 0, 1); }\n" + source.Substring(end);
        }
        Directory.CreateDirectory("Assets/WaterFlowValidation");
        string path = "Assets/WaterFlowValidation/" + name + ".shader";
        File.WriteAllText(path, source.Replace("Shader \"FirstSettlers/Murky Planar Water\"", "Shader \"Hidden/WaterFlow/" + name + "\"")
            .Replace("CBUFFER_START(UnityPerMaterial)", "float _WaterFlowValidationTime;\n            CBUFFER_START(UnityPerMaterial)")
            .Replace("_Time.y", "_WaterFlowValidationTime"));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<Shader>(path);
    }

    static void Fixture()
    {
        camera = new GameObject("Water flow camera").AddComponent<Camera>();
        camera.enabled = false; camera.cullingMask = (1 << 31) | (1 << 4);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.29f, .44f, .56f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 600; camera.fieldOfView = 60;
        camera.allowHDR = true;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.requiresDepthTexture = true; cameraData.requiresColorTexture = true; cameraData.renderPostProcessing = false;
        target = new RenderTexture(1024, 640, 24, RenderTextureFormat.ARGBHalf);
        target.Create(); camera.targetTexture = target;
        var sun = new GameObject("Wave highlight sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.4f; sun.color = new Color(1, .95f, .85f);
        sun.transform.rotation = Quaternion.LookRotation(new Vector3(.35f, -.5f, -.8f));
        RenderSettings.sun = sun;
        var bedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        var checker = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        checker.wrapMode = TextureWrapMode.Repeat; checker.filterMode = FilterMode.Trilinear;
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            checker.SetPixel(x, y, ((x / 8 + y / 8) % 2 == 0) ? new Color(.32f, .29f, .20f) : new Color(.43f, .41f, .29f));
        checker.Apply(); bedMaterial.SetTexture("_BaseMap", checker);
        // A sloping bank actually intersects the water, rather than an alpha mask.
        var vertices = new Vector3[5 * 2]; var uv = new Vector2[vertices.Length];
        float[] xs = { -100, -23, -14, -6, 100 };
        float[] ys = { 3, 3, .15f, -1.8f, -3 };
        for (int z = 0; z < 2; z++) for (int x = 0; x < 5; x++)
        { int i = z * 5 + x; vertices[i] = new Vector3(xs[x], ys[x], z == 0 ? -60 : 400); uv[i] = new Vector2(xs[x] * .03f, (z == 0 ? -60 : 400) * .03f); }
        var triangles = new int[24];
        for (int x = 0; x < 4; x++) { int i = x * 6; triangles[i] = x; triangles[i+1] = x+5; triangles[i+2] = x+1; triangles[i+3] = x+1; triangles[i+4] = x+5; triangles[i+5] = x+6; }
        var bed = new GameObject("Sloping checked riverbed"); bed.layer = 31;
        bed.AddComponent<MeshFilter>().sharedMesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles };
        bed.AddComponent<MeshRenderer>().sharedMaterial = bedMaterial;
        water = Surface("Water", -70, 70, waterMaterial);
        var trunk = new Material(Shader.Find("Universal Render Pipeline/Unlit")); trunk.SetColor("_BaseColor", new Color(.16f,.11f,.07f));
        var leaves = new Material(Shader.Find("Universal Render Pipeline/Unlit")); leaves.SetColor("_BaseColor", new Color(.10f,.24f,.12f));
        for (int i = 0; i < 7; i++)
        {
            float z = 5 + i * 14;
            Box("Bank trunk", new Vector3(-24, 5, z), new Vector3(.7f, 4, .7f), trunk);
            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere); crown.layer = 31;
            crown.transform.position = new Vector3(-24, 9, z); crown.transform.localScale = new Vector3(6,8,6);
            crown.GetComponent<Renderer>().sharedMaterial = leaves;
        }
        Box("Shallow post", new Vector3(-8, 1, 8), new Vector3(.6f, 4, .6f), trunk);
        reflection = new GameObject("Fixture planar reflection").AddComponent<PlanarWaterReflection>();
        reflection.Configure(camera, 0, 1, 30, 120, 500);
    }

    static GameObject Surface(string name, float x0, float x1, Material material)
    {
        var surface = new GameObject(name); surface.layer = 4;
        surface.transform.position = new Vector3(x0, 0, 0);
        int columns = Mathf.CeilToInt((x1-x0) / WaterMeshGenerator.WaveVertexSpacing);
        int rows = Mathf.CeilToInt(430 / WaterMeshGenerator.WaveVertexSpacing);
        var data = new WaterMeshData((columns + 1) * (rows + 1) / 4);
        for (int z = 0; z <= rows; z++) for (int x = 0; x <= columns; x++)
        {
            float wx = Mathf.Lerp(x0, x1, x / (float)columns);
            float[] xs = { -100, -23, -14, -6, 100 }, ys = { 3, 3, .15f, -1.8f, -3 };
            int segment = 0; while (segment < 3 && wx > xs[segment+1]) segment++;
            float slope = (ys[segment+1] - ys[segment]) / (xs[segment+1] - xs[segment]);
            float bedY = ys[segment] + (wx - xs[segment]) * slope;
            data.AddWaveVertex(new Vector3(wx-x0, 0, Mathf.Lerp(-30,400,z/(float)rows)),
                new Vector2(x/(float)columns,z/(float)rows), new Vector4(-bedY,-slope,0,1));
        }
        for (int z = 0; z < rows; z++) for (int x = 0; x < columns; x++)
        {
            int a = z * (columns+1) + x, b = a+1, c = a+columns+1, d = c+1;
            data.AddIndexedTriangle(a,c,b); data.AddIndexedTriangle(b,c,d);
        }
        var mesh = data.CreateMesh();
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        surface.AddComponent<MeshRenderer>().sharedMaterial = material;
        return surface;
    }

    static void Box(string name, Vector3 position, Vector3 size, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name; box.layer = 31;
        box.transform.position = position; box.transform.localScale = size; box.GetComponent<Renderer>().sharedMaterial = material;
    }

    static void Pose(string view)
    {
        Vector3 position, focus;
        switch (view)
        {
            case "Grazing": position = new Vector3(2, 1.1f, -10); focus = new Vector3(-4, 0, 30); break;
            case "Middle": position = new Vector3(25, 18, -70); focus = new Vector3(-10, 0, 30); break;
            case "Far": position = new Vector3(0, 240, -150); focus = new Vector3(0, 0, 80); break;
            default: position = new Vector3(2, 5, -8); focus = new Vector3(-9, 0, 15); break;
        }
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
    }

    static Texture2D Capture(string name, float time, bool renderReflection = true)
    {
        Shader.SetGlobalFloat("_WaterFlowValidationTime", time);
        if (renderReflection)
        {
            typeof(PlanarWaterReflection).GetMethod("EnsureTexture", Private).Invoke(reflection, null);
            typeof(PlanarWaterReflection).GetMethod("RenderReflection", Private).Invoke(reflection, null);
        }
        for (int i = 0; i < 2; i++) RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply();
        if (name != null)
        {
            var png = new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            var colors = pixels.GetPixels(); for (int i = 0; i < colors.Length; i++) colors[i] = colors[i].gamma;
            png.SetPixels(colors); png.Apply(); File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG()); Object.DestroyImmediate(png);
        }
        RenderTexture.active = null;
        return pixels;
    }

    static float Difference(Texture2D a, Texture2D b)
    {
        var aa = a.GetPixels(); var bb = b.GetPixels(); double sum = 0;
        for (int i = 0; i < aa.Length; i++) sum += Math.Abs(aa[i].r-bb[i].r)+Math.Abs(aa[i].g-bb[i].g)+Math.Abs(aa[i].b-bb[i].b);
        return (float)(sum / (aa.Length * 3));
    }

    static Vector2 HeightRange(Texture2D texture)
    {
        float low = 0, high = 0; int count = 0;
        foreach (var c in texture.GetPixels()) if (c.g > .999f && Mathf.Abs(c.b) < .0001f)
        { low = Mathf.Min(low, c.r); high = Mathf.Max(high, c.r); count++; }
        Check(count > 1000, "Geometric diagnostic contains visible water fragments");
        return new Vector2(low, high);
    }

    static bool Finite(Texture2D texture)
    {
        foreach (var c in texture.GetPixels()) if (float.IsNaN(c.r) || float.IsInfinity(c.r) || float.IsNaN(c.g) || float.IsInfinity(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.b)) return false;
        return true;
    }
}
