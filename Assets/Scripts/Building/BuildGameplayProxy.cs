using UnityEngine;

/// <summary>Pooled collision and query identity only. The visible mesh stays instanced.</summary>
public sealed class BuildGameplayProxy : QueryTarget
{
    public BuildPieceRecord Record { get; private set; }
    public BoxCollider Shape { get; private set; }
    public MeshCollider RampShape { get; private set; }
    private SmoothWalkSurface smoothWalk;
    private readonly System.Collections.Generic.List<MeshCollider> volumes = new();
    private readonly System.Collections.Generic.List<BoxCollider> boxes = new();
    public int BoundRevision { get; private set; }
    public void GetEnabledColliders(System.Collections.Generic.List<Collider> output)
    {
        output.Clear();
        if(this==null || Record==null || !gameObject.activeInHierarchy)return;
        if(Shape!=null && Shape.enabled)output.Add(Shape);
        if(RampShape!=null && RampShape.enabled)output.Add(RampShape);
        foreach(var c in boxes)if(c!=null && c.enabled)output.Add(c);
        foreach(var c in volumes)if(c!=null && c.enabled)output.Add(c);
    }
    public Collider ActiveShape => Record != null && Record.Definition.kind==BuildPartKind.Stair ? RampShape :
        Record != null && BuildOccupancy.Custom(Record.Definition) && volumes.Count>0 ? volumes[0] : Shape;
    protected override bool IsAvailable => Record != null;
    public void Bind(BuildPieceRecord record)
    {
        if (record.Definition.kind == BuildPartKind.Stair && record.Definition.collisionMesh == null)
            throw new System.InvalidOperationException("A stair requires its authored walking hull.");
        if (Record != record) InvalidateIdentity();
        Record = record;
        BoundRevision=record.ResolvedRevision;
        gameObject.layer = GameplayLayers.WorldSolid;
        if (Shape != null) Shape.enabled = false;
        if (RampShape != null) RampShape.enabled = false;
        if (smoothWalk != null) smoothWalk.enabled = false;
        foreach (var volume in volumes) volume.enabled = false;
        foreach (var box in boxes) box.enabled=false;
        transform.SetPositionAndRotation(record.Origin, BuildGeometry.Rotation(record.WorldYawStep));
        transform.localScale = Vector3.one;
        var state=record.Resolved;
        if(record.Definition.kind==BuildPartKind.Stair)
        {
            RampShape ??=gameObject.AddComponent<MeshCollider>();RampShape.convex=true;RampShape.sharedMesh=record.Definition.collisionMesh;RampShape.enabled=true;
            smoothWalk ??=gameObject.AddComponent<SmoothWalkSurface>();smoothWalk.enabled=true;
        }
        else if (state!=null)
        {
            for(int i=0;i<state.boxes.Length;i++)
            {
                BoxCollider collider;
                if(i==0){Shape??=gameObject.AddComponent<BoxCollider>();collider=Shape;}
                else
                {
                    if(i-1==boxes.Count){var child=new GameObject("Resolved solid"){layer=GameplayLayers.WorldSolid};child.transform.SetParent(transform,false);boxes.Add(child.AddComponent<BoxCollider>());}
                    collider=boxes[i-1];
                }
                collider.center=state.boxes[i].center;collider.size=state.boxes[i].size;collider.isTrigger=false;collider.enabled=true;
            }
            for(int i=0;i<state.volumes.Length;i++)
            {
                if(i==volumes.Count){var child=new GameObject("Resolved convex solid"){layer=GameplayLayers.WorldSolid};child.transform.SetParent(transform,false);volumes.Add(child.AddComponent<MeshCollider>());}
                var collider=volumes[i];collider.convex=true;collider.sharedMesh=state.volumes[i].mesh;collider.enabled=true;
            }
        }
        else if (BuildOccupancy.Custom(record.Definition))
        {
            for (int i=0; i<record.Definition.occupiedVolumes.Length; i++)
            {
                if (i == volumes.Count)
                {
                    var child=new GameObject("Occupied roof volume") { layer=GameplayLayers.WorldSolid };
                    child.transform.SetParent(transform,false); volumes.Add(child.AddComponent<MeshCollider>());
                }
                var collider=volumes[i]; collider.convex=true; collider.isTrigger=false;
                collider.sharedMesh=record.Definition.occupiedVolumes[i].mesh; collider.enabled=true;
            }
        }
        else if (record.Definition.kind == BuildPartKind.Stair)
        {
            RampShape ??= gameObject.AddComponent<MeshCollider>();
            RampShape.convex = true; RampShape.isTrigger = false;
            if (RampShape.sharedMesh != record.Definition.collisionMesh) RampShape.sharedMesh = record.Definition.collisionMesh;
            RampShape.enabled = true;
            smoothWalk ??= gameObject.AddComponent<SmoothWalkSurface>(); smoothWalk.enabled = true;
        }
        else
        {
            Shape ??= gameObject.AddComponent<BoxCollider>();
            Shape.center = record.Definition.LocalBounds.center;
            Shape.size = record.Definition.LocalBounds.size;
            Shape.isTrigger = false; Shape.enabled = true;
        }
        gameObject.SetActive(true);
    }
    public void Unbind()
    {
        InvalidateIdentity(); Record = null;
        if (this == null) return;
        if (Shape != null) Shape.enabled = false;
        if (RampShape != null) RampShape.enabled = false;
        if (smoothWalk != null) smoothWalk.enabled = false;
        foreach (var volume in volumes) if(volume!=null)volume.enabled = false;
        foreach (var box in boxes) if(box!=null)box.enabled=false;
        gameObject.SetActive(false);
    }
    public override bool TryGetInfo(out QueryTargetInfo info)
    {
        info = default;
        if (!IsQueryAvailable) return false;
        info = new QueryTargetInfo(this, Record.Definition.displayName, null, QueryTargetCapabilities.Breakable, Record);
        return true;
    }
}
