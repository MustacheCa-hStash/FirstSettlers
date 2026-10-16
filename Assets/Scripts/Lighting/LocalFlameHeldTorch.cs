using UnityEngine;

/// <summary>Development offhand equip fixture until inventory ownership exists. Input stays in LocalPlayerInput.</summary>
[DefaultExecutionOrder(300)]
public sealed class LocalFlameHeldTorch : MonoBehaviour
{
    GameObject prop;
    LocalFlameSource source;
    MeshRenderer solid;
    Camera viewCamera;
    public bool Equipped=>prop!=null && prop.activeSelf;
    public void Toggle(Camera view,BuildWorld world)
    {
        viewCamera=view;if(viewCamera==null)return;
        LocalFlameLighting.Ensure().targetCamera=view;
        if(prop==null)
        {
            var prefab=Resources.Load<GameObject>("Lighting/HandTorch");if(prefab==null)return;
            prop=Instantiate(prefab,transform);source=prop.GetComponent<LocalFlameSource>();solid=prop.GetComponent<MeshRenderer>();source.coverageWorld=world;
            var occluder=prop.AddComponent<LocalFlameOccluder>();occluder.dynamicOccluder=true;occluder.GeometryChanged();source.ignoredGeometryOwner=occluder;
        }
        else prop.SetActive(!prop.activeSelf);
    }
    void LateUpdate()
    {
        if(!Equipped || viewCamera==null)return;
        Vector3 forward=Vector3.ProjectOnPlane(viewCamera.transform.forward,Vector3.up).normalized;
        if(forward.sqrMagnitude<.1f)forward=Vector3.ProjectOnPlane(viewCamera.transform.up,Vector3.up).normalized;
        Quaternion rotation=Quaternion.LookRotation(forward,Vector3.up);
        Vector3 desired=viewCamera.transform.position+viewCamera.transform.right*.28f+viewCamera.transform.forward*.42f-Vector3.up*.08f;
        Vector3 delta=desired-viewCamera.transform.position;float distance=delta.magnitude;
        bool clear=true;
        if(Physics.SphereCast(viewCamera.transform.position,.08f,delta/distance,out var hit,distance+.08f,GameplayLayers.SolidSurfaceMask,QueryTriggerInteraction.Ignore))
        {
            float available=Mathf.Max(0,hit.distance-.04f);desired=viewCamera.transform.position+delta.normalized*Mathf.Min(distance,available);
            clear=available>.16f;
        }
        prop.transform.SetPositionAndRotation(desired-rotation*source.localEmitter,rotation);
        source.burning=clear;solid.enabled=clear;
    }
    void OnDisable(){if(prop!=null)prop.SetActive(false);}
    void OnDestroy(){if(prop!=null)Destroy(prop);}
}
