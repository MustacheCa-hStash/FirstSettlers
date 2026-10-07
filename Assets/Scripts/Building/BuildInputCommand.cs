using UnityEngine;

public readonly struct BuildInputCommand
{
    public readonly int Turn;
    public readonly Vector3Int Nudge;
    public readonly bool Place, Remove;
    public BuildInputCommand(int turn, Vector3Int nudge, bool place, bool remove)
    { Turn = turn; Nudge = nudge; Place = place; Remove = remove; }
}
