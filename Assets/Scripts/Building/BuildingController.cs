using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(BuildWorld))]
[DefaultExecutionOrder(150)]
public sealed class BuildingController : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField, Min(1)] private float reach = 8;
    [SerializeField, Min(0)] private float acquireMargin = .1f;
    [SerializeField, Min(0)] private float releaseMargin = .15f;
    private BuildWorld world;
    private BuildMenuView view;
    private BuildDefinition selected;
    private BuildPieceRecord target;
    private readonly List<BuildPieceRecord> candidates = new();
    private int worldHeading, contextTurn, suppressPlaceUntil;
    private Vector3Int nudge;
    private BuildInputCommand pending;
    private CursorLockMode previousLock;
    private bool previousVisibility;
    public bool Active { get; private set; }
    public bool MenuOpen { get; private set; }
    public BuildPreview Preview { get; private set; }
    public BuildWorld World => world;
    public Camera ViewCamera => viewCamera;

    private void Start()
    {
        world = GetComponent<BuildWorld>();
        if (viewCamera == null) viewCamera = GetComponentInParent<CharacterMotor>()?.GetComponentInChildren<Camera>();
        world.Camera = viewCamera;
        world.Focus = GetComponentInParent<CharacterMotor>()?.transform ?? transform;
        var input = GetComponent<LocalPlayerInput>();
        if (input != null) input.SetBuilding(this);
        if (world.Catalog == null || world.Catalog.presets == null || world.Catalog.presets.Length == 0)
        { Debug.LogError("Building prototype catalog is missing.", this); enabled = false; return; }
        view = new BuildMenuView(world.Catalog, transform, Select);
    }
    public void Toggle()
    {
        if (!enabled || view == null) return;
        if (Active) Exit();
        else
        {
            previousLock = Cursor.lockState; previousVisibility = Cursor.visible;
            Active = true; OpenMenu();
        }
    }
    public void OpenMenu()
    {
        if (!Active) return;
        MenuOpen = true; pending = default;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true; view?.SetState(true, true);
    }
    public void Select(BuildDefinition definition)
    {
        if (!Active || definition == null) return;
        selected = definition; MenuOpen = false; nudge = default; contextTurn = 0; target = null; pending = default;
        suppressPlaceUntil = Time.frameCount + 1;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; view?.SetState(true, false);
    }
    public void Exit()
    {
        if (Active) { Cursor.lockState = previousLock; Cursor.visible = previousVisibility; }
        Active = false; MenuOpen = false; pending = default; target = null; Preview = default; view?.SetState(false, false);
    }
    public void Submit(BuildInputCommand command) => pending = command;
    private void OnDisable() => Exit();
    private void OnDestroy() { Exit(); view?.Dispose(); }
    private void LateUpdate()
    {
        var command = pending; pending = default;
        if (!Active || MenuOpen || selected == null || viewCamera == null) return;
        var ray = viewCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
        if (!Physics.Raycast(ray, out var hit, reach, GameplayLayers.SolidSurfaceMask, QueryTriggerInteraction.Ignore))
        {
            Preview = default; target = null; nudge = default;
            view.Show(selected.displayName, "Aim at ground or a building within reach", false); return;
        }
        BuildPieceRecord direct = hit.collider.GetComponent<BuildGameplayProxy>()?.Record;
        if (direct != null && !world.Session.TryGet(direct.Id, out direct)) direct = null;
        var nextTarget = direct ?? (BuildWorld.IsGround(hit.collider) ? NearbyTarget(hit.point) : null);
        if (nextTarget != target) { target = nextTarget; contextTurn = 0; nudge = default; }
        if (target == null) worldHeading = BuildGeometry.Turn(worldHeading + command.Turn);
        else contextTurn = BuildGeometry.Turn(contextTurn + command.Turn);
        nudge += command.Nudge;
        var preview = BuildPlacement.Solve(selected, hit.point, hit.normal, target,
            target != null ? world.Session.Frame(target.OwnFrameId) : null, worldHeading, contextTurn, nudge);
        world.Validate(ref preview);
        // Nudges cannot extend an interaction into arbitrary distant construction.
        if (BuildGeometry.Distance(selected.LocalBounds, preview.Origin, preview.WorldYaw, ray.origin) > reach)
        { preview.Valid = false; preview.Message = "Preview is beyond building reach"; }
        if (command.Remove && direct != null)
        {
            world.Remove(direct.Id); target = null; nudge = default; Preview = default;
            view.Show(selected.displayName, "Piece removed · detached pieces have 3 seconds to regain support", true); return;
        }
        if (command.Place && Time.frameCount > suppressPlaceUntil && preview.Valid)
        {
            world.Commit(preview); suppressPlaceUntil = Time.frameCount;
        }
        Preview = preview;
        view.Show(selected.displayName + " · " + (preview.WorldYaw * 45) + "°", preview.Message, preview.Valid);
        var parameters = new RenderParams(preview.Valid ? world.Catalog.validPreview : world.Catalog.invalidPreview)
        { camera = viewCamera, worldBounds = BuildGeometry.WorldBounds(selected.LocalBounds, preview.Origin, preview.WorldYaw),
            shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
        Graphics.RenderMesh(parameters, selected.mesh, 0, Matrix4x4.TRS(preview.Origin, BuildGeometry.Rotation(preview.WorldYaw), Vector3.one));
    }
    private BuildPieceRecord NearbyTarget(Vector3 hit)
    {
        if (target != null && world.Session.TryGet(target.Id, out _) &&
            BuildGeometry.Distance(target.Definition.LocalBounds, target.Origin, target.WorldYawStep, hit) <= Mathf.Max(acquireMargin, releaseMargin)) return target;
        world.Session.Query(new Bounds(hit, Vector3.one * (acquireMargin * 2)), candidates);
        BuildPieceRecord best = null; float distance = acquireMargin;
        foreach (var piece in candidates)
        {
            float value = BuildGeometry.Distance(piece.Definition.LocalBounds, piece.Origin, piece.WorldYawStep, hit);
            if (value <= distance) { best = piece; distance = value; }
        }
        return best;
    }
}
