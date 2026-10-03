using UnityEngine;

[DisallowMultipleComponent]
public class SkyboxCycleController : MonoBehaviour
{
    public enum SkyPhase { Day, SunriseSunset, Night }

    [Header("Clock")]
    [SerializeField] private GameTimeManager timeManager;

    [Header("Skybox Materials")]
    [Tooltip("Falls back to the scene's original skybox when unassigned.")]
    [SerializeField] private Material daySkybox;
    [Tooltip("Shared skybox for sunrise and sunset. Falls back to the day skybox when unassigned.")]
    [SerializeField] private Material sunriseSunsetSkybox;
    [Tooltip("Falls back to the day skybox when unassigned.")]
    [SerializeField] private Material nightSkybox;

    [Header("Twilight")]
    [Tooltip("Game hours on EACH side of dawn and dusk. Dawn/dusk follow the clock's daylight settings. Zero disables twilight; large values are limited to prevent overlapping windows.")]
    [Min(0f)] [SerializeField] private float twilightHours = 1f;

    [Header("Environment")]
    [Tooltip("Refresh the sky environment only when the material changes. Does not rebake reflection probes.")]
    [SerializeField] private bool updateDynamicGIOnSwap = true;

    private GameTimeManager subscribedClock;
    private Material originalSkybox;
    private Material appliedSkybox;
    private bool hasAppliedSkybox;

    public SkyPhase CurrentPhase { get; private set; }

    private void Reset()
    {
        timeManager = GetComponent<GameTimeManager>();
        daySkybox = RenderSettings.skybox;
    }

    private void OnEnable()
    {
        originalSkybox = RenderSettings.skybox;
        hasAppliedSkybox = false;
    }

    private void Start()
    {
        ApplyCurrentTime();
    }

    private void LateUpdate()
    {
        // Also picks up Inspector changes while the clock is paused and late-created clocks.
        ApplyCurrentTime();
    }

    private void OnValidate()
    {
        twilightHours = Mathf.Max(0f, twilightHours);
    }

    private void ApplyCurrentTime()
    {
        if (timeManager == null)
        {
            timeManager = GetComponent<GameTimeManager>();
            if (timeManager == null)
                timeManager = GameTimeManager.Instance;
        }

        if (subscribedClock != timeManager)
        {
            Unsubscribe();
            subscribedClock = timeManager;
            if (subscribedClock != null)
                subscribedClock.TimeChanged += ApplySkybox;
        }

        if (timeManager != null)
            ApplySkybox(timeManager.CurrentSnapshot);
    }

    private void ApplySkybox(GameTimeSnapshot snapshot)
    {
        float hoursPerDay = timeManager.HoursPerDay;
        // Keep clock precision at exact boundaries and wrap negative time jumps too.
        double elapsedMinutes = (snapshot.TotalGameMinutesExact -
            timeManager.DaylightStartHour * 60d) % timeManager.MinutesPerDay;
        if (elapsedMinutes < 0d)
            elapsedMinutes += timeManager.MinutesPerDay;
        double elapsedSinceDawn = elapsedMinutes / 60d;
        float width = Mathf.Clamp(twilightHours, 0f,
            Mathf.Min(timeManager.DaylightHours, timeManager.NighttimeHours) * 0.5f);
        bool isTwilight = width > 0f &&
            (elapsedSinceDawn < width || elapsedSinceDawn >= hoursPerDay - width ||
             (elapsedSinceDawn >= timeManager.DaylightHours - width &&
              elapsedSinceDawn < timeManager.DaylightHours + width));

        CurrentPhase = isTwilight ? SkyPhase.SunriseSunset :
            snapshot.IsDaylight ? SkyPhase.Day : SkyPhase.Night;
        Material fallback = daySkybox != null ? daySkybox : originalSkybox;
        Material selected = CurrentPhase == SkyPhase.SunriseSunset ? sunriseSunsetSkybox :
            CurrentPhase == SkyPhase.Night ? nightSkybox : daySkybox;
        if (selected == null)
            selected = fallback;

        appliedSkybox = selected;
        hasAppliedSkybox = true;
        if (RenderSettings.skybox == selected)
            return;

        // Assign references; never mutate the shared material assets.
        RenderSettings.skybox = selected;
        if (updateDynamicGIOnSwap && Application.isPlaying)
            DynamicGI.UpdateEnvironment();
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (hasAppliedSkybox && RenderSettings.skybox == appliedSkybox)
        {
            RenderSettings.skybox = originalSkybox;
            if (updateDynamicGIOnSwap && Application.isPlaying)
                DynamicGI.UpdateEnvironment();
        }
        hasAppliedSkybox = false;
    }

    private void Unsubscribe()
    {
        if (subscribedClock != null)
            subscribedClock.TimeChanged -= ApplySkybox;
        subscribedClock = null;
    }
}
