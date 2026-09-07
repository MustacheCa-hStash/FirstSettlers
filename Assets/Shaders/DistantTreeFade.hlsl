#ifndef DISTANT_TREE_FADE_INCLUDED
#define DISTANT_TREE_FADE_INCLUDED

// Default zero leaves every existing material unchanged. Per-renderer near fade is
// complementary to the billboard transition; density/load fade affects billboards only.
float _DistantTreeEnabled;
float _DistantTreeBillboard;
float _DistantTreeNearFade;
UNITY_INSTANCING_BUFFER_START(DistantTreeInstances)
    UNITY_DEFINE_INSTANCED_PROP(float4, _DistantTreeFade)
UNITY_INSTANCING_BUFFER_END(DistantTreeInstances)

void ApplyDistantTreeFade(float2 pixel)
{
    if (_DistantTreeEnabled < 0.5) return;
    float threshold = frac(52.9829189 * frac(dot(floor(pixel), float2(0.06711056, 0.00583715))));
    if (_DistantTreeBillboard > 0.5)
    {
        float4 fade = UNITY_ACCESS_INSTANCED_PROP(DistantTreeInstances, _DistantTreeFade);
        clip(fade.x - threshold - 0.00001);
        clip(fade.y - threshold - 0.00001);
    }
    else
        clip(threshold - _DistantTreeNearFade);
}

#endif
