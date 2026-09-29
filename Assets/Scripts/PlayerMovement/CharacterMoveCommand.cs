using UnityEngine;

// Input intent for one simulation step. This can later come from a player, AI, or network replay.
public readonly struct CharacterMoveCommand
{
    public readonly Vector2 Move;
    public readonly bool SprintHeld;
    public readonly bool CrouchHeld;
    public readonly bool JumpPressed;

    public CharacterMoveCommand(Vector2 move, bool sprintHeld, bool crouchHeld, bool jumpPressed)
    {
        Move = move;
        SprintHeld = sprintHeld;
        CrouchHeld = crouchHeld;
        JumpPressed = jumpPressed;
    }
}
