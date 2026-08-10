using UnityEngine;

public struct RenderGeometryStats
{
    public long instances;
    public long vertices;
    public long triangles;

    public void AddMesh(Mesh mesh)
    {
        AddMeshInstances(mesh, 1);
    }

    public void AddMeshInstances(Mesh mesh, int instanceCount)
    {
        if (mesh == null || instanceCount <= 0)
            return;

        instances += instanceCount;
        vertices += (long)mesh.vertexCount * instanceCount;
        triangles += GetTriangleCount(mesh) * instanceCount;
    }

    private static long GetTriangleCount(Mesh mesh)
    {
        long triangleCount = 0;
        int subMeshCount = mesh.subMeshCount;

        for (int i = 0; i < subMeshCount; i++)
        {
            triangleCount += (long)mesh.GetIndexCount(i) / 3;
        }

        return triangleCount;
    }
}
