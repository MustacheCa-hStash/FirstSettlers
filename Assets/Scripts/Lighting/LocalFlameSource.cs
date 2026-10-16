using UnityEngine;

/// <summary>One stable point emitter. Flame animation never changes its shadow origin.</summary>
[DisallowMultipleComponent]
public sealed class LocalFlameSource : MonoBehaviour
{
    public bool stationary=true;
    public bool burning=true;
    [Range(.5f,12)] public float range=12;
    [Min(0)] public float intensity=5;
    public Color color=new(1,.7f,.4f);
    [Range(0,.2f)] public float flicker=.08f;
    public Vector3 localEmitter=new(0,.83f,.32f);
    public float phase;
    [System.NonSerialized] public BuildWorld coverageWorld;
    [System.NonSerialized] public object ignoredGeometryOwner;
    [System.NonSerialized] public Transform ignoredCasterRoot;
    public bool HasLight { get; internal set; }
    public bool FlameVisible { get; internal set; }
    public Vector3 Position=>transform.TransformPoint(localEmitter);
    public float FlameStrength(float time)=>1+flicker*(Mathf.Sin(time*7.3f+phase)*.5f+Mathf.Sin(time*11.1f+phase*1.7f)*.3f+Mathf.Sin(time*17.9f+phase*2.3f)*.2f);
    Vector3Int cell;
    internal Vector3Int Cell=>cell;
    void OnEnable()
    {
        ignoredCasterRoot=transform;
        if(phase==0)phase=(GetEntityId().GetHashCode()&65535)*.0137f;
        cell=LocalFlameGeometry.Cell(Position);LocalFlameLighting.Ensure().Register(this);
    }
    void LateUpdate()
    {
        var next=LocalFlameGeometry.Cell(Position);
        if(next!=cell && LocalFlameLighting.Instance!=null)
        {LocalFlameLighting.Instance.Move(this,cell,next);cell=next;}
    }
    void OnDisable(){if(LocalFlameLighting.Instance!=null)LocalFlameLighting.Instance.Unregister(this);HasLight=false;FlameVisible=false;}
}
