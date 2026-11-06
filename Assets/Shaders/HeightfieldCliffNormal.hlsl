#ifndef FS_HEIGHTFIELD_CLIFF_NORMAL_INCLUDED
#define FS_HEIGHTFIELD_CLIFF_NORMAL_INCLUDED
// A unit vertex normal at a narrow lip can point mostly upward even when its
// triangle extends metres down a cliff. Interpolating it produces a bright comb.
// Keep gentle ground and small curvature smooth. Correct only excessive normal
// deflection on steep faces; a blanket face normal would expose triangle facets.
float3 FSHeightfieldCliffNormal(float3 smoothNormal,float3 positionDx,float3 positionDy)
{
    #if defined(FS_CLIFF_NORMAL_LEGACY)
    return smoothNormal;
    #else
    float3 face=cross(positionDy,positionDx);
    float areaSquared=dot(face,face);
    face*=rsqrt(max(areaSquared,1e-20));
    face*=dot(face,smoothNormal)<0.0?-1.0:1.0;
    float disagreement=1.0-saturate(dot(face,smoothNormal));
    float cliff=(1.0-smoothstep(0.25,0.55,abs(face.y)))*step(1e-20,areaSquared);
    cliff*=smoothstep(0.009732,0.060307,disagreement); // 8 to 20 degrees
    return normalize(lerp(smoothNormal,face,cliff));
    #endif
}
#endif
