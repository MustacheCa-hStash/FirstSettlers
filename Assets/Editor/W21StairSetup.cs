using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Stages independent stair collision now; registers the visual build option only after FBX linking.</summary>
public static class W21StairSetup
{
    public const string ModelFolder = "Assets/Models/Buildings/Wood/Stairs";
    public const string ModelPath = ModelFolder + "/HalfStoryStair_1.25Wx1.5Hx2.0D.fbx";
    public const string DefinitionPath = "Assets/Resources/Building/w21-stair.asset";
    public const string ColliderPath = "Assets/Resources/Building/w21-stair-collision.asset";
    public const string VisualPath = "Assets/Resources/Building/w21-stair-mesh.asset";
    public const string PrefabPath = "Assets/Resources/Building/w21-stair.prefab";
    public const string ContentId = "build.wood.w21-half-storey-stair";
    public const float Width = 1.25f, Rise = 1.5f, Run = 2f, ExitLength = .25f;
    public static readonly Vector3 Size = new(Width,Rise,Run);

    [MenuItem("Tools/Building/Prepare W21 Stair Import")]
    public static void Prepare()
    {
        PrepareDefinition(); AssetDatabase.SaveAssets();
        Debug.Log("W21 collision/definition/prefab prepared. Visual model is linked and catalog registration occurs only when the FBX is available.");
    }

    [MenuItem("Tools/Building/Link or Rebuild W21 Stair")]
    public static void Rebuild()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!model) throw new InvalidOperationException("Upload the finished W21 FBX to " + ModelPath + " before linking.");
        var definition = PrepareDefinition(); LinkVisual(definition,model);
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if (!catalog) throw new InvalidOperationException("Building catalog is missing.");
        var presets = new List<BuildDefinition>(catalog.presets);
        int index = presets.FindIndex(p => p != null && p.contentId == ContentId);
        if (index < 0) presets.Add(definition); else presets[index] = definition;
        catalog.presets = presets.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
    }

    public static BuildDefinition PrepareDefinition()
    {
        Directory.CreateDirectory(ModelFolder); Directory.CreateDirectory(BuildingPrototypeSetup.Folder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var generated = MakeWalkingHull();
        var collision = AssetDatabase.LoadAssetAtPath<Mesh>(ColliderPath);
        if (!collision) { collision = generated; AssetDatabase.CreateAsset(collision,ColliderPath); }
        else { EditorUtility.CopySerialized(generated,collision); Object.DestroyImmediate(generated); EditorUtility.SetDirty(collision); }
        var definition = AssetDatabase.LoadAssetAtPath<BuildDefinition>(DefinitionPath);
        if (!definition) { definition = ScriptableObject.CreateInstance<BuildDefinition>(); AssetDatabase.CreateAsset(definition,DefinitionPath); }
        definition.contentId = ContentId; definition.displayName = "W21 half-storey wood stair";
        definition.kind = BuildPartKind.Stair; definition.sizeUnits = new Vector3Int(5,6,8);
        definition.minimumUnits = Vector3Int.zero; definition.wallEndInsetUnits = 0;
        definition.collisionMesh = collision;
        definition.material = AssetDatabase.LoadAssetAtPath<Material>(SplitPlankWallSetup.MaterialPath);
        if (!definition.material) throw new InvalidOperationException("The existing opaque wood material is missing.");
        SavePrefab(definition); EditorUtility.SetDirty(definition);
        return definition;
    }

    public static BuildDefinition CreateIfModelAvailable()
    {
        var definition = PrepareDefinition();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!model) return null;
        LinkVisual(definition,model); return definition;
    }

    private static void LinkVisual(BuildDefinition definition, GameObject model)
    {
        var generated = WattleWallSetup.BakeStaticMesh(model,Size,"W21 half-storey stair",out string inspection);
        try
        {
            // Bounds alone cannot distinguish a staircase facing the wrong way.
            var vertices = generated.vertices; var normals = generated.normals;
            bool topAtRear = false, lowAtToe = false;
            for (int i = 0; i < vertices.Length; ++i)
            {
                topAtRear |= vertices[i].y > Rise-.02f && vertices[i].z > Run-ExitLength-.02f && normals[i].y > .5f;
                lowAtToe |= vertices[i].y < .10f && vertices[i].z < .30f;
            }
            if (!topAtRear || !lowAtToe)
                throw new InvalidOperationException("W21 must ascend along Unity +Z: low foot near Z=0, final tread at Y=1.5 in Z=1.75..2. Check Blender/export orientation before linking.");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(VisualPath);
            if (!mesh) { mesh = generated; AssetDatabase.CreateAsset(mesh,VisualPath); generated = null; }
            else { EditorUtility.CopySerialized(generated,mesh); EditorUtility.SetDirty(mesh); }
            definition.mesh = mesh; SavePrefab(definition); EditorUtility.SetDirty(definition);
            Debug.Log(inspection);
        }
        finally { if (generated != null) Object.DestroyImmediate(generated); }
    }

    private static void SavePrefab(BuildDefinition definition)
    {
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(definition.displayName);
        try
        {
            root.layer = GameplayLayers.WorldSolid; root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); root.transform.localScale = Vector3.one;
            if (definition.mesh)
            {
                if (!root.TryGetComponent<MeshFilter>(out var filter)) filter = root.AddComponent<MeshFilter>();
                if (!root.TryGetComponent<MeshRenderer>(out var renderer)) renderer = root.AddComponent<MeshRenderer>();
                filter.sharedMesh = definition.mesh; renderer.sharedMaterial = definition.material;
            }
            if (root.TryGetComponent<BoxCollider>(out var box)) Object.DestroyImmediate(box);
            if (!root.TryGetComponent<MeshCollider>(out var collider)) collider = root.AddComponent<MeshCollider>();
            collider.convex = true; collider.isTrigger = false; collider.sharedMesh = definition.collisionMesh;
            if (!root.TryGetComponent<SmoothWalkSurface>(out _)) root.AddComponent<SmoothWalkSurface>();
            definition.authoringPrefab = PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally { if (existing) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
    }

    public static Mesh MakeWalkingHull()
    {
        // A closed convex prism: no vertical lip at the toe, and a flat exit
        // at the exact upper walking elevation. It remains within the 2 m run.
        Vector2[] profile = { new(0,0), new(Rise,Run-ExitLength), new(Rise,Run), new(0,Run) };
        var vertices = new Vector3[8];
        for (int side = 0; side < 2; ++side)
            for (int i = 0; i < 4; ++i) vertices[side*4+i] = new Vector3(side*Width,profile[i].x,profile[i].y);
        var indices = new List<int>();
        void Triangle(int a,int b,int c)
        {
            Vector3 normal = Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
            Vector3 outward = (vertices[a]+vertices[b]+vertices[c])/3 - Size*.5f;
            if (Vector3.Dot(normal,outward) < 0) (b,c) = (c,b);
            indices.Add(a); indices.Add(b); indices.Add(c);
        }
        Triangle(0,1,2); Triangle(0,2,3); Triangle(4,5,6); Triangle(4,6,7);
        for (int i = 0; i < 4; ++i) { int next=(i+1)%4; Triangle(i,next,next+4); Triangle(i,next+4,i+4); }
        var mesh = new Mesh { name = "W21 walking ramp (8 vertices, 12 triangles, 0.25 m flat exit)" };
        mesh.vertices = vertices; mesh.SetTriangles(indices,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    public static void PrepareBatch()
    {
        try { Prepare(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
