using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class GameTimeDebugOverlay : MonoBehaviour
{
    [SerializeField] GameTimeManager timeManager;
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
        if (timeManager == null)
            timeManager = GameTimeManager.Instance;

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

        GameObject canvasObject = new GameObject("Game Time Debug Canvas");
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
        panelRect.sizeDelta = new Vector2(390f, 220f);

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
        if (Keyboard.current == null)
            return;

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

        if (timeManager == null)
            timeManager = GameTimeManager.Instance;

        builder.Clear();
        builder.AppendLine("Game Time Debug");

        if (timeManager == null)
        {
            builder.AppendLine("Time Manager: missing");
            debugText.text = builder.ToString();
            return;
        }

        GameTimeSnapshot snapshot = timeManager.CurrentSnapshot;

        builder.Append("Running: ");
        builder.AppendLine(timeManager.IsRunning ? "yes" : "no");

        builder.Append("Day: ");
        builder.AppendLine(snapshot.Day.ToString());

        builder.Append("Clock: ");
        builder.AppendLine(snapshot.ClockText);

        builder.Append("Phase: ");
        builder.AppendLine(snapshot.IsDaylight ? "Daylight" : "Night");

        builder.Append("Phase Progress: ");
        builder.AppendLine((snapshot.IsDaylight ? snapshot.DaylightProgress : snapshot.NightProgress).ToString("P1"));

        builder.Append("Day Progress: ");
        builder.AppendLine(snapshot.NormalizedDay.ToString("P1"));

        builder.Append("Total Minutes: ");
        builder.AppendLine(snapshot.TotalGameMinutes.ToString());

        builder.Append("Exact Minutes: ");
        builder.AppendLine(snapshot.TotalGameMinutesExact.ToString("0.00"));

        builder.Append("Seconds / Game Hour: ");
        builder.AppendLine(timeManager.RealSecondsPerGameHour.ToString("0.##"));

        builder.Append("Seconds / Game Minute: ");
        builder.AppendLine(timeManager.RealSecondsPerGameMinute.ToString("0.###"));

        debugText.text = builder.ToString();
    }
}
