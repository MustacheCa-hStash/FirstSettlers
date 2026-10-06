using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Exercises the actual datacard-to-query-to-HUD path, including obstruction and pooled lifetime.</summary>
public static class QueryHudValidation
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException(message);
    }

    [MenuItem("Tools/UI/Validate Query HUD")]
    public static void Run()
    {
        checks = 0;
        ValidateAssetsAndScene();
        ValidateQueryAndView();
        QueryTargetValidation.Run();
        Debug.Log("QUERY HUD PASS: " + checks + " checks; authored spruce definition, sprite/font imports, scene wiring, " +
            "physical/query target presentation, obstruction/miss/lifetime clearing, fallback objects and unchanged-frame allocations.");
    }

    public static void RunBatch()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(QueryHudSetup.ScenePath);
            Run();
            QueryHudRenderValidation.Run();
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void SetReference(Object target, string property, Object value)
    {
        var fields = new SerializedObject(target);
        fields.FindProperty(property).objectReferenceValue = value;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ValidateAssetsAndScene()
    {
        var card = AssetDatabase.LoadAssetAtPath<WorldObjectDefinition>(QueryHudSetup.CardPath);
        Check(card != null && card.ContentId == "tree.spruce" && card.DisplayName == "Spruce tree" &&
            card.ShortDescription == "Wood source" && card.Icon != null, "Spruce datacard is missing or incorrect.");
        Check(card.Icon.texture.width == 256 && card.Icon.texture.height == 256, "HUD sprite is not 256 x 256.");
        var importer = (TextureImporter)AssetImporter.GetAtPath(QueryHudSetup.IconPath);
        Check(importer.textureType == TextureImporterType.Sprite && !importer.mipmapEnabled && !importer.isReadable,
            "Icon retains unnecessary mipmaps/readable CPU storage or is not a Sprite.");
        var style = AssetDatabase.LoadAssetAtPath<QueryHudStyle>(QueryHudSetup.StylePath);
        Check(style != null && Mathf.Approximately(style.TextScale, .95f) && Mathf.Approximately(style.PanelColor.a, .75f) &&
            Mathf.Approximately(style.CornerRadius, 14), "Woodland style does not match the selected settings.");
        Check(style.RegularFont != null && style.NameFont != null && style.RegularFont.atlasPopulationMode == AtlasPopulationMode.DynamicOS &&
            style.NameFont.atlasPopulationMode == AtlasPopulationMode.DynamicOS, "Segoe UI fonts were not configured as Dynamic OS.");
        Check(style.RegularFont.faceInfo.familyName == "Segoe UI" && style.NameFont.faceInfo.familyName == "Segoe UI",
            "The configured font is not Segoe UI.");
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(QueryHudSetup.PanelPath);
        Check(panel != null && panel.scaleMode == PanelScaleMode.ScaleWithScreenSize &&
            panel.referenceResolution == new Vector2Int(1920, 1080), "HUD resolution scaling is incorrect.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Spruce_LOD0_v06.prefab");
        Check(source.GetComponent<TreeGameplayAuthoring>().QueryDefinition == card, "Spruce source prefab has no datacard link.");
        var presenters = Object.FindObjectsByType<QueryHudPresenter>(FindObjectsInactive.Include);
        Check(presenters.Length == 1 && presenters[0].Query != null && presenters[0].View != null,
            "SmearScene must contain one explicitly wired local query HUD.");
        Check(presenters[0].transform.parent.name == "UI", "Query HUD is not beneath the local player's UI root.");
    }

    private static void ValidateQueryAndView()
    {
        const int seed = 391;
        const float width = 10;
        var origin = new Vector3(20000, 0, 20000);
        var coord = new ChunkCoord(2000, 2000);
        TreeInstanceData Placement(int cell) => new(new Vector3(-5, 0, -2), Quaternion.identity, Vector3.one,
            WorldFeatureVariant.SpruceTree, new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), 0,
            TreeId.Generated(seed, coord, TreePlacementSource.Forest, cell));
        var first = Placement(0);
        var registry = new TreeRegistry(seed, width);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Spruce_LOD0_v06.prefab");
        var settings = new TreeSettings { spruceTreePrefab = source, gameplay = new TreeGameplaySettings
            { activationRadiusChunks = 1, releaseRadiusChunks = 1.5f, queryCanopyActivationRadiusChunks = 1,
              queryCanopyReleaseRadiusChunks = 1.5f, enableCanopyQueries = true, activationBudgetMs = 0 } };
        using var manager = new TreeGameplayManager(registry, settings, width);
        var cameraObject = new GameObject("Query HUD validation camera");
        cameraObject.transform.position = origin + Vector3.up;
        var camera = cameraObject.AddComponent<Camera>();
        var query = cameraObject.AddComponent<PlayerQuery>();
        SetReference(query, "viewCamera", camera);
        var hud = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(QueryHudSetup.PrefabPath));
        var view = hud.GetComponent<QueryHudView>();
        var presenter = hud.GetComponent<QueryHudPresenter>();
        SetReference(presenter, "query", query);
        GameObject wall = null, rock = null;
        try
        {
            Check(view.Bind(), "Imported UXML did not create the required query elements.");
            registry.RegisterChunk(coord, new[] { first }, TreePlacementDetail.Detailed);
            manager.Update(origin, 0);
            query.Sample(); presenter.Refresh();
            var card = AssetDatabase.LoadAssetAtPath<WorldObjectDefinition>(QueryHudSetup.CardPath);
            Check(query.Current.HasTarget && query.Current.Target.Definition == card && query.Current.Target.Icon == card.Icon &&
                query.Current.Target.Description == "Wood source", "Actual tree template/proxy did not expose its datacard.");
            Check(query.Current.Target.TryGetData<TreeRecord>(out var record) && record.Id == first.id,
                "Datacard integration changed the tree's world identity.");
            var root = hud.GetComponent<UIDocument>().rootVisualElement;
            var visualCard = root.Q<VisualElement>("query-card");
            Check(view.IsVisible && root.Q<Label>("query-name").text == "Spruce tree" &&
                root.Q<Label>("query-description").text == "Wood source",
                "Query did not render the datacard's name/description.");
            Check(root.Q<Image>() == null && root.Q<Label>("query-name").style.color.value == view.Style.AccentColor,
                "Query HUD retains a sprite or does not apply the accent to the object name.");
            Check(Mathf.Approximately(visualCard.style.backgroundColor.value.a, .75f) &&
                Mathf.Approximately(visualCard.style.borderTopLeftRadius.value.value, 14) &&
                Mathf.Approximately(root.Q<Label>("query-name").style.fontSize.value.value, 16.15f),
                "Rendered view does not apply the selected opacity, radius and text scale.");
            Check(root.pickingMode == PickingMode.Ignore && visualCard.pickingMode == PickingMode.Ignore,
                "Passive query UI captures input.");
            var captured = query.Current.Target;
            Check(manager.TryGetProxy(first.id, out var proxy), "Fixture has no pooled tree proxy.");
            foreach (var collider in proxy.QueryCanopyColliders) collider.enabled = false;
            Physics.SyncTransforms(); query.Sample(); presenter.Refresh();
            Check(query.Current.Kind == PlayerQueryHitKind.Solid && query.Current.Target.Definition == card && view.IsVisible,
                "Physical trunk and canopy did not share presentation data.");
            wall = new GameObject("Query HUD validation obstruction") { layer = GameplayLayers.WorldSolid };
            wall.transform.position = origin + new Vector3(0, 1, 1);
            wall.AddComponent<BoxCollider>().size = Vector3.one * .2f;
            Physics.SyncTransforms(); query.Sample(); presenter.Refresh();
            Check(query.Current.HasHit && !query.Current.HasTarget && !view.IsVisible, "HUD identifies a target through an unnamed wall.");
            Object.DestroyImmediate(wall); wall = null;
            cameraObject.transform.rotation = Quaternion.Euler(0, 180, 0);
            Physics.SyncTransforms(); query.Sample(); presenter.Refresh();
            Check(!view.IsVisible, "HUD retains an old target after a miss.");
            cameraObject.transform.rotation = Quaternion.identity;
            query.Sample(); presenter.Refresh();
            registry.TrySetState(first.id, TreeState.Cut);
            presenter.Refresh();
            Check(!captured.IsValid && !view.IsVisible, "HUD retains a tree after its standing identity is invalidated.");
            manager.Update(origin, .2);
            registry.RegisterChunk(coord, new[] { Placement(1) }, TreePlacementDetail.Detailed);
            manager.Update(origin, .4); query.Sample(); presenter.Refresh();
            Check(!captured.IsValid && query.Current.HasTarget && view.IsVisible, "Pooled rebind recovers an old identity or fails to refresh HUD.");

            // Profile settled presentation separately from the ray/metadata allocation regression below.
            for (int i = 0; i < 20; i++) presenter.Refresh();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) presenter.Refresh();
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Unchanged-frame query HUD refresh allocates.");

            rock = new GameObject("Query HUD fallback rock") { layer = GameplayLayers.WorldSolid };
            rock.transform.position = origin + new Vector3(0, 1, 1);
            rock.AddComponent<BoxCollider>().size = Vector3.one * .3f;
            rock.AddComponent<RockQueryTarget>();
            Physics.SyncTransforms(); query.Sample(); presenter.Refresh();
            Check(view.IsVisible && root.Q<Label>("query-name").text == "Rock" &&
                root.Q<Label>("query-description").style.display.value == DisplayStyle.None,
                "Targets without datacards do not retain their existing name or hide missing fields.");
            var document = hud.GetComponent<UIDocument>();
            document.enabled = false; presenter.Refresh();
            Check(!view.IsVisible, "Disabling UIDocument leaves a visible label or accesses a destroyed visual tree.");
            document.enabled = true; presenter.Refresh();
            Check(view.IsVisible && document.rootVisualElement.Q<Label>("query-name").text == "Rock",
                "Re-enabling UIDocument does not rebind and restore the current target.");
            presenter.enabled = false; presenter.Refresh();
            Check(!view.IsVisible, "Disabling the presenter leaves its label visible.");
        }
        finally
        {
            if (wall != null) Object.DestroyImmediate(wall);
            if (rock != null) Object.DestroyImmediate(rock);
            Object.DestroyImmediate(hud);
            Object.DestroyImmediate(cameraObject);
        }
    }
}
