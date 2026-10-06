using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Passive UI Toolkit view. Does not query physics, inspect tools or execute interactions.</summary>
[RequireComponent(typeof(UIDocument))]
public sealed class QueryHudView : MonoBehaviour
{
    [SerializeField] private QueryHudStyle style;
    private UIDocument document;
    private VisualElement root, card;
    private Label nameLabel, descriptionLabel;
    private bool visible;
    private QueryHudData displayed;

    public QueryHudStyle Style => style;
    public bool IsVisible => visible;

    private void OnEnable() => Bind();
    private void OnDisable() => Hide();

    public bool Bind()
    {
        document = GetComponent<UIDocument>();
        root = document != null && document.isActiveAndEnabled ? document.rootVisualElement : null;
        if (root == null)
        {
            card = null;
            visible = false;
            return false;
        }
        card = root.Q<VisualElement>("query-card");
        nameLabel = root.Q<Label>("query-name");
        descriptionLabel = root.Q<Label>("query-description");
        visible = false;
        if (card == null || nameLabel == null || descriptionLabel == null)
            return false;
        IgnorePicking(root);
        ApplyStyle();
        card.style.display = DisplayStyle.None;
        return true;
    }

    private static void IgnorePicking(VisualElement element)
    {
        element.pickingMode = PickingMode.Ignore;
        foreach (var child in element.Children()) IgnorePicking(child);
    }

    public void ApplyStyle()
    {
        if (card == null || style == null) return;
        card.style.backgroundColor = style.PanelColor;
        card.style.borderTopLeftRadius = style.CornerRadius;
        card.style.borderTopRightRadius = style.CornerRadius;
        card.style.borderBottomLeftRadius = style.CornerRadius;
        card.style.borderBottomRightRadius = style.CornerRadius;
        nameLabel.style.color = style.AccentColor;
        descriptionLabel.style.color = style.DescriptionColor;
        nameLabel.style.fontSize = style.NameSize;
        descriptionLabel.style.fontSize = style.DescriptionSize;
        if (style.RegularFont != null)
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(style.RegularFont);
        if (style.NameFont != null)
            nameLabel.style.unityFontDefinition = FontDefinition.FromSDFFont(style.NameFont);
    }

    public void Show(QueryHudData data)
    {
        if (document != null && !document.isActiveAndEnabled)
        {
            Hide();
            return;
        }
        // UIDocument recreates its tree when re-enabled; never retain detached visual elements.
        if (document == null || root != document.rootVisualElement || card == null || card.panel == null)
            if (!Bind()) return;
        if (!visible || displayed.Name != data.Name) nameLabel.text = data.Name ?? string.Empty;
        if (!visible || displayed.Description != data.Description)
        {
            descriptionLabel.text = data.Description ?? string.Empty;
            descriptionLabel.style.display = string.IsNullOrWhiteSpace(data.Description) ? DisplayStyle.None : DisplayStyle.Flex;
        }
        displayed = data;
        if (!visible) card.style.display = DisplayStyle.Flex;
        visible = true;
    }

    public void Hide()
    {
        if (card != null) card.style.display = DisplayStyle.None;
        visible = false;
        displayed = default;
    }
}
