using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public class FarTerrainErrorWindow : EditorWindow
{
    private sealed class Measurement
    {
        public MeshFilter Filter;
        public Mesh Mesh;
        public MeshRenderer Renderer;
        public Matrix4x4 Transform;
        public Bounds Bounds;
        public string Name;
        public int Triangles, Samples, ProjectedSamples;
        public float MaxError, RmsError, SignedWorstError, MaxViewportError;
        public Vector3 WorstPoint;
        public double ElapsedMs;
        public bool IsCurrent => Filter && Renderer && Filter.gameObject.activeInHierarchy &&
            Renderer.enabled && Filter.sharedMesh == Mesh && Filter.transform.localToWorldMatrix == Transform;
    }

    [SerializeField] private WorldManager world;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private int viewportHeight = 2160;
    [SerializeField] private float targetPixels = 2f;
    [SerializeField] private int maxPatches = 16;
    [SerializeField] private bool denseSampling;
    [SerializeField] private bool drawOutlines = true;
    private readonly List<Measurement> results = new List<Measurement>();
    private readonly Queue<Measurement> queue = new Queue<Measurement>();
    private readonly Plane[] planes = new Plane[6];
    private TerrainErrorSamplingSettings settings;
    private TerrainErrorView measuredView;
    private Measurement pending;
    private NativeArray<float3> points;
    private NativeArray<float> errors;
    private JobHandle handle;
    private double started;
    private Vector2 scroll;
    private string status = "Enter Play Mode and measure generated macro patches in the camera frustum.";

    [MenuItem("Tools/Terrain/Far Terrain Screen Error")]
    public static void Open() => GetWindow<FarTerrainErrorWindow>("Far Terrain Error");

    private void OnEnable()
    {
        minSize = new Vector2(820f, 420f);
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        SceneView.duringSceneGui += DrawScene;
    }

    private void OnInspectorUpdate()
    {
        if (results.Count == 0) return;
        Repaint();
        if (drawOutlines) SceneView.RepaintAll();
    }

    private void OnDisable()
    {
        Cancel();
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SceneView.duringSceneGui -= DrawScene;
    }

    private void PlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode && state != PlayModeStateChange.ExitingEditMode) return;
        Cancel();
        results.Clear();
        status = "Enter Play Mode and measure generated macro patches in the camera frustum.";
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("4K / 200 fps target: 5 ms per frame", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("On-demand editor diagnostics. Sampling adds work: cancel or finish it before profiling. " +
            "Projected px uses the camera position and angle captured when measurement starts. Proxy px is a live distance estimate. " +
            "Samples can miss narrow peaks; neither value tests occlusion. " +
            "Regenerate terrain after editing generation settings, then remeasure.", MessageType.Info);
        using (new EditorGUI.DisabledScope(pending != null || queue.Count != 0))
        {
            world = (WorldManager)EditorGUILayout.ObjectField("World", world, typeof(WorldManager), true);
            viewCamera = (Camera)EditorGUILayout.ObjectField("View camera", viewCamera, typeof(Camera), true);
            maxPatches = Mathf.Clamp(EditorGUILayout.IntField("Nearest patches to measure", maxPatches), 1, 128);
            denseSampling = EditorGUILayout.Toggle("Dense sampling (10 / triangle)", denseSampling);
        }
        viewportHeight = Mathf.Clamp(EditorGUILayout.IntField("Target viewport height (px)", viewportHeight), 64, 16384);
        targetPixels = Mathf.Max(0.1f, EditorGUILayout.FloatField("Error target (px)", targetPixels));
        drawOutlines = EditorGUILayout.Toggle("Scene view outlines", drawOutlines);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || pending != null || queue.Count != 0))
            if (GUILayout.Button("Measure camera-frustum macro patches")) BeginMeasurement();
        if (pending != null || queue.Count != 0)
            if (GUILayout.Button("Cancel remaining measurements")) { Cancel(); status = "Cancelled; completed results retained."; }
        EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
        if (results.Count != 0 && !measuredView.Matches(viewCamera))
            EditorGUILayout.HelpBox("Camera moved or changed: projected errors and colors refer to the captured view. " +
                "Remeasure for this view. The distance proxy still updates live.", MessageType.Info);
        int current = 0, aboveTarget = 0, onScreen = 0;
        foreach (Measurement item in results)
        {
            if (!item.IsCurrent) continue;
            current++;
            if (item.ProjectedSamples == 0) continue;
            onScreen++;
            if (ProjectedError(item) > targetPixels) aboveTarget++;
        }
        EditorGUILayout.LabelField($"{current} current measurements; {onScreen} with on-screen probes; {aboveTarget} above {targetPixels:F1} projected px.");
        using (new EditorGUI.DisabledScope(results.Count == 0))
            if (GUILayout.Button("Export current results as CSV")) Export();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (Measurement item in results)
        {
            if (!item.IsCurrent) continue;
            float pixels = ProjectedError(item);
            EditorGUILayout.BeginHorizontal();
            Color previous = GUI.color;
            GUI.color = ErrorColor(pixels);
            if (GUILayout.Button(item.Name, GUILayout.Width(185))) Selection.activeGameObject = item.Filter.gameObject;
            GUI.color = previous;
            string projected = item.ProjectedSamples == 0 ? "no on-screen probes" : $"{pixels:F2} px";
            EditorGUILayout.LabelField($"Projected {projected} | proxy {PixelError(item):F2} px | max {item.MaxError:F3} u | RMS {item.RmsError:F3} u | " +
                $"{item.Triangles:N0} surface triangles | {item.Samples:N0} samples", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        if (GUI.changed) SceneView.RepaintAll();
    }

    private void BeginMeasurement()
    {
        try
        {
            if (!world) world = UnityEngine.Object.FindAnyObjectByType<WorldManager>();
            if (!world) throw new InvalidOperationException("No active WorldManager found.");
            var source = new SerializedObject(world);
            if (!viewCamera) viewCamera = (Camera)source.FindProperty("viewerCamera").objectReferenceValue;
            if (!viewCamera) viewCamera = Camera.main;
            if (!viewCamera) throw new InvalidOperationException("Assign the gameplay camera.");
            Transform parent = (Transform)source.FindProperty("chunkParent").objectReferenceValue;
            if (!parent) throw new InvalidOperationException("WorldManager has no chunk parent.");
            settings = TerrainErrorSamplingSettings.Read(world);
            measuredView = TerrainErrorView.Capture(viewCamera);
            GeometryUtility.CalculateFrustumPlanes(viewCamera, planes);
            var candidates = new List<Measurement>();
            foreach (MeshFilter filter in parent.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.name.StartsWith("FarPatch_", StringComparison.Ordinal) || !filter.sharedMesh) continue;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer || !renderer.enabled || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;
                candidates.Add(new Measurement { Filter = filter, Mesh = filter.sharedMesh, Renderer = renderer,
                    Transform = filter.transform.localToWorldMatrix, Bounds = renderer.bounds, Name = filter.name });
            }
            Vector3 cameraPosition = viewCamera.transform.position;
            candidates.Sort((a, b) => a.Bounds.SqrDistance(cameraPosition).CompareTo(b.Bounds.SqrDistance(cameraPosition)));
            results.Clear();
            for (int i = 0; i < Mathf.Min(maxPatches, candidates.Count); i++) queue.Enqueue(candidates[i]);
            status = $"Queued {queue.Count} of {candidates.Count} macro patches in the frustum. Single-chunk far meshes are excluded.";
        }
        catch (Exception error) { status = error.Message; Debug.LogException(error); }
    }

    private void Tick()
    {
        try
        {
            if (pending != null)
            {
                if (!handle.IsCompleted) return;
                handle.Complete();
                double sumSquares = 0d;
                for (int i = 0; i < errors.Length; i++)
                {
                    float signed = errors[i];
                    if (!math.isfinite(signed)) throw new InvalidOperationException("Non-finite terrain sample; measurement discarded.");
                    float absolute = Mathf.Abs(signed);
                    sumSquares += (double)signed * signed;
                    // Store error in units of viewport height so changing the target pixel
                    // height rescales the result without evaluating the height field again.
                    float projected = TerrainErrorMeasurement.ProjectedPixels(measuredView, (Vector3)points[i], signed, 1);
                    if (!float.IsNaN(projected))
                    {
                        pending.ProjectedSamples++;
                        pending.MaxViewportError = Mathf.Max(pending.MaxViewportError, projected);
                    }
                    if (absolute > pending.MaxError)
                    {
                        pending.MaxError = absolute;
                        pending.SignedWorstError = signed;
                        pending.WorstPoint = (Vector3)points[i];
                    }
                }
                pending.RmsError = (float)Math.Sqrt(sumSquares / errors.Length);
                pending.ElapsedMs = (EditorApplication.timeSinceStartup - started) * 1000d;
                if (pending.IsCurrent) results.Add(pending);
                DisposeArrays();
                pending = null;
                status = $"Measured {results.Count}; {queue.Count} queued. Projected pixels use the view captured at measurement start.";
                Repaint();
                SceneView.RepaintAll();
                return; // At most one patch completion or submission per editor update.
            }
            if (queue.Count == 0) return;
            Measurement next = queue.Dequeue();
            if (!next.IsCurrent) return;
            started = EditorApplication.timeSinceStartup;
            Vector3[] samples = TerrainErrorMeasurement.BuildSamples(next.Mesh, next.Transform, denseSampling, out next.Triangles);
            if (samples.Length == 0) return;
            points = new NativeArray<float3>(samples.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            errors = new NativeArray<float>(samples.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < samples.Length; i++) points[i] = (float3)samples[i];
            next.Samples = samples.Length;
            handle = new TerrainErrorMeasurementJob { Settings = settings, SurfacePoints = points, SignedErrors = errors }
                .Schedule(samples.Length, 64);
            pending = next;
        }
        catch (Exception error) { Cancel(); status = error.Message; Debug.LogException(error); Repaint(); }
    }

    private void Cancel()
    {
        if (pending != null) handle.Complete();
        pending = null;
        queue.Clear();
        DisposeArrays();
    }

    private void DisposeArrays()
    {
        if (points.IsCreated) points.Dispose();
        if (errors.IsCreated) errors.Dispose();
    }

    private float PixelError(Measurement item) => viewCamera
        ? TerrainErrorMeasurement.EstimatedPixels(viewCamera, item.Bounds, item.MaxError, viewportHeight) : float.NaN;

    private float ProjectedError(Measurement item) => item.ProjectedSamples == 0
        ? float.NaN : item.MaxViewportError * viewportHeight;

    private Color ErrorColor(float pixels) => float.IsNaN(pixels) ? Color.gray : pixels <= targetPixels ? Color.green
        : pixels <= targetPixels * 2f ? Color.yellow : new Color(1f, 0.3f, 0.2f);

    private void DrawScene(SceneView scene)
    {
        if (!drawOutlines || !viewCamera) return;
        Color previous = Handles.color;
        foreach (Measurement item in results)
        {
            if (!item.IsCurrent) continue;
            float pixels = ProjectedError(item);
            Handles.color = ErrorColor(pixels);
            Handles.DrawWireCube(item.Bounds.center, item.Bounds.size);
            Handles.DrawLine(item.WorstPoint, item.WorstPoint + Vector3.up * item.SignedWorstError);
            string projected = item.ProjectedSamples == 0 ? "No on-screen probes" : $"Projected {pixels:F2} px";
            Handles.Label(item.Bounds.center, $"{item.Name}\n{projected}; max {item.MaxError:F3} u");
        }
        Handles.color = previous;
    }

    private void Export()
    {
        string path = EditorUtility.SaveFilePanel("Terrain error measurements", "", "terrain-error-4k", "csv");
        if (string.IsNullOrEmpty(path)) return;
        var csv = new StringBuilder("patch,camera_x,camera_y,camera_z,vertical_fov,orthographic,orthographic_size,viewport_height,target_pixels,surface_triangles,samples,max_error_world,rms_error_world,snapshot_proxy_pixels,diagnostic_elapsed_ms,world_seed,world_scale,height_multiplier,sample_scale,mountain_coverage,water_level,dense_sampling,camera_rotation_x,camera_rotation_y,camera_rotation_z,camera_rotation_w,projected_pixels,on_screen_samples,camera_aspect\n");
        foreach (Measurement item in results)
        {
            if (!item.IsCurrent) continue;
            Vector3 p = measuredView.Position;
            Quaternion rotation = measuredView.Rotation;
            csv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4:R},{5},{6:R},{7},{8:R},{9},{10},{11:R},{12:R},{13:R},{14:F3},{15},{16:R},{17:R},{18:R},{19:R},{20:R},{21},{22:R},{23:R},{24:R},{25:R},{26:R},{27},{28:R}\n",
                item.Name, p.x, p.y, p.z, measuredView.VerticalFov, measuredView.Orthographic, measuredView.OrthographicSize,
                viewportHeight, targetPixels, item.Triangles, item.Samples, item.MaxError, item.RmsError,
                TerrainErrorMeasurement.EstimatedPixels(measuredView, item.Bounds, item.MaxError, viewportHeight), item.ElapsedMs,
                settings.Context.RiverSeed - 60000, settings.WorldScale, settings.HeightMultiplier, settings.SampleScale,
                settings.Context.MountainHorizontalScale, settings.Context.WaterLevel, item.Samples == item.Triangles * TerrainErrorMeasurement.DenseSamplesPerTriangle,
                rotation.x, rotation.y, rotation.z, rotation.w, ProjectedError(item), item.ProjectedSamples, measuredView.Aspect);
        }
        File.WriteAllText(path, csv.ToString());
        status = $"Exported {path}";
    }
}
