using System;
using System.Collections.Generic;
using UnityEngine;

public class GameTimeManager : MonoBehaviour
{
    private const int MinutesPerHour = 60;

    private static GameTimeManager instance;

    [Header("Clock Scale")]
    [SerializeField] private bool startAutomatically = true;
    [SerializeField] private bool useUnscaledDeltaTime;
    [SerializeField] private float realSecondsPerGameHour = 75f;
    [SerializeField] private float timeScale = 1f;

    [Header("Day Cycle")]
    [SerializeField] private int daylightHours = 16;
    [SerializeField] private int nighttimeHours = 8;
    [SerializeField] private int daylightStartHour = 6;
    [SerializeField] private int startingDay;
    [SerializeField] private int startingHour = 6;
    [SerializeField] private int startingMinute;

    [Header("Discrete Events")]
    [SerializeField] private int maxDiscreteMinutesProcessedPerFrame = 360;

    private readonly List<GameTimeCycleSubscription> customCycles = new();
    private double totalGameMinutes;
    private long lastDiscreteMinute;
    private bool isRunning;
    private bool wasDaylight;

    public event Action<GameTimeSnapshot> MinuteAdvanced;
    public event Action<GameTimeSnapshot> HourAdvanced;
    public event Action<GameTimeSnapshot> DayAdvanced;
    public event Action<GameTimeSnapshot> DaylightStarted;
    public event Action<GameTimeSnapshot> NightStarted;
    public event Action<GameTimeSnapshot> TimeChanged;

    public static GameTimeManager Instance => instance;

    public bool IsRunning => isRunning;
    public float RealSecondsPerGameHour => realSecondsPerGameHour;
    public float RealSecondsPerGameMinute => realSecondsPerGameHour / MinutesPerHour;
    public int DaylightHours => daylightHours;
    public int NighttimeHours => nighttimeHours;
    public int HoursPerDay => daylightHours + nighttimeHours;
    public int MinutesPerDay => HoursPerDay * MinutesPerHour;
    public int DaylightStartHour => daylightStartHour;
    public int NightStartHour => PositiveModulo(daylightStartHour + daylightHours, HoursPerDay);
    public double TotalGameMinutesExact => totalGameMinutes;
    public float NormalizedDay => CurrentSnapshot.NormalizedDay;
    public GameTimeSnapshot CurrentSnapshot => CreateSnapshot(totalGameMinutes);

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError("Only one GameTimeManager can exist in the scene.");
            enabled = false;
            return;
        }

        instance = this;
        SanitizeSettings();
        totalGameMinutes = GetClampedStartMinute();
        lastDiscreteMinute = (long)Math.Floor(totalGameMinutes);
        wasDaylight = CurrentSnapshot.IsDaylight;
        isRunning = startAutomatically;
    }

    private void OnValidate()
    {
        SanitizeSettings();
    }

    private void Update()
    {
        if (!isRunning || realSecondsPerGameHour <= 0f || timeScale <= 0f)
            return;

        float deltaTime = useUnscaledDeltaTime ? Time.unscaledDeltaTime : Time.deltaTime;
        AdvanceRealSeconds(deltaTime * timeScale);
    }

    public void Play()
    {
        isRunning = true;
    }

    public void Pause()
    {
        isRunning = false;
    }

    public void SetTimeScale(float newTimeScale)
    {
        timeScale = Mathf.Max(0f, newTimeScale);
    }

    public void SetTime(int day, int hour, int minute)
    {
        SanitizeSettings();
        totalGameMinutes = GetTotalMinute(day, hour, minute);
        lastDiscreteMinute = (long)Math.Floor(totalGameMinutes);
        GameTimeSnapshot snapshot = CurrentSnapshot;
        wasDaylight = snapshot.IsDaylight;
        TimeChanged?.Invoke(snapshot);
    }

    public void AdvanceRealSeconds(float realSeconds)
    {
        AdvanceGameMinutes(realSeconds / RealSecondsPerGameMinute);
    }

    public void AdvanceGameMinutes(double gameMinutes)
    {
        totalGameMinutes += gameMinutes;
        ProcessDiscreteTime();

        GameTimeSnapshot snapshot = CurrentSnapshot;
        TimeChanged?.Invoke(snapshot);
    }

    public GameTimeCycleSubscription RegisterCycle(
        string cycleId,
        long intervalGameMinutes,
        Action<GameTimeSnapshot, long> callback,
        bool invokeForEachMissedInterval = false)
    {
        if (string.IsNullOrWhiteSpace(cycleId))
            throw new ArgumentException("Cycle id cannot be empty.", nameof(cycleId));

        if (intervalGameMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalGameMinutes), "Cycle interval must be positive.");

        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        long currentMinute = (long)Math.Floor(totalGameMinutes);
        long nextMinute = currentMinute + intervalGameMinutes;

        GameTimeCycleSubscription subscription = new(
            this,
            cycleId,
            intervalGameMinutes,
            callback,
            invokeForEachMissedInterval,
            nextMinute);

        customCycles.Add(subscription);
        return subscription;
    }

    public GameTimeCycleSubscription RegisterCycle(
        string cycleId,
        int days,
        int hours,
        int minutes,
        Action<GameTimeSnapshot, long> callback,
        bool invokeForEachMissedInterval = false)
    {
        return RegisterCycle(
            cycleId,
            GetGameMinuteDuration(days, hours, minutes),
            callback,
            invokeForEachMissedInterval);
    }

    public long GetGameMinuteDuration(int days, int hours, int minutes)
    {
        return days * MinutesPerDay + hours * MinutesPerHour + minutes;
    }

    internal void UnregisterCycle(GameTimeCycleSubscription subscription)
    {
        customCycles.Remove(subscription);
    }

    private void ProcessDiscreteTime()
    {
        long currentMinute = (long)Math.Floor(totalGameMinutes);

        if (currentMinute <= lastDiscreteMinute)
            return;

        long minutesToProcess = currentMinute - lastDiscreteMinute;
        long firstMinute = lastDiscreteMinute + 1;

        if (minutesToProcess > maxDiscreteMinutesProcessedPerFrame)
            firstMinute = currentMinute - maxDiscreteMinutesProcessedPerFrame + 1;

        for (long minute = firstMinute; minute <= currentMinute; minute++)
        {
            GameTimeSnapshot snapshot = CreateSnapshot(minute);
            MinuteAdvanced?.Invoke(snapshot);
            ProcessPhaseChange(snapshot);

            if (snapshot.Minute == 0)
            {
                HourAdvanced?.Invoke(snapshot);

                if (snapshot.Hour == 0)
                    DayAdvanced?.Invoke(snapshot);
            }
        }

        lastDiscreteMinute = currentMinute;
        ProcessCustomCycles(currentMinute);
    }

    private void ProcessPhaseChange(GameTimeSnapshot snapshot)
    {
        if (snapshot.IsDaylight == wasDaylight)
            return;

        wasDaylight = snapshot.IsDaylight;

        if (snapshot.IsDaylight)
            DaylightStarted?.Invoke(snapshot);
        else
            NightStarted?.Invoke(snapshot);
    }

    private void ProcessCustomCycles(long currentMinute)
    {
        for (int i = customCycles.Count - 1; i >= 0; i--)
        {
            GameTimeCycleSubscription subscription = customCycles[i];

            if (subscription == null || subscription.IsDisposed)
            {
                customCycles.RemoveAt(i);
                continue;
            }

            if (currentMinute < subscription.NextGameMinute)
                continue;

            if (subscription.InvokeForEachMissedInterval)
            {
                while (currentMinute >= subscription.NextGameMinute && !subscription.IsDisposed)
                    InvokeCustomCycle(subscription, subscription.NextGameMinute);
            }
            else
            {
                long intervalsElapsed = ((currentMinute - subscription.NextGameMinute) / subscription.IntervalGameMinutes) + 1;
                subscription.CompletedCycles += intervalsElapsed;
                subscription.NextGameMinute += intervalsElapsed * subscription.IntervalGameMinutes;
                subscription.Callback(CreateSnapshot(currentMinute), subscription.CompletedCycles);
            }
        }
    }

    private void InvokeCustomCycle(GameTimeCycleSubscription subscription, long boundaryMinute)
    {
        subscription.CompletedCycles++;
        subscription.NextGameMinute += subscription.IntervalGameMinutes;
        subscription.Callback(CreateSnapshot(boundaryMinute), subscription.CompletedCycles);
    }

    private GameTimeSnapshot CreateSnapshot(double totalMinuteExact)
    {
        int minutesPerDay = Mathf.Max(MinutesPerHour, MinutesPerDay);
        long totalWholeMinute = (long)Math.Floor(totalMinuteExact);
        long day = totalWholeMinute / minutesPerDay;
        long wholeMinuteOfDay = totalWholeMinute % minutesPerDay;

        if (wholeMinuteOfDay < 0)
        {
            day--;
            wholeMinuteOfDay += minutesPerDay;
        }

        double exactMinuteOfDay = totalMinuteExact - day * minutesPerDay;

        if (exactMinuteOfDay < 0d)
            exactMinuteOfDay += minutesPerDay;

        int hour = (int)(wholeMinuteOfDay / MinutesPerHour);
        int minute = (int)(wholeMinuteOfDay % MinutesPerHour);
        float normalizedDay = (float)(exactMinuteOfDay / minutesPerDay);
        float hourOfDay = (float)(exactMinuteOfDay / MinutesPerHour);
        bool isDaylight = IsHourInDaylight(hourOfDay);
        float daylightProgress = isDaylight ? GetPhaseProgress(hourOfDay, daylightStartHour, daylightHours) : 0f;
        float nightProgress = isDaylight ? 0f : GetPhaseProgress(hourOfDay, NightStartHour, nighttimeHours);

        return new GameTimeSnapshot(
            totalWholeMinute,
            totalMinuteExact,
            (int)day,
            hour,
            minute,
            normalizedDay,
            isDaylight,
            daylightProgress,
            nightProgress);
    }

    private bool IsHourInDaylight(float hour)
    {
        float start = daylightStartHour;
        float end = daylightStartHour + daylightHours;
        float wrappedHour = PositiveModulo(hour, HoursPerDay);

        if (end <= HoursPerDay)
            return wrappedHour >= start && wrappedHour < end;

        return wrappedHour >= start || wrappedHour < end - HoursPerDay;
    }

    private float GetPhaseProgress(float hour, float startHour, float durationHours)
    {
        if (durationHours <= 0f)
            return 0f;

        float elapsed = PositiveModulo(hour - startHour, HoursPerDay);
        return Mathf.Clamp01(elapsed / durationHours);
    }

    private long GetClampedStartMinute()
    {
        return GetTotalMinute(startingDay, startingHour, startingMinute);
    }

    private long GetTotalMinute(int day, int hour, int minute)
    {
        int minutesPerDay = Mathf.Max(MinutesPerHour, MinutesPerDay);
        int clampedHour = PositiveModulo(hour, HoursPerDay);
        int clampedMinute = Mathf.Clamp(minute, 0, MinutesPerHour - 1);
        return (long)Mathf.Max(0, day) * minutesPerDay + clampedHour * MinutesPerHour + clampedMinute;
    }

    private void SanitizeSettings()
    {
        realSecondsPerGameHour = Mathf.Max(0.001f, realSecondsPerGameHour);
        daylightHours = Mathf.Max(1, daylightHours);
        nighttimeHours = Mathf.Max(1, nighttimeHours);
        daylightStartHour = PositiveModulo(daylightStartHour, HoursPerDay);
        startingDay = Mathf.Max(0, startingDay);
        startingHour = PositiveModulo(startingHour, HoursPerDay);
        startingMinute = Mathf.Clamp(startingMinute, 0, MinutesPerHour - 1);
        maxDiscreteMinutesProcessedPerFrame = Mathf.Max(1, maxDiscreteMinutesProcessedPerFrame);
    }

    private static int PositiveModulo(int value, int modulo)
    {
        if (modulo <= 0)
            return 0;

        int result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    private static float PositiveModulo(float value, float modulo)
    {
        if (modulo <= 0f)
            return 0f;

        float result = value % modulo;
        return result < 0f ? result + modulo : result;
    }
}
