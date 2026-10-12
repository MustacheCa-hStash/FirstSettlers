using System;
using UnityEngine;

/// <summary>One local menu at a time, with no return stack. Hardware stays in LocalPlayerInput.</summary>
[DisallowMultipleComponent]
public sealed class LocalPlayerMenus : MonoBehaviour
{
    private readonly object inventoryOwner = new();
    private object owner;
    private Action closeCurrent;
    private bool allowMovement;
    private int captureFrames;
    private InventoryView view;
    private LocalPlayerInput input;
    private BuildingController building;

    public bool IsOpen => owner != null;
    public bool InventoryOpen => ReferenceEquals(owner, inventoryOwner);
    public bool AllowsMovement => !IsOpen || allowMovement;
    public bool BlocksWorldInteraction => IsOpen;
    public int SelectedHotbarIndex { get; private set; }
    public InventoryView View => view;

    private void Awake()
    {
        input = GetComponent<LocalPlayerInput>();
        view = new InventoryView(transform, SelectHotbar);
        RefreshView();
    }

    public void SetBuilding(BuildingController controller) { building = controller; RefreshView(); }

    public void ToggleInventory()
    {
        if (InventoryOpen) Close(inventoryOwner);
        else
        {
            Open(inventoryOwner, null, true);
        }
    }

    /// <summary>Other menus use this entry point; replacing a menu never resumes it later.</summary>
    public void Open(object newOwner, Action onClosed, bool movementAllowed = false)
    {
        if (newOwner == null) throw new ArgumentNullException(nameof(newOwner));
        if (ReferenceEquals(owner, newOwner)) return;
        // Placement is also an interaction, even when the building catalog is closed.
        if (building != null && building.Active && !ReferenceEquals(newOwner, building)) building.Exit();
        DismissCurrent();
        owner = newOwner; closeCurrent = onClosed; allowMovement = movementAllowed;
        captureFrames = 0;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        RefreshView();
    }

    public void Close(object menuOwner, bool notifyOwner = true)
    {
        if (!ReferenceEquals(owner, menuOwner)) return;
        if (!notifyOwner) closeCurrent = null;
        CloseActive();
    }

    public void CloseActive()
    {
        if (!IsOpen) return;
        DismissCurrent();
        if (!IsOpen) CaptureCursor();
        RefreshView();
    }

    private void DismissCurrent()
    {
        var callback = closeCurrent;
        owner = null; closeCurrent = null; allowMovement = false;
        callback?.Invoke();
    }

    public void SelectHotbar(int index)
    {
        if (index < 0 || index >= 9) throw new ArgumentOutOfRangeException(nameof(index));
        SelectedHotbarIndex = index;
        view?.SetSelection(index);
    }

    private void CaptureCursor()
    {
        captureFrames = 3;
        input?.SuppressLookAfterCursorCapture();
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    public void RefreshView() => view?.SetState(InventoryOpen, !IsOpen);

    private void LateUpdate()
    {
        if (captureFrames <= 0 || IsOpen || !Application.isFocused) return;
        captureFrames--;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused || !isActiveAndEnabled) return;
        if (IsOpen) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        else CaptureCursor();
    }

    private void OnDisable()
    {
        DismissCurrent(); captureFrames = 0;
        view?.SetState(false, false);
    }

    private void OnEnable() => RefreshView();
    private void OnDestroy() { DismissCurrent(); view?.Dispose(); }
}
