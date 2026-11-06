#ifndef FS_TERRAIN_SURFACE_FRAME_INCLUDED
#define FS_TERRAIN_SURFACE_FRAME_INCLUDED
// Interpolate a small camera-relative position. Reconstruct world coordinates
// only for world-anchored lookups; never differentiate that large value.
struct FSTerrainSurfaceFrame
{
    float3 positionWS;
    float3 dpdxWS, dpdyWS;
    float3 viewDirectionWS;
    float distanceToCamera;
};
float3 FSTerrainRelativePosition(float3 positionWS)
{ return positionWS-_WorldSpaceCameraPos.xyz; }
FSTerrainSurfaceFrame FSCreateTerrainSurfaceFrame(float3 positionRWS)
{
    FSTerrainSurfaceFrame frame;
    frame.positionWS=positionRWS+_WorldSpaceCameraPos.xyz;
    #if defined(FS_TERRAIN_LEGACY_DERIVATIVES)
    // Editor-only regression variant reproduces the old precision failure.
    frame.dpdxWS=ddx(frame.positionWS);frame.dpdyWS=ddy(frame.positionWS);
    #else
    frame.dpdxWS=ddx(positionRWS);frame.dpdyWS=ddy(positionRWS);
    #endif
    frame.distanceToCamera=length(positionRWS);
    frame.viewDirectionWS=-positionRWS/max(frame.distanceToCamera,1e-8);
    return frame;
}
#endif
