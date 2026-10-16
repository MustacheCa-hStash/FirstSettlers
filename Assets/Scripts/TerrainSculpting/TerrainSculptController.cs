using System;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Development excavation interaction. Consumes commands after look/movement;
/// never reads devices, queues strokes, or samples the procedural generator on the main thread.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(175)]
public sealed class TerrainSculptController : MonoBehaviour
{
    public const float Reach=6, Inset=.25f, MinimumSize=.5f, MaximumSize=2;
    private TerrainSculptRuntime runtime;
    private Camera viewCamera;
    private LocalPlayerInput input;
    private TerrainSculptInputCommand pending;
    private bool submitted, focused=true;
    private TerrainSculptRepeat repeat;
    private TerrainSculptView view;
    public TerrainSculptRuntime Runtime => runtime;
    public bool Available => runtime!=null&&!runtime.IsDisposed;
    public bool Active { get; private set; }
    public TerrainDensityBrushShape Shape { get; private set; }
    public float Size { get; private set; }=MinimumSize;
    public bool HasPreview { get; private set; }
    public bool CanExcavate { get; private set; }
    public TerrainDensityBrush Preview { get; private set; }
    public string Status { get; private set; }="Sculpt mode off";

    private void Awake() => input=GetComponent<LocalPlayerInput>();
    public void Configure(TerrainSculptRuntime service,Camera camera)
    {
        Exit();runtime=service;viewCamera=camera;
        if(service==null){view?.Dispose();view=null;}
        input=GetComponent<LocalPlayerInput>();
        input?.SetSculpting(this);
    }
    public void Enter()
    {
        if(!isActiveAndEnabled||!Available)return;
        Active=true;Suspend();Status="Aim at terrain within 6 m";
    }
    public void Exit()
    { Active=false;Suspend();Status="Sculpt mode off"; }
    public void Suspend()
    { pending=default;submitted=false;repeat.Suspend();HasPreview=CanExcavate=false;view?.Hide(); }
    public void SetFocus(bool hasFocus)
    { focused=hasFocus;if(!hasFocus)Suspend(); }
    public void Submit(TerrainSculptInputCommand command)
    {
        if(!Active||!focused)return;
        pending=command;submitted=true; // Replace this frame's command, never enqueue it.
    }
    private void LateUpdate() => ProcessFrame(Time.unscaledTimeAsDouble);

    // Also used by isolated fixtures with real raycasts/runtime and an explicit clock.
    public void ProcessFrame(double now)
    {
        var command=pending;bool hasCommand=submitted;pending=default;submitted=false;
        if(!Active||!Available||!focused||!isActiveAndEnabled||
            input!=null&&(!input.WorldInteractionAllowed||input.BuildingActive)||
            viewCamera==null||!viewCamera.isActiveAndEnabled)
        {Suspend();return;}
        if(!double.IsFinite(now))throw new ArgumentOutOfRangeException(nameof(now));
        if(command.CycleShape)Shape=Shape==TerrainDensityBrushShape.Sphere?TerrainDensityBrushShape.Box:TerrainDensityBrushShape.Sphere;
        if(command.SizeSteps!=0)Size=Mathf.Clamp(Mathf.Round((Size+Mathf.Clamp(command.SizeSteps,-15,15)*.1f)*10)/10,MinimumSize,MaximumSize);
        string saveNotice=null;
        if(command.Save)
            saveNotice=runtime.Persistence==null?"Session-only terrain; disk saving is disabled":runtime.Persistence.RequestSave()?"Save requested":PersistenceBlockReason();

        UpdateAim();
        if(CanExcavate&&!runtime.Busy&&runtime.Status!="Ready")
        {
            // A failed backend transaction must remain visible, and a held stroke must not
            // retry an inadmissible allocation every quarter-second. Retry needs a new click.
            repeat.Suspend();CanExcavate=hasCommand&&command.DigHeld&&command.DigPressed;
            Status=runtime.Status+" · click to retry";
        }
        if(!hasCommand)repeat.Suspend();
        else if(repeat.WantsCut(command.DigHeld,command.DigPressed,now)&&CanExcavate)
        {
            if(runtime.TryExcavate(Preview))
            {repeat.Accepted(now);CanExcavate=false;Status="Excavation pending";}
            else {CanExcavate=false;Status=runtime.Persistence!=null&&!runtime.Persistence.CanExcavate?PersistenceBlockReason():runtime.Status;}
        }
        else if(command.DigHeld&&command.DigPressed&&CanExcavate)Status="Wait briefly between cuts (maximum 4/s)";
        if(saveNotice!=null)Status=saveNotice;
        view??=new TerrainSculptView(transform);
        view.Show(Shape,Size,Status,runtime.Persistence,HasPreview,CanExcavate,runtime.Busy,Preview);
    }

    private void UpdateAim()
    {
        HasPreview=CanExcavate=false;
        if(runtime.Persistence!=null&&!runtime.Persistence.Ready){Status=PersistenceBlockReason();return;}
        var ray=new Ray(viewCamera.transform.position,viewCamera.transform.forward);
        if(!Physics.Raycast(ray,out var hit,Reach,GameplayLayers.SolidSurfaceMask,QueryTriggerInteraction.Ignore))
        {Status="Aim at terrain within 6 m";return;}
        // Identity is checked after the nearest solid hit. Query-only targets/triggers never
        // let a ray pass through an obstructing building, rock or other physical object.
        if(!(hit.collider is TerrainCollider)&&hit.collider.GetComponent<WorldGroundSurface>()==null)
        {Status="Blocked by a solid object";return;}
        if(!runtime.CanMove((float3)ray.origin,(float3)hit.point,.05,.1))
        {Status="Waiting for edited terrain collision";return;}
        double3 center=(float3)(hit.point-hit.normal*Inset);
        Preview=Shape==TerrainDensityBrushShape.Sphere?TerrainDensityBrush.Sphere(center,Size):TerrainDensityBrush.Box(center,new double3(Size));
        HasPreview=true;
        if(Preview.Minimum.y<runtime.Density.MinimumRemovalY)
        {Status=$"Below dry excavation limit ({runtime.Density.MinimumRemovalY:0.00} m); brush bottom {Preview.Minimum.y:0.00} m";return;}
        if(runtime.Persistence!=null&&!runtime.Persistence.CanExcavate){Status=PersistenceBlockReason();return;}
        if(runtime.Busy){Status=runtime.Status;return;}
        CanExcavate=true;Status="Ready to excavate";
    }
    private string PersistenceBlockReason()
    {
        var store=runtime.Persistence;
        if(store.FailedToLoad)return "Terrain save could not be loaded; see F3";
        if(!store.Ready)return "Loading terrain edits";
        if(store.ReadOnly)return "Recovered terrain is read-only; see F3";
        return "Save failed; press F10 to retry before digging";
    }
    private void OnDisable()=>Exit();
    private void OnDestroy(){Exit();input?.ClearSculpting(this);view?.Dispose();view=null;}
}
