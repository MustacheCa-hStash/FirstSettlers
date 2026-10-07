using UnityEngine;
using UnityEngine.UIElements;

[CreateAssetMenu(menuName = "First Settlers/Building/Catalog")]
public sealed class BuildCatalog : ScriptableObject
{
    public BuildDefinition[] presets;
    public Material validPreview, invalidPreview;
    public PanelSettings panel;
    public VisualTreeAsset layout;
    public BuildDefinition Find(string id)
    {
        foreach (var definition in presets)
            if (definition != null && definition.contentId == id) return definition;
        return null;
    }
}
