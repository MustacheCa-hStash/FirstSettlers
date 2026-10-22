using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// GPU-only copies of authored mip subsets. No source textures or import settings
// are changed. The original textures stay loaded during these A/B tests.
public static class TreeBillboardMemoryInvestigation
{
    const string Folder = ".utmp/tree-billboard-memory";
    static readonly string[] Properties = { "_AlbedoCoverage", "_SurfaceAtlas", "_DepthAtlas", "_MaterialIdAtlas", "_AmbientAtlas" };
    static readonly string[] Presets = { "8192", "4096", "2048", "4096-shading-only" };
    sealed class Species
    {
        public string Name;
        public Mesh Mesh;
        public Material[] Materials = new Material[4];
        public Matrix4x4[] Matrices = new Matrix4x4[64];
    }
    static readonly List<Species> species = new();
    static readonly Dictionary<(Texture2D, int), Texture2D> reduced = new();
    static readonly MaterialPropertyBlock block = new();
    static Camera camera;
    static GameObject caster;
    static int quality, focus = -1;
    static float distance;
    static IEnumerator routine;
    static long graphicsBefore, graphicsWithOriginals;
    public static void RunBatch()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            ShaderUtil.allowAsyncCompilation = false;
            GenerateMixedShaders();
            graphicsBefore = Profiler.GetAllocatedMemoryForGraphicsDriver();
            EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity");
            var world = Object.FindAnyObjectByType<WorldManager>();
            var settings = (TreeSettings)typeof(WorldManager).GetField("treeSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            var unique = new HashSet<Material>();
            foreach (var entry in TreeSpeciesCatalog.All)
            {
                var prefab = entry.BillboardPrefab(settings); if (prefab == null) continue;
                var source = prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                if (!unique.Add(source)) continue;
                var item = new Species { Name = prefab.name, Mesh = Object.Instantiate(prefab.GetComponentInChildren<MeshFilter>().sharedMesh) };
                item.Mesh.hideFlags = HideFlags.HideAndDontSave;
                item.Mesh.bounds = DistantTreeGpuBatch.RenderingBounds(item.Mesh.bounds, source);
                for (int q = 0; q < 4; q++)
                {
                    var material = new Material(source) { enableInstancing = true, hideFlags = HideFlags.HideAndDontSave };
                    if (q == 3) material.shader = Shader.Find("Hidden/TreeMemory/" + source.shader.name.Substring(7));
                    material.SetFloat("_WindStrength", 0); material.DisableKeyword("PROCEDURAL_INSTANCING_ON");
                    if (q > 0)
                    {
                        foreach (string property in Properties)
                        {
                            if (source.GetTexture(property) is not Texture2D texture) continue;
                            int drop = q == 3 ? (property == "_MaterialIdAtlas" || property == "_AmbientAtlas" ? 0 : 1) : q;
                            if (drop == 0) continue;
                            var key = (texture, drop);
                            if (!reduced.TryGetValue(key, out var copy))
                            {
                                copy = new Texture2D(texture.width >> drop, texture.height >> drop, texture.format, texture.mipmapCount - drop,
                                    !GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat))
                                { name = texture.name + " reduced " + q, filterMode = texture.filterMode, wrapMode = texture.wrapMode, anisoLevel = texture.anisoLevel, hideFlags = HideFlags.HideAndDontSave };
                                copy.Apply(false, true);
                                for (int mip = drop; mip < texture.mipmapCount; mip++) Graphics.CopyTexture(texture, 0, mip, copy, 0, mip - drop);
                                reduced.Add(key, copy);
                            }
                            material.SetTexture(property, copy);
                        }
                        // All five atlases are reduced equally, preserving their
                        // normalized content rectangles, including the ambient atlas.
                        if (q < 3)
                        {
                            material.SetFloat("_AtlasPadding", source.GetFloat("_AtlasPadding") / (1 << q));
                            material.SetFloat("_AtlasTileResolution", source.GetFloat("_AtlasTileResolution") / (1 << q));
                            material.SetFloat("_AmbientTileResolution", source.GetFloat("_AmbientTileResolution") / (1 << q));
                        }
                    }
                    item.Materials[q] = material;
                }
                for (int i = 0; i < 64; i++)
                {
                    int index = i * 6 + species.Count;
                    item.Matrices[i] = Matrix4x4.TRS(new Vector3((index % 24 - 11.5f) * 12, 0, 154 + index / 24 * 14),
                        Quaternion.Euler(0, index * 137.5f, 0), Vector3.one * 1.3f);
                }
                species.Add(item);
            }
            graphicsWithOriginals = Profiler.GetAllocatedMemoryForGraphicsDriver();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline; QualitySettings.vSyncCount = 0;
            RenderSettings.fog = false; RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.gray;
            var sun = new GameObject("Atlas test sun").AddComponent<Light>(); sun.type = LightType.Directional;
            sun.intensity = 1; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(45, -20, 0);
            caster = GameObject.CreatePrimitive(PrimitiveType.Cube); caster.transform.position = new Vector3(0, 10, 40);
            camera = new GameObject("Atlas test camera").AddComponent<Camera>(); camera.enabled = false;
            camera.fieldOfView = 65; camera.nearClipPlane = .1f; camera.farClipPlane = 650;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            block.SetVectorArray("_TreeLeafTint", Enumerable.Repeat(Vector4.one, 64).ToArray());
            WriteMemory();
            routine = Run(); RenderPipelineManager.beginCameraRendering += Draw; EditorApplication.update += Tick;
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void GenerateMixedShaders()
    {
        foreach (string name in new[] { "TreeHQOctaImpostor", "SpruceOctaImpostor" })
        {
            string source = File.ReadAllText("Assets/Shaders/" + name + ".shader");
            source = source.Replace("Shader \"Custom/" + name + "\"", "Shader \"Hidden/TreeMemory/" + name + "\"");
            int start = source.IndexOf("half4 SampleAtlas(", StringComparison.Ordinal), brace = source.IndexOf('{', start), end = brace + 1, depth = 1;
            while (depth > 0) { if (source[end] == '{') depth++; if (source[end] == '}') depth--; end++; }
            string function = source.Substring(start, end - start);
            string full = function.Replace("SampleAtlas(", "SampleAtlasFullPadding(");
            source = source.Substring(0, start) + full + "\n" + function.Replace("_AtlasPadding", "(_AtlasPadding * 0.5)") + source.Substring(end);
            source = source.Replace("SampleAtlas(TEXTURE2D_ARGS(_AmbientAtlas", "SampleAtlasFullPadding(TEXTURE2D_ARGS(_AmbientAtlas");
            File.WriteAllText("Assets/Editor/TreeMemory" + name + ".shader", source);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }
    static ulong TextureBytes(Texture2D texture)
    {
        ulong result = 0;
        for (int mip = 0; mip < texture.mipmapCount; mip++)
            result += GraphicsFormatUtility.ComputeMipmapSize(Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip), texture.graphicsFormat);
        return result;
    }
    static void WriteMemory()
    {
        using var output = new StreamWriter(Folder + "/memory.csv");
        output.WriteLine("quality,texture,width,height,format,mips,readable,formatMipBytes,unityRuntimeBytes");
        for (int q = 0; q < 4; q++)
        {
            var seen = new HashSet<Texture2D>();
            foreach (var item in species) foreach (string property in Properties)
            {
                var texture = item.Materials[q].GetTexture(property) as Texture2D;
                if (texture == null || !seen.Add(texture)) continue;
                output.WriteLine($"{Presets[q]},{texture.name},{texture.width},{texture.height},{texture.graphicsFormat},{texture.mipmapCount},{texture.isReadable},{TextureBytes(texture)},{Profiler.GetRuntimeMemorySizeLong(texture)}");
            }
        }
        File.WriteAllText(Folder + "/graphics-memory.txt", $"Device: {SystemInfo.graphicsDeviceName}; reported VRAM {SystemInfo.graphicsMemorySize} MiB\nEstimated graphics-driver allocation before fixture: {graphicsBefore}\nEstimated graphics-driver allocation after loading originals and all reduced copies: {graphicsWithOriginals}\nOriginals and reduced variants coexist in this test; reduced variants do not free originals. Texture byte sums describe format/mip payload, not observed process VRAM residency.\n");
    }
    static void Tick()
    {
        try { if (!routine.MoveNext()) Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Finish(int code)
    {
        EditorApplication.update -= Tick; RenderPipelineManager.beginCameraRendering -= Draw;
        foreach (var item in species) { Object.DestroyImmediate(item.Mesh); foreach (var material in item.Materials) if (material != null) Object.DestroyImmediate(material); }
        foreach (var texture in reduced.Values) Object.DestroyImmediate(texture);
        foreach (string name in new[] { "TreeHQOctaImpostor", "SpruceOctaImpostor" }) AssetDatabase.DeleteAsset("Assets/Editor/TreeMemory" + name + ".shader");
        Debug.Log("BILLBOARD_MEMORY_STUDY_EXIT " + code); EditorApplication.Exit(code);
    }
    static void Draw(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering != camera) return;
        if (focus >= 0)
        {
            var item = species[focus]; var matrix = new[] { Matrix4x4.TRS(new Vector3(0, 0, distance), Quaternion.Euler(0, 37, 0), Vector3.one * 1.3f) };
            Graphics.DrawMeshInstanced(item.Mesh, 0, item.Materials[quality], matrix, 1, block, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
        }
        else foreach (var item in species)
            Graphics.DrawMeshInstanced(item.Mesh, 0, item.Materials[quality], item.Matrices, item.Matrices.Length, block, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
    }
    static void Render(RenderTexture target)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var request = AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1); request.WaitForCompletion();
        if (request.hasError) throw new Exception("Atlas study GPU synchronization failed");
    }
    static Color32[] Read(RenderTexture target, string name)
    {
        var previous = RenderTexture.active; var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            if (name != null) File.WriteAllBytes(Folder + "/" + name + ".png", pixels.EncodeToPNG());
            return pixels.GetPixels32();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
    }
    static bool Foreground(Color32 p) => Math.Max(p.r, Math.Max(p.g, p.b)) > 8;
    static IEnumerator Run()
    {
        using var performance = new StreamWriter(Folder + "/performance.csv");
        performance.WriteLine("width,height,atlasSize,pass,medianMs,p95Ms");
        foreach (int width in new[] { 2560, 3840 })
        {
            var target = new RenderTexture(width, width * 9 / 16, 24); target.Create(); camera.aspect = 16f / 9;
            camera.transform.SetPositionAndRotation(new Vector3(0, 1.7f, 0), Quaternion.identity);
            for (int pass = 0; pass < 2; pass++) foreach (int q in pass == 0 ? new[] { 0, 1, 3, 2 } : new[] { 2, 3, 1, 0 })
            {
                quality = q; var timings = new List<double>();
                for (int frame = 0; frame < 80; frame++)
                {
                    long started = Stopwatch.GetTimestamp(); Render(target);
                    if (frame >= 24) timings.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
                    yield return null;
                }
                timings.Sort(); performance.WriteLine($"{width},{target.height},{Presets[q]},{pass},{timings[28]:F4},{timings[53]:F4}"); performance.Flush();
            }
            target.Release(); Object.DestroyImmediate(target);
        }
        using var comparison = new StreamWriter(Folder + "/quality.csv");
        comparison.WriteLine("species,distance,atlasSize,referencePixels,unionPixels,missingPixels,addedPixels,foregroundRgbError");
        var image = new RenderTexture(2560, 1440, 24); image.Create(); camera.aspect = 16f / 9; caster.SetActive(false);
        for (focus = 0; focus < species.Count; focus++)
        foreach (float testDistance in new[] { 80f, 154f, 350f })
        {
            distance = testDistance;
            float center = species[focus].Materials[0].GetVector("_CaptureCenterLS").y * 1.3f;
            camera.transform.position = new Vector3(0, 1.7f, 0); camera.transform.LookAt(new Vector3(0, center, distance));
            Color32[] reference = null;
            for (quality = 0; quality < 4; quality++)
            {
                Render(image); yield return null; Render(image);
                var pixels = Read(image, species[focus].Name + "-" + distance + "m-" + Presets[quality]);
                if (reference == null) reference = pixels;
                long error = 0; int union = 0, missing = 0, added = 0, count = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    bool a = Foreground(reference[i]), b = Foreground(pixels[i]);
                    if (a) count++; if (a && !b) missing++; if (!a && b) added++;
                    if (!a && !b) continue;
                    union++; error += Math.Abs(pixels[i].r - reference[i].r) + Math.Abs(pixels[i].g - reference[i].g) + Math.Abs(pixels[i].b - reference[i].b);
                }
                comparison.WriteLine($"{species[focus].Name},{distance},{Presets[quality]},{count},{union},{missing},{added},{error / (Math.Max(1, union) * 3.0 * 255):F6}"); comparison.Flush();
                yield return null;
            }
            Debug.Log($"BILLBOARD_QUALITY_PROGRESS {species[focus].Name} {distance}m");
        }
        image.Release(); Object.DestroyImmediate(image);
        Debug.Log("BILLBOARD_MEMORY_STUDY_PASS");
    }
}
