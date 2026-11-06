#ifndef TREE_IMPOSTOR_OUTLINE_INCLUDED
#define TREE_IMPOSTOR_OUTLINE_INCLUDED
#if defined(TREE_IMPOSTOR_OUTLINE)
StructuredBuffer<float2> _TreeOutlineUVs;
uint _TreeOutlineVertexCount;
#endif

float2 TreeOutlineUV(float2 originalUV, uint vertexID, float2 frame, float framesPerAxis)
{
#if defined(TREE_IMPOSTOR_OUTLINE)
    uint frameIndex = (uint)frame.y * (uint)framesPerAxis + (uint)frame.x;
    return _TreeOutlineUVs[frameIndex * _TreeOutlineVertexCount + vertexID];
#else
    return originalUV;
#endif
}
#endif
