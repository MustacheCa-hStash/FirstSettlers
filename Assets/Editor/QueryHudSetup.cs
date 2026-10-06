using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Explicit, repeatable asset setup. Never runs automatically on import or enters Play mode.</summary>
public static class QueryHudSetup
{
    public const string CardPath = "Assets/ScriptableObjects/WorldObjects/SpruceTree.asset";
    public const string IconPath = "Assets/UI/Icons/SpruceTree.png";
    public const string StylePath = "Assets/UI/QueryHud/WoodlandOriginal.asset";
    public const string PanelPath = "Assets/UI/QueryHud/QueryHudPanelSettings.asset";
    public const string LayoutPath = "Assets/UI/QueryHud/QueryHud.uxml";
    public const string PrefabPath = "Assets/Prefabs/UI/QueryHud.prefab";
    public const string ScenePath = "Assets/Scenes/SmearScene.unity";
    private const string SprucePrefabPath = "Assets/Prefabs/Spruce_LOD0_v06.prefab";

    [MenuItem("Tools/UI/Install Spruce Query HUD")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Install the query HUD outside Play mode.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CreateAssets();
        WireSpruce();
        WireScene();
        AssetDatabase.SaveAssets();
        Debug.Log("QUERY HUD INSTALLED: spruce datacard, 256px sprite, Segoe UI Woodland style and local-player HUD in SmearScene.");
    }

    public static void InstallBatch()
    {
        try
        {
            CreateAssets();
            WireSpruce();
            WireScene();
            AssetDatabase.SaveAssets();
            QueryHudValidation.Run();
            Debug.Log("QUERY HUD INSTALL AND VALIDATION PASS");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static T GetOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void CreateAssets()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Generated spruce icon is missing: " + IconPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var spriteSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(spriteSettings);
        spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(spriteSettings);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 256;
        importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
        // Downsample the generated source using Unity's own importer, retaining alpha. No large source ships in the HUD.
        bool reduceSource = sourceWidth > 256 || sourceHeight > 256;
        importer.isReadable = reduceSource;
        importer.SaveAndReimport();
        if (reduceSource)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            File.WriteAllBytes(IconPath, texture.EncodeToPNG());
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        if (sprite == null) throw new InvalidOperationException("Spruce icon was not imported as a Sprite.");

        var card = GetOrCreate<WorldObjectDefinition>(CardPath);
        var cardFields = new SerializedObject(card);
        cardFields.FindProperty("contentId").stringValue = "tree.spruce";
        cardFields.FindProperty("displayName").stringValue = "Spruce tree";
        cardFields.FindProperty("shortDescription").stringValue = "Wood source";
        cardFields.FindProperty("icon").objectReferenceValue = sprite;
        cardFields.ApplyModifiedPropertiesWithoutUndo();

        var regular = GetSystemFont("Regular", "Assets/UI/QueryHud/Fonts/SegoeUI-Regular.asset");
        var semibold = GetSystemFont("Semibold", "Assets/UI/QueryHud/Fonts/SegoeUI-Semibold.asset");
        var style = GetOrCreate<QueryHudStyle>(StylePath);
        var styleFields = new SerializedObject(style);
        styleFields.FindProperty("regularFont").objectReferenceValue = regular;
        styleFields.FindProperty("nameFont").objectReferenceValue = semibold;
        styleFields.FindProperty("textScale").floatValue = .95f;
        styleFields.FindProperty("panelOpacity").floatValue = .75f;
        styleFields.FindProperty("cornerRadius").floatValue = 14f;
        styleFields.ApplyModifiedPropertiesWithoutUndo();

        var panel = GetOrCreate<PanelSettings>(PanelPath);
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1920, 1080);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = .5f;
        panel.sortingOrder = 20;
        panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI/QueryHud/QueryHudTheme.tss");
        EditorUtility.SetDirty(panel);
        var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath);
        if (layout == null || panel.themeStyleSheet == null)
            throw new InvalidOperationException("Query HUD layout or theme failed to import.");

        EnsureFolder("Assets/Prefabs/UI");
        bool loadedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        GameObject prefabRoot = loadedPrefab
            ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Query HUD");
        try
        {
            var document = prefabRoot.GetComponent<UIDocument>() ?? prefabRoot.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = layout;
            var view = prefabRoot.GetComponent<QueryHudView>() ?? prefabRoot.AddComponent<QueryHudView>();
            SetReference(view, "style", style);
            var presenter = prefabRoot.GetComponent<QueryHudPresenter>() ?? prefabRoot.AddComponent<QueryHudPresenter>();
            SetReference(presenter, "view", view);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        }
        finally
        {
            if (loadedPrefab) PrefabUtility.UnloadPrefabContents(prefabRoot);
            else Object.DestroyImmediate(prefabRoot);
        }
    }

    private static FontAsset GetSystemFont(string face, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
        if (existing != null) return existing;
        EnsureFolder("Assets/UI/QueryHud/Fonts");
        // Dynamic OS resolves installed Segoe UI on the Windows playtest machine; no Microsoft font file is copied.
        var font = FontAsset.CreateFontAsset("Segoe UI", face, 48, 6, GlyphRenderMode.SDFAA);
        if (font == null) throw new InvalidOperationException("Installed Segoe UI " + face + " could not be resolved.");
        font.name = "Segoe UI " + face;
        font.atlasPopulationMode = AtlasPopulationMode.DynamicOS;
        font.isMultiAtlasTexturesEnabled = true;
        font.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /.,:-");
        AssetDatabase.CreateAsset(font, path);
        if (font.material != null) AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var atlas in font.atlasTextures)
            if (atlas != null) AssetDatabase.AddObjectToAsset(atlas, font);
        EditorUtility.SetDirty(font);
        return font;
    }

    private static void WireSpruce()
    {
        var prefab = PrefabUtility.LoadPrefabContents(SprucePrefabPath);
        try
        {
            var authoring = prefab.GetComponent<TreeGameplayAuthoring>();
            if (authoring == null) throw new InvalidOperationException("Spruce prefab has no TreeGameplayAuthoring.");
            SetReference(authoring, "queryDefinition", AssetDatabase.LoadAssetAtPath<WorldObjectDefinition>(CardPath));
            PrefabUtility.SaveAsPrefabAsset(prefab, SprucePrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }

    private static void WireScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var queries = Object.FindObjectsByType<PlayerQuery>(FindObjectsInactive.Include);
        if (queries.Length != 1) throw new InvalidOperationException("Expected exactly one local PlayerQuery in SmearScene.");
        var query = queries[0];
        var player = query.transform.parent;
        Transform uiRoot = player.Find("UI");
        if (uiRoot == null)
        {
            uiRoot = new GameObject("UI").transform;
            uiRoot.SetParent(player, false);
        }
        var presenter = uiRoot.GetComponentInChildren<QueryHudPresenter>(true);
        if (presenter == null)
        {
            var hud = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), uiRoot);
            presenter = hud.GetComponent<QueryHudPresenter>();
        }
        SetReference(presenter, "query", query);
        PrefabUtility.RecordPrefabInstancePropertyModifications(presenter);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetReference(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
