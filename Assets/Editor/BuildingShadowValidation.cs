using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Isolated batch regression: a fixed roof must shade the same world point as the camera turns/moves.</summary>
public static class BuildingShadowValidation
{
    private static Fixture fixture;
    private static int stage, frames;
    private static bool thatchMode;
    private static readonly float[] luminance = new float[5];
    private static readonly string[] names = { "unshadowed", "look-down", "look-ahead", "moved", "roof-removed" };

    public static void RunBatch()
    { Start(false); }
    public static void RunThatchBatch()
    { Start(true); }
    private static void Start(bool useThatch)
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            thatchMode=useThatch; fixture = new Fixture(useThatch); stage = frames = 0;
            EditorApplication.update += Tick;
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void Tick()
    {
        try
        {
            fixture.Configure(stage);
            fixture.Render();
            if (++frames < 3) return;
            luminance[stage] = fixture.Capture(names[stage]);
            Debug.Log($"BUILDING SHADOW SAMPLE: {names[stage]}, luminance={luminance[stage]:F4}, roofInView={fixture.RoofInView}, submissions={fixture.Renderer.DrawCalls}");
            ++stage; frames = 0;
            if (stage < names.Length) return;
            if (luminance[1] >= luminance[0] * .75f)
                throw new InvalidOperationException("Off-screen roof stopped shading the floor when looking down.");
            if (Mathf.Abs(luminance[1] - luminance[2]) > .03f || Mathf.Abs(luminance[1] - luminance[3]) > .03f)
                throw new InvalidOperationException("Fixed roof shadow changed substantially with camera rotation/movement.");
            if (luminance[4] < luminance[0] * .95f)
                throw new InvalidOperationException("Removing the roof left a stale shadow caster.");
            Debug.Log((thatchMode?"THATCH ":"")+"BUILDING SHADOW PASS: real instanced roof shadows remain on a fixed floor point when the roof is off-screen; camera turn/movement, disabled shadow range and caster removal verified.");
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void Finish(Exception exception)
    {
        EditorApplication.update -= Tick;
        fixture?.Dispose(); fixture = null;
        if (exception != null) Debug.LogException(exception);
        EditorApplication.Exit(exception == null ? 0 : 1);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject cameraObject, lightObject;
        private readonly Camera camera;
        private readonly RenderTexture target;
        private readonly BuildSession session = new();
        private readonly ulong[] roofIds = new ulong[4];
        private readonly RenderPipelineAsset oldPipeline = GraphicsSettings.defaultRenderPipeline;
        private readonly RenderPipelineAsset oldQualityPipeline = QualitySettings.renderPipeline;
        private readonly Light oldSun = RenderSettings.sun;
        private readonly AmbientMode oldAmbientMode = RenderSettings.ambientMode;
        private readonly Color oldAmbient = RenderSettings.ambientLight;
        private readonly bool oldFog = RenderSettings.fog;
        private readonly float oldReflectionIntensity = RenderSettings.reflectionIntensity;
        private readonly Action<ScriptableRenderContext, Camera> submit;
        private readonly Bounds roofBounds;
        private readonly Vector3 probe;
        private readonly bool useThatch;
        private float shadowRange;
        private bool roofRemoved;
        public readonly BuildRenderer Renderer;
        public bool RoofInView => GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), roofBounds);

        public Fixture(bool useThatch=false)
        {
            this.useThatch=useThatch;
            roofBounds=useThatch?new Bounds(new Vector3(4,3.91f,2),new Vector3(8.54f,2.32f,4.54f)):
                new Bounds(new Vector3(4,2.875f,4),new Vector3(8,.25f,8));
            probe=useThatch?new Vector3(3.75f,.005f,2.5f):new Vector3(3.75f,.005f,4.5f);
            var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            if (!catalog) throw new InvalidOperationException("Building catalog missing.");
            var frame = session.CreateFrame(Vector3.zero, 0);
            int roofIndex = 0;
            foreach (var position in new[] { Vector3Int.zero, new Vector3Int(16, 0, 0), new Vector3Int(0, 0, 16), new Vector3Int(16, 0, 16) })
            {
                session.Add(catalog.presets[2], frame, position, 0, true);
                if(!useThatch)roofIds[roofIndex++] = session.Add(catalog.presets[1], frame, position + new Vector3Int(0, 12, 0), 0, false).Id;
            }
            if(useThatch)
            {
                var l=catalog.Find(ThatchRoofSetup.ContentId);var r=l;
                roofIds[0]=session.Add(l,frame,new Vector3Int(0,11,0),0,false).Id;
                roofIds[1]=session.Add(r,frame,new Vector3Int(16,11,0),0,false).Id;
                roofIds[2]=session.Add(r,frame,new Vector3Int(16,11,16),4,false).Id;
                roofIds[3]=session.Add(l,frame,new Vector3Int(32,11,16),4,false).Id;
                bool first=true;
                foreach(var piece in session.Pieces.Values)
                    if(piece.Definition.kind==BuildPartKind.Roof)
                    {
                        var visual=BuildGeometry.WorldBounds(piece.Definition.mesh.bounds,piece.Origin,piece.WorldYawStep);
                        if(first){roofBounds=visual;first=false;}else roofBounds.Encapsulate(visual);
                    }
            }
            Renderer = new BuildRenderer(session);
            cameraObject = new GameObject("Off-screen roof shadow camera");
            camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.fieldOfView = 60; camera.aspect = 1.5f; camera.nearClipPlane = .05f; camera.farClipPlane = 200;
            camera.cullingMask = 1; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .1f, .12f);
            target = new RenderTexture(768, 512, 24, RenderTextureFormat.ARGB32); target.Create();
            lightObject = new GameObject("Fixed overhead shadow-test sun");
            var sun = lightObject.AddComponent<Light>(); sun.type = LightType.Directional;
            sun.intensity = 1.5f; sun.shadows = LightShadows.Hard; sun.shadowStrength = 1; sun.cullingMask = 1;
            sun.transform.rotation = Quaternion.Euler(90, 0, 0);
            RenderSettings.sun = sun; RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.04f, .04f, .04f); RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0;
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            submit = (_, renderingCamera) => { if (renderingCamera == camera) Renderer.Draw(camera, 3000, shadowRange); };
            RenderPipelineManager.beginCameraRendering += submit;
        }

        public void Configure(int index)
        {
            if (index == 4 && !roofRemoved)
            {
                foreach (ulong id in roofIds) session.Remove(id);
                roofRemoved = true;
            }
            camera.transform.position = (index == 3 ? new Vector3(4.35f, 1.6f, 2.15f) : new Vector3(4, 1.6f, 2)) - (useThatch?new Vector3(0,0,2):Vector3.zero);
            camera.transform.rotation = Quaternion.Euler(index == 2 ? 10 : 55, index == 3 ? -5 : 0, 0);
            shadowRange = index == 0 ? 0 : 140;
            if (RoofInView != (index == 2)) throw new InvalidOperationException("Roof visibility fixture is not isolating camera culling.");
        }

        public void Render() => RenderPipeline.SubmitRenderRequest(camera,
            new UniversalRenderPipeline.SingleCameraRequest { destination = target });

        public float Capture(string name)
        {
            var oldTarget = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                Directory.CreateDirectory(".utmp/building-prototype");
                File.WriteAllBytes($".utmp/building-prototype/{(useThatch?"thatch-":"")}shadow-{name}.png", pixels.EncodeToPNG());
                Vector3 point = camera.WorldToViewportPoint(probe);
                if (point.z <= 0 || point.x < .05f || point.x > .95f || point.y < .05f || point.y > .95f)
                    throw new InvalidOperationException("Fixed shadow probe is outside the camera view.");
                int x = Mathf.RoundToInt(point.x * (target.width - 1)), y = Mathf.RoundToInt(point.y * (target.height - 1));
                float sum = 0;
                for (int dy = -2; dy <= 2; ++dy)
                    for (int dx = -2; dx <= 2; ++dx)
                    {
                        Color color = pixels.GetPixel(x + dx, y + dy);
                        sum += color.r * .2126f + color.g * .7152f + color.b * .0722f;
                    }
                return sum / 25;
            }
            finally { RenderTexture.active = oldTarget; Object.DestroyImmediate(pixels); }
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= submit;
            GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQualityPipeline;
            RenderSettings.sun = oldSun; RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbient;
            RenderSettings.fog = oldFog; RenderSettings.reflectionIntensity = oldReflectionIntensity;
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
            target.Release(); Object.DestroyImmediate(target);
        }
    }
}
