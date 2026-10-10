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
    private readonly Collider[] overlaps = new Collider[128];
    private readonly Dictionary<ulong, float> collapseAt = new();
    private readonly List<ulong> expired = new();
    private BuildGameplay gameplay;
    private BuildRenderer buildingRenderer;
    private MeshCollider placementProbe;
    private int supportRevision = -1;
    private int previewRevision=-1,previewYaw;
    private Vector3 previewOrigin;
    private BuildDefinition previewDefinition;
    private bool previewGrounded;
    private Dictionary<ulong,BuildResolvedState> previewStates;
    private HashSet<ulong> previewSupported;
    public BuildSession Session { get; private set; }
    public BuildCatalog Catalog => catalog;
    public Camera Camera { get; set; }
    public Transform Focus { get; set; }
    public int DrawCalls => buildingRenderer?.DrawCalls ?? 0;
    public int ActiveColliders => gameplay?.ActiveCount ?? 0;
    public BuildColliderDebug ColliderDebug { get; private set; }
    public BuildPreview DebugPreview { get; private set; }
    public IReadOnlyDictionary<ulong,BuildResolvedState> DebugStates { get; private set; }
    public bool TryGetCollisionProxy(ulong id,out BuildGameplayProxy proxy) => gameplay.TryGetProxy(id,out proxy);
    public void ClearPreview()
    { buildingRenderer?.SetPreview(null);DebugPreview=default;DebugStates=null; }
    public void UpdateDebugPreview(BuildPreview preview) => DebugPreview=preview;
    private void Awake()
    {
        catalog ??= Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
        Session = new BuildSession(); buildingRenderer = new BuildRenderer(Session); gameplay = new BuildGameplay(Session);
        ColliderDebug=GetComponent<BuildColliderDebug>() ?? gameObject.AddComponent<BuildColliderDebug>();
    }
    private void LateUpdate()
    {
        if (Session == null) return;
        if (Focus != null) gameplay.Update(Focus.position, colliderRange, Mathf.Max(colliderRange + 1, colliderReleaseRange), activationsPerFrame);
        UpdateCollapse(); buildingRenderer.Draw(Camera, renderRange, shadowRange);
    }
    private void OnDestroy() { gameplay?.Dispose();Session?.Dispose(); if (placementProbe != null) BuildLifetime.Destroy(placementProbe.gameObject);if(boxProbe!=null)BuildLifetime.Destroy(boxProbe.gameObject); }
    public BuildPieceRecord Commit(BuildPreview preview)
    {
        Validate(ref preview);
        if (!preview.Valid) return null;
        var frame = preview.Frame.Id == 0 ? Session.CreateFrame(preview.Frame.Origin, preview.Frame.YawStep) : preview.Frame;
        var piece=Session.Add(preview.Definition, frame, preview.Anchor, preview.YawStep, preview.Grounded);ClearPreview();return piece;
    }
    public bool Remove(ulong id)
    {
        ClearPreview();
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
        using var scope=PlacementMarker.Auto();
        // Keep the selected snap and manual nudge; clearance warnings never move the piece.
        preview.FitStairContinuation=false;
        ValidateCore(ref preview);
        DebugPreview=preview;
    }
    private BoxCollider boxProbe;
    private void ValidateCore(ref BuildPreview preview)
    {
        preview.Valid=false;preview.RoofContinuesFromBelow=false;
        preview.Failure=BuildPlacementFailure.None;preview.BlockingPieceId=0;preview.BlockingCollider=null;
        preview.Warning=BuildPlacementWarning.None;preview.WarningPieceId=0;preview.WarningMessage=null;
        buildingRenderer?.SetPreview(null);
        DebugPreview=preview;
        preview.Grounded=preview.Definition.kind==BuildPartKind.Foundation && Grounded(preview);
        var candidate=new BuildPieceRecord{Id=ulong.MaxValue,Definition=preview.Definition,Origin=preview.Origin,
            WorldYawStep=(byte)preview.WorldYaw,Grounded=preview.Grounded};
        candidate.WorldBounds=BuildSession.IndexBounds(preview.Definition,candidate.Origin,candidate.WorldYawStep);
        bool cached=previewRevision==Session.Revision && previewDefinition==preview.Definition && previewOrigin==preview.Origin && previewYaw==preview.WorldYaw && previewGrounded==preview.Grounded;
        var states=cached?previewStates:Session.ResolvePreview(candidate);var state=states[candidate.Id];preview.Resolved=state;
        DebugStates=states;
        if(state.boxes.Length+state.volumes.Length==0)
        {Reject(ref preview,BuildPlacementFailure.Joint,"No compatible floor joint at this orientation");return;}
        if(state.invalidJoint)Warn(ref preview,BuildPlacementWarning.UnresolvedJoint);
        foreach(var piece in Session.Pieces.Values)
        {
            Bounds area=candidate.WorldBounds;area.Expand(.04f);if(!area.Intersects(piece.WorldBounds))continue;
            var other=states[piece.Id];
            if(BuildPlacementPolicy.IsDuplicate(candidate,piece))
            {Reject(ref preview,BuildPlacementFailure.Solid,"Duplicate build piece",piece);return;}
            if(other.invalidJoint)Warn(ref preview,BuildPlacementWarning.UnresolvedJoint,piece);
            if(BuildRoof.IsBelow(preview.Definition,preview.Origin,preview.WorldYaw,piece.Definition,piece.Origin,piece.WorldYawStep))preview.RoofContinuesFromBelow=true;
            var warning=BuildPlacementPolicy.Assess(candidate,state,piece,other);
            if(warning!=BuildPlacementWarning.None)Warn(ref preview,warning,piece);
        }
        var all=new List<BuildPieceRecord>(Session.Pieces.Values){candidate};
        var supported=cached?previewSupported:BuildSupportState.Resolve(all,states,p=>Session.NeighboursFor(p,candidate));
        previewStates=states;previewSupported=supported;previewRevision=Session.Revision;previewDefinition=preview.Definition;previewOrigin=preview.Origin;previewYaw=preview.WorldYaw;previewGrounded=preview.Grounded;
        if(!supported.Contains(candidate.Id)){Reject(ref preview,BuildPlacementFailure.Support,"Needs a grounded foundation or supported piece");return;}
        foreach(var piece in Session.Pieces.Values)if(piece.Supported && !supported.Contains(piece.Id))
        {Reject(ref preview,BuildPlacementFailure.Support,"Would remove support from an existing piece",piece);return;}
        Bounds local=state.Bounds;
        int count=Physics.OverlapBoxNonAlloc(preview.Origin+BuildGeometry.Rotation(preview.WorldYaw)*local.center,
            Vector3.Max(local.extents-Vector3.one*.003f,Vector3.one*.001f),overlaps,BuildGeometry.Rotation(preview.WorldYaw),
            GameplayLayers.SolidSurfaceMask|(1<<GameplayLayers.Player),QueryTriggerInteraction.Ignore);
        if(count==overlaps.Length){Reject(ref preview,BuildPlacementFailure.Crowded,"Placement area is too crowded");return;}
        for(int i=0;i<count;i++)
        {
            var collider=overlaps[i];if(collider.GetComponentInParent<BuildGameplayProxy>()!=null)continue;
            if(preview.Grounded && IsGround(collider))continue;
            bool hit=false;
            foreach(var b in state.boxes)
            {
                if(boxProbe==null){var obj=new GameObject("Build box probe"){hideFlags=HideFlags.HideAndDontSave};boxProbe=obj.AddComponent<BoxCollider>();boxProbe.enabled=false;}
                boxProbe.center=b.center;boxProbe.size=Vector3.Max(b.size-Vector3.one*.006f,Vector3.one*.001f);boxProbe.enabled=true;
                try{hit=Physics.ComputePenetration(boxProbe,preview.Origin,BuildGeometry.Rotation(preview.WorldYaw),collider,collider.transform.position,collider.transform.rotation,out _,out float depth)&&depth>.003f;}
                finally{boxProbe.enabled=false;}
                if(hit)break;
            }
            if(!hit)foreach(var volume in state.volumes)
            {
                if(placementProbe==null){var obj=new GameObject("Build convex probe"){hideFlags=HideFlags.HideAndDontSave};placementProbe=obj.AddComponent<MeshCollider>();placementProbe.convex=true;placementProbe.enabled=false;}
                placementProbe.enabled=true;placementProbe.sharedMesh=volume.mesh;
                try{hit=Physics.ComputePenetration(placementProbe,preview.Origin,BuildGeometry.Rotation(preview.WorldYaw),collider,collider.transform.position,collider.transform.rotation,out _,out float depth)&&depth>.003f;}
                finally{placementProbe.enabled=false;}
                if(hit)break;
            }
            if(hit)
            {
                Reject(ref preview,BuildPlacementFailure.External,collider.gameObject.layer==GameplayLayers.Player?"Move clear of the preview":"Blocked by terrain or another object");
                preview.BlockingCollider=collider;preview.Message+=" · "+collider.name;return;
            }
        }
        buildingRenderer?.SetPreview(states);
        preview.Valid=true;preview.Message=state.invalidJoint?"Ready to place · joint fitting unavailable; full shape retained":state.cuts.Length>0?"Ready to place · fitted opening":"Ready to place";
    }
    private static void Reject(ref BuildPreview preview,BuildPlacementFailure failure,string message,BuildPieceRecord piece=null)
    {
        preview.Failure=failure;preview.BlockingPieceId=piece?.Id ?? 0;
        preview.Message=piece==null?message:message+" · "+piece.Definition.displayName+" #"+piece.Id;
    }
    private static void Warn(ref BuildPreview preview,BuildPlacementWarning warning,BuildPieceRecord piece=null)
    {
        if(BuildPlacementPolicy.Priority(warning)<=BuildPlacementPolicy.Priority(preview.Warning))return;
        preview.Warning=warning;preview.WarningPieceId=piece?.Id ?? 0;
        preview.WarningMessage=BuildPlacementPolicy.Describe(warning)+
            (piece==null?"":" · "+piece.Definition.displayName+" #"+piece.Id);
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
