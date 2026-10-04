using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PcgDebugOverlay : MonoBehaviour
{
    [SerializeField] WorldManager worldManager;
    [SerializeField] Transform target;
    [SerializeField] TextMeshProUGUI debugText;
    [SerializeField] Canvas debugCanvas;
    [SerializeField] Key toggleKey = Key.F3;
    [SerializeField] bool visibleOnStart;
    [SerializeField] float refreshInterval = 0.1f;

    private readonly StringBuilder builder = new StringBuilder(512);
    private bool isVisible;
    private float nextRefreshTime;
    private TextMeshProUGUI performanceText;
    private readonly FrameTimeStatistics frameTimes = new FrameTimeStatistics();
    private readonly StringBuilder performanceBuilder = new StringBuilder(256);
    private double previousFrameTime = double.NaN;
    private float nextPerformanceRefreshTime;
    private int generationRevision = -1;

    void Awake()
    {
        EnsureOverlay();
        EnsurePerformanceOverlay();
        isVisible = visibleOnStart;
        if (debugCanvas != null) debugCanvas.enabled = isVisible;
    }

    void Update()
    {
        HandleToggleInput();
        if (Keyboard.current?.f7Key.wasPressedThisFrame == true) ResetPerformanceSample();

        if (!isVisible || Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshText();
    }

    void LateUpdate()
    {
        int revision = worldManager != null ? worldManager.TerrainGenerationRevision : 0;
        if (revision != generationRevision) { generationRevision = revision; ResetPerformanceSample(); }
        double now = Time.realtimeSinceStartupAsDouble;
        if (Application.isFocused && !double.IsNaN(previousFrameTime)) frameTimes.Add((float)(now - previousFrameTime));
        previousFrameTime = Application.isFocused ? now : double.NaN;
        if (isVisible && Time.unscaledTime >= nextPerformanceRefreshTime)
        { nextPerformanceRefreshTime = Time.unscaledTime + 0.5f; RefreshPerformanceText(); }
    }
    void OnApplicationFocus(bool focused) { ResetPerformanceSample(); }
    void OnEnable() { ResetPerformanceSample(); }
    private void ResetPerformanceSample()
    { frameTimes.Reset(); previousFrameTime = double.NaN; nextPerformanceRefreshTime = 0f; }

    private void EnsureOverlay()
    {
        if (debugText != null)
        {
            if (debugCanvas == null)
                debugCanvas = debugText.GetComponentInParent<Canvas>();

            return;
        }

        GameObject canvasObject = new GameObject("PCG Debug Canvas");
        canvasObject.transform.SetParent(transform, false);

        debugCanvas = canvasObject.AddComponent<Canvas>();
        debugCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        debugCanvas.sortingOrder = 1000;

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
        panelRect.sizeDelta = new Vector2(430f, 275f);

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
        debugText.fontSize = 16f;
        debugText.raycastTarget = false;
        debugText.text = string.Empty;
    }

    private void EnsurePerformanceOverlay()
    {
        if (debugCanvas == null) return;
        GameObject panel = new GameObject("Performance Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(debugCanvas.transform, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-12f, -12f); rect.sizeDelta = new Vector2(340f, 135f);
        Image image = panel.GetComponent<Image>(); image.color = new Color(0f, 0f, 0f, 0.72f); image.raycastTarget = false;
        GameObject text = new GameObject("Performance Text", typeof(RectTransform)); text.transform.SetParent(panel.transform, false);
        RectTransform textRect = text.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 10f); textRect.offsetMax = new Vector2(-10f, -10f);
        performanceText = text.AddComponent<TextMeshProUGUI>(); performanceText.alignment = TextAlignmentOptions.TopLeft;
        performanceText.font = debugText.font;
        performanceText.fontSize = 17f; performanceText.color = Color.white; performanceText.raycastTarget = false;
    }
    private void RefreshPerformanceText()
    {
        if (performanceText == null) return;
        frameTimes.Calculate(out double average, out double low);
        performanceBuilder.Clear(); performanceBuilder.AppendLine("Performance (30 s window)");
        performanceBuilder.Append("Average FPS: "); performanceBuilder.AppendLine(frameTimes.Count > 0 ? average.ToString("0.0") : "warming up");
        performanceBuilder.Append("1% low FPS: "); performanceBuilder.AppendLine(frameTimes.Count >= 100 ? low.ToString("0.0") : "warming up");
        performanceBuilder.Append("Sample: "); performanceBuilder.Append(frameTimes.SampleSeconds.ToString("0.0")); performanceBuilder.AppendLine(" s");
        performanceBuilder.Append("F7 reset");
        performanceText.text = performanceBuilder.ToString();
    }

    private void HandleToggleInput()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
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
            nextPerformanceRefreshTime = 0f;
            RefreshText();
        }
    }

    private void RefreshText()
    {
        if (debugText == null)
            return;

        builder.Clear();
        builder.AppendLine("PCG Debug");

        if (worldManager == null)
        {
            builder.AppendLine("World Manager: missing");
            debugText.text = builder.ToString();
            return;
        }

        if (target == null)
        {
            builder.AppendLine("Target: missing");
            debugText.text = builder.ToString();
            return;
        }

        WorldDebugInfo info = worldManager.GetDebugInfoAtWorldPosition(target.position);
        Vector3 pos = info.WorldPosition;

        builder.Append("World Pos: ");
        builder.Append(pos.x.ToString("0.00"));
        builder.Append(", ");
        builder.Append(pos.y.ToString("0.00"));
        builder.Append(", ");
        builder.AppendLine(pos.z.ToString("0.00"));

        builder.Append("Chunk: ");
        builder.Append(info.ChunkCoord.x);
        builder.Append(", ");
        builder.AppendLine(info.ChunkCoord.z.ToString());

        if (!info.HasChunkRecord)
        {
            builder.AppendLine("Terrain: chunk not requested");
            debugText.text = builder.ToString();
            return;
        }

        if (!info.HasTerrainData)
        {
            builder.AppendLine("Terrain: loading");
            debugText.text = builder.ToString();
            return;
        }

        builder.Append("Biome: ");
        builder.AppendLine(info.Biome.ToString());
        if (info.ForestMembership >= 0f)
        {
            builder.Append("Forest / meadow: ");
            builder.Append(info.ForestMembership.ToString("0.00"));
            builder.Append(" / ");
            builder.AppendLine((1f - info.ForestMembership).ToString("0.00"));
        }
        builder.Append("Surface: ");
        builder.AppendLine(info.SurfaceType.ToString());
        builder.Append("World Height: ");
        builder.AppendLine(info.WorldHeight.ToString("0.00"));
        builder.Append("Slope (degrees): ");
        builder.AppendLine(info.Slope.ToString("0.0"));
        builder.Append("Moisture: ");
        builder.AppendLine(info.Moisture.ToString("0.000"));
        builder.Append("Temperature: ");
        builder.AppendLine(info.Temperature.ToString("0.000"));
        builder.Append("River Mask: ");
        builder.AppendLine(info.RiverMask.ToString("0.000"));

        debugText.text = builder.ToString();
    }
}
