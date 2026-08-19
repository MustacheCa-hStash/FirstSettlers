using UnityEngine;

public readonly struct GameTimeSnapshot
{
    public readonly long TotalGameMinutes;
    public readonly double TotalGameMinutesExact;
    public readonly int Day;
    public readonly int Hour;
    public readonly int Minute;
    public readonly float NormalizedDay;
    public readonly bool IsDaylight;
    public readonly float DaylightProgress;
    public readonly float NightProgress;

    public GameTimeSnapshot(
        long totalGameMinutes,
        double totalGameMinutesExact,
        int day,
        int hour,
        int minute,
        float normalizedDay,
        bool isDaylight,
        float daylightProgress,
        float nightProgress)
    {
        TotalGameMinutes = totalGameMinutes;
        TotalGameMinutesExact = totalGameMinutesExact;
        Day = day;
        Hour = hour;
        Minute = minute;
        NormalizedDay = normalizedDay;
        IsDaylight = isDaylight;
        DaylightProgress = daylightProgress;
        NightProgress = nightProgress;
    }

    public string ClockText => $"{Hour:00}:{Minute:00}";

    public override string ToString()
    {
        string phase = IsDaylight ? "Day" : "Night";
        return $"Day {Day}, {ClockText} ({phase})";
    }
}
