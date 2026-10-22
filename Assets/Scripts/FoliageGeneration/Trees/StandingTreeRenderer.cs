using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Shared prefab render definitions and reusable instanced draws. Never creates tree GameObjects.</summary>
public sealed class StandingTreeRenderer : IDisposable, LocalFlameGeometry.IProvider
{
    // Match the existing foliage submission API; keep batches below its 1023-instance limit.
    private const int Capacity = 500;
    private sealed class Batch
    {
        public Mesh Mesh;
        public Material Material;
        public int Submesh;
        public bool Trunk;
        public int Count;
        public readonly Matrix4x4[] Matrices = new Matrix4x4[Capacity];
        public readonly Vector4[] Leaves = new Vector4[Capacity], Bark = new Vector4[Capacity],
            Appearance = new Vector4[Capacity], Fade = new Vector4[Capacity];
        public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
    }
    private struct Part { public Batch Batch; public Matrix4x4 Local; }
    private struct SpatialTree
    {
        public ChunkCoord Region;
        public int Sequence;
        public TreeInstanceData Tree;
        public Matrix4x4 Matrix;
        public float Handoff, Load;
    }
    private sealed class SpatialComparer : IComparer<SpatialTree>
    {
        public static readonly SpatialComparer Instance = new();
        public int Compare(SpatialTree a, SpatialTree b)
        {
            int result = a.Region.z.CompareTo(b.Region.z);
            if (result == 0) result = a.Region.x.CompareTo(b.Region.x);
            return result != 0 ? result : a.Sequence.CompareTo(b.Sequence);
        }
    }
    private sealed class Definition
    {
        public Part[][] Levels;
        public float[] Thresholds;
        public Bounds Bounds;
    }
    private readonly TreeSettings settings;
    private readonly Dictionary<GameObject, Definition> definitions = new();
    private readonly Dictionary<WorldFeatureVariant, Definition> variants = new();
    private readonly Dictionary<Material, Material> materials = new();
    private readonly Dictionary<Mesh, Mesh> meshes = new();
    private readonly List<Batch> batches = new(), touched = new();
    private readonly List<SpatialTree> spatialTrees = new();
    private Camera camera;
    private Vector3 cameraPosition;
    private float screenHeightScale;
    private bool orthographic;
    private readonly TreeRegistry flameRegistry;
    private readonly List<TreeRecord> flameTrees=new();
    private readonly Dictionary<ChunkCoord, Bounds> flameChunkBounds = new();
    public RenderGeometryStats RenderStats { get; private set; }
    public int DrawCalls { get; private set; }

    public StandingTreeRenderer(TreeSettings settings,TreeRegistry registry=null)
    {
        this.settings = settings;
        flameRegistry=registry;
        if(registry!=null){registry.ChunkChanged+=FlameChunkChanged;LocalFlameGeometry.RegisterProvider(this);}
        foreach (var species in TreeSpeciesCatalog.All)
            Add(species.Variant, species.NearPrefab(settings));
        if (registry != null)
        {
            var existingChunks = new HashSet<ChunkCoord>();
            foreach (var tree in registry.Records) existingChunks.Add(tree.Chunk);
            foreach (var coord in existingChunks) FlameChunkChanged(coord);
        }
    }
    private void FlameChunkChanged(ChunkCoord coord)
    {
        bool changed = flameChunkBounds.Remove(coord, out var bounds);
        bool hasCurrent = false;
        Bounds current = default;
        foreach (var tree in flameRegistry.GetChunk(coord))
        {
            if (tree.State != TreeState.Standing || !variants.TryGetValue(tree.Placement.variant, out var definition)) continue;
            var matrix = Matrix4x4.TRS(tree.WorldPosition, tree.Placement.localRotation, tree.Placement.localScale);
            var treeBounds = TransformBounds(definition.Bounds, matrix);
            if (hasCurrent) current.Encapsulate(treeBounds);
            else { current = treeBounds; hasCurrent = true; }
        }
        if (hasCurrent)
        {
            flameChunkBounds.Add(coord, current);
            if (changed) bounds.Encapsulate(current);
            else bounds = current;
        }
        // Empty placement publications have no shadow geometry. Real changes
        // invalidate both the old and new canopy, including removals and moves.
        if (changed || hasCurrent) LocalFlameGeometry.NotifyChanged(bounds);
    }
    public void CollectFlameGeometry(Vector3 position,float range,List<LocalFlameGeometry.Part> output)
    {
        flameTrees.Clear();flameRegistry.CollectOriginsInRadiusXZ(position,range+45,flameTrees);
        foreach(var tree in flameTrees)
        {
            if(tree.State!=TreeState.Standing || !variants.TryGetValue(tree.Placement.variant,out var definition))continue;
            var matrix=Matrix4x4.TRS(tree.WorldPosition,tree.Placement.localRotation,tree.Placement.localScale);
            foreach(var part in definition.Levels[0])
            {
                var b=part.Batch;var geometry=new LocalFlameGeometry.Part(b.Mesh,matrix*part.Local,b.Material,b.Submesh);
                if(geometry.Bounds.SqrDistance(position)<=range*range)output.Add(geometry);
            }
        }
    }

    private void Add(WorldFeatureVariant variant, GameObject prefab)
    {
        if (prefab == null) return;
        if (!definitions.TryGetValue(prefab, out var definition))
        {
            var group = prefab.GetComponentInChildren<LODGroup>(true);
            var lods = group != null ? group.GetLODs() : Array.Empty<LOD>();
            int count = Mathf.Max(1, lods.Length);
            definition = new Definition { Levels = new Part[count][], Thresholds = new float[count] };
            bool bounded = false;
            for (int level = 0; level < count; level++)
            {
                var parts = new List<Part>();
                var renderers = lods.Length > 0 ? lods[level].renderers : prefab.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var renderer in renderers)
                {
                    if (renderer is not MeshRenderer mr || !mr.enabled || !IsActive(mr.transform, prefab.transform)) continue;
                    var filter = mr.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    Matrix4x4 local = RelativeMatrix(mr.transform, prefab.transform);
                    var source = filter.sharedMesh;
                    if (!meshes.TryGetValue(source, out var mesh))
                    {
                        mesh = UnityEngine.Object.Instantiate(source);
                        mesh.name += " (Standing Tree Bounds)";
                        var bounds = mesh.bounds;
                        // Authored tree wind amplitudes are sub-metre; generous padding prevents edge pops.
                        bounds.Expand(2f);
                        mesh.bounds = bounds;
                        meshes.Add(source, mesh);
                    }
                    Bounds transformed = TransformBounds(mesh.bounds, local);
                    if (!bounded) { definition.Bounds = transformed; bounded = true; }
                    else definition.Bounds.Encapsulate(transformed);
                    var shared = mr.sharedMaterials;
                    for (int submesh = 0; submesh < mesh.subMeshCount && submesh < shared.Length; submesh++)
                    {
                        var sourceMaterial = shared[submesh];
                        if (sourceMaterial == null) continue;
                        if (!materials.TryGetValue(sourceMaterial, out var material))
                        {
                            material = new Material(sourceMaterial) { enableInstancing = true };
                            material.name += " (Standing Trees)";
                            material.DisableKeyword("LOD_FADE_CROSSFADE");
                            material.SetFloat("_StandingTreeEnabled", 1f);
                            materials.Add(sourceMaterial, material);
                        }
                        bool trunk = sourceMaterial.shader.name.Contains("Bark") || sourceMaterial.name.Contains("Bark") ||
                            sourceMaterial.shader.name.Contains("Trunk") || sourceMaterial.name.Contains("Trunk");
                        var batch = batches.Find(b => b.Mesh == mesh && b.Material == material && b.Submesh == submesh && b.Trunk == trunk);
                        if (batch == null)
                        {
                            batch = new Batch { Mesh = mesh, Material = material, Submesh = submesh, Trunk = trunk };
                            batches.Add(batch);
                        }
                        parts.Add(new Part { Batch = batch, Local = local });
                    }
                }
                definition.Levels[level] = parts.ToArray();
                definition.Thresholds[level] = lods.Length > 0 ? lods[level].screenRelativeTransitionHeight : 0f;
            }
            definitions.Add(prefab, definition);
        }
        if (definition.Levels.Length > 0 && definition.Levels[0].Length > 0) variants.Add(variant, definition);
    }

    private static bool IsActive(Transform child, Transform root)
    {
        while (child != root) { if (!child.gameObject.activeSelf) return false; child = child.parent; }
        return true;
    }
    private static Matrix4x4 RelativeMatrix(Transform child, Transform root)
    {
        Matrix4x4 result = Matrix4x4.identity;
        while (child != root)
        {
            result = Matrix4x4.TRS(child.localPosition, child.localRotation, child.localScale) * result;
            child = child.parent;
        }
        return result;
    }
    private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        Vector3 e = bounds.extents;
        Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
        Vector3 y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
        Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
        return new Bounds(matrix.MultiplyPoint3x4(bounds.center), new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f);
    }

    public bool Supports(WorldFeatureVariant variant) => variants.ContainsKey(variant);
    public Bounds BoundsFor(WorldFeatureVariant variant, Matrix4x4 matrix) =>
        variants.TryGetValue(variant, out var definition) ? TransformBounds(definition.Bounds, matrix) : new Bounds(matrix.GetColumn(3), Vector3.zero);
    public void BeginFrame(Camera camera)
    {
        spatialTrees.Clear();
        this.camera = camera; RenderStats = default; DrawCalls = 0;
        if (camera == null) return;
        cameraPosition = camera.transform.position;
        orthographic = camera.orthographic;
        screenHeightScale = QualitySettings.lodBias / (2f * (orthographic ? camera.orthographicSize :
            Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f)));
    }

    public void SubmitSpatial(ChunkCoord chunk, TreeInstanceData tree, Matrix4x4 matrix, float billboardFade, float loadFade)
    {
        if (billboardFade >= 1f || loadFade <= 0f || !variants.ContainsKey(tree.variant)) return;
        int span = Mathf.Clamp(settings.standingTreeBatchSizeChunks, 1, 8);
        spatialTrees.Add(new SpatialTree
        {
            Region = new ChunkCoord(FloorRegion(chunk.x, span), FloorRegion(chunk.z, span)),
            Sequence = spatialTrees.Count, Tree = tree, Matrix = matrix, Handoff = billboardFade, Load = loadFade
        });
    }

    private static int FloorRegion(int coordinate, int span) => coordinate >= 0 ? coordinate / span : (coordinate + 1) / span - 1;

    public void EndFrame()
    {
        spatialTrees.Sort(SpatialComparer.Instance);
        for (int i = 0; i < spatialTrees.Count; i++)
        {
            var tree = spatialTrees[i];
            if (i > 0 && tree.Region != spatialTrees[i - 1].Region) EndChunk();
            Submit(tree.Tree, tree.Matrix, tree.Handoff, tree.Load);
        }
        EndChunk();
        spatialTrees.Clear();
    }

    // A distance handoff is supplied by the world renderer. Prefab LODs remain screen-size based.
    public void Submit(TreeInstanceData tree, Matrix4x4 matrix, float billboardFade, float loadFade)
    {
        if (billboardFade >= 1f || loadFade <= 0f || !variants.TryGetValue(tree.variant, out var definition)) return;
        float screenHeight = float.MaxValue;
        if (camera != null && definition.Levels.Length > 1)
        {
            float size = definition.Bounds.size.y * Mathf.Max(Mathf.Abs(tree.localScale.x), Mathf.Abs(tree.localScale.y), Mathf.Abs(tree.localScale.z));
            Vector3 center = matrix.MultiplyPoint3x4(definition.Bounds.center);
            screenHeight = size * screenHeightScale;
            if (!orthographic) screenHeight /= Mathf.Max(.01f, Vector3.Distance(cameraPosition, center));
        }
        int level = 0;
        while (level < definition.Levels.Length - 1 && screenHeight < definition.Thresholds[level]) level++;
        // Crossfade each authored boundary over a narrow screen-size band, never cull before the world handoff.
        for (int boundary = Mathf.Max(0, level - 1); boundary <= level && boundary < definition.Levels.Length - 1; boundary++)
        {
            float threshold = definition.Thresholds[boundary];
            float width = Mathf.Max(.001f, threshold * .1f);
            if (Mathf.Abs(screenHeight - threshold) < width)
            {
                float coarse = 1f - Mathf.InverseLerp(threshold - width, threshold + width, screenHeight);
                SubmitLevel(definition.Levels[boundary], tree, matrix, billboardFade, loadFade, coarse, 1f);
                SubmitLevel(definition.Levels[boundary + 1], tree, matrix, billboardFade, loadFade, 0f, coarse);
                return;
            }
        }
        SubmitLevel(definition.Levels[level], tree, matrix, billboardFade, loadFade, 0f, 1f);
    }

    private void SubmitLevel(Part[] parts, TreeInstanceData tree, Matrix4x4 matrix, float handoff, float load, float lower, float upper)
    {
        bool alphaShadows = tree.snowCoverage > 0f || TreeSpeciesCatalog.IsGrassland(tree.variant);
        foreach (var part in parts)
        {
            var b = part.Batch;
            if (b.Count == 0 && !touched.Contains(b)) touched.Add(b);
            int index = b.Count++;
            b.Matrices[index] = matrix * part.Local;
            b.Leaves[index] = (Color)tree.leafTint;
            b.Bark[index] = (Color)tree.barkTint;
            b.Appearance[index] = new Vector4(tree.snowCoverage, alphaShadows ? 1f : 0f, 0f, 0f);
            b.Fade[index] = new Vector4(Mathf.Max(lower, handoff), upper, load, 0f);
            if (b.Count == Capacity) Flush(b);
        }
    }

    // Flush a region without dropping off-screen shadow casters. Legacy callers
    // can still use this directly for per-chunk submission.
    public void EndChunk() { foreach (var b in touched) Flush(b); touched.Clear(); }
    private void Flush(Batch b)
    {
        if (b.Count == 0) return;
        b.Properties.SetVectorArray("_StandingTreeLeaves", b.Leaves);
        b.Properties.SetVectorArray("_StandingTreeBark", b.Bark);
        b.Properties.SetVectorArray("_StandingTreeAppearance", b.Appearance);
        b.Properties.SetVectorArray("_StandingTreeFade", b.Fade);
        Graphics.DrawMeshInstanced(b.Mesh, b.Submesh, b.Material, b.Matrices, b.Count, b.Properties,
            settings.castTreeShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
            settings.receiveTreeShadows && b.Trunk, 0, camera, LightProbeUsage.Off);
        var stats = RenderStats;
        stats.instances += b.Count;
        stats.vertices += (long)b.Mesh.vertexCount * b.Count;
        stats.triangles += (long)b.Mesh.GetIndexCount(b.Submesh) / 3 * b.Count;
        RenderStats = stats;
        DrawCalls++;
        b.Count = 0;
    }

    public void Dispose()
    {
        if(flameRegistry!=null){flameRegistry.ChunkChanged-=FlameChunkChanged;LocalFlameGeometry.RemoveProvider(this);}
        foreach (var material in materials.Values) DestroyOwned(material);
        foreach (var mesh in meshes.Values) DestroyOwned(mesh);
        materials.Clear(); meshes.Clear(); variants.Clear(); definitions.Clear(); batches.Clear(); touched.Clear(); flameChunkBounds.Clear(); spatialTrees.Clear();
    }
    private static void DestroyOwned(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
