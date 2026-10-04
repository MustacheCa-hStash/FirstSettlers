#ifndef STANDING_TREE_INSTANCE_INCLUDED
#define STANDING_TREE_INSTANCE_INCLUDED

// Zero is the default for existing scene materials and captured impostors.
float _StandingTreeEnabled;
UNITY_INSTANCING_BUFFER_START(StandingTreeInstances)
    UNITY_DEFINE_INSTANCED_PROP(float4, _StandingTreeLeaves)
    UNITY_DEFINE_INSTANCED_PROP(float4, _StandingTreeBark)
    UNITY_DEFINE_INSTANCED_PROP(float4, _StandingTreeAppearance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _StandingTreeFade)
UNITY_INSTANCING_BUFFER_END(StandingTreeInstances)

half4 StandingTreeLeafTint(half4 fallback)
{
    return _StandingTreeEnabled > .5 ? UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeLeaves) : fallback;
}
half4 StandingTreeBarkTint(half4 fallback)
{
    return _StandingTreeEnabled > .5 ? UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeBark) : fallback;
}
half StandingTreeSnow(half fallback)
{
    return _StandingTreeEnabled > .5 ? UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeAppearance).x : fallback;
}
half StandingTreeAlphaShadows(half fallback)
{
    return _StandingTreeEnabled > .5 ? UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeAppearance).y : fallback;
}
void ApplyStandingTreeFade(float2 pixel)
{
    if (_StandingTreeEnabled < .5) return;
    float4 fade = UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeFade);
    float threshold = frac(52.9829189 * frac(dot(floor(pixel), float2(.06711056, .00583715))));
    // An interval partitions the same noise between mesh LODs and the far billboard.
    clip(threshold - fade.x);
    clip(fade.y - threshold - .00001);
    // Independent load noise avoids holes when load and handoff fades run together.
    float loadThreshold = frac(52.9829189 * frac(dot(floor(pixel) + 19.0, float2(.06711056, .00583715))));
    clip(fade.z - loadThreshold - .00001);
}
#endif
