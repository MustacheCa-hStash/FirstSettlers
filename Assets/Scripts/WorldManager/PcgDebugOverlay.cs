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

    void Awake()
    {
        EnsureOverlay();
        isVisible = visibleOnStart;
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
        panelRect.sizeDelta = new Vector2(430f, 245f);

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
        builder.Append("Surface: ");
        builder.AppendLine(info.SurfaceType.ToString());
        builder.Append("Ground Cover: ");
        builder.AppendLine(info.GroundCoverType.ToString());
        builder.Append("World Height: ");
        builder.AppendLine(info.WorldHeight.ToString("0.00"));
        builder.Append("Slope: ");
        builder.AppendLine(info.Slope.ToString("0.000"));
        builder.Append("Moisture: ");
        builder.AppendLine(info.Moisture.ToString("0.000"));
        builder.Append("Temperature: ");
        builder.AppendLine(info.Temperature.ToString("0.000"));
        builder.Append("River Mask: ");
        builder.AppendLine(info.RiverMask.ToString("0.000"));
        builder.Append("Planned Trees: ");
        builder.AppendLine(info.PlannedTreeCount.ToString());
        builder.Append("Generated Trees: ");
        builder.AppendLine(info.GeneratedTreeCount.ToString());
        builder.Append("Tree GameObjects: ");
        builder.AppendLine(info.TreeGameObjectCount.ToString());
        builder.Append("GPU Grass: ");
        builder.AppendLine(info.GpuGrassInstanceCount.ToString());
        builder.Append("GPU Flowers: ");
        builder.AppendLine(info.GpuFlowerInstanceCount.ToString());
        builder.Append("GPU Clover: ");
        builder.AppendLine(info.GpuCloverInstanceCount.ToString());
        builder.Append("GPU Trees: ");
        builder.AppendLine(info.GpuTreeInstanceCount.ToString());

        if (!info.HasFoliageRuntime)
            builder.AppendLine("Foliage Runtime: none");

        debugText.text = builder.ToString();
    }
}
