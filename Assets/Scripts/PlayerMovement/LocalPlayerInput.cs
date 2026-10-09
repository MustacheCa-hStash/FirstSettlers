using UnityEngine;
using UnityEngine.InputSystem;

// The only character component that reads local hardware. AI or a network owner can
// drive CharacterMotor with the same command without depending on this component.
public sealed class LocalPlayerInput : MonoBehaviour
{
    [SerializeField] private CharacterMotor motor;
    [SerializeField] private FirstPersonLook look;
    [SerializeField] private BuildingController building;
    [Header("Building arrow nudges")]
    [SerializeField] private bool repeatArrowNudges = true;
    [SerializeField, Min(0)] private float nudgeRepeatDelay = .3f;
    [SerializeField, Min(.02f)] private float nudgeRepeatInterval = .1f;
    private BuildNudgeRepeat leftNudge, rightNudge, forwardNudge, backNudge;
    [System.Flags]
    private enum BuildButton { Toggle = 1, Cancel = 2, Picker = 4, LeftTurn = 8, RightTurn = 16,
        Left = 32, Right = 64, Forward = 128, Back = 256, Up = 512, Down = 1024, Place = 2048, Remove = 4096 }
    private BuildButton heldBuildButtons;
    private int suppressLookFrames;
    public void SetBuilding(BuildingController controller) => building = controller;
    public void SuppressLookAfterCursorCapture() => suppressLookFrames = 2;

    private void OnEnable()
    {
        ResetNudgeRepeat();
        heldBuildButtons = ReadBuildButtons(Keyboard.current, Mouse.current);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        ResetNudgeRepeat();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        if (motor == null || look == null)
        {
            ResetNudgeRepeat();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        BuildButton held = ReadBuildButtons(keyboard, Mouse.current);
        BuildButton pressed = held & ~heldBuildButtons;
        heldBuildButtons = held;
        bool Pressed(BuildButton button) => (pressed & button) != 0;
        if (building != null && keyboard != null)
        {
            if (Pressed(BuildButton.Toggle)) building.Toggle();
            else if (Pressed(BuildButton.Cancel) && building.Active) building.Exit();
            else if (Pressed(BuildButton.Picker) && building.Active) building.OpenMenu();
        }
        if (building != null && building.MenuOpen)
        {
            ResetNudgeRepeat();
            motor.Simulate(default, Time.deltaTime);
            return;
        }
        if (suppressLookFrames > 0) suppressLookFrames--;
        else if (Mouse.current != null)
            look.ApplyLook(Mouse.current.delta.ReadValue());

        if (Keyboard.current == null)
        {
            ResetNudgeRepeat();
            motor.Simulate(default, Time.deltaTime);
            return;
        }

        if (building != null && building.Active)
        {
            var mouse = Mouse.current;
            int turn = (Pressed(BuildButton.RightTurn) ? 1 : 0) - (Pressed(BuildButton.LeftTurn) ? 1 : 0);
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0) turn += scroll > 0 ? 1 : -1;
            }
            double now = Time.unscaledTimeAsDouble;
            bool Arrow(BuildButton button, ref BuildNudgeRepeat state) => state.Sample((held & button) != 0,
                (pressed & button) != 0, now, repeatArrowNudges, nudgeRepeatDelay, nudgeRepeatInterval);
            var nudge = new Vector3Int(
                (Arrow(BuildButton.Right, ref rightNudge) ? 1 : 0) - (Arrow(BuildButton.Left, ref leftNudge) ? 1 : 0),
                (Pressed(BuildButton.Up) ? 1 : 0) - (Pressed(BuildButton.Down) ? 1 : 0),
                (Arrow(BuildButton.Forward, ref forwardNudge) ? 1 : 0) - (Arrow(BuildButton.Back, ref backNudge) ? 1 : 0));
            if ((held & (BuildButton.Left | BuildButton.Right)) == (BuildButton.Left | BuildButton.Right)) nudge.x = 0;
            if ((held & (BuildButton.Forward | BuildButton.Back)) == (BuildButton.Forward | BuildButton.Back)) nudge.z = 0;
            building.Submit(new BuildInputCommand(turn, nudge, Pressed(BuildButton.Place), Pressed(BuildButton.Remove)));
        }
        else ResetNudgeRepeat();
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
    private void ResetNudgeRepeat() => leftNudge = rightNudge = forwardNudge = backNudge = default;
    private void OnApplicationFocus(bool focused) { if (!focused) ResetNudgeRepeat(); }
    // Explicit edges are sampled once by this owner, including while the picker is open.
    // This also consumes menu clicks and prevents repeated toggles/placements while held.
    private static BuildButton ReadBuildButtons(Keyboard keyboard, Mouse mouse)
    {
        BuildButton buttons = 0;
        if (keyboard != null)
        {
            if (keyboard.bKey.isPressed) buttons |= BuildButton.Toggle;
            if (keyboard.escapeKey.isPressed) buttons |= BuildButton.Cancel;
            if (keyboard.tabKey.isPressed) buttons |= BuildButton.Picker;
            if (keyboard.qKey.isPressed) buttons |= BuildButton.LeftTurn;
            if (keyboard.eKey.isPressed) buttons |= BuildButton.RightTurn;
            if (keyboard.leftArrowKey.isPressed) buttons |= BuildButton.Left;
            if (keyboard.rightArrowKey.isPressed) buttons |= BuildButton.Right;
            if (keyboard.upArrowKey.isPressed) buttons |= BuildButton.Forward;
            if (keyboard.downArrowKey.isPressed) buttons |= BuildButton.Back;
            if (keyboard.pageUpKey.isPressed) buttons |= BuildButton.Up;
            if (keyboard.pageDownKey.isPressed) buttons |= BuildButton.Down;
        }
        if (mouse != null)
        {
            if (mouse.leftButton.isPressed) buttons |= BuildButton.Place;
            if (mouse.rightButton.isPressed) buttons |= BuildButton.Remove;
        }
        return buttons;
    }
}
