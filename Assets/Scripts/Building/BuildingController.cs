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
    [SerializeField, Min(0)] private float skyRoofHeight = 1;
    [SerializeField, Min(0)] private float skySideMargin = .15f;
    private BuildWorld world;
    private BuildMenuView view;
    private BuildDefinition selected;
    private Material twoSidedValidPreview, twoSidedInvalidPreview;
    private BuildPieceRecord target;
    private BuildSurfaceAimGuide skyGuide;
    private readonly List<BuildPieceRecord> candidates = new();
    private readonly RaycastHit[] jointGuideHits = new RaycastHit[16];
    private int worldHeading, contextTurn, suppressPlaceUntil;
    private Vector3Int nudge;
    private BuildInputCommand pending;
    private LocalPlayerInput input;
    private int cursorCaptureFrames;
    public bool GameplayCursorRequested { get; private set; }
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
        input = GetComponent<LocalPlayerInput>();
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
            Active = true; OpenMenu();
        }
    }
    public void OpenMenu()
    {
        if (!Active) return;
        MenuOpen = true; pending = default;
        skyGuide = default;
        GameplayCursorRequested = false; cursorCaptureFrames = 0;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true; view?.SetState(true, true);
    }
    public void Select(BuildDefinition definition)
    {
        if (!Active || definition == null) return;
        selected = definition; MenuOpen = false; nudge = default; contextTurn = 0; target = null; pending = default;
        skyGuide = default; Preview = default;
        if (definition.IsWallInfill && viewCamera != null) worldHeading = BuildPlacement.FacingYaw(-viewCamera.transform.forward);
        suppressPlaceUntil = Time.frameCount + 1;
        RequestGameplayCursor(); view?.SetState(true, false);
    }
    public void Exit() => Close(true);
    private void Close(bool capture)
    {
        bool wasActive = Active;
        Active = false; MenuOpen = false; pending = default; target = null; Preview = default; view?.SetState(false, false);
        skyGuide = default;
        if (capture && wasActive) RequestGameplayCursor();
        else if (!capture) { GameplayCursorRequested = false; cursorCaptureFrames = 0; }
    }
    private void RequestGameplayCursor()
    {
        GameplayCursorRequested = true;
        // The picker callback or Editor Escape handling can release capture later in the same frame.
        // Repeat briefly after the transition, rather than continually fighting window/UI focus.
        cursorCaptureFrames = 3;
        input?.SuppressLookAfterCursorCapture();
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }
    private void OnApplicationFocus(bool focused)
    {
        if (focused && isActiveAndEnabled && GameplayCursorRequested && !MenuOpen) RequestGameplayCursor();
    }
    private void RestoreCursorCapture()
    {
        if (cursorCaptureFrames <= 0 || MenuOpen || !Application.isFocused) return;
        cursorCaptureFrames--;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }
    public void Submit(BuildInputCommand command) => pending = command;
    private void OnDisable() => Close(false);
    private void OnDestroy()
    {
        Close(false); view?.Dispose();
        BuildLifetime.Destroy(twoSidedValidPreview); BuildLifetime.Destroy(twoSidedInvalidPreview);
    }
    private void LateUpdate()
    {
        RestoreCursorCapture();
        var command = pending; pending = default;
        if (!Active || MenuOpen || selected == null || viewCamera == null) return;
        var ray = viewCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
        if (!TryResolveAim(ray, out var aimPoint, out var aimNormal, out var direct, out var nextTarget, out bool guided))
        {
            Preview = default; target = null; nudge = default; skyGuide = default;
            view.Show(selected.displayName, "Aim at ground or a building within reach", false); return;
        }
        bool preferTop = nextTarget == target && Preview.TopAttachment;
        if (nextTarget != target) { target = nextTarget; contextTurn = 0; nudge = default; }
        if (target == null) worldHeading = BuildGeometry.Turn(worldHeading + command.Turn);
        else contextTurn = BuildGeometry.Turn(contextTurn + command.Turn);
        int gridYaw = target != null ? world.Session.Frame(target.OwnFrameId).YawStep : worldHeading;
        nudge += BuildGeometry.ViewRelativeNudge(command.Nudge, viewCamera.transform.rotation, gridYaw);
        var preview = BuildPlacement.Solve(selected, aimPoint, aimNormal, target,
            target != null ? world.Session.Frame(target.OwnFrameId) : null, worldHeading, contextTurn, nudge, ray.origin, preferTop);
        if (guided) preview.Hint = "Sky guide · " + preview.Hint;
        world.Validate(ref preview);
        // Nudges cannot extend an interaction into arbitrary distant construction.
        if (BuildGeometry.Distance(selected.LocalBounds, preview.Origin, preview.WorldYaw, ray.origin) > reach)
        { preview.Valid = false; preview.Message = "Preview is beyond building reach"; }
        if (command.Remove && direct != null)
        {
            world.Remove(direct.Id); target = null; nudge = default; Preview = default;
            skyGuide = default;
            view.Show(selected.displayName, "Piece removed · detached pieces have 3 seconds to regain support", true); return;
        }
        if (command.Place && Time.frameCount > suppressPlaceUntil && preview.Valid)
        {
            world.Commit(preview); suppressPlaceUntil = Time.frameCount;
        }
        Preview = preview;
        view.Show(selected.displayName + " · " + (preview.WorldYaw * 45) + "°",
            preview.Valid ? preview.Message + " · " + preview.Hint : preview.Message, preview.Valid);
        var parameters = new RenderParams(GetPreviewMaterial(preview.Valid))
        { camera = viewCamera, worldBounds = BuildGeometry.WorldBounds(preview.VisualMesh.bounds, preview.Origin, preview.WorldYaw),
            shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
        Graphics.RenderMesh(parameters, preview.VisualMesh, 0, Matrix4x4.TRS(preview.Origin, BuildGeometry.Rotation(preview.WorldYaw), Vector3.one));
    }
    private Material GetPreviewMaterial(bool valid)
    {
        var shared = valid ? world.Catalog.validPreview : world.Catalog.invalidPreview;
        // The ghost must show the same face sides as the authored component (e.g. thin wattle sheets).
        if (selected == null || selected.material == null || !selected.material.HasProperty("_Cull") ||
            selected.material.GetFloat("_Cull") >= .5f) return shared;
        return valid ? twoSidedValidPreview ??= TwoSidedPreview(shared) : twoSidedInvalidPreview ??= TwoSidedPreview(shared);
    }
    private static Material TwoSidedPreview(Material shared)
    {
        var material = new Material(shared) { name = shared.name + " (two-sided)", hideFlags = HideFlags.HideAndDontSave };
        material.SetFloat("_Cull", (float)CullMode.Off);
        return material;
    }
    private bool TryResolveAim(Ray ray, out Vector3 point, out Vector3 normal, out BuildPieceRecord direct,
        out BuildPieceRecord nextTarget, out bool guided)
    {
        direct = nextTarget = null; guided = false;
        if (Physics.Raycast(ray, out var hit, reach, GameplayLayers.SolidSurfaceMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point; normal = hit.normal;
            direct = hit.collider.GetComponentInParent<BuildGameplayProxy>()?.Record;
            if (direct != null && !world.Session.TryGet(direct.Id, out direct)) direct = null;
            nextTarget = direct ?? (BuildWorld.IsGround(hit.collider) ? NearbyTarget(hit.point) : null);
            skyGuide = default;
            // A wall face beside a real corner can hide the narrow post from the centre ray.
            // Resolve that connected joint locally; unrelated solids keep their ordinary priority.
            if (selected.kind == BuildPartKind.Corner && direct != null && direct.Definition.kind == BuildPartKind.Wall && hit.normal.y > -.6f &&
                TryJointCorner(ray, hit.point, hit.normal, hit.distance, direct, out var corner, out var cornerPoint, out var cornerNormal, out guided))
            {
                nextTarget = corner; point = cornerPoint; normal = cornerNormal;
                skyGuide.Capture(corner, world.Session.Frame(corner.OwnFrameId), normal, ray.origin);
            }
            else if (direct != null && ((direct.Definition.kind == BuildPartKind.Wall &&
                (selected.kind == BuildPartKind.Wall || selected.kind == BuildPartKind.Floor)) ||
                (direct.Definition.kind == BuildPartKind.Corner && selected.kind == BuildPartKind.Corner)))
                skyGuide.Capture(direct, world.Session.Frame(direct.OwnFrameId), hit.normal, ray.origin);
            return true;
        }
        if (target != null && world.Session.TryGet(target.Id, out _) &&
            skyGuide.TryContinue(ray, reach, selected.kind == BuildPartKind.Floor ? skyRoofHeight : selected.LocalBounds.size.y,
                skySideMargin, out point, out normal))
        {
            nextTarget = target; guided = true; return true;
        }
        point = normal = default; skyGuide = default; return false;
    }
    private bool TryJointCorner(Ray ray, Vector3 hit, Vector3 hitNormal, float hitDistance, BuildPieceRecord wall,
        out BuildPieceRecord corner, out Vector3 point, out Vector3 normal, out bool guided)
    {
        corner = null; point = hit; normal = hitNormal; guided = false;
        Bounds search = wall.WorldBounds; search.Expand(Mathf.Max(acquireMargin, releaseMargin) * 2);
        world.Session.Query(search, candidates);
        float best = float.PositiveInfinity;
        foreach (var piece in candidates)
        {
            if (piece.Definition.kind != BuildPartKind.Corner || !BuildGeometry.Connects(wall.Definition.LocalBounds, wall.Origin,
                wall.WorldYawStep, piece.Definition.LocalBounds, piece.Origin, piece.WorldYawStep)) continue;
            var frame = world.Session.Frame(piece.OwnFrameId);
            Vector3 localHit = BuildGeometry.LocalPoint(frame, hit);
            float distance = BuildGeometry.Distance(piece.Definition.LocalBounds, piece.Origin, piece.WorldYawStep, hit);
            bool nearUpper = localHit.y >= piece.Definition.LocalBounds.center.y &&
                distance <= (piece == target ? Mathf.Max(acquireMargin, releaseMargin) : acquireMargin);
            var guide = new BuildSurfaceAimGuide(); guide.Capture(piece, frame, hitNormal, ray.origin);
            bool inferred = guide.TryContinue(ray, reach, selected.LocalBounds.size.y, skySideMargin, out var guidePoint, out var guideNormal) &&
                (Vector3.Distance(ray.origin, guidePoint) <= hitDistance + .01f || ClearCornerGuide(ray, guidePoint, piece));
            if (!nearUpper && !inferred) continue;
            float score = nearUpper ? distance : Vector3.Distance(guidePoint, hit);
            if (score >= best) continue;
            best = score; corner = piece; guided = !nearUpper;
            point = guided ? guidePoint : hit; normal = guided ? guideNormal : hitNormal;
        }
        return corner != null;
    }
    private bool ClearCornerGuide(Ray ray, Vector3 guidePoint, BuildPieceRecord corner)
    {
        int count = Physics.RaycastNonAlloc(ray, jointGuideHits, Vector3.Distance(ray.origin, guidePoint),
            GameplayLayers.SolidSurfaceMask, QueryTriggerInteraction.Ignore);
        if (count == jointGuideHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var piece = jointGuideHits[i].collider.GetComponentInParent<BuildGameplayProxy>()?.Record;
            if (piece == corner) continue;
            // Only the walls touching this post may mask the aim above their top edge.
            if (piece == null || !world.Session.TryGet(piece.Id, out _) || piece.Definition.kind != BuildPartKind.Wall ||
                piece.WorldBounds.max.y > guidePoint.y + .01f || !BuildGeometry.Connects(piece.Definition.LocalBounds, piece.Origin,
                    piece.WorldYawStep, corner.Definition.LocalBounds, corner.Origin, corner.WorldYawStep)) return false;
        }
        return true;
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
