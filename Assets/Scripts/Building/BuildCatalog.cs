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
            if (definition != null)
            {
                if(definition.contentId == id)return definition;
                if(definition.flipVariant!=null && definition.flipVariant.contentId==id)return definition.flipVariant;
            }
        return null;
    }
}
