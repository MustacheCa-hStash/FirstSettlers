using UnityEngine;

public readonly struct BuildInputCommand
{
    public readonly int Turn;
    // X/Z are player-relative arrow directions; the controller maps them into the active grid. Y is vertical.
    public readonly Vector3Int Nudge;
    public readonly bool Place, Remove, Flip;
    public BuildInputCommand(int turn, Vector3Int nudge, bool place, bool remove, bool flip=false)
    { Turn = turn; Nudge = nudge; Place = place; Remove = remove; Flip = flip; }
}

/// <summary>One edge-triggered nudge followed by bounded hold repeat, using unscaled time.</summary>
public struct BuildNudgeRepeat
{
    private double nextRepeat;
    public bool Sample(bool held, bool pressed, double now, bool repeat, double delay, double interval)
    {
        if (!held) { nextRepeat = 0; return false; }
        if (pressed) { nextRepeat = repeat ? now + System.Math.Max(.001, delay) : 0; return true; }
        // Reset/menu-suppressed holds need a fresh press; never consume time as a backlog of movement.
        if (!repeat) { nextRepeat = 0; return false; }
        if (nextRepeat == 0 || now < nextRepeat) return false;
        nextRepeat = now + System.Math.Max(.02, interval); return true;
    }
}
