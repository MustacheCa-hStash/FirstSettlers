using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WaterReflectionVisibilityValidation
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        var coord = new ChunkCoord(0,0); var record = new ChunkRecord(coord);
        var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        var mesh = new Mesh { vertices = new[] { new Vector3(-2,0,-2),new Vector3(2,0,-2),new Vector3(0,0,2) }, triangles = new[] { 0,1,2 } }; mesh.RecalculateBounds();
        var runtime = new ChunkRuntime(record,16,1,null,material,material,false);
        var far = new FarTerrainTileRuntime(new FarTerrainTileRecord(new FarTerrainPatchKey(new ChunkCoord(5,5),1)),16,1,null,material,false,material);
        var manager = (ChunkManager)FormatterServices.GetUninitializedObject(typeof(ChunkManager));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ChunkManager).GetField("loadedChunks",flags).SetValue(manager,new Dictionary<ChunkCoord,ChunkRuntime>{{coord,runtime}});
        typeof(ChunkManager).GetField("loadedFarTerrainTiles",flags).SetValue(manager,new Dictionary<FarTerrainPatchKey,FarTerrainTileRuntime>{{new FarTerrainPatchKey(new ChunkCoord(5,5),1),far}});
        var planes = new[] { new Plane(Vector3.right,0),new Plane(Vector3.left,16),new Plane(Vector3.up,10),
            new Plane(Vector3.down,10),new Plane(Vector3.forward,0),new Plane(Vector3.back,16) };
        int mask = 1 << LayerMask.NameToLayer("Water");
        try
        {
            runtime.SetVisible(true); far.SetVisible(true); far.SetMesh(mesh,mesh);
            Check(!manager.HasVisibleWater(planes,mask),"Far offscreen water enabled reflections.");
            runtime.SetMeshes(mesh,mesh,0);
            Check(manager.HasVisibleWater(planes,mask),"Visible normal water was missed.");
            Check(!manager.HasVisibleWater(planes,0),"Excluded water layer enabled reflections.");
            runtime.SetTerrainHandoffHidden(true);
            Check(!manager.HasVisibleWater(planes,mask),"Hidden replacement water enabled reflections.");
            runtime.SetTerrainHandoffHidden(false);
            Check(manager.HasVisibleWater(planes,mask),"Water re-entry was missed.");
            runtime.SetRenderVisible(false);
            Check(!manager.HasVisibleWater(planes,mask),"Disabled water renderer enabled reflections.");
            // The provider traverses retained runtime meshes rather than desired coverage alone.
            far.Reinitialize(new FarTerrainTileRecord(new FarTerrainPatchKey(coord,1)),16,1,null,false);
            far.SetMesh(mesh,mesh); far.SetVisible(true);
            Check(manager.HasVisibleWater(planes,mask),"Visible outgoing far water was missed.");
            far.SetVisible(false);
            Check(!manager.HasVisibleWater(planes,mask),"Inactive pooled water enabled reflections.");
        }
        finally { runtime.DestroyRuntime(); far.DestroyRuntime(); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); record.Dispose(); }
        Debug.Log("WATER VISIBILITY PASS: normal/far bounds, excluded layer, hidden replacements, outgoing coverage, re-entry and pooled inactivity; no camera or scene rendering.");
    }
}
