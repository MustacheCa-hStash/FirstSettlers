using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class StandingTreeLodStudy
{
    const string Output = ".utmp/standing-tree-lods";
    const string Models = "Assets/Models/Trees/StandingTreeLevels/";
    const string Prefabs = "Assets/Prefabs/";
    const int Width = 1280, Height = 720;
    const float ReferenceScale = .5f;
    static readonly int[] Headings = { 0, 67, 139 };
    static readonly int[] MidDistances = { 76, 90, 104, 118 };
    static readonly int[] FarDistances = { 118, 132, 146, 158 };

    sealed class Species
    {
        public readonly string Name, Source, Datacard;
        public Species(string name, string source, string datacard)
        { Name = name; Source = source; Datacard = datacard; }
        public string Product => Prefabs + Name + "_DetailLevels.prefab";
    }

    static readonly Species[] Trees = {
        new("SugarMaple", "SugarMaple_LOD0_v04", "SugarMapleTree"),
        new("Birch", "Birch_LOD0_v04", "BirchAspenTree"),
        new("Spruce", "Spruce_LOD0_v06", "SpruceTree"),
        new("WhitePine", "WhitePine_LOD0_v01", "WhitePineTree"),
        new("OakCrown_v04", "OakCrown_v04", "OakTree")
    };

    public static void PreviewOakBatch() => Execute(true);
    public static void BuildBatch() => Execute(false);

    static void Execute(bool preview)
    {
        try
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildOakSource();
            using (var fixture = new Fixture())
            {
                if (preview) fixture.PreviewOak();
                else foreach (var species in Trees) fixture.Build(species);
            }
            Debug.Log(preview ? "OAK CROWN PREVIEW COMPLETE" : "STANDING TREE LOD STUDY COMPLETE");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static GameObject InstantiateAsset(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) throw new FileNotFoundException("Missing tree asset", path);
        return (GameObject)PrefabUtility.InstantiatePrefab(asset);
    }

    static Material MaterialFor(GameObject tree, bool bark)
    {
        return tree.GetComponentsInChildren<MeshRenderer>()
            .SelectMany(renderer => renderer.sharedMaterials).First(m =>
                (m.name.Contains("Bark") || m.shader.name.Contains("Bark")) == bark);
    }

    static GameObject MakeLevel(string model, GameObject source)
    {
        var level = InstantiateAsset(Models + model + ".fbx");
        var bark = MaterialFor(source, true);
        var leaves = MaterialFor(source, false);
        var parts = level.GetComponentsInChildren<MeshRenderer>();
        if (parts.Length != 2) throw new InvalidOperationException("Expected separate bark and foliage: " + model);
        foreach (var part in parts)
            part.sharedMaterial = part.name.StartsWith("Bark", StringComparison.Ordinal) ? bark : leaves;
        return level;
    }

    static void BuildOakSource()
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Oak_LOD0_v03.prefab");
        var root = MakeLevel("OakCrown_v04_LOD0", template);
        try
        {
            root.name = "OakCrown_v04";
            PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "OakCrown_v04.prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    sealed class Fixture : IDisposable
    {
        readonly Camera camera;
        readonly RenderTexture target;
        readonly Texture2D texture;
        readonly Color32 clear;
        readonly StreamWriter report;
        readonly StreamWriter assignments;
        readonly StreamWriter groves;
        StandingTreeRenderer standing;
        TreeInstanceData tree;
        Bounds bounds;
        Matrix4x4 matrix;
        bool draw;
        bool grove;

        public Fixture()
        {
            QualitySettings.SetQualityLevel(Array.IndexOf(QualitySettings.names, "PC"));
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .56f, .60f);
            var sun = new GameObject("Study sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(42, -30, 0);
            sun.shadows = LightShadows.None;
            camera = new GameObject("Study camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 60;
            camera.farClipPlane = 680;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.52f, .69f, .84f);
            target = new RenderTexture(Width, Height, 24);
            target.Create();
            texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            report = new StreamWriter(Output + "/sweep.csv");
            report.WriteLine("species,level,distance,yaw,coverageRatio,outlineOverlap");
            assignments = new StreamWriter(Output + "/assignments.csv");
            assignments.WriteLine("species,path,guid,fileId,lod0,lod1,lod2,midMeters,farMeters,midHeight,farHeight");
            groves = new StreamWriter(Output + "/groves.csv");
            groves.WriteLine("species,mode,triangles,drawCalls");
            RenderPipelineManager.beginCameraRendering += Draw;
            clear = Capture()[0];
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= Draw;
            report.Dispose();
            assignments.Dispose();
            groves.Dispose();
            standing?.Dispose();
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(camera.gameObject);
        }

        void Draw(ScriptableRenderContext _, Camera rendering)
        {
            if (!draw || rendering != camera) return;
            standing.BeginFrame(camera);
            if (grove)
            {
                for (int row = 0; row < 12; ++row)
                for (int column = 0; column < 12; ++column)
                {
                    var position = new Vector3((column - 5.5f) * 5f, 0, 59 + 10 * row);
                    var rotation = Quaternion.Euler(0, 53 * (row + 3 * column), 0);
                    standing.Submit(tree, Matrix4x4.TRS(position, rotation, tree.localScale), 0, 1);
                }
            }
            else standing.Submit(tree, matrix, 0, 1);
            standing.EndFrame();
        }

        void Use(GameObject source, float scale)
        {
            standing?.Dispose();
            standing = new StandingTreeRenderer(new TreeSettings { mapleTreePrefab = source, castTreeShadows = false });
            tree = new TreeInstanceData(Vector3.zero, Quaternion.identity, Vector3.one * scale, WorldFeatureVariant.MapleTree);
            matrix = Matrix4x4.Scale(tree.localScale);
            bounds = standing.BoundsFor(tree.variant, matrix);
            draw = true;
        }

        Color32[] Capture()
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            RenderTexture.active = previous;
            return texture.GetPixels32();
        }

        void View(int distance, int yaw, Vector3 aim, float elevation = 1.7f)
        {
            tree.localRotation = Quaternion.Euler(0, yaw, 0);
            matrix = Matrix4x4.TRS(Vector3.zero, tree.localRotation, tree.localScale);
            camera.transform.position = new Vector3(0, elevation, -distance);
            camera.transform.LookAt(aim);
        }

        void Save(string name) => File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());

        // Compare occupancy in 8px blocks so small differences between leaf
        // silhouettes do not dominate the crown-level outline measurement.
        int[] Occupancy(Color32[] pixels)
        {
            const int block = 8;
            var bins = new int[(Width / block) * (Height / block)];
            for (int y = 0; y < Height; ++y)
            for (int x = 0; x < Width; ++x)
            {
                var pixel = pixels[y * Width + x];
                if (Math.Abs(pixel.r - clear.r) + Math.Abs(pixel.g - clear.g) + Math.Abs(pixel.b - clear.b) > 12)
                    bins[y / block * (Width / block) + x / block]++;
            }
            return bins;
        }

        static (float coverage, float outline) Compare(int[] reference, int[] reduced)
        {
            int intersection = 0, union = 0;
            for (int i = 0; i < reference.Length; ++i)
            {
                if (reference[i] >= 5 && reduced[i] >= 5) intersection++;
                if (reference[i] >= 5 || reduced[i] >= 5) union++;
            }
            if (union == 0 || reference.Sum() == 0) throw new InvalidOperationException("Empty canopy capture.");
            return (reduced.Sum() / (float)reference.Sum(), intersection / (float)union);
        }

        public void PreviewOak()
        {
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Oak_LOD0_v03.prefab");
            var crown = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "OakCrown_v04.prefab");
            foreach (int yaw in Headings)
            foreach (float elevation in new[] { 1.7f, 40f })
            {
                Vector3 aim = new Vector3(0, 36, 0);
                Use(old, 6.25f); View(91, yaw, aim, elevation);
                var reference = Occupancy(Capture());
                Save($"oak-old-{yaw}-{elevation:F0}");
                Use(crown, 6.25f); View(91, yaw, aim, elevation);
                var sample = Occupancy(Capture());
                Save($"oak-new-{yaw}-{elevation:F0}");
                Debug.Log($"OAK CROWN yaw={yaw} elevation={elevation} footprint={sample.Sum() / (float)reference.Sum():F3}");
            }
        }

        public void Build(Species species)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + species.Source + ".prefab");
            var card = AssetDatabase.LoadAssetAtPath<WorldObjectDefinition>(
                "Assets/ScriptableObjects/WorldObjects/" + species.Datacard + ".asset");
            float scale = (card.TreeUniformScaleRange.x + card.TreeUniformScaleRange.y) * .5f;
            var root = InstantiateAsset(Prefabs + species.Source + ".prefab");
            var mid = MakeLevel(species.Name + "_LOD1", source);
            var far = MakeLevel(species.Name + "_LOD2", source);
            // Keep fixture objects outside the camera. The renderer uses their
            // local matrices to submit the measured instance at the origin.
            root.transform.position = mid.transform.position = far.transform.position = Vector3.one * 10000;
            try
            {
                Use(source, scale);
                float sourceHeight = bounds.size.y;
                Vector3 aim = bounds.center;
                int[] distances = MidDistances.Concat(FarDistances).Distinct().ToArray();
                var references = new Dictionary<(int distance, int yaw), int[]>();
                foreach (int distance in distances)
                foreach (int yaw in Headings)
                {
                    View(distance, yaw, aim);
                    references.Add((distance, yaw), Occupancy(Capture()));
                    if (distance == 104 && yaw == 67) Save(species.Name + "-full");
                }
                long[] triangles = new long[3];
                triangles[0] = standing.RenderStats.triangles;
                int midDistance = CheckLevel(species, mid, 1, MidDistances, references, scale, aim, out triangles[1]);
                int farDistance = CheckLevel(species, far, 2, FarDistances.Where(d => d >= midDistance + 25),
                    references, scale, aim, out triangles[2]);
                if (!(triangles[0] > triangles[1] && triangles[1] > triangles[2]))
                    throw new InvalidOperationException("LOD mesh budgets did not decrease for " + species.Name);

                var full = root.GetComponentsInChildren<MeshRenderer>();
                mid.transform.SetParent(root.transform, false);
                far.transform.SetParent(root.transform, false);
                mid.transform.localPosition = far.transform.localPosition = Vector3.zero;
                root.transform.position = Vector3.zero;
                var reference = root.AddComponent<StandingTreeLodReference>();
                var serialized = new SerializedObject(reference);
                serialized.FindProperty("scale").floatValue = ReferenceScale;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var group = root.AddComponent<LODGroup>();
                // Use the union of the renderer's padded bounds when computing
                // the thresholds, matching the runtime selector.
                group.SetLODs(new[] { new LOD(.9f, full), new LOD(.5f, mid.GetComponentsInChildren<MeshRenderer>()),
                    new LOD(0, far.GetComponentsInChildren<MeshRenderer>()) });
                Use(root, scale);
                if (bounds.size.y > sourceHeight * 1.15f)
                    throw new InvalidOperationException("Unexpected model units for " + species.Name);
                float midHeight = HeightAt(midDistance);
                float farHeight = HeightAt(farDistance);
                if (midHeight > 1 || midHeight <= farHeight)
                    throw new InvalidOperationException("Invalid projected thresholds for " + species.Name);
                group.SetLODs(new[] { new LOD(midHeight, full) { fadeTransitionWidth = .18f },
                    new LOD(farHeight, mid.GetComponentsInChildren<MeshRenderer>()) { fadeTransitionWidth = .05f },
                    new LOD(0, far.GetComponentsInChildren<MeshRenderer>()) });
                group.fadeMode = LODFadeMode.CrossFade;
                group.RecalculateBounds();
                root.name = species.Name + "_DetailLevels";
                var saved = PrefabUtility.SaveAsPrefabAsset(root, species.Product);
                root.transform.position = Vector3.one * 10000;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(saved, out string guid, out long fileId);
                Use(saved, scale);
                int[] probes = { midDistance - 12, (midDistance + farDistance) / 2, farDistance + 10 };
                for (int level = 0; level < 3; ++level)
                {
                    View(probes[level], 67, aim);
                    Capture();
                    if (standing.RenderStats.triangles != triangles[level])
                        throw new InvalidOperationException("Saved prefab selected an unexpected level: " + species.Name);
                    Save($"{species.Name}-selected-{level}");
                }
                MeasureGrove(species.Name, "full", source, scale);
                MeasureGrove(species.Name, "auto", saved, scale);
                var sourceAuthoring = source.GetComponent<TreeGameplayAuthoring>();
                var savedAuthoring = saved.GetComponent<TreeGameplayAuthoring>();
                if (sourceAuthoring != null && (savedAuthoring == null ||
                    savedAuthoring.PhysicalTrunkColliders.Count != sourceAuthoring.PhysicalTrunkColliders.Count ||
                    savedAuthoring.QueryCanopyColliders.Count != sourceAuthoring.QueryCanopyColliders.Count ||
                    savedAuthoring.QueryDefinition != sourceAuthoring.QueryDefinition ||
                    savedAuthoring.PhysicalTrunkColliders.Any(c => c == null) ||
                    savedAuthoring.QueryCanopyColliders.Any(c => c == null)))
                    throw new InvalidOperationException("Lost tree gameplay authoring for " + species.Name);
                assignments.WriteLine(FormattableString.Invariant(
                    $"{species.Name},{species.Product},{guid},{fileId},{triangles[0]},{triangles[1]},{triangles[2]},{midDistance},{farDistance},{midHeight:F4},{farHeight:F4}"));
                assignments.Flush();
                Debug.Log($"TREE LOD PASS {species.Name} {string.Join("/", triangles)} triangles; {midDistance}m / {farDistance}m");
            }
            finally
            {
                Object.DestroyImmediate(mid);
                Object.DestroyImmediate(far);
                Object.DestroyImmediate(root);
            }
        }

        void MeasureGrove(string species, string mode, GameObject source, float scale)
        {
            Use(source, scale);
            camera.transform.position = new Vector3(0, 1.7f, 0);
            camera.transform.LookAt(new Vector3(0, bounds.center.y, 100));
            grove = true;
            try { Capture(); }
            finally { grove = false; }
            groves.WriteLine($"{species},{mode},{standing.RenderStats.triangles},{standing.DrawCalls}");
            groves.Flush();
        }

        float HeightAt(int distance)
        {
            float centerDistance = Vector3.Distance(new Vector3(0, 1.7f, -distance), bounds.center);
            return bounds.size.y * QualitySettings.lodBias * ReferenceScale /
                (2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * centerDistance);
        }

        int CheckLevel(Species species, GameObject level, int index, IEnumerable<int> distances,
            Dictionary<(int distance, int yaw), int[]> references, float scale, Vector3 aim, out long triangles)
        {
            Use(level, scale);
            int? selected = null;
            int last = 0;
            foreach (int distance in distances)
            {
                last = distance;
                bool passes = true;
                foreach (int yaw in Headings)
                {
                    View(distance, yaw, aim);
                    var sample = Occupancy(Capture());
                    var comparison = Compare(references[(distance, yaw)], sample);
                    report.WriteLine(FormattableString.Invariant(
                        $"{species.Name},{index},{distance},{yaw},{comparison.coverage:F4},{comparison.outline:F4}"));
                    passes &= comparison.coverage >= (index == 1 ? .87f : .81f) &&
                        comparison.coverage <= 1.18f && comparison.outline >= (index == 1 ? .80f : .73f);
                    if (yaw == 67 && distance == (index == 1 ? 104 : 146))
                        Save($"{species.Name}-lod{index}");
                }
                if (passes) selected ??= distance;
            }
            triangles = standing.RenderStats.triangles;
            report.Flush();
            if (!selected.HasValue)
                Debug.LogWarning($"TREE LOD {species.Name} level {index} fell outside coverage targets. Using last tested distance {last}m.");
            return selected ?? last;
        }
    }
}
