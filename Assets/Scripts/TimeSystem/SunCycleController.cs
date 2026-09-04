using UnityEngine;
using UnityEngine.Rendering;

public class SunCycleController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameTimeManager timeManager;
    [SerializeField] private Light sunLight;
    [SerializeField] private Light moonLight;
    [SerializeField] private bool bindAsRenderSettingsSun = true;

    [Header("Sun Arc")]
    [Tooltip("Compass-style azimuth in degrees where 0 is world +Z, 90 is +X, 180 is -Z, and 270 is -X.")]
    [SerializeField] private float sunriseAzimuth = 105f;
    [SerializeField] private float sunsetAzimuth = 255f;
    [SerializeField] private float maxSunAltitude = 62f;
    [SerializeField] private float nightAltitude = -18f;
    [SerializeField] private float horizonFadeDegrees = 8f;

    [Header("Direct Sunlight")]
    [SerializeField] private float maxSunIntensity = 1.35f;
    [SerializeField] private Gradient sunColor = CreateDefaultSunColor();
    [SerializeField] private AnimationCurve sunIntensityCurve = CreateDefaultSunIntensityCurve();
    [SerializeField] private LightShadows daylightShadowMode = LightShadows.Soft;
    [SerializeField] private bool disableShadowsAtNight = true;
    [SerializeField] private float maxShadowStrength = 0.85f;
    [SerializeField] private AnimationCurve shadowStrengthCurve = CreateDefaultShadowStrengthCurve();

    [Header("Moonlight")]
    [SerializeField] private float moonriseAzimuth = 75f;
    [SerializeField] private float moonsetAzimuth = 285f;
    [SerializeField] private float maxMoonAltitude = 45f;
    [SerializeField] private float moonBelowHorizonAltitude = -12f;
    [SerializeField] private float maxMoonIntensity = 0.22f;
    [SerializeField] private Color moonColor = new Color(0.60f, 0.70f, 1.0f, 1f);
    [SerializeField] private AnimationCurve moonIntensityCurve = CreateDefaultMoonIntensityCurve();
    [SerializeField] private LightShadows moonShadowMode = LightShadows.Soft;
    [SerializeField] private float maxMoonShadowStrength = 0.22f;

    [Header("Ambient")]
    [SerializeField] private bool updateAmbientLighting = true;
    [SerializeField] private float twilightAmbientBlend = 0.22f;
    [SerializeField] private float twilightNightFraction = 0.18f;
    [SerializeField] private float dayAmbientIntensity = 1.05f;
    [SerializeField] private float nightAmbientIntensity = 0.26f;
    [SerializeField] private Color dayAmbientSkyColor = new Color(0.63f, 0.74f, 0.92f, 1f);
    [SerializeField] private Color dayAmbientEquatorColor = new Color(0.42f, 0.47f, 0.52f, 1f);
    [SerializeField] private Color dayAmbientGroundColor = new Color(0.24f, 0.22f, 0.18f, 1f);
    [SerializeField] private Color nightAmbientSkyColor = new Color(0.075f, 0.085f, 0.13f, 1f);
    [SerializeField] private Color nightAmbientEquatorColor = new Color(0.055f, 0.062f, 0.09f, 1f);
    [SerializeField] private Color nightAmbientGroundColor = new Color(0.038f, 0.042f, 0.055f, 1f);

    [Header("Tree Night Lighting")]
    [SerializeField] private bool updateTreeNightLighting = true;
    [SerializeField] private float midnightTreeAmbientFloorScale = 0.48f;

    [Header("Fog")]
    [SerializeField] private bool updateFogColor = true;
    [SerializeField] private Color dayFogColor = new Color(0.52f, 0.66f, 0.95f, 1f);
    [SerializeField] private Color dawnDuskFogColor = new Color(0.95f, 0.55f, 0.32f, 1f);
    [SerializeField] private Color nightFogColor = new Color(0.055f, 0.065f, 0.10f, 1f);

    [Header("Global Illumination")]
    [SerializeField] private bool updateDynamicGI;
    [SerializeField] private float dynamicGiUpdateIntervalSeconds = 8f;

    private static readonly int TreeNightAmbientFloorDimAmountId = Shader.PropertyToID("_TreeNightAmbientFloorDimAmount");
    private static readonly int TreeNightAmbientFloorScaleAtMidnightId = Shader.PropertyToID("_TreeNightAmbientFloorScaleAtMidnight");

    private float nextDynamicGiUpdateTime;

    private void Reset()
    {
        sunLight = GetComponent<Light>();
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureCurvesAndGradients();
    }

    private void OnValidate()
    {
        ResolveReferences();
        EnsureCurvesAndGradients();

        maxSunAltitude = Mathf.Clamp(maxSunAltitude, 1f, 89f);
        nightAltitude = Mathf.Clamp(nightAltitude, -60f, -1f);
        horizonFadeDegrees = Mathf.Max(0.1f, horizonFadeDegrees);
        maxSunIntensity = Mathf.Max(0f, maxSunIntensity);
        maxShadowStrength = Mathf.Clamp01(maxShadowStrength);
        maxMoonAltitude = Mathf.Clamp(maxMoonAltitude, 1f, 89f);
        moonBelowHorizonAltitude = Mathf.Clamp(moonBelowHorizonAltitude, -60f, -1f);
        maxMoonIntensity = Mathf.Max(0f, maxMoonIntensity);
        maxMoonShadowStrength = Mathf.Clamp01(maxMoonShadowStrength);
        twilightAmbientBlend = Mathf.Clamp01(twilightAmbientBlend);
        twilightNightFraction = Mathf.Clamp(twilightNightFraction, 0.01f, 0.5f);
        dayAmbientIntensity = Mathf.Max(0f, dayAmbientIntensity);
        nightAmbientIntensity = Mathf.Max(0f, nightAmbientIntensity);
        midnightTreeAmbientFloorScale = Mathf.Clamp01(midnightTreeAmbientFloorScale);
        dynamicGiUpdateIntervalSeconds = Mathf.Max(0.1f, dynamicGiUpdateIntervalSeconds);
    }

    private void Start()
    {
        ApplyLighting();
    }

    private void LateUpdate()
    {
        ApplyLighting();
    }

    private void ApplyLighting()
    {
        if (!ResolveReferences())
            return;

        EnsureCurvesAndGradients();

        GameTimeSnapshot snapshot = timeManager.CurrentSnapshot;
        SunPose pose = CalculateSunPose(snapshot);
        SunPose moonPose = CalculateMoonPose(snapshot);
        float directVisibility = GetDirectSunVisibility(snapshot, pose.Altitude);
        float moonVisibility = GetMoonVisibility(snapshot, moonPose.Altitude);
        float ambientBlend = GetAmbientBlend(snapshot);

        sunLight.transform.rotation = Quaternion.LookRotation(-pose.Direction, Vector3.up);
        sunLight.intensity = maxSunIntensity * directVisibility;
        sunLight.color = sunColor.Evaluate(Mathf.Clamp01(snapshot.DaylightProgress));
        float shadowCurveValue = Mathf.Clamp01(shadowStrengthCurve.Evaluate(Mathf.Clamp01(snapshot.DaylightProgress)));
        sunLight.shadowStrength = Mathf.Clamp01(maxShadowStrength * shadowCurveValue * directVisibility);

        if (disableShadowsAtNight && directVisibility <= 0.001f)
            sunLight.shadows = LightShadows.None;
        else
            sunLight.shadows = daylightShadowMode;

        if (moonLight != null)
        {
            moonLight.transform.rotation = Quaternion.LookRotation(-moonPose.Direction, Vector3.up);
            moonLight.intensity = maxMoonIntensity * moonVisibility;
            moonLight.color = moonColor;
            moonLight.shadowStrength = Mathf.Clamp01(maxMoonShadowStrength * moonVisibility);
            moonLight.shadows = moonVisibility > 0.001f ? moonShadowMode : LightShadows.None;
        }

        if (bindAsRenderSettingsSun)
            RenderSettings.sun = !snapshot.IsDaylight && moonLight != null ? moonLight : sunLight;

        if (updateAmbientLighting)
            ApplyAmbient(ambientBlend);

        ApplyTreeNightLighting(snapshot);

        if (updateFogColor)
            ApplyFog(snapshot, ambientBlend);

        if (updateDynamicGI && Application.isPlaying && Time.unscaledTime >= nextDynamicGiUpdateTime)
        {
            DynamicGI.UpdateEnvironment();
            nextDynamicGiUpdateTime = Time.unscaledTime + dynamicGiUpdateIntervalSeconds;
        }
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(TreeNightAmbientFloorDimAmountId, 0f);
        Shader.SetGlobalFloat(TreeNightAmbientFloorScaleAtMidnightId, 1f);
    }

    private bool ResolveReferences()
    {
        if (sunLight == null)
            sunLight = GetComponent<Light>();

        if (timeManager == null)
            timeManager = GameTimeManager.Instance;

        return sunLight != null && timeManager != null;
    }

    private SunPose CalculateSunPose(GameTimeSnapshot snapshot)
    {
        if (snapshot.IsDaylight)
        {
            float t = Mathf.Clamp01(snapshot.DaylightProgress);
            float altitude = Mathf.Sin(t * Mathf.PI) * maxSunAltitude;
            float azimuth = Mathf.Lerp(sunriseAzimuth, sunsetAzimuth, t);
            return new SunPose(DirectionFromAzimuthAltitude(azimuth, altitude), altitude);
        }

        float nightT = Mathf.Clamp01(snapshot.NightProgress);
        float nightAzimuth = Mathf.Lerp(sunsetAzimuth, sunriseAzimuth + 360f, nightT);
        return new SunPose(DirectionFromAzimuthAltitude(nightAzimuth, nightAltitude), nightAltitude);
    }

    private SunPose CalculateMoonPose(GameTimeSnapshot snapshot)
    {
        if (!snapshot.IsDaylight)
        {
            float t = Mathf.Clamp01(snapshot.NightProgress);
            float altitude = Mathf.Sin(t * Mathf.PI) * maxMoonAltitude;
            float azimuth = Mathf.Lerp(moonriseAzimuth, moonsetAzimuth, t);
            return new SunPose(DirectionFromAzimuthAltitude(azimuth, altitude), altitude);
        }

        float daylightT = Mathf.Clamp01(snapshot.DaylightProgress);
        float daylightAzimuth = Mathf.Lerp(moonsetAzimuth, moonriseAzimuth + 360f, daylightT);
        return new SunPose(DirectionFromAzimuthAltitude(daylightAzimuth, moonBelowHorizonAltitude), moonBelowHorizonAltitude);
    }

    private float GetDirectSunVisibility(GameTimeSnapshot snapshot, float altitude)
    {
        if (!snapshot.IsDaylight)
            return 0f;

        float curveVisibility = Mathf.Clamp01(sunIntensityCurve.Evaluate(Mathf.Clamp01(snapshot.DaylightProgress)));
        float horizonVisibility = Smooth01(Mathf.InverseLerp(0f, horizonFadeDegrees, altitude));
        return curveVisibility * horizonVisibility;
    }

    private float GetMoonVisibility(GameTimeSnapshot snapshot, float altitude)
    {
        if (snapshot.IsDaylight)
            return 0f;

        float curveVisibility = Mathf.Clamp01(moonIntensityCurve.Evaluate(Mathf.Clamp01(snapshot.NightProgress)));
        float horizonVisibility = Smooth01(Mathf.InverseLerp(0f, horizonFadeDegrees, altitude));
        return curveVisibility * horizonVisibility;
    }

    private float GetAmbientBlend(GameTimeSnapshot snapshot)
    {
        if (snapshot.IsDaylight)
        {
            float daylightArc = Mathf.Sin(Mathf.Clamp01(snapshot.DaylightProgress) * Mathf.PI);
            return Mathf.Lerp(twilightAmbientBlend, 1f, Smooth01(daylightArc));
        }

        float edgeDistance = Mathf.Min(snapshot.NightProgress, 1f - snapshot.NightProgress);
        float twilightFade = 1f - Smooth01(Mathf.InverseLerp(0f, twilightNightFraction, edgeDistance));
        return twilightAmbientBlend * twilightFade;
    }

    private void ApplyTreeNightLighting(GameTimeSnapshot snapshot)
    {
        float dimAmount = updateTreeNightLighting ? GetTreeAmbientFloorDimAmount(snapshot) : 0f;
        Shader.SetGlobalFloat(TreeNightAmbientFloorDimAmountId, dimAmount);
        Shader.SetGlobalFloat(TreeNightAmbientFloorScaleAtMidnightId, midnightTreeAmbientFloorScale);
    }

    private float GetTreeAmbientFloorDimAmount(GameTimeSnapshot snapshot)
    {
        if (snapshot.IsDaylight)
            return 0f;

        float edgeDistance = Mathf.Min(snapshot.NightProgress, 1f - snapshot.NightProgress);
        return Smooth01(Mathf.InverseLerp(0f, 0.5f, edgeDistance));
    }

    private void ApplyAmbient(float ambientBlend)
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientIntensity = Mathf.Lerp(nightAmbientIntensity, dayAmbientIntensity, ambientBlend);
        RenderSettings.ambientSkyColor = Color.Lerp(nightAmbientSkyColor, dayAmbientSkyColor, ambientBlend);
        RenderSettings.ambientEquatorColor = Color.Lerp(nightAmbientEquatorColor, dayAmbientEquatorColor, ambientBlend);
        RenderSettings.ambientGroundColor = Color.Lerp(nightAmbientGroundColor, dayAmbientGroundColor, ambientBlend);
    }

    private void ApplyFog(GameTimeSnapshot snapshot, float ambientBlend)
    {
        Color baseFog = Color.Lerp(nightFogColor, dayFogColor, ambientBlend);
        float horizonWarmth = snapshot.IsDaylight
            ? 1f - Smooth01(Mathf.Sin(Mathf.Clamp01(snapshot.DaylightProgress) * Mathf.PI))
            : 0f;

        RenderSettings.fogColor = Color.Lerp(baseFog, dawnDuskFogColor, horizonWarmth * ambientBlend);
    }

    private void EnsureCurvesAndGradients()
    {
        if (sunColor == null || sunColor.colorKeys.Length == 0)
            sunColor = CreateDefaultSunColor();

        if (sunIntensityCurve == null || sunIntensityCurve.length == 0)
            sunIntensityCurve = CreateDefaultSunIntensityCurve();

        if (shadowStrengthCurve == null || shadowStrengthCurve.length == 0)
            shadowStrengthCurve = CreateDefaultShadowStrengthCurve();

        if (moonIntensityCurve == null || moonIntensityCurve.length == 0)
            moonIntensityCurve = CreateDefaultMoonIntensityCurve();
    }

    private static Vector3 DirectionFromAzimuthAltitude(float azimuthDegrees, float altitudeDegrees)
    {
        float azimuth = azimuthDegrees * Mathf.Deg2Rad;
        float altitude = altitudeDegrees * Mathf.Deg2Rad;
        float horizontal = Mathf.Cos(altitude);

        return new Vector3(
            Mathf.Sin(azimuth) * horizontal,
            Mathf.Sin(altitude),
            Mathf.Cos(azimuth) * horizontal).normalized;
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static Gradient CreateDefaultSunColor()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.47f, 0.24f), 0f),
                new GradientColorKey(new Color(1f, 0.76f, 0.45f), 0.12f),
                new GradientColorKey(new Color(1f, 0.96f, 0.84f), 0.5f),
                new GradientColorKey(new Color(1f, 0.70f, 0.36f), 0.88f),
                new GradientColorKey(new Color(1f, 0.38f, 0.18f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });

        return gradient;
    }

    private static AnimationCurve CreateDefaultSunIntensityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.08f, 0.35f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.92f, 0.35f),
            new Keyframe(1f, 0f));
    }

    private static AnimationCurve CreateDefaultShadowStrengthCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.65f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.88f, 0.65f),
            new Keyframe(1f, 0f));
    }

    private static AnimationCurve CreateDefaultMoonIntensityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.15f, 0.35f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.85f, 0.35f),
            new Keyframe(1f, 0f));
    }

    private readonly struct SunPose
    {
        public SunPose(Vector3 direction, float altitude)
        {
            Direction = direction;
            Altitude = altitude;
        }

        public Vector3 Direction { get; }
        public float Altitude { get; }
    }
}
