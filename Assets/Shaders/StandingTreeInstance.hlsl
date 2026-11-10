#ifndef STANDING_TREE_INSTANCE_INCLUDED
#define STANDING_TREE_INSTANCE_INCLUDED

// Zero is the default for existing scene materials and captured impostors.
float _StandingTreeEnabled;
float _FSTreeSway;
UNITY_INSTANCING_BUFFER_START(StandingTreeInstances)
    UNITY_DEFINE_INSTANCED_PROP(float4, _StandingTreeLeaves)
    UNITY_DEFINE_INSTANCED_PROP(float4, _StandingTreeBark)
    UNITY_DEFINE_INSTANCED_PROP(float4, _StandingTreeAppearance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _StandingTreeFade)
    UNITY_DEFINE_INSTANCED_PROP(float4, _StandingTreeRoot)
UNITY_INSTANCING_BUFFER_END(StandingTreeInstances)

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(STANDING_TREE_GPU_SUPPORT)
struct StandingTreeGpuSource
{
    float4x4 objectToWorld;
    float4 leaves;
    float4 bark;
    float4 appearance;
    float4 fade;
};
struct StandingTreeGpuVisible
{
    float4 fade;
    uint4 source;
};
StructuredBuffer<StandingTreeGpuSource> _StandingTreeSources;
StructuredBuffer<StandingTreeGpuVisible> _StandingTreeVisible;
StructuredBuffer<float4x4> _StandingTreeInverses;
uint _StandingTreeVisibleStart;
float _StandingTreeHasPartTransform;
float4x4 _StandingTreePartLocal, _StandingTreePartInverse;
static float3 standingTreeRootWS;

StandingTreeGpuVisible GetStandingTreeVisible()
{
    return _StandingTreeVisible[_StandingTreeVisibleStart + unity_InstanceID];
}

void SetupStandingTree()
{
    uint index = GetStandingTreeVisible().source.x;
    float4x4 transform = _StandingTreeSources[index].objectToWorld;
    float4x4 inverse = _StandingTreeInverses[index];
    standingTreeRootWS = float3(transform._m03, transform._m13, transform._m23);
    if (_StandingTreeHasPartTransform > .5)
    {
        transform = mul(transform, _StandingTreePartLocal);
        inverse = mul(_StandingTreePartInverse, inverse);
    }
    unity_ObjectToWorld = transform;
    unity_WorldToObject = inverse;
}

#define STANDING_TREE_PROP(name, field) _StandingTreeSources[GetStandingTreeVisible().source.x].field
#else
#define STANDING_TREE_PROP(name, field) UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, name)
#endif

float3 TreeSwayOffset(float3 positionWS, float3 rootWS, float strength)
{
    if (strength <= 0) return 0;
    // Travel in the same direction as the grass wind. The phase is keyed to the
    // tree origin, so every part and its billboard see the same gust.
    const float2 direction = float2(.943858, .330350);
    float phase = _Time.y * 1.05 - dot(rootWS.xz, direction) * .12;
    // Keep the displacement linear in height so the billboard bends in a single plane.
    float lean = sin(phase) * strength * (positionWS.y - rootWS.y);
    return float3(direction.x * lean, 0, direction.y * lean);
}

float3 ApplyStandingTreeSway(float3 positionWS)
{
    if (_StandingTreeEnabled < .5 || _FSTreeSway <= 0) return positionWS;
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(STANDING_TREE_GPU_SUPPORT)
    float3 rootWS = standingTreeRootWS;
#else
    float3 rootWS = UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeRoot).xyz;
#endif
    return positionWS + TreeSwayOffset(positionWS, rootWS, _FSTreeSway);
}

half4 StandingTreeLeafTint(half4 fallback)
{
    return _StandingTreeEnabled > .5 ? STANDING_TREE_PROP(_StandingTreeLeaves, leaves) : fallback;
}
half4 StandingTreeBarkTint(half4 fallback)
{
    return _StandingTreeEnabled > .5 ? STANDING_TREE_PROP(_StandingTreeBark, bark) : fallback;
}
half StandingTreeSnow(half fallback)
{
    return _StandingTreeEnabled > .5 ? STANDING_TREE_PROP(_StandingTreeAppearance, appearance).x : fallback;
}
half StandingTreeAlphaShadows(half fallback)
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(STANDING_TREE_GPU_SUPPORT)
    return _StandingTreeEnabled > .5 ? GetStandingTreeVisible().source.y : fallback;
#else
    return _StandingTreeEnabled > .5 ? STANDING_TREE_PROP(_StandingTreeAppearance, appearance).y : fallback;
#endif
}
void ApplyStandingTreeFade(float2 pixel)
{
    if (_StandingTreeEnabled < .5) return;
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && defined(STANDING_TREE_GPU_SUPPORT)
    float4 fade = GetStandingTreeVisible().fade;
#else
    float4 fade = UNITY_ACCESS_INSTANCED_PROP(StandingTreeInstances, _StandingTreeFade);
#endif
    float threshold = frac(52.9829189 * frac(dot(floor(pixel), float2(.06711056, .00583715))));
    // An interval partitions the same noise between mesh LODs and the far billboard.
    clip(threshold - fade.x);
    clip(fade.y - threshold - .00001);
    // Independent load noise avoids holes when load and handoff fades run together.
    float loadThreshold = frac(52.9829189 * frac(dot(floor(pixel) + 19.0, float2(.06711056, .00583715))));
    clip(fade.z - loadThreshold - .00001);
}
#endif
