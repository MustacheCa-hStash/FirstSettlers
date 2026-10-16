using UnityEngine;

/// <summary>Shared-material flipbook; phase is per renderer, animation stays on the GPU.</summary>
public sealed class LocalFlameEffect : MonoBehaviour
{
    public LocalFlameSource source;
    public Renderer flameRenderer;
    MaterialPropertyBlock properties;
    void OnEnable()
    {
        properties??=new MaterialPropertyBlock();
        if(source==null)source=GetComponentInParent<LocalFlameSource>();
        if(flameRenderer==null)flameRenderer=GetComponent<Renderer>();
        if(source!=null && flameRenderer!=null)
        {properties.SetFloat("_Phase",source.phase);properties.SetFloat("_Flicker",source.flicker);flameRenderer.SetPropertyBlock(properties);}
    }
    void LateUpdate()
    {if(flameRenderer!=null)flameRenderer.enabled=source!=null && source.burning && source.FlameVisible;}
}
