using UnityEngine;
using UnityEngine.InputSystem;

// The only character component that reads local hardware. AI or a network owner can
// drive CharacterMotor with the same command without depending on this component.
public sealed class LocalPlayerInput : MonoBehaviour
{
    [SerializeField] private CharacterMotor motor;
    [SerializeField] private FirstPersonLook look;

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        if (motor == null || look == null)
            return;

        if (Mouse.current != null)
            look.ApplyLook(Mouse.current.delta.ReadValue());

        if (Keyboard.current == null)
        {
            motor.Simulate(default, Time.deltaTime);
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Vector2 move = new Vector2(
            (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
            (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));

        if (move.sqrMagnitude > 1f)
            move.Normalize();

        var command = new CharacterMoveCommand(
            move,
            keyboard.leftShiftKey.isPressed,
            keyboard.leftCtrlKey.isPressed,
            keyboard.spaceKey.wasPressedThisFrame);

        motor.Simulate(command, Time.deltaTime);
    }
}
