using UnityEngine;

public class FarTerrainTileRecord
{
    private readonly ChunkCoord tileCoord;
    private Mesh terrainMesh;
    private Texture2D[] controlMapData;
    private bool requestInFlight;
    private int requestVersion;
    private bool ready;

    public ChunkCoord TileCoord => tileCoord;
    public bool IsRequestInFlight => requestInFlight;
    public int RequestVersion => requestVersion;
    public Texture2D[] ControlMapData => controlMapData;
    public bool HasTerrain => ready && terrainMesh != null && controlMapData != null;

    public FarTerrainTileRecord(ChunkCoord tileCoord)
    {
        this.tileCoord = tileCoord;
    }

    public int BeginRequest()
    {
        requestVersion++;
        requestInFlight = true;
        return requestVersion;
    }

    public void CancelRequest(int version)
    {
        if (requestVersion == version)
            requestInFlight = false;
    }

    public bool TryCompleteRequest(int version, Mesh returnedTerrainMesh, Texture2D[] returnedControlMapData)
    {
        if (!requestInFlight || requestVersion != version)
            return false;

        terrainMesh = returnedTerrainMesh;
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
}
