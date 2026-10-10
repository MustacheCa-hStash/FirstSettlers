using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Explicit asset authoring. Batch runs use an isolated project and never open the user's scene.</summary>
public static class BuildingPrototypeSetup
{
    public const string Folder = "Assets/Resources/Building";
    public const string CatalogPath = Folder + "/PrototypeCatalog.asset";
    [MenuItem("Tools/Building/Install Prototype Assets and Player")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install outside Play mode.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CreateAssets();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity");
        foreach (var root in scene.GetRootGameObjects())
            foreach (var input in root.GetComponentsInChildren<LocalPlayerInput>(true))
            {
                var controller = input.GetComponent<BuildingController>() ?? input.gameObject.AddComponent<BuildingController>();
                var properties = new SerializedObject(controller);
                properties.FindProperty("viewCamera").objectReferenceValue = input.GetComponentInParent<CharacterMotor>().GetComponentInChildren<Camera>();
                properties.ApplyModifiedPropertiesWithoutUndo();
                var inputProperties = new SerializedObject(input);
                inputProperties.FindProperty("building").objectReferenceValue = controller;
                inputProperties.ApplyModifiedPropertiesWithoutUndo();
            }
        EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
    }
    public static void CreateAssets()
    {
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit shader is required by the prototype.");
        var wallMaterial = Material("PlainWall", shader, new Color(.63f, .56f, .43f));
        var floorMaterial = Material("PlainFloor", shader, new Color(.43f, .36f, .25f));
        var foundationMaterial = Material("PlainFoundation", shader, new Color(.43f, .43f, .39f));
        var valid = Material("PreviewValid", shader, new Color(.3f, .85f, .55f, .45f), true);
        var invalid = Material("PreviewInvalid", shader, new Color(.95f, .3f, .2f, .45f), true);
        var wall = Part("wall", "Plain wall", BuildPartKind.Wall, new Vector3Int(14, 11, 1), Vector3Int.zero, wallMaterial);
        var floor = Part("floor", "Timber floor", BuildPartKind.Floor, new Vector3Int(16, 1, 16), new Vector3Int(0, -1, 0), floorMaterial);
        var foundation = Part("foundation", "Stone foundation", BuildPartKind.Foundation, new Vector3Int(16, 2, 16), new Vector3Int(0, -2, 0), foundationMaterial);
        BayPostSetup.MigrateLegacyAssets();
        var corner = Part("corner", "Bay post", BuildPartKind.Corner, new Vector3Int(1, 11, 1), Vector3Int.zero, wallMaterial, "bay-post");
        SplitPlankWallSetup.Apply(wall);
        FloorFramingSetup.Apply(floor);
        BayPostSetup.Apply(corner, wall.material);
        var wattle = WattleWallSetup.Create(wall.material);
        ModularWoodSetup.Link(wall,wattle,corner);
        var panel = Asset<PanelSettings>(Folder + "/BuildPanel.asset");
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1920, 1080);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; panel.match = .5f; panel.sortingOrder = 30;
        panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Folder + "/BuildTheme.tss");
        var catalog = Asset<BuildCatalog>(CatalogPath);
        var presets = new List<BuildDefinition> { wall, floor, foundation, corner, wattle };
        var stair = W21StairSetup.CreateIfModelAvailable();
        if (stair != null) presets.Add(stair);
        presets.AddRange(ThatchRoofSetup.CreateIfModelsAvailable());
        catalog.presets = presets.ToArray(); catalog.validPreview = valid; catalog.invalidPreview = invalid;
        catalog.panel = panel; catalog.layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Folder + "/BuildMenu.uxml");
        if (catalog.layout == null || panel.themeStyleSheet == null) throw new InvalidOperationException("Building UI failed to import.");
        EditorUtility.SetDirty(panel); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
    }
    private static T Asset<T>(string path) where T : ScriptableObject
    {
        var value = AssetDatabase.LoadAssetAtPath<T>(path);
        if (value == null) { value = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(value, path); }
        return value;
    }
    private static Material Material(string name, Shader shader, Color color, bool transparent = false)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.enableInstancing = true; material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .05f);
        if (transparent)
        {
            material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000; material.SetShaderPassEnabled("ShadowCaster", false);
        }
        EditorUtility.SetDirty(material); return material;
    }
    private static BuildDefinition Part(string id, string name, BuildPartKind kind, Vector3Int size, Vector3Int minimum, Material material, string assetId = null)
    {
        assetId ??= id;
        var definition = Asset<BuildDefinition>(Folder + "/" + assetId + ".asset");
        definition.contentId = "build.prototype." + id; definition.displayName = name; definition.kind = kind;
        definition.sizeUnits = size; definition.minimumUnits = minimum; definition.material = material;
        definition.wallEndInsetUnits = kind == BuildPartKind.Wall ? 1 : 0;
        string meshPath = Folder + "/" + assetId + "-mesh.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        // Reauthor generated geometry in place when the dimensional contract changes; preserve asset GUIDs.
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            var generated = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh); generated.name = name + " (exact bounds)";
            var vertices = generated.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = Vector3.Scale(vertices[i] + Vector3.one * .5f, definition.LocalBounds.size) + definition.LocalBounds.min;
            generated.vertices = vertices; generated.RecalculateBounds();
            if (mesh == null) { mesh = generated; AssetDatabase.CreateAsset(mesh, meshPath); }
            else { EditorUtility.CopySerialized(generated, mesh); Object.DestroyImmediate(generated); EditorUtility.SetDirty(mesh); }
        }
        finally { Object.DestroyImmediate(cube); }
        definition.mesh = mesh;
        var obj = new GameObject(name) { layer = GameplayLayers.WorldSolid };
        try
        {
            obj.AddComponent<MeshFilter>().sharedMesh = mesh; obj.AddComponent<MeshRenderer>().sharedMaterial = material;
            var collider = obj.AddComponent<BoxCollider>(); collider.center = definition.LocalBounds.center; collider.size = definition.LocalBounds.size;
            definition.authoringPrefab = PrefabUtility.SaveAsPrefabAsset(obj, Folder + "/" + assetId + ".prefab");
        }
        finally { Object.DestroyImmediate(obj); }
        EditorUtility.SetDirty(definition); return definition;
    }
}
