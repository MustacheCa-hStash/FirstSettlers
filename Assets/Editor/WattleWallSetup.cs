using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Preserves the authored FBX and UVs, baking its hierarchy into one runtime mesh.</summary>
public static class WattleWallSetup
{
    public const string ModelPath = "Assets/Models/Buildings/Wood/Modular/WattleWall_4.00Wx3.00Hx0.25D.fbx";
    public const string MeshPath = "Assets/Resources/Building/wattle-wall-mesh.asset";
    public const string DefinitionPath = "Assets/Resources/Building/wattle-wall.asset";
    public const string PrefabPath = "Assets/Resources/Building/wattle-wall.prefab";
    public const string MaterialPath = "Assets/Materials/Buildings/Wood/WattleWood.mat";
    public const string ContentId = "build.wood.wattle-wall";
    private static readonly Vector3 Size = new(4, 3, .25f);

    [MenuItem("Tools/Building/Rebuild Wattle Wall")]
    public static void Rebuild()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        var wood = AssetDatabase.LoadAssetAtPath<Material>(SplitPlankWallSetup.MaterialPath);
        if (catalog == null || wood == null) throw new InvalidOperationException("Install the existing wood prototype first.");
        var definition = Create(wood);
        var presets = new List<BuildDefinition>(catalog.presets);
        int index = presets.FindIndex(p => p != null && p.contentId == ContentId);
        if (index < 0) presets.Add(definition); else presets[index] = definition;
        catalog.presets = presets.ToArray();
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        Debug.Log("Wattle wall added; full logical/collider box retained, one instanced cutout material.");
    }

    public static BuildDefinition Create(Material opaqueWood)
    {
        var model = ModularWoodSetup.Import(ModelPath);
        if (model == null || opaqueWood == null) throw new InvalidOperationException("Wattle FBX or shared wood material is missing.");
        var baked = Bake(model, out string inspection);
        Debug.Log(inspection);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null) { mesh = baked; AssetDatabase.CreateAsset(mesh, MeshPath); }
        else { EditorUtility.CopySerialized(baked, mesh); Object.DestroyImmediate(baked); EditorUtility.SetDirty(mesh); }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(opaqueWood) { name = "WattleWood" };
            material.SetFloat("_Cutoff", .5f);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = Shader.Find(SplitPlankWallSetup.ShaderName);
        if (material.shader == null) throw new InvalidOperationException("Matte wood shader is missing.");
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SplitPlankWallSetup.TexturePath));
        material.SetFloat("_AlphaClip", 1);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.enableInstancing = true;
        material.doubleSidedGI = true;
        EditorUtility.SetDirty(material);

        var definition = AssetDatabase.LoadAssetAtPath<BuildDefinition>(DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<BuildDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        definition.contentId = ContentId; definition.displayName = "Wattle wall";
        definition.kind = BuildPartKind.Wall; definition.wallPlacementMode = WallPlacementMode.Panel;
        ModularWoodSetup.Profile(definition,true); definition.mesh = mesh; definition.material = material;
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Wattle wall");
        try
        {
            root.layer = GameplayLayers.WorldSolid;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
            if (!root.TryGetComponent<MeshFilter>(out var filter)) filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            if (!root.TryGetComponent<MeshRenderer>(out var renderer)) renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            if (!root.TryGetComponent<BoxCollider>(out var collider)) collider = root.AddComponent<BoxCollider>();
            collider.center = definition.LocalBounds.center; collider.size = definition.LocalBounds.size; collider.isTrigger = false;
            definition.authoringPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { if (existing) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
        EditorUtility.SetDirty(definition);
        return definition;
    }

    public static Mesh Bake(GameObject model, out string inspection)
        => BakeStaticMesh(model, Size, "Wattle wall", out inspection,true);

    public static Mesh BakeStaticMesh(GameObject model, Vector3 requiredSize, string meshName, out string inspection, bool preserveStructuralOrigin = false)
    {
        var report = new StringBuilder(meshName.ToUpperInvariant() + " IMPORT INSPECTION\n");
        var filters = model.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0 || model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
            throw new InvalidOperationException("Wattle must contain static mesh geometry.");
        var vertices = new List<Vector3>(); var normals = new List<Vector3>();
        var uvs = new List<Vector2>(); var indices = new List<int>();
        foreach (var filter in filters)
        {
            var source = filter.sharedMesh;
            if (source == null) throw new InvalidOperationException("An imported wattle object has no mesh.");
            var transform = filter.transform.localToWorldMatrix;
            if (Mathf.Abs(transform.determinant) < .000001f) throw new InvalidOperationException("Wattle has a singular object transform.");
            report.AppendLine($"{filter.name}: vertices={source.vertexCount}, submeshes={source.subMeshCount}, raw bounds={source.bounds}, hierarchy matrix={transform}");
            using var snapshot = MeshUtility.AcquireReadOnlyMeshData(source);
            var data = snapshot[0];
            if (!data.HasVertexAttribute(VertexAttribute.TexCoord0) || !data.HasVertexAttribute(VertexAttribute.Normal))
                throw new InvalidOperationException("Wattle requires authored UV0 and imported normals.");
            using var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
            using var ns = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
            using var uv = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp);
            data.GetVertices(positions); data.GetNormals(ns); data.GetUVs(0, uv);
            int offset = vertices.Count;
            var normalTransform = transform.inverse.transpose;
            for (int i = 0; i < data.vertexCount; ++i)
            {
                vertices.Add(transform.MultiplyPoint3x4(positions[i]));
                normals.Add(normalTransform.MultiplyVector(ns[i]).normalized); uvs.Add(uv[i]);
            }
            for (int submesh = 0; submesh < data.subMeshCount; ++submesh)
            {
                var description = data.GetSubMesh(submesh);
                if (description.topology != MeshTopology.Triangles) throw new InvalidOperationException("Wattle submeshes must be triangles.");
                using var triangles = new NativeArray<int>(description.indexCount, Allocator.Temp);
                data.GetIndices(triangles, submesh);
                bool mirrored = transform.determinant < 0;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    indices.Add(offset + triangles[i]);
                    indices.Add(offset + triangles[i + (mirrored ? 2 : 1)]);
                    indices.Add(offset + triangles[i + (mirrored ? 1 : 2)]);
                }
            }
        }
        if (vertices.Count == 0) throw new InvalidOperationException("Wattle mesh is empty.");
        var envelope = new Bounds(vertices[0], Vector3.zero);
        foreach (var point in vertices) envelope.Encapsulate(point);
        report.AppendLine($"Transformed envelope: min={envelope.min:F6}, max={envelope.max:F6}, size={envelope.size:F6}; normalization offset={(preserveStructuralOrigin ? Vector3.zero : -envelope.min):F6}");
        if (!preserveStructuralOrigin && (envelope.size - requiredSize).sqrMagnitude > .000001f)
            throw new InvalidOperationException(meshName + " transformed dimensions do not match " + requiredSize + "; inspect the FBX rather than stretching its UV-mapped geometry. " + report);
        // Bake hierarchy transforms, then translate to the definition's lower-corner origin.
        // Snap only float round-off at the six intended boundary faces; never rescale UVs.
        for (int i = 0; i < vertices.Count; ++i)
        {
            Vector3 point = vertices[i] - (preserveStructuralOrigin ? Vector3.zero : envelope.min);
            for (int axis = 0; axis < 3; ++axis)
            {
                if (Mathf.Abs(point[axis]) < .00001f) point[axis] = 0;
                if (!preserveStructuralOrigin && Mathf.Abs(point[axis] - requiredSize[axis]) < .00001f) point[axis] = requiredSize[axis];
            }
            vertices[i] = point;
        }
        var mesh = new Mesh { name = meshName + " (baked hierarchy, UV0 preserved)", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
        report.AppendLine($"Runtime: vertices={mesh.vertexCount}, triangles={indices.Count / 3}, submeshes={mesh.subMeshCount}, min={mesh.bounds.min:F6}, max={mesh.bounds.max:F6}, UV0={uvs.Count}");
        inspection = report.ToString();
        return mesh;
    }

    public static void InspectBatch()
    {
        try
        {
            var model = ModularWoodSetup.Import(ModelPath);
            if (!model) throw new InvalidOperationException("Wattle FBX was not imported.");
            var mesh = Bake(model, out string report);
            Object.DestroyImmediate(mesh);
            Directory.CreateDirectory(".utmp/building-prototype");
            File.WriteAllText(".utmp/building-prototype/wattle-import-inspection.txt", report);
            Debug.Log(report); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    public static void RebuildBatch()
    {
        try { Rebuild(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
