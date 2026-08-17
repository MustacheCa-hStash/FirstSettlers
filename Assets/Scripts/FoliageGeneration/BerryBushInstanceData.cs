using UnityEngine;

public struct BerryBushInstanceData
{
    public ulong id;
    public ChunkCoord chunkCoord;
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public WorldFeatureVariant variant;

    public BerryBushInstanceData(
        ulong id,
        ChunkCoord chunkCoord,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        WorldFeatureVariant variant)
    {
        this.id = id;
        this.chunkCoord = chunkCoord;
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.variant = variant;
    }
}
