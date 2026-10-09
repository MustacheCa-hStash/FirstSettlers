using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

/// <summary>Owns one play-session registry and independent visible/physical representations.</summary>
public sealed class BuildWorld : MonoBehaviour
{
    private static readonly ProfilerMarker PlacementMarker = new("FS.Building.PlacementValidation");
    [SerializeField] private BuildCatalog catalog;
    [SerializeField, Min(1)] private float renderRange = 3000;
    [SerializeField, Min(0)] private float shadowRange = 140;
    [SerializeField, Min(12)] private float colliderRange = 32;
    [SerializeField, Min(16)] private float colliderReleaseRange = 40;
    [SerializeField, Min(1)] private int activationsPerFrame = 8;
    [SerializeField, Min(0)] private float unsupportedGraceSeconds = 3;
    private readonly List<BuildPieceRecord> nearby = new();
    private readonly Collider[] overlaps = new Collider[128];
    private readonly Dictionary<ulong, float> collapseAt = new();
    private readonly List<ulong> expired = new();
    private BuildGameplay gameplay;
    private BuildRenderer buildingRenderer;
    private int supportRevision = -1;
    public BuildSession Session { get; private set; }
    public BuildCatalog Catalog => catalog;
    public Camera Camera { get; set; }
    public Transform Focus { get; set; }
    public int DrawCalls => buildingRenderer?.DrawCalls ?? 0;
    public int ActiveColliders => gameplay?.ActiveCount ?? 0;
    private void Awake()
    {
        catalog ??= Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
        Session = new BuildSession(); buildingRenderer = new BuildRenderer(Session); gameplay = new BuildGameplay(Session);
    }
    private void LateUpdate()
    {
        if (Session == null) return;
        if (Focus != null) gameplay.Update(Focus.position, colliderRange, Mathf.Max(colliderRange + 1, colliderReleaseRange), activationsPerFrame);
        UpdateCollapse(); buildingRenderer.Draw(Camera, renderRange, shadowRange);
    }
    private void OnDestroy() { gameplay?.Dispose(); }
    public BuildPieceRecord Commit(BuildPreview preview)
    {
        Validate(ref preview);
        if (!preview.Valid) return null;
        var frame = preview.Frame.Id == 0 ? Session.CreateFrame(preview.Frame.Origin, preview.Frame.YawStep) : preview.Frame;
        return Session.Add(preview.Definition, frame, preview.Anchor, preview.YawStep, preview.Grounded);
    }
    public bool Remove(ulong id)
    {
        bool removed = Session.Remove(id);
        if (removed) gameplay.Remove(id);
        return removed;
    }
    public bool Damage(ulong id, float amount)
    {
        bool changed = Session.Damage(id, amount);
        if (changed && !Session.TryGet(id, out _)) gameplay.Remove(id);
        return changed;
    }
    public void Validate(ref BuildPreview preview)
    {
        using var scope = PlacementMarker.Auto();
        preview.Valid = false;
        Bounds local = preview.Definition.LocalBounds;
        Bounds world = BuildGeometry.WorldBounds(local, preview.Origin, preview.WorldYaw);
        Bounds search = world; search.Expand(.04f); Session.Query(search, nearby);
        bool supported = false;
        foreach (var piece in nearby)
        {
            if (BuildGeometry.Overlaps(local, preview.Origin, preview.WorldYaw, piece.Definition.LocalBounds, piece.Origin, piece.WorldYawStep))
            {
                preview.Message = preview.TopAttachment && piece.Definition.kind == BuildPartKind.Wall
                    ? "Wall occupies the floor edge · place the floor before the upper wall"
                    : "Overlaps an existing piece";
                return;
            }
            if (piece.Supported && BuildGeometry.Connects(preview.Definition, preview.Origin, preview.WorldYaw, piece.Definition, piece.Origin, piece.WorldYawStep)) supported = true;
        }
        preview.Grounded = preview.Definition.kind == BuildPartKind.Foundation && Grounded(preview);
        // Query player separately as well as world solids, with no dependency on the physics collision matrix.
        Vector3 extent = local.extents - Vector3.one * .003f;
        int count = Physics.OverlapBoxNonAlloc(preview.Origin + BuildGeometry.Rotation(preview.WorldYaw) * local.center,
            extent, overlaps, BuildGeometry.Rotation(preview.WorldYaw), GameplayLayers.SolidSurfaceMask | (1 << GameplayLayers.Player), QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) { preview.Message = "Placement area is too crowded"; return; }
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            if (collider.GetComponent<BuildGameplayProxy>() != null) continue;
            if (preview.Grounded && IsGround(collider)) continue;
            preview.Message = collider.gameObject.layer == GameplayLayers.Player ? "Move clear of the preview" : "Blocked by terrain or another object";
            return;
        }
        if (!preview.Grounded && !supported) { preview.Message = "Needs a grounded foundation or supported piece"; return; }
        preview.Valid = true; preview.Message = "Ready to place";
    }
    public static bool IsGround(Collider collider) => collider != null && (collider is TerrainCollider || collider.GetComponent<WorldGroundSurface>() != null);
    private static bool Grounded(BuildPreview preview)
    {
        Bounds b = preview.Definition.LocalBounds;
        for (int i = 0; i < 5; i++)
        {
            Vector3 p = i == 0 ? b.center : new Vector3(i % 2 == 0 ? b.max.x - .05f : b.min.x + .05f, 0,
                i < 3 ? b.min.z + .05f : b.max.z - .05f);
            p.y = b.min.y;
            Vector3 bottom = preview.Origin + BuildGeometry.Rotation(preview.WorldYaw) * p;
            if (Physics.Raycast(bottom + Vector3.up * .3f, Vector3.down, out var hit, .65f, GameplayLayers.SolidSurfaceMask, QueryTriggerInteraction.Ignore)
                && IsGround(hit.collider) && hit.point.y - bottom.y >= -.04f && hit.point.y - bottom.y <= .251f && hit.normal.y >= .5f) return true;
        }
        return false;
    }
    private void UpdateCollapse()
    {
        if (supportRevision != Session.Revision)
        {
            expired.Clear();
            foreach (var pair in collapseAt) if (!Session.TryGet(pair.Key, out var piece) || piece.Supported) expired.Add(pair.Key);
            foreach (ulong id in expired) collapseAt.Remove(id);
            foreach (var piece in Session.Pieces.Values) if (!piece.Supported && !collapseAt.ContainsKey(piece.Id)) collapseAt.Add(piece.Id, Time.time + unsupportedGraceSeconds);
            supportRevision = Session.Revision;
        }
        expired.Clear();
        foreach (var pair in collapseAt) if (Time.time >= pair.Value) expired.Add(pair.Key);
        foreach (ulong id in expired) { Remove(id); collapseAt.Remove(id); }
    }
}
