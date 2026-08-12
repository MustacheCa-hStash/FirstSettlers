using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class RenderStatsDebugOverlay : MonoBehaviour
{
    [SerializeField] WorldManager worldManager;
    [SerializeField] TextMeshProUGUI debugText;
    [SerializeField] Canvas debugCanvas;
    [SerializeField] Key toggleKey = Key.F4;
    [SerializeField] bool visibleOnStart;
    [SerializeField] float refreshInterval = 0.25f;

    private readonly StringBuilder builder = new StringBuilder(768);
    private bool isVisible;
    private float nextRefreshTime;

    void Awake()
    {
        EnsureOverlay();
        isVisible = visibleOnStart;

        if (debugCanvas != null)
            debugCanvas.enabled = isVisible;
    }

    void Update()
    {
        HandleToggleInput();

        if (!isVisible || Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshText();
    }

    private void EnsureOverlay()
    {
        if (debugText != null)
        {
            if (debugCanvas == null)
                debugCanvas = debugText.GetComponentInParent<Canvas>();

            return;
        }

        GameObject canvasObject = new GameObject("Render Stats Debug Canvas");
        canvasObject.transform.SetParent(transform, false);

        debugCanvas = canvasObject.AddComponent<Canvas>();
        debugCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        debugCanvas.sortingOrder = 1001;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject panelObject = new GameObject("Panel");
        panelObject.transform.SetParent(canvasObject.transform, false);

        RectTransform panelRect = panelObject.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(12f, -12f);
        panelRect.sizeDelta = new Vector2(520f, 360f);

        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.72f);
        panelImage.raycastTarget = false;

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(panelObject.transform, false);

        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 10f);
        textRect.offsetMax = new Vector2(-10f, -10f);

        debugText = textObject.AddComponent<TextMeshProUGUI>();
        debugText.alignment = TextAlignmentOptions.TopLeft;
        debugText.color = Color.white;
        debugText.fontSize = 15f;
        debugText.raycastTarget = false;
        debugText.text = string.Empty;
    }

    private void HandleToggleInput()
    {
        if (Keyboard.current[toggleKey].wasPressedThisFrame)
            SetVisible(!isVisible);
    }

    private void SetVisible(bool visible)
    {
        isVisible = visible;

        if (debugCanvas != null)
            debugCanvas.enabled = visible;

        if (visible)
        {
            nextRefreshTime = 0f;
            RefreshText();
        }
    }

    private void RefreshText()
    {
        if (debugText == null)
            return;

        builder.Clear();
        builder.AppendLine("Render Stats Debug");

        if (worldManager == null)
        {
            builder.AppendLine("World Manager: missing");
            debugText.text = builder.ToString();
            return;
        }

        WorldRenderStatsDebugInfo stats = worldManager.GetVisibleRenderStatsDebugInfo();

        builder.Append("Visible Chunks: ");
        builder.Append(stats.VisibleChunkCount);
        builder.Append(" (terrain ");
        builder.Append(stats.VisibleChunkWithTerrainMeshCount);
        builder.AppendLine(")");

        builder.Append("LOD: ");
        builder.Append("0=");
        builder.Append(stats.CurrentLOD0ChunkCount);
        builder.Append(" 1=");
        builder.Append(stats.CurrentLOD1ChunkCount);
        builder.Append(" 2=");
        builder.Append(stats.CurrentLOD2ChunkCount);
        builder.Append(" 3=");
        builder.Append(stats.CurrentLOD3ChunkCount);
        builder.Append(" 4+=");
        builder.AppendLine(stats.CurrentLOD4PlusChunkCount.ToString());

        builder.Append("Total: ");
        AppendCompactNumber(stats.TotalVertices);
        builder.Append(" verts / ");
        AppendCompactNumber(stats.TotalTriangles);
        builder.AppendLine(" tris");

        builder.AppendLine();
        AppendCategory("Terrain", stats.Terrain);
        AppendCategory("Lake", stats.Lake);
        AppendCategory("River", stats.River);
        AppendCategory("Grass", stats.Grass);
        AppendCategory("Billboard Grass", stats.BillboardGrass);
        AppendCategory("Flowers", stats.Flowers);
        AppendCategory("Tree Billboards", stats.TreeBillboards);
        AppendCategory("Tree GameObjects", stats.TreeGameObjects);
        AppendCategory("Bush GameObjects", stats.BushGameObjects);
        AppendCategory("Rock GameObjects", stats.RockGameObjects);

        debugText.text = builder.ToString();
    }

    private void AppendCategory(string label, RenderGeometryStats stats)
    {
        builder.Append(label);
        builder.Append(": ");
        AppendCompactNumber(stats.vertices);
        builder.Append("v / ");
        AppendCompactNumber(stats.triangles);
        builder.Append("t");

        if (stats.instances > 0)
        {
            builder.Append(" / ");
            AppendCompactNumber(stats.instances);
            builder.Append(" inst");
        }

        builder.AppendLine();
    }

    private void AppendCompactNumber(long value)
    {
        if (value >= 1000000)
        {
            builder.Append((value / 1000000f).ToString("0.00"));
            builder.Append("M");
        }
        else if (value >= 1000)
        {
            builder.Append((value / 1000f).ToString("0.0"));
            builder.Append("k");
        }
        else
        {
            builder.Append(value);
        }
    }
}
