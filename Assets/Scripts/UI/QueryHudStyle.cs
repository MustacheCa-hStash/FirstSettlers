using UnityEngine;
using UnityEngine.TextCore.Text;

/// <summary>Query HUD appearance in logical pixels at the panel's reference resolution.</summary>
[CreateAssetMenu(menuName = "First Settlers/UI/Query HUD Style")]
public sealed class QueryHudStyle : ScriptableObject
{
    [SerializeField] private FontAsset regularFont;
    [SerializeField] private FontAsset nameFont;
    [SerializeField, Range(.5f, 2f)] private float textScale = .95f;
    [SerializeField, Range(0f, 1f)] private float panelOpacity = .75f;
    [SerializeField, Min(0f)] private float cornerRadius = 14f;
    [SerializeField] private Color panelColor = new Color32(38, 53, 48, 255);
    [SerializeField] private Color descriptionColor = new Color32(198, 207, 190, 255);
    [SerializeField] private Color accentColor = new Color32(217, 191, 123, 255);
    [SerializeField, Min(1f)] private float nameSize = 17f;
    [SerializeField, Min(1f)] private float descriptionSize = 13f;

    public FontAsset RegularFont => regularFont;
    public FontAsset NameFont => nameFont != null ? nameFont : regularFont;
    public float TextScale => textScale;
    public float CornerRadius => cornerRadius;
    public Color PanelColor { get { var color = panelColor; color.a = panelOpacity; return color; } }
    public Color DescriptionColor => descriptionColor;
    public Color AccentColor => accentColor;
    public float NameSize => nameSize * textScale;
    public float DescriptionSize => descriptionSize * textScale;
}
