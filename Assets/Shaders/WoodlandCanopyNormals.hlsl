#ifndef WOODLAND_CANOPY_NORMALS_INCLUDED
#define WOODLAND_CANOPY_NORMALS_INCLUDED

// The second UV set stores an octahedral normal in Blender mesh axes.
// The FBX importer mirrors mesh X; the model transform handles the up-axis rotation.
float3 WoodlandCanopyNormalWS(float2 packed)
{
    float3 normal = UnpackNormalOctQuadEncode(packed * 2.0 - 1.0);
    return TransformObjectToWorldNormal(float3(-normal.x, normal.y, normal.z));
}

#endif
