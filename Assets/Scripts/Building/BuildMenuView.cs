using System;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>UI Toolkit picker and passive placement HUD. Input remains owned by LocalPlayerInput.</summary>
public sealed class BuildMenuView : IDisposable
{
    private readonly GameObject obj;
    private readonly VisualElement menu, hud, root;
    private readonly Label heading, status;
    private string lastHeading, lastStatus;
    public BuildMenuView(BuildCatalog catalog, Transform parent, Action<BuildDefinition> select)
    {
        obj = new GameObject("Building UI"); obj.transform.SetParent(parent, false);
        var document = obj.AddComponent<UIDocument>(); document.panelSettings = catalog.panel; document.visualTreeAsset = catalog.layout;
        document.sortingOrder = 30;
        root = document.rootVisualElement; root.pickingMode = PickingMode.Ignore;
        menu = root.Q<VisualElement>("build-menu"); hud = root.Q<VisualElement>("build-hud");
        heading = root.Q<Label>("build-heading"); status = root.Q<Label>("build-status");
        foreach (var definition in catalog.presets)
        {
            var chosen = definition;
            var button = new Button(() => select(chosen)) { name = chosen.contentId,
                text = $"{chosen.displayName}\n{chosen.LocalBounds.size.x:0.##} × {chosen.LocalBounds.size.z:0.##} m · {chosen.LocalBounds.size.y:0.##} m high" };
            button.AddToClassList("build-preset");
            root.Q<VisualElement>("build-presets").Add(button);
        }
        IgnorePicking(hud); SetState(false, false);
    }
    private static void IgnorePicking(VisualElement node) { node.pickingMode = PickingMode.Ignore; foreach (var child in node.Children()) IgnorePicking(child); }
    public void SetState(bool active, bool choosing)
    {
        menu.style.display = active && choosing ? DisplayStyle.Flex : DisplayStyle.None;
        hud.style.display = active && !choosing ? DisplayStyle.Flex : DisplayStyle.None;
    }
    public void Show(string title, string message, bool valid)
    {
        if (lastHeading != title) heading.text = lastHeading = title;
        if (lastStatus != message) status.text = lastStatus = message;
        status.EnableInClassList("build-error", !valid);
    }
    public void Dispose() { if (obj != null) BuildLifetime.Destroy(obj); }
}
