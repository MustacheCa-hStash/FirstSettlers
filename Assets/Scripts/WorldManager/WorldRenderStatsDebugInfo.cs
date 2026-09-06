public struct WorldRenderStatsDebugInfo
{
    public int VisibleChunkCount;
    public int VisibleChunkWithTerrainMeshCount;
    public int CurrentLOD0ChunkCount;
    public int CurrentLOD1ChunkCount;
    public int CurrentLOD2ChunkCount;
    public int CurrentLOD3ChunkCount;
    public int CurrentLOD4PlusChunkCount;

    public RenderGeometryStats Terrain;
    public RenderGeometryStats Water;
    public RenderGeometryStats Grass;
    public RenderGeometryStats BillboardGrass;
    public RenderGeometryStats Flowers;
    public RenderGeometryStats Clover;
    public RenderGeometryStats Dandelions;
    public RenderGeometryStats TreeBillboards;
    public RenderGeometryStats TreeGameObjects;
    public RenderGeometryStats BushGameObjects;
    public RenderGeometryStats RockGameObjects;

    public long TotalVertices =>
        Terrain.vertices +
        Water.vertices +
        Grass.vertices +
        BillboardGrass.vertices +
        Flowers.vertices +
        Clover.vertices +
        Dandelions.vertices +
        TreeBillboards.vertices +
        TreeGameObjects.vertices +
        BushGameObjects.vertices +
        RockGameObjects.vertices;

    public long TotalTriangles =>
        Terrain.triangles +
        Water.triangles +
        Grass.triangles +
        BillboardGrass.triangles +
        Flowers.triangles +
        Clover.triangles +
        Dandelions.triangles +
        TreeBillboards.triangles +
        TreeGameObjects.triangles +
        BushGameObjects.triangles +
        RockGameObjects.triangles;

    public void AddLOD(int lod)
    {
        switch (lod)
        {
            case 0:
                CurrentLOD0ChunkCount++;
                break;
            case 1:
                CurrentLOD1ChunkCount++;
                break;
            case 2:
                CurrentLOD2ChunkCount++;
                break;
            case 3:
                CurrentLOD3ChunkCount++;
                break;
            default:
                CurrentLOD4PlusChunkCount++;
                break;
        }
    }
}
