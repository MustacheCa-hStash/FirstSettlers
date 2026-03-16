using System.Collections.Generic;
using UnityEngine;

public class ChunkManager
{
    private int viewDistance;
    private int chunkSize;
    private Transform viewer;
    private Transform chunkParent;
    private Dictionary<ChunkCoord, Chunk> loadedChunks = new();
    private HashSet<ChunkCoord> visibleLastUpdate = new();
    private HashSet<ChunkCoord> visibleThisUpdate = new();

    public ChunkManager(int viewDistance, int chunkSize, Transform viewer, Transform chunkParent)
    {
        this.viewDistance = viewDistance;
        this.chunkSize = chunkSize;
        this.viewer = viewer;
        this.chunkParent = chunkParent;
    }

    public ChunkCoord GetViewerChunkCoord()
    {
        int cx = Mathf.FloorToInt(viewer.position.x / chunkSize);
        int cz = Mathf.FloorToInt(viewer.position.z / chunkSize);

        ChunkCoord viewerChunk = new ChunkCoord(cx, cz);
        Debug.Log($"Viewer Chunk: {viewerChunk.x}, {viewerChunk.z}");

        return viewerChunk;
    }

    public void UpdateVisibleChunks()
    {
        visibleThisUpdate.Clear();

        ChunkCoord viewerCoord = GetViewerChunkCoord();

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                int sqrDistance = x * x + z * z;
                int sqrViewRadius = viewDistance * viewDistance;
                if (sqrDistance > sqrViewRadius) continue;

                ChunkCoord coord = new ChunkCoord(viewerCoord.x + x, viewerCoord.z + z);
                visibleThisUpdate.Add(coord);
                if (!loadedChunks.TryGetValue(coord, out Chunk chunk))
                {
                    chunk = new Chunk(coord, chunkSize, chunkParent);
                    loadedChunks.Add(coord, chunk);
                }
                if (!chunk.IsVisible())
                {
                    chunk.SetVisible(true);
                }
            }
        }

        foreach (ChunkCoord coord in visibleLastUpdate)
        {
            if (!visibleThisUpdate.Contains(coord))
            {
                loadedChunks[coord].SetVisible(false);
            }
        }

        var temp = visibleLastUpdate;
        visibleLastUpdate = visibleThisUpdate;
        visibleThisUpdate = temp;
    }
}
