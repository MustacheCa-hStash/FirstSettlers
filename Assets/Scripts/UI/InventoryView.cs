using System;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>Empty inventory presentation. No item ownership, sample stacks or crafting operations.</summary>
public sealed class InventoryView : IDisposable
{
    public const int BackpackSlots = 27, HotbarSlots = 9, ArmorSlots = 4;
    private readonly GameObject host;
    private readonly PanelSettings settings;
    private readonly VisualElement root, window, hud;
    private readonly VisualElement[] inventoryHotbar = new VisualElement[HotbarSlots];
    private readonly VisualElement[] gameplayHotbar = new VisualElement[HotbarSlots];
    private readonly Action<int> select;
    private bool disposed;
    public VisualElement Root => root;
    public PanelSettings PanelSettings => settings;

    public InventoryView(Transform parent, Action<int> select)
    {
        this.select = select;
        var template = Resources.Load<VisualTreeAsset>("Inventory/Inventory");
        var panel = Resources.Load<PanelSettings>("Building/BuildPanel");
        if (template == null || panel == null) throw new InvalidOperationException("Inventory UI resources are missing.");
        settings = UnityEngine.Object.Instantiate(panel);
        settings.name = "Local inventory panel";
        settings.scaleMode = PanelScaleMode.ConstantPixelSize; settings.scale = 1; settings.sortingOrder = 40;
        host = new GameObject("Inventory UI"); host.transform.SetParent(parent, false);
        var document = host.AddComponent<UIDocument>();
        document.panelSettings = settings; document.visualTreeAsset = template; document.sortingOrder = 40;
        root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
        window = root.Q<VisualElement>("inventory-window");
        hud = root.Q<VisualElement>("inventory-hud");
        var font = Resources.Load<FontAsset>("Building/UI/Fonts/StoneHeading");
        if (font != null) root.Q<Label>("inventory-crafting-title").style.unityFontDefinition = FontDefinition.FromSDFFont(font);
        Populate(root.Q<VisualElement>("inventory-backpack"), "backpack", BackpackSlots);
        Populate(root.Q<VisualElement>("inventory-armor"), "armor", ArmorSlots);
        Populate(root.Q<VisualElement>("inventory-crafting"), "crafting", 4);
        root.Q<VisualElement>("inventory-offhand").Add(Slot("offhand"));
        root.Q<VisualElement>("inventory-output").Add(Slot("crafting-output"));
        root.Q<VisualElement>("hud-offhand").Add(Slot("hud-offhand-slot"));
        for (int i = 0; i < HotbarSlots; i++)
        {
            int index = i;
            inventoryHotbar[i] = Slot("inventory-hotbar-" + i);
            inventoryHotbar[i].RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0) this.select?.Invoke(index);
                e.StopPropagation();
            });
            root.Q<VisualElement>("inventory-hotbar").Add(inventoryHotbar[i]);
            gameplayHotbar[i] = Slot("hud-hotbar-" + i);
            root.Q<VisualElement>("hud-hotbar").Add(gameplayHotbar[i]);
        }
        root.Q<Button>("inventory-book").Add(new InventoryGlyph(true));
        root.Q<VisualElement>("inventory-craft-arrow").Add(new InventoryGlyph(false));
        IgnorePicking(hud);
        SetSelection(0); SetState(false, true);
        root.RegisterCallback<GeometryChangedEvent>(e => Fit(e.newRect.size));
        // WASD/space/Tab remain hardware commands; the empty UI has no navigation actions.
        window.RegisterCallback<NavigationMoveEvent>(e => e.StopPropagation());
        window.RegisterCallback<NavigationSubmitEvent>(e => e.StopPropagation());
    }

    private static void Populate(VisualElement container, string prefix, int count)
    {
        for (int i = 0; i < count; i++) container.Add(Slot(prefix + "-" + i));
    }

    private static VisualElement Slot(string name)
    {
        var slot = new VisualElement { name = name, focusable = false };
        slot.AddToClassList("inventory-slot");
        var surface = new VisualElement { pickingMode = PickingMode.Ignore };
        surface.AddToClassList("inventory-slot-surface"); slot.Add(surface);
        return slot;
    }

    private static void IgnorePicking(VisualElement element)
    {
        element.pickingMode = PickingMode.Ignore;
        foreach (var child in element.Children()) IgnorePicking(child);
    }

    private void Fit(Vector2 size)
    {
        if (size.x <= 0 || size.y <= 0) return;
        float scale = Mathf.Min(size.x / 1920f, size.y / 1080f);
        window.style.scale = hud.style.scale = new Scale(new Vector3(scale, scale, 1));
        window.style.left = (size.x - 704) * .5f; window.style.top = (size.y - 648) * .5f;
        hud.style.left = (size.x - 756) * .5f;
        // Transform origin is center; offset the unscaled frame to keep a 2% bottom margin.
        hud.style.top = size.y - size.y * .02f - 88 * (1 + scale) * .5f;
    }

    public void SetSelection(int index)
    {
        for (int i = 0; i < HotbarSlots; i++)
        {
            inventoryHotbar[i].EnableInClassList("selected", i == index);
            gameplayHotbar[i].EnableInClassList("selected", i == index);
        }
    }

    public void SetState(bool inventoryVisible, bool hudVisible)
    {
        window.style.display = inventoryVisible ? DisplayStyle.Flex : DisplayStyle.None;
        hud.style.display = hudVisible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (Application.isPlaying) { UnityEngine.Object.Destroy(host); UnityEngine.Object.Destroy(settings); }
        else { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(settings); }
    }
}

/// <summary>Small UI symbols; the book has no action and the crafting arrow has no computed result.</summary>
internal sealed class InventoryGlyph : VisualElement
{
    private readonly bool book;
    public InventoryGlyph(bool book)
    {
        this.book = book; pickingMode = PickingMode.Ignore;
        style.width = style.height = Length.Percent(100);
        generateVisualContent += Draw;
    }
    private void Draw(MeshGenerationContext context)
    {
        var p = context.painter2D;
        var scale = new Vector2(contentRect.width / 32, contentRect.height / 32);
        void Polygon(Color fill, params Vector2[] points)
        {
            p.fillColor = fill; p.BeginPath(); p.MoveTo(Vector2.Scale(points[0], scale));
            for (int i = 1; i < points.Length; i++) p.LineTo(Vector2.Scale(points[i], scale));
            p.ClosePath(); p.Fill();
        }
        if (!book)
        {
            Polygon(new Color(.57f,.53f,.44f), new(2,12),new(19,12),new(19,6),new(30,16),new(19,26),new(19,20),new(2,20));
            return;
        }
        Polygon(new Color(.21f,.29f,.23f),new(3,9),new(20,3),new(29,13),new(29,23),new(12,29),new(3,20));
        Polygon(new Color(.87f,.82f,.66f),new(5,18),new(13,25),new(27,20),new(27,24),new(12,29),new(5,22));
        Polygon(new Color(.31f,.46f,.31f),new(3,9),new(20,3),new(29,13),new(12,20));
        Polygon(new Color(.69f,.60f,.36f),new(15,5),new(18,4),new(26,14),new(23,15));
    }
}
