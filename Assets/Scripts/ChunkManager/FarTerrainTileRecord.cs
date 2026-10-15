using UnityEngine;

public class FarTerrainTileRecord : System.IDisposable
{
    private readonly TerrainAssetOwner generatedAssets = new();
    private bool disposed;
    public float[,] FarTreeHeightGrid { get; private set; }
    public Mesh WaterMesh { get; private set; }
    private readonly FarTerrainPatchKey patchKey;
    private Mesh terrainMesh;
    private Texture2D[] controlMapData;
    private bool requestInFlight;
    private int requestVersion;
    private bool ready;

    public FarTerrainPatchKey PatchKey => patchKey;
    public ChunkCoord TileCoord => patchKey.Origin;
    public int SizeInChunks => patchKey.SizeInChunks;
    public bool IsRequestInFlight => requestInFlight;
    public int RequestVersion => requestVersion;
    public Texture2D[] ControlMapData => controlMapData;
    public bool HasTerrain => ready && terrainMesh != null && controlMapData != null;

    public FarTerrainTileRecord(FarTerrainPatchKey patchKey)
    {
        this.patchKey = patchKey;
    }

    public int BeginRequest()
    {
        if (disposed) throw new System.ObjectDisposedException(nameof(FarTerrainTileRecord));
        requestVersion = TerrainRequestIdentity.Next();
        requestInFlight = true;
        return requestVersion;
    }

    public void CancelRequest(int version)
    {
        if (requestVersion == version)
            requestInFlight = false;
    }

    public bool IsRequestCurrent(int version)
    {
        return !disposed && requestInFlight && requestVersion == version;
    }

    public bool TryCompleteRequest(int version, Mesh returnedTerrainMesh, Texture2D[] returnedControlMapData, Mesh returnedWaterMesh = null, float[,] heightGrid = null)
    {
        if (!IsRequestCurrent(version))
            return false;

        generatedAssets.Replace(terrainMesh, WaterMesh, returnedTerrainMesh, returnedWaterMesh);
        generatedAssets.Replace(controlMapData, returnedControlMapData);
        terrainMesh = returnedTerrainMesh;
        WaterMesh = returnedWaterMesh;
        FarTreeHeightGrid = heightGrid;
        controlMapData = returnedControlMapData;
        ready = terrainMesh != null && controlMapData != null;
        requestInFlight = false;
        return true;
    }

    public bool TryGetTerrainMesh(out Mesh mesh)
    {
        mesh = terrainMesh;
        return HasTerrain;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReleaseGeneratedData();
    }

    internal void ReleaseGeneratedData()
    {
        requestInFlight = ready = false;
        generatedAssets.Dispose();
        terrainMesh = WaterMesh = null; controlMapData = null; FarTreeHeightGrid = null;
    }
}
